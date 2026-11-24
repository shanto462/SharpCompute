// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Threading;

namespace SharpCompute.Samples.Audio;

/// <summary>
/// Renders sounds on the GPU with a SharpCompute compute shader: one thread per output sample. A
/// song renders every channel's notes and mixes them in the same kernel, so a whole loop is one
/// dispatch whatever its number of notes.
/// </summary>
/// <remarks>
/// <para>
/// The result lands in a shared (unified-memory) buffer that <see cref="AudioClip"/> wraps, so the
/// player's audio thread reads what the GPU wrote, with no copy or upload in between.
/// </para>
/// <para>
/// Rendering is deterministic on the same machine and OS version: the same <see cref="ToneSpec"/> or
/// <see cref="Song"/> at the same sample rate gives bit-identical samples on every run (noise is a hash
/// of the cycle index, and each sample sums its voices in a fixed order). Across GPU families or OS
/// releases the last bits may differ, since the kernel uses <c>exp</c>, <c>log</c>, <c>sin</c> and
/// <c>cos</c>, whose last-bit results Metal does not guarantee.
/// </para>
/// <para>
/// <b>Thread safety.</b> <see cref="Render(ToneSpec)"/> and <see cref="Render(Song)"/> may be called
/// from several threads at once; each call uses its own buffers and dispatch.
/// </para>
/// </remarks>
public sealed class ChipSynth : IDisposable
{
    /// <summary>The longest clip a render may produce, in seconds (a guard against a mistyped duration allocating gigabytes).</summary>
    public const double MaxClipSeconds = 600;

    /// <summary>
    /// The longest a single voice (a <see cref="ToneSpec"/>'s duration, or one note of a song) may
    /// last, in seconds. A voice's phase is a single-precision count of cycles, so its precision falls
    /// with frequency × length; see <see cref="ToneSpec.Duration"/>.
    /// </summary>
    public const float MaxVoiceSeconds = 30f;

    private int _disposed;

    /// <param name="device">The device to render on.</param>
    /// <param name="sampleRate">Samples per second of the rendered clips, 8 000..192 000.</param>
    public ChipSynth(GraphicsDevice device, int sampleRate = 48000)
    {
        ArgumentNullException.ThrowIfNull(device);
        ValidateSampleRate(sampleRate);
        Device = device;
        SampleRate = sampleRate;
    }

    public GraphicsDevice Device { get; }

    public int SampleRate { get; }

    /// <summary>Renders one tone; the clip is <c>Duration + Envelope.Release</c> seconds long.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A field of <paramref name="tone"/> is out of range (non-positive frequency or duration, negative volume, an invalid envelope...).</exception>
    public AudioClip Render(ToneSpec tone)
    {
        ArgumentNullException.ThrowIfNull(tone);
        ThrowIfDisposed();
        tone.Validate();

        int length = ClipLength(tone.Duration + (double)tone.Envelope.Release);
        VoiceEvent e = MakeEvent(tone.Wave, 0, tone.Duration, tone.StartHz, tone.EndHz, tone.Volume, tone.Envelope, seed: 0);
        e.VibratoHz = tone.VibratoHz;
        e.VibratoDepth = tone.VibratoDepth;
        e.DutySweep = tone.DutySweep;
        return RenderEvents([e], length);
    }

    /// <summary>
    /// Renders a song as one loop of <see cref="Song.LengthInBeats"/> beats: notes or releases that
    /// run past the end wrap around to the start (see <see cref="Song"/>).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The tempo or length is not positive, or a channel or note is out of range.</exception>
    public AudioClip Render(Song song)
    {
        ArgumentNullException.ThrowIfNull(song);
        ArgumentNullException.ThrowIfNull(song.Channels);
        ThrowIfDisposed();

        if (!(song.BeatsPerMinute > 0f) || !float.IsFinite(song.BeatsPerMinute))
        {
            throw new ArgumentOutOfRangeException(nameof(song), song.BeatsPerMinute, "BeatsPerMinute must be finite and positive.");
        }

        if (!(song.LengthInBeats > 0f) || !float.IsFinite(song.LengthInBeats))
        {
            throw new ArgumentOutOfRangeException(nameof(song), song.LengthInBeats, "LengthInBeats must be finite and positive.");
        }

        double secondsPerBeat = 60.0 / song.BeatsPerMinute;
        int length = ClipLength(song.LengthInBeats * secondsPerBeat);
        List<VoiceEvent> events = [];

        for (int c = 0; c < song.Channels.Count; c++)
        {
            SongChannel channel = song.Channels[c] ?? throw new ArgumentException($"Channel {c} is null.", nameof(song));
            ArgumentNullException.ThrowIfNull(channel.Notes, nameof(song));

            if (!Enum.IsDefined(channel.Wave))
            {
                throw new ArgumentOutOfRangeException(nameof(song), channel.Wave, $"Channel {c} has an unknown waveform.");
            }

            if (!(channel.Volume >= 0f) || !float.IsFinite(channel.Volume))
            {
                throw new ArgumentOutOfRangeException(nameof(song), channel.Volume, $"Channel {c}'s volume must be finite and non-negative.");
            }

            channel.Envelope.Validate(nameof(song));

            foreach (Note note in channel.Notes)
            {
                if ((uint)note.Pitch > 127 || !(note.Start >= 0f) || !(note.Length > 0f) || !(note.Velocity >= 0f) ||
                    !float.IsFinite(note.Start + note.Length + note.Velocity))
                {
                    throw new ArgumentOutOfRangeException(nameof(song), note, $"Channel {c} has an invalid note (pitch 0..127, start ≥ 0, length > 0, velocity ≥ 0).");
                }

                if (note.Length * 60f / song.BeatsPerMinute > MaxVoiceSeconds)
                {
                    throw new ArgumentOutOfRangeException(nameof(song), note, $"Channel {c} has a note longer than {MaxVoiceSeconds} s; split it into shorter notes.");
                }

                if (note.Start >= song.LengthInBeats)
                {
                    continue;
                }

                int start = (int)Math.Round(note.Start * secondsPerBeat * SampleRate);
                float hz = Note.Frequency(note.Pitch);
                VoiceEvent e = MakeEvent(channel.Wave, start, (float)(note.Length * secondsPerBeat), hz, hz, channel.Volume * note.Velocity, channel.Envelope, (uint)c);
                events.Add(e);

                // The part that runs past the end of the loop sounds again from its start.
                if ((long)start + e.SpanSamples > length)
                {
                    e.StartSample = start - length;
                    events.Add(e);
                }
            }
        }

        return RenderEvents(events.ToArray(), length);
    }

