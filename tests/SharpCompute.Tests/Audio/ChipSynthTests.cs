using System;
using System.Linq;
using System.Runtime.InteropServices;
using SharpCompute.Samples.Audio;

namespace SharpCompute.Tests.Audio;

public sealed class ChipSynthTests
{
    private const int Rate = 48000;

    private static ChipSynth NewSynth(int sampleRate = Rate) => new(GraphicsDevice.GetDefault(), sampleRate);

    /// <summary>A tone that exercises every stage: slide, vibrato, duty sweep and all four envelope segments.</summary>
    private static ToneSpec Busy(Waveform wave) =>
        new(wave, 220f, 660f, 0.3f, new Envelope(0.02f, 0.05f, 0.6f, 0.1f), Volume: 0.7f, VibratoHz: 6f, VibratoDepth: 0.02f, DutySweep: 0.2f);

    [MetalTheory]
    [InlineData(Waveform.Sine, 2e-3, 0.0)]
    [InlineData(Waveform.Triangle, 2e-3, 0.0)]
    [InlineData(Waveform.Square, 0.01, 0.001)]
    [InlineData(Waveform.Pulse25, 0.01, 0.001)]
    [InlineData(Waveform.Pulse12, 0.01, 0.001)]
    [InlineData(Waveform.Saw, 0.01, 0.001)]
    [InlineData(Waveform.Noise, 2.0, 0.002)]
    public void Tone_MatchesCpuReference(Waveform wave, double maxError, double maxFractionAbove1e3)
    {
        // Float on the GPU against double on the CPU: the phases differ by ~1e-4 cycles. Smooth
        // waves stay within 2e-3 everywhere; at a PolyBLEP edge that shifts the corrected sample a
        // little, and a noise value can land on the other side of a cycle boundary, so for those
        // only a handful of samples may differ by more than 1e-3.
        using ChipSynth synth = NewSynth();
        ToneSpec tone = Busy(wave);
        using AudioClip clip = synth.Render(tone);
        double[] expected = ChipReference.Render(tone, Rate);

        Assert.Equal(expected.Length, clip.Length);
        (double max, double fraction) = Compare(clip.Samples, expected);
        Assert.True(max <= maxError, $"{wave}: max error {max:G4} > {maxError}");
        Assert.True(fraction <= maxFractionAbove1e3, $"{wave}: {fraction:P3} of samples differ by more than 1e-3");
    }

    [MetalFact]
    public void Envelope_ShapesTheAmplitude()
    {
        // A 1 Hz square is +1 for the first half second (PolyBLEP only touches the first sample),
        // so the samples there are exactly volume × envelope.
        Envelope envelope = new(0.05f, 0.1f, 0.5f, 0.2f);
        using ChipSynth synth = NewSynth();
        using AudioClip clip = synth.Render(new ToneSpec(Waveform.Square, 1f, 1f, 0.4f, envelope, Volume: 0.8f));
        ReadOnlySpan<float> s = clip.Samples;

        Assert.Equal((int)Math.Ceiling(0.6 * Rate), clip.Length);

        for (int i = 2; i < Rate / 2 - 2; i++)
        {
            double t = (double)i / Rate;
            Assert.Equal(0.8 * ChipReference.Envelope(t, 0.4, envelope), s[i], 1e-4);
        }

        Assert.Equal(0.8, s[(int)(0.05 * Rate)], 1e-3);   // end of attack
        Assert.Equal(0.4, s[(int)(0.15 * Rate)], 1e-3);   // end of decay: sustain
        Assert.Equal(0.4, s[(int)(0.3 * Rate)], 1e-3);    // sustain
        Assert.Equal(0.2, s[(int)(0.5 * Rate) - 3], 1e-3); // halfway through the release
        Assert.Equal(0.0, s[^1], 1e-3);                     // silent at the end
        Assert.Equal(0.0, s[0], 1e-6);                      // and at the start (attack from 0)
    }

    [Fact]
    public void Envelope_LevelAt_HandlesEdgeCases()
    {
        // Zero attack starts at full level; zero decay goes straight to sustain.
        Assert.Equal(1f, new Envelope(0f, 0.1f, 0.5f, 0.1f).LevelAt(0f, 1f));
        Assert.Equal(0.5f, new Envelope(0f, 0f, 0.5f, 0.1f).LevelAt(0.001f, 1f));

        // Released mid-attack: the release starts from the level reached, not from 1.
        Envelope slow = new(1f, 0f, 1f, 0.5f);
        Assert.Equal(0.25f, slow.LevelAt(0.25f, 0.25f), 1e-6f);
        Assert.Equal(0.125f, slow.LevelAt(0.5f, 0.25f), 1e-6f);

        // Zero release cuts the note at the gate.
        Envelope cut = new(0f, 0f, 1f, 0f);
        Assert.Equal(1f, cut.LevelAt(0.2f, 0.2f));
        Assert.Equal(0f, cut.LevelAt(0.2001f, 0.2f));

        // Before note-on and after the release: silence.
        Assert.Equal(0f, slow.LevelAt(-0.1f, 1f));
        Assert.Equal(0f, slow.LevelAt(2f, 1f));
    }

