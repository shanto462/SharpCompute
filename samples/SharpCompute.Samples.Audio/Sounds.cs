// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;

namespace SharpCompute.Samples.Audio;

/// <summary>The oscillator shapes of the synthesizer, modelled on classic sound chips.</summary>
/// <remarks>
/// Square, the pulses and the saw are band-limited with PolyBLEP (a two-sample polynomial
/// correction at each discontinuity), which removes most of the aliasing at a few instructions per
/// sample. The triangle is not corrected: its harmonics fall off as 1/n², so its aliasing is quiet,
/// and the chip sound arguably wants a little of it. Pulses have their DC offset removed, so an
/// envelope over them does not thump, and are scaled so their larger swing is 1 (a 25 % pulse
/// swings between +1 and −1/3): every waveform peaks at the tone's volume, and narrow pulses sound
/// quieter than the square, as they do on the chips.
/// </remarks>
public enum Waveform
{
    /// <summary>A 50 % pulse.</summary>
    Square,

    /// <summary>A 25 % pulse: the thinner, nasal lead sound.</summary>
    Pulse25,

    /// <summary>A 12.5 % pulse: thinner still.</summary>
    Pulse12,

    /// <summary>A triangle (not band-limited, see the remarks on <see cref="Waveform"/>).</summary>
    Triangle,

    /// <summary>A rising sawtooth.</summary>
    Saw,

    /// <summary>A pure sine.</summary>
    Sine,

    /// <summary>
    /// White noise held for one cycle of the tone's frequency (sample-and-hold), so pitch sets its
    /// color: low pitches rumble, high pitches hiss. Each value is a hash of the cycle index, never
    /// a random generator, so a noise clip renders bit-identically every time.
    /// </summary>
    Noise,
}

/// <summary>
/// A linear ADSR envelope: rise from 0 to 1 over <paramref name="Attack"/> seconds, fall to
/// <paramref name="Sustain"/> over <paramref name="Decay"/> seconds, hold while the note is held,
/// then fall to 0 over <paramref name="Release"/> seconds from whatever level it had reached.
/// </summary>
/// <param name="Attack">Seconds from silence to full level; 0 starts at full level.</param>
/// <param name="Decay">Seconds from full level down to <paramref name="Sustain"/>.</param>
/// <param name="Sustain">The level held while the note is held, 0..1.</param>
/// <param name="Release">Seconds from the level at note-off down to silence; 0 cuts the note off.</param>
public readonly record struct Envelope(float Attack, float Decay, float Sustain, float Release)
{
    /// <summary>A plucked shape that fades while held: instant attack, 0.15 s decay to 30 %, a short release.</summary>
    public static Envelope Pluck => new(0f, 0.15f, 0.3f, 0.05f);

    /// <summary>An organ-like gate: instant on, full level while held, a 10 ms release against clicks.</summary>
    public static Envelope Gate => new(0f, 0f, 1f, 0.01f);

    /// <summary>
    /// The envelope level at <paramref name="time"/> seconds after note-on for a note held for
    /// <paramref name="gateTime"/> seconds. This is the formula the GPU kernel evaluates per sample.
    /// </summary>
    public float LevelAt(float time, float gateTime) =>
        ChipKernel.EnvelopeLevel(time, gateTime, Attack, Decay, Sustain, Release);

    internal void Validate(string paramName)
    {
        if (!(Attack >= 0f && Decay >= 0f && Release >= 0f) || !float.IsFinite(Attack + Decay + Release))
        {
            throw new ArgumentOutOfRangeException(paramName, this, "Envelope times must be finite and non-negative.");
        }

        if (!(Sustain >= 0f && Sustain <= 1f))
        {
            throw new ArgumentOutOfRangeException(paramName, this, "Envelope sustain must be between 0 and 1.");
        }
    }
}

