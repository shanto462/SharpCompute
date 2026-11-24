using System;
using SharpCompute.Samples.Audio;

namespace SharpCompute.Tests.Audio;

/// <summary>
/// An independent CPU implementation of the synthesizer's formulas in double precision, written
/// from the documented behavior rather than by calling the kernel's helpers, so the GPU output is
/// checked against the math and not against itself.
/// </summary>
internal static class ChipReference
{
    internal static double[] Render(ToneSpec tone, int sampleRate)
    {
        int length = Length(tone.Duration + (double)tone.Envelope.Release, sampleRate);
        double[] samples = new double[length];

        for (int i = 0; i < length; i++)
        {
            samples[i] = Math.Clamp(Voice(
                tone.Wave, (double)i / sampleRate, tone.Duration, tone.StartHz, tone.EndHz, tone.VibratoHz, tone.VibratoDepth,
                tone.DutySweep, tone.Volume, tone.Envelope, seed: 0, sampleRate), -1, 1);
        }

        return samples;
    }

    internal static double[] Render(Song song, int sampleRate)
    {
        double secondsPerBeat = 60.0 / song.BeatsPerMinute;
        int length = Length(song.LengthInBeats * secondsPerBeat, sampleRate);
        double[] samples = new double[length];

        for (int c = 0; c < song.Channels.Count; c++)
        {
            SongChannel channel = song.Channels[c];

            foreach (Note note in channel.Notes)
            {
                if (note.Start >= song.LengthInBeats)
                {
                    continue;
                }

                int start = (int)Math.Round(note.Start * secondsPerBeat * sampleRate);
                double gate = (float)(note.Length * secondsPerBeat);
                double hz = 440.0 * Math.Pow(2.0, (note.Pitch - 69) / 12.0);

                // The loop wraps: the note sounds at `start` and again one loop earlier.
                foreach (int origin in (int[])[start, start - length])
                {
                    for (int i = Math.Max(0, origin); i < length; i++)
                    {
                        double t = (double)(i - origin) / sampleRate;

                        if (t > gate + channel.Envelope.Release)
                        {
                            break;
                        }

                        samples[i] += Voice(channel.Wave, t, gate, hz, hz, 0, 0, 0, channel.Volume * note.Velocity, channel.Envelope, (uint)c, sampleRate);
                    }
                }
            }
        }

        for (int i = 0; i < length; i++)
        {
            samples[i] = Math.Clamp(samples[i], -1, 1);
        }

        return samples;
    }

    /// <summary>Clip length: seconds × rate rounded up, ignoring float noise below a thousandth of a sample.</summary>
    internal static int Length(double seconds, int sampleRate) => Math.Max(1, (int)Math.Ceiling((seconds * sampleRate) - 1e-3));

    internal static double Envelope(double t, double gate, Envelope e)
    {
        static double Held(double t, Envelope e) =>
            t < e.Attack ? t / e.Attack
            : t - e.Attack < e.Decay ? 1 + ((e.Sustain - 1) * (t - e.Attack) / e.Decay)
            : e.Sustain;

        if (t < 0)
        {
            return 0;
        }

        if (t <= gate)
        {
            return Held(t, e);
        }

        double since = t - gate;
        return since >= e.Release ? 0 : Held(gate, e) * (1 - (since / e.Release));
    }

    internal static double BaseDuty(Waveform wave) => wave switch { Waveform.Pulse25 => 0.25, Waveform.Pulse12 => 0.125, _ => 0.5 };

    /// <summary>The instantaneous frequency of a tone (exponential slide over the gate, then held).</summary>
    internal static double Frequency(double t, double gate, double f0, double f1) =>
        f0 * Math.Pow(f1 / f0, Math.Min(t, gate) / gate);

    /// <summary>The phase in cycles: the integral of <see cref="Frequency"/>.</summary>
    internal static double Phase(double t, double gate, double f0, double f1)
    {
        double held = Math.Min(t, gate);
        double k = Math.Log(f1 / f0) / gate;
        double slide = Math.Abs(k) < 1e-12 ? f0 * held : f0 * (Math.Exp(k * held) - 1) / k;
        return slide + (Frequency(t, gate, f0, f1) * (t - held));
    }

    private static double Voice(
        Waveform wave, double t, double gate, double f0, double f1, double vibratoHz, double vibratoDepth, double dutySweep,
        double volume, Envelope envelope, uint seed, int sampleRate)
    {
        double level = Envelope(t, gate, envelope);

        if (level <= 0)
        {
            return 0;
        }

        double f = Frequency(t, gate, f0, f1);
        double phase = Phase(t, gate, f0, f1);
        double modulation = 1;

        if (vibratoHz > 0)
        {
            double w = 2 * Math.PI * vibratoHz;
            phase += vibratoDepth * f * (1 - Math.Cos(w * t)) / w;
            modulation = 1 + (vibratoDepth * Math.Sin(w * t));
        }

        double dt = Math.Min(f * modulation / sampleRate, 0.5);
        double duty = Math.Clamp(BaseDuty(wave) + (dutySweep * Math.Min(t, gate) / gate), 0.05, 0.95);
        double p = phase - Math.Floor(phase);

        double value = wave switch
        {
            Waveform.Square or Waveform.Pulse25 or Waveform.Pulse12 =>
                ((p < duty ? 1 : -1) + Blep(p, dt) - Blep(Wrap(p - duty), dt) - ((2 * duty) - 1)) / (2 * Math.Max(duty, 1 - duty)),
            Waveform.Triangle => 1 - (4 * Math.Abs(Wrap(phase + 0.25) - 0.5)),
            Waveform.Saw => (2 * Wrap(phase + 0.5)) - 1 - Blep(Wrap(phase + 0.5), dt),
            Waveform.Sine => Math.Sin(2 * Math.PI * phase),
            _ => Noise((uint)Math.Floor(phase), seed),
        };

        return value * level * volume;
    }

    private static double Wrap(double x) => x - Math.Floor(x);

    private static double Blep(double t, double dt) =>
        t < dt ? (2 * (t / dt)) - ((t / dt) * (t / dt)) - 1
        : t > 1 - dt ? (((t - 1) / dt) * ((t - 1) / dt)) + (2 * ((t - 1) / dt)) + 1
        : 0;

    /// <summary>lowbias32 of the cycle index salted with the seed, mapped to −1..1 from its top 24 bits.</summary>
    internal static double Noise(uint cycle, uint seed)
    {
        uint x = cycle ^ unchecked(seed * 0x9E3779B9u);
        x ^= x >> 16;
        x = unchecked(x * 0x7FEB352Du);
        x ^= x >> 15;
        x = unchecked(x * 0x846CA68Bu);
        x ^= x >> 16;
        return ((x >> 8) / 8388608.0) - 1;
    }
}