    [MetalTheory]
    [InlineData(Waveform.Square, 440f)]
    [InlineData(Waveform.Pulse25, 300f)]
    [InlineData(Waveform.Pulse12, 300f)]
    [InlineData(Waveform.Triangle, 300f)]
    [InlineData(Waveform.Saw, 523.25f)]
    [InlineData(Waveform.Sine, 1000f)]
    public void Tone_HasThePitchItWasAskedFor(Waveform wave, float hz)
    {
        using ChipSynth synth = NewSynth();
        using AudioClip clip = synth.Render(new ToneSpec(wave, hz, hz, 1f, Envelope.Gate, Volume: 0.5f));

        // Every one of these waves crosses zero upward once per cycle.
        int crossings = RisingCrossings(clip.Samples[..Rate]);
        Assert.InRange(crossings, hz - 1.5, hz + 1.5);
    }

    [MetalFact]
    public void Tone_SlidesExponentially()
    {
        using ChipSynth synth = NewSynth();
        using AudioClip clip = synth.Render(new ToneSpec(Waveform.Sine, 200f, 800f, 1f, Envelope.Gate, Volume: 0.5f));

        // Cycles in a window = phase difference across it.
        foreach ((double from, double to) in new[] { (0.0, 0.1), (0.45, 0.55), (0.9, 1.0) })
        {
            double expected = ChipReference.Phase(to, 1, 200, 800) - ChipReference.Phase(from, 1, 200, 800);
            int crossings = RisingCrossings(clip.Samples[(int)(from * Rate)..(int)(to * Rate)]);
            Assert.InRange(crossings, expected - 1.5, expected + 1.5);
        }

        // Halfway through an exponential 200 → 800 slide is 400 Hz (an octave up), not 500.
        Assert.Equal(400, ChipReference.Frequency(0.5, 1, 200, 800), 1e-9);
    }

    [MetalFact]
    public void Song_NotePitchFollowsMidi()
    {
        using ChipSynth synth = NewSynth();
        Song song = new(60f, 1f, [new SongChannel(Waveform.Sine, Envelope.Gate, 0.5f, [new Note(69, 0f, 1f)])]);
        using AudioClip clip = synth.Render(song);

        Assert.Equal(Rate, clip.Length);
        Assert.InRange(RisingCrossings(clip.Samples), 439, 441);
        Assert.Equal(261.6256f, Note.Frequency(60), 1e-3f);
    }

    [MetalTheory]
    [InlineData(Waveform.Noise)]
    [InlineData(Waveform.Pulse25)]
    public void Render_IsBitIdenticalEveryTime(Waveform wave)
    {
        using ChipSynth first = NewSynth();
        using ChipSynth second = NewSynth();
        ToneSpec tone = Busy(wave);

        using AudioClip a = first.Render(tone);
        using AudioClip b = first.Render(tone);
        using AudioClip c = second.Render(tone);
        using AudioClip songA = first.Render(ChipPresets.Loop);
        using AudioClip songB = second.Render(ChipPresets.Loop);

        Assert.True(Bits(a.Samples).SequenceEqual(Bits(b.Samples)));
        Assert.True(Bits(a.Samples).SequenceEqual(Bits(c.Samples)));
        Assert.True(Bits(songA.Samples).SequenceEqual(Bits(songB.Samples)));
    }

    [MetalFact]
    public void Noise_IsWhiteAndChangesOncePerCycle()
    {
        using ChipSynth synth = NewSynth();
        using AudioClip clip = synth.Render(new ToneSpec(Waveform.Noise, 4800f, 4800f, 1f, Envelope.Gate, Volume: 1f));
        ReadOnlySpan<float> s = clip.Samples[..Rate];

        // 4800 Hz at 48 kHz: each value is held for 10 samples.
        int changes = 0;

        for (int i = 1; i < s.Length; i++)
        {
            changes += s[i] != s[i - 1] ? 1 : 0;
        }

        Assert.InRange(changes, 4790, 4800);

        // Uniform in −1..1: mean near 0, mean square near 1/3.
        double mean = 0, square = 0;

        foreach (float v in s)
        {
            mean += v;
            square += v * v;
        }

        Assert.InRange(mean / s.Length, -0.05, 0.05);
        Assert.InRange(square / s.Length, 0.30, 0.36);
    }