/// <summary>One synthesized sound: a tone that slides from <paramref name="StartHz"/> to <paramref name="EndHz"/> over <paramref name="Duration"/> seconds.</summary>
/// <param name="Wave">The oscillator.</param>
/// <param name="StartHz">The frequency at the start. For <see cref="Waveform.Noise"/>, the rate at which the noise changes value.</param>
/// <param name="EndHz">The frequency reached at <paramref name="Duration"/> (and held during the release). The slide is exponential, so it moves by equal musical intervals per second.</param>
/// <param name="Duration">How long the tone is held, in seconds; the clip is <c>Duration + Envelope.Release</c> long.</param>
/// <param name="Envelope">The volume envelope.</param>
/// <param name="Volume">Peak amplitude, 0..1.</param>
/// <param name="VibratoHz">Vibrato rate in Hz; 0 for none.</param>
/// <param name="VibratoDepth">Vibrato depth as a fraction of the frequency (0.03 swings ±3 %, about half a semitone).</param>
/// <param name="DutySweep">For the pulse waves, how much the duty cycle changes over <paramref name="Duration"/> (e.g. 0.25 sweeps a 25 % pulse to 50 %); clamped to 5–95 %.</param>
public sealed record ToneSpec(
    Waveform Wave, float StartHz, float EndHz, float Duration, Envelope Envelope,
    float Volume = 0.5f, float VibratoHz = 0f, float VibratoDepth = 0f, float DutySweep = 0f)
{
    internal void Validate()
    {
        if (!Enum.IsDefined(Wave))
        {
            throw new ArgumentOutOfRangeException(nameof(Wave), Wave, "Unknown waveform.");
        }

        if (!(StartHz > 0f && EndHz > 0f) || !float.IsFinite(StartHz + EndHz))
        {
            throw new ArgumentOutOfRangeException(nameof(StartHz), $"Frequencies must be finite and positive (got {StartHz} → {EndHz} Hz).");
        }

        if (!(Duration > 0f) || !float.IsFinite(Duration))
        {
            throw new ArgumentOutOfRangeException(nameof(Duration), Duration, "Duration must be finite and positive.");
        }

        if (!(Volume >= 0f) || !float.IsFinite(Volume))
        {
            throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "Volume must be finite and non-negative.");
        }

        if (!(VibratoHz >= 0f && VibratoDepth >= 0f) || !float.IsFinite(VibratoHz + VibratoDepth + DutySweep))
        {
            throw new ArgumentOutOfRangeException(nameof(VibratoHz), "Vibrato rate and depth must be finite and non-negative, and the duty sweep finite.");
        }

        Envelope.Validate(nameof(Envelope));
    }
}

/// <summary>A note of a <see cref="SongChannel"/>. Pitch is a MIDI note number (60 = C4, 69 = A4 = 440 Hz); <paramref name="Start"/> and <paramref name="Length"/> are in beats.</summary>
/// <param name="Pitch">MIDI note number, 0..127. For a noise channel it sets the noise rate the same way.</param>
/// <param name="Start">The beat the note starts on, from 0.</param>
/// <param name="Length">How many beats the note is held; the channel's release follows.</param>
/// <param name="Velocity">A volume factor, 0..1.</param>
public readonly record struct Note(int Pitch, float Start, float Length, float Velocity = 1f)
{
    /// <summary>The frequency of a MIDI note number in equal temperament, A4 (69) = 440 Hz.</summary>
    public static float Frequency(int pitch) => 440f * MathF.Pow(2f, (pitch - 69) / 12f);
}

/// <summary>One voice of a <see cref="Song"/>: every note is played with the same oscillator, envelope and volume.</summary>
/// <remarks>Notes of one channel may overlap (each note's release rings over the next): every note is its own voice when the song renders.</remarks>
public sealed record SongChannel(Waveform Wave, Envelope Envelope, float Volume, IReadOnlyList<Note> Notes);

/// <summary>A song: channels of notes, <paramref name="LengthInBeats"/> long at <paramref name="BeatsPerMinute"/>.</summary>
/// <remarks>
/// A song renders as a loop: a note (or its release) that runs past the end wraps around to the
/// start, so <see cref="AudioPlayer.PlayLoop"/> repeats it without a seam. Notes that start at or
/// after <paramref name="LengthInBeats"/> are not played.
/// </remarks>
public sealed record Song(float BeatsPerMinute, float LengthInBeats, IReadOnlyList<SongChannel> Channels)
{
    /// <summary>The length of one loop, in seconds.</summary>
    public double DurationSeconds => LengthInBeats * 60.0 / BeatsPerMinute;
}