    /// <summary>Releases nothing (the synth owns no GPU resources between renders) but makes later renders throw.</summary>
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

    internal static void ValidateSampleRate(int sampleRate)
    {
        if (sampleRate is < 8000 or > 192000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "The sample rate must be between 8000 and 192000 Hz.");
        }
    }

    private VoiceEvent MakeEvent(Waveform wave, int start, float gate, float startHz, float endHz, float volume, Envelope envelope, uint seed) => new()
    {
        StartSample = start,
        Wave = (int)wave,
        Seed = seed,
        SpanSamples = (int)Math.Min(int.MaxValue, Math.Ceiling(((double)gate + envelope.Release) * SampleRate) + 1),
        Gate = gate,
        StartHz = startHz,
        EndHz = endHz,
        Duty = wave switch { Waveform.Pulse25 => 0.25f, Waveform.Pulse12 => 0.125f, _ => 0.5f },
        Volume = volume,
        Attack = envelope.Attack,
        Decay = envelope.Decay,
        Sustain = envelope.Sustain,
        Release = envelope.Release,
    };

    private int ClipLength(double seconds)
    {
        if (seconds > MaxClipSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), seconds, $"A clip may be at most {MaxClipSeconds} s long.");
        }

        // Float durations aren't exact (0.4f + 0.2f is 0.600000009 s): ignore a thousandth of a
        // sample of excess so a 0.6 s clip is 0.6 × rate samples, not one more.
        return Math.Max(1, (int)Math.Ceiling((seconds * SampleRate) - 1e-3));
    }

    /// <summary>
    /// Builds the per-block voice lists (a counting sort: count the voices overlapping each block,
    /// prefix-sum, fill) and runs the kernel over <paramref name="length"/> samples.
    /// </summary>
    private AudioClip RenderEvents(VoiceEvent[] events, int length)
    {
        int blocks = (length + ChipKernel.BlockSize - 1) >> ChipKernel.BlockShift;
        int[] blockStarts = new int[blocks + 1];

        foreach (VoiceEvent e in events)
        {
            if (Overlap(e, length, out int first, out int last))
            {
                for (int b = first; b <= last; b++)
                {
                    blockStarts[b + 1]++;
                }
            }
        }

        for (int b = 0; b < blocks; b++)
        {
            blockStarts[b + 1] += blockStarts[b];
        }

        int[] blockEvents = new int[Math.Max(1, blockStarts[blocks])];
        int[] fill = blockStarts[..blocks];

        for (int i = 0; i < events.Length; i++)
        {
            if (Overlap(events[i], length, out int first, out int last))
            {
                for (int b = first; b <= last; b++)
                {
                    blockEvents[fill[b]++] = i;
                }
            }
        }

        ReadWriteBuffer<float> output = Device.AllocateReadWriteBuffer<float>(length);

        try
        {
            using ReadWriteBuffer<VoiceEvent> eventBuffer = Device.AllocateReadWriteBuffer<VoiceEvent>(events.Length == 0 ? [default] : events);
            using ReadWriteBuffer<int> startBuffer = Device.AllocateReadWriteBuffer<int>(blockStarts);
            using ReadWriteBuffer<int> listBuffer = Device.AllocateReadWriteBuffer<int>(blockEvents);
            Device.For(length, new ChipKernel(output, eventBuffer, startBuffer, listBuffer, SampleRate));
        }
        catch
        {
            output.Dispose();
            throw;
        }

        return new AudioClip(output, SampleRate);
    }

    /// <summary>The first and last block a voice can sound in, or false if it is entirely outside the clip.</summary>
    private static bool Overlap(in VoiceEvent e, int length, out int first, out int last)
    {
        long start = Math.Max(0, e.StartSample);
        long end = Math.Min(length, (long)e.StartSample + e.SpanSamples);
        first = (int)(start >> ChipKernel.BlockShift);
        last = (int)((end - 1) >> ChipKernel.BlockShift);
        return end > start;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