    [MetalFact]
    public void Song_LengthIsTheLoopLength()
    {
        using ChipSynth synth = NewSynth();
        using AudioClip clip = synth.Render(ChipPresets.Loop);

        Assert.Equal(16 * 60.0 / 150, ChipPresets.Loop.DurationSeconds, 1e-9);
        Assert.Equal(16 * 60 * Rate / 150, clip.Length);
        Assert.Equal(6.4, clip.Duration, 1e-9);
        Assert.Equal(Rate, clip.SampleRate);
    }

    [MetalFact]
    public void Song_MixesChannelsBySumming()
    {
        Envelope envelope = new(0.01f, 0.1f, 0.6f, 0.2f);
        SongChannel lead = new(Waveform.Pulse25, envelope, 0.3f, [new Note(72, 0f, 1f), new Note(76, 1f, 1f), new Note(79, 2.5f, 0.5f)]);
        SongChannel bass = new(Waveform.Triangle, envelope, 0.4f, [new Note(48, 0f, 2f), new Note(43, 2f, 2f)]);

        using ChipSynth synth = NewSynth();
        using AudioClip both = synth.Render(new Song(120f, 4f, [lead, bass]));
        using AudioClip leadOnly = synth.Render(new Song(120f, 4f, [lead]));
        using AudioClip bassOnly = synth.Render(new Song(120f, 4f, [bass]));

        ReadOnlySpan<float> mix = both.Samples;

        for (int i = 0; i < mix.Length; i++)
        {
            Assert.Equal(leadOnly.Samples[i] + bassOnly.Samples[i], mix[i], 1e-6f);
        }
    }

    [MetalFact]
    public void Song_MatchesCpuReference()
    {
        using ChipSynth synth = NewSynth(44100);
        using AudioClip clip = synth.Render(ChipPresets.Loop);
        double[] expected = ChipReference.Render(ChipPresets.Loop, 44100);

        Assert.Equal(expected.Length, clip.Length);
        (double max, double fraction) = Compare(clip.Samples, expected);

        // Pulse edges and noise cycle boundaries, as in Tone_MatchesCpuReference.
        Assert.True(fraction <= 0.002, $"{fraction:P3} of samples differ by more than 1e-3 (max {max:G4})");
    }

    [MetalFact]
    public void Song_WrapsTailsToTheStart()
    {
        // The last note's release runs 0.25 s past the end of the loop, so it sounds at the start.
        SongChannel channel = new(Waveform.Sine, new Envelope(0f, 0f, 1f, 0.25f), 0.5f, [new Note(69, 3.5f, 0.5f)]);
        Song song = new(120f, 4f, [channel]);

        using ChipSynth synth = NewSynth();
        using AudioClip clip = synth.Render(song);
        double[] expected = ChipReference.Render(song, Rate);

        Assert.True(Rms(clip.Samples[..(Rate / 10)]) > 0.05);
        Assert.Equal(0f, clip.Samples[(int)(0.3 * Rate)]);
        (double max, _) = Compare(clip.Samples, expected);
        Assert.True(max < 2e-3, $"max error {max:G4}");

        // And the seam is continuous: the end of the clip flows into its start.
        float last = clip.Samples[^1];
        float first = clip.Samples[0];
        Assert.True(Math.Abs(first - last) < 0.05, $"seam jump {last} → {first}");
    }

    [MetalFact]
    public void Song_IgnoresNotesPastTheEnd_AndClampsTheMix()
    {
        using ChipSynth synth = NewSynth();
        using AudioClip silent = synth.Render(new Song(120f, 2f, [new SongChannel(Waveform.Square, Envelope.Gate, 1f, [new Note(60, 2f, 1f)])]));
        Assert.All(silent.Samples.ToArray(), v => Assert.Equal(0f, v));

        SongChannel loud = new(Waveform.Square, Envelope.Gate, 1f, [new Note(60, 0f, 2f)]);
        using AudioClip clipped = synth.Render(new Song(120f, 2f, [loud, loud, loud]));
        float[] samples = clipped.Samples.ToArray();
        Assert.All(samples, v => Assert.InRange(v, -1f, 1f));
        Assert.Contains(1f, samples);
        Assert.Contains(-1f, samples);
    }

    [MetalFact]
    public void EmptySong_IsSilence()
    {
        using ChipSynth synth = NewSynth();
        using AudioClip clip = synth.Render(new Song(100f, 1f, []));
        Assert.Equal((int)Math.Ceiling(0.6 * Rate), clip.Length);
        Assert.All(clip.Samples.ToArray(), v => Assert.Equal(0f, v));
    }

    [MetalFact]
    public void Render_RejectsInvalidInput()
    {
        using ChipSynth synth = NewSynth();
        Envelope ok = Envelope.Gate;

        Assert.Throws<ArgumentNullException>(() => synth.Render((ToneSpec)null!));
        Assert.Throws<ArgumentNullException>(() => synth.Render((Song)null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new ToneSpec(Waveform.Sine, 0f, 440f, 1f, ok)));
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new ToneSpec(Waveform.Sine, 440f, float.NaN, 1f, ok)));
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new ToneSpec(Waveform.Sine, 440f, 440f, 0f, ok)));
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new ToneSpec(Waveform.Sine, 440f, 440f, 1f, ok, Volume: -1f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new ToneSpec(Waveform.Sine, 440f, 440f, 1f, new Envelope(-1f, 0f, 1f, 0f))));
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new ToneSpec(Waveform.Sine, 440f, 440f, 1f, new Envelope(0f, 0f, 1.5f, 0f))));
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new ToneSpec((Waveform)42, 440f, 440f, 1f, ok)));
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new ToneSpec(Waveform.Sine, 440f, 440f, 1000f, ok)));

        // One voice is capped at MaxVoiceSeconds (precision falls with frequency × length).
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new ToneSpec(Waveform.Sine, 440f, 440f, ChipSynth.MaxVoiceSeconds + 1f, ok)));
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new Song(60f, 64f, [new SongChannel(Waveform.Sine, ok, 0.5f, [new Note(60, 0f, 31f)])])));

        SongChannel channel = new(Waveform.Sine, ok, 0.5f, [new Note(60, 0f, 1f)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new Song(0f, 4f, [channel])));
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new Song(120f, 0f, [channel])));
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new Song(120f, 4f, [channel with { Notes = [new Note(128, 0f, 1f)] }])));
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new Song(120f, 4f, [channel with { Notes = [new Note(60, -1f, 1f)] }])));
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new Song(120f, 4f, [channel with { Notes = [new Note(60, 0f, 0f)] }])));
        Assert.Throws<ArgumentOutOfRangeException>(() => synth.Render(new Song(120f, 4f, [channel with { Volume = -0.1f }])));
        Assert.Throws<ArgumentException>(() => synth.Render(new Song(120f, 4f, [null!])));

        Assert.Throws<ArgumentOutOfRangeException>(() => new ChipSynth(GraphicsDevice.GetDefault(), 1000));
        Assert.Throws<ArgumentNullException>(() => new ChipSynth(null!));

        synth.Dispose();
        Assert.Throws<ObjectDisposedException>(() => synth.Render(ChipPresets.Blip));
    }

    [MetalFact]
    public void Presets_RenderWithinRange()
    {
        using ChipSynth synth = NewSynth();

        foreach ((string name, ToneSpec tone) in ChipPresets.Effects)
        {
            using AudioClip clip = synth.Render(tone);
            float[] s = clip.Samples.ToArray();
            Assert.True(s.All(v => v is >= -1f and <= 1f), name);
            Assert.True(Rms(s) > 0.01, $"{name} is silent");
            Assert.Equal(0f, s[^1], 0.01f);
        }
    }

    [MetalFact]
    public void SampleRate_ScalesTheClip()
    {
        using ChipSynth synth = NewSynth(22050);
        using AudioClip clip = synth.Render(new ToneSpec(Waveform.Square, 441f, 441f, 1f, new Envelope(0f, 0f, 1f, 0f)));
        Assert.Equal(22050, clip.Length);
        Assert.Equal(22050, clip.SampleRate);
        Assert.InRange(RisingCrossings(clip.Samples), 440, 442);
    }

    internal static (double Max, double FractionAbove1e3) Compare(ReadOnlySpan<float> actual, double[] expected)
    {
        double max = 0;
        int above = 0;

        for (int i = 0; i < expected.Length; i++)
        {
            double error = Math.Abs(actual[i] - expected[i]);
            max = Math.Max(max, error);
            above += error > 1e-3 ? 1 : 0;
        }

        return (max, (double)above / expected.Length);
    }

    internal static int RisingCrossings(ReadOnlySpan<float> s)
    {
        int count = 0;

        for (int i = 1; i < s.Length; i++)
        {
            count += s[i - 1] < 0f && s[i] >= 0f ? 1 : 0;
        }

        return count;
    }

    private static double Rms(ReadOnlySpan<float> s)
    {
        double sum = 0;

        foreach (float v in s)
        {
            sum += v * v;
        }

        return Math.Sqrt(sum / s.Length);
    }

    private static uint[] Bits(ReadOnlySpan<float> s) => MemoryMarshal.Cast<float, uint>(s).ToArray();
}
