// Original SharpCompute sample (MIT).
using System.Runtime.InteropServices;

namespace SharpCompute.Samples.Audio;

/// <summary>
/// One sounding note as the kernel sees it: a <see cref="ToneSpec"/>, or a song note with its
/// channel's settings folded in. All fields are 4 bytes, so the C# and MSL layouts agree trivially.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VoiceEvent
{
    /// <summary>The output sample of note-on. Negative for the wrapped-around tail of a looping song's last notes.</summary>
    public int StartSample;

    /// <summary>The <see cref="Waveform"/> as an int.</summary>
    public int Wave;

    /// <summary>Salt for the noise hash (the channel index for songs), so overlapping noise voices don't correlate.</summary>
    public uint Seed;

    /// <summary>Samples from note-on to silence (gate plus release, rounded up); the kernel never reads past it.</summary>
    public int SpanSamples;

    /// <summary>Seconds the note is held (the slide and the duty sweep run over this time).</summary>
    public float Gate;

    public float StartHz;
    public float EndHz;
    public float VibratoHz;
    public float VibratoDepth;

    /// <summary>The pulse duty cycle at note-on (0.5, 0.25 or 0.125).</summary>
    public float Duty;

    public float DutySweep;
    public float Volume;
    public float Attack;
    public float Decay;
    public float Sustain;
    public float Release;
}

/// <summary>
/// Renders and mixes voices, one GPU thread per output sample. Each thread looks up its block of
/// <see cref="BlockSize"/> samples in <c>blockStarts</c>, sums every voice listed for that block in
/// <c>blockEvents</c> and clamps the sum to −1..1. Nothing carries state from one sample to the
/// next: phase, envelope and noise are closed-form functions of the sample index, which is what
/// lets every sample render in parallel and what makes a render bit-identical every time (each
/// thread adds its voices in the same order on every run).
/// </summary>
/// <remarks>
/// The CPU builds the block lists (ChipSynth), so a thread only visits the voices that
/// can sound in its block instead of every note of the song.
/// </remarks>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ChipKernel(
    ReadWriteBuffer<float> output, ReadWriteBuffer<VoiceEvent> events, ReadWriteBuffer<int> blockStarts,
    ReadWriteBuffer<int> blockEvents, int sampleRate) : IComputeShader
{
    /// <summary>log2 of <see cref="BlockSize"/>.</summary>
    public const int BlockShift = 10;

    /// <summary>Samples per block of the voice lists (about 21 ms at 48 kHz).</summary>
    public const int BlockSize = 1 << BlockShift;

    public void Execute()
    {
        int i = ThreadIds.X;
        int block = i >> BlockShift;
        int last = blockStarts[block + 1];
        float sum = 0f;

        for (int k = blockStarts[block]; k < last; k++)
        {
            sum += VoiceSample(events[blockEvents[k]], i, sampleRate);
        }

        output[i] = Gpu.Clamp(sum, -1f, 1f);
    }

    /// <summary>The value of voice <paramref name="e"/> at output sample <paramref name="sample"/>.</summary>
    public static float VoiceSample(VoiceEvent e, int sample, int sampleRate)
    {
        int offset = sample - e.StartSample;

        if (offset < 0 || offset >= e.SpanSamples)
        {
            return 0f;
        }

        float t = (float)offset / (float)sampleRate;
        float level = EnvelopeLevel(t, e.Gate, e.Attack, e.Decay, e.Sustain, e.Release);

        if (level <= 0f)
        {
            return 0f;
        }

        // Exponential slide over the gate, f(t) = f0·e^(kt); the phase is its integral,
        // f0·t·(e^(kt) − 1)/(kt), then the end frequency is held through the release.
        float held = Gpu.Min(t, e.Gate);
        float k = Gpu.Log(e.EndHz / e.StartHz) / e.Gate;
        float frequency = e.StartHz * Gpu.Exp(k * held);
        float phase = (e.StartHz * held * ExpRatio(k * held)) + (frequency * (t - held));
        float modulation = 1f;

        if (e.VibratoHz > 0f)
        {
            // Frequency × (1 + depth·sin ωt); integrated as if the slide were constant over one
            // vibrato cycle, which is close enough for a vibrato.
            float w = 6.2831855f * e.VibratoHz;
            phase += e.VibratoDepth * frequency * (1f - Gpu.Cos(w * t)) / w;
            modulation = 1f + (e.VibratoDepth * Gpu.Sin(w * t));
        }

        float dt = Gpu.Min(frequency * modulation / (float)sampleRate, 0.5f);
        float duty = Gpu.Clamp(e.Duty + (e.DutySweep * (held / e.Gate)), 0.05f, 0.95f);
        return Oscillator(e.Wave, phase, dt, duty, e.Seed) * level * e.Volume;
    }

    /// <summary>
    /// (e^x − 1)/x, accurate for small x. Computing <c>exp(x) − 1</c> directly loses most digits
    /// when x is tiny (a slow slide early in a note); Kahan's trick (u − 1)/log(u), with u = e^x,
    /// cancels the rounding error of u and stays within a few ULP on both the GPU and the CPU.
    /// </summary>
    public static float ExpRatio(float x)
    {
        float u = Gpu.Exp(x);
        return u == 1f ? 1f : (u - 1f) / Gpu.Log(u);
    }

    /// <summary>The ADSR level at <paramref name="t"/> seconds after note-on for a note held <paramref name="gate"/> seconds.</summary>
    public static float EnvelopeLevel(float t, float gate, float attack, float decay, float sustain, float release)
    {
        if (t < 0f)
        {
            return 0f;
        }

        float level = HeldLevel(Gpu.Min(t, gate), attack, decay, sustain);

        if (t <= gate)
        {
            return level;
        }

        float sinceRelease = t - gate;
        return sinceRelease >= release ? 0f : level * (1f - (sinceRelease / release));
    }

    /// <summary>The attack–decay–sustain part of the envelope, while the note is held.</summary>
    public static float HeldLevel(float t, float attack, float decay, float sustain)
    {
        if (t < attack)
        {
            return t / attack;
        }

        float sinceDecay = t - attack;
        return sinceDecay < decay ? 1f + ((sustain - 1f) * (sinceDecay / decay)) : sustain;
    }

    /// <summary>
    /// One oscillator sample. <paramref name="phase"/> is in cycles, <paramref name="dt"/> is the
    /// phase advance per sample (for PolyBLEP), <paramref name="duty"/> the pulse width.
    /// </summary>
    public static float Oscillator(int wave, float phase, float dt, float duty, uint seed)
    {
        float p = Gpu.Fract(phase);

        if (wave <= 2)
        {
            // Pulse: +1 for the first `duty` of the cycle, −1 after, corrected at the rising edge
            // (p = 0) and the falling edge (p = duty), then shifted to zero mean and scaled so the
            // larger swing is 1 (a 25 % pulse swings between +1 and −1/3).
            float pulse = p < duty ? 1f : -1f;
            pulse += PolyBlep(p, dt) - PolyBlep(Gpu.Fract(p - duty + 1f), dt);
            return (pulse - ((2f * duty) - 1f)) / (2f * Gpu.Max(duty, 1f - duty));
        }

        if (wave == 3)
        {
            // Triangle starting at 0 and rising.
            return 1f - (4f * Gpu.Abs(Gpu.Fract(phase + 0.25f) - 0.5f));
        }

        if (wave == 4)
        {
            // Rising saw starting at 0, corrected at its falling edge.
            float q = Gpu.Fract(phase + 0.5f);
            return (2f * q) - 1f - PolyBlep(q, dt);
        }

        if (wave == 5)
        {
            return Gpu.Sin(6.2831855f * p);
        }

        // Noise: one hashed value per cycle (sample-and-hold), uniform in −1..1.
        uint h = Hash((uint)Gpu.Floor(phase) ^ (seed * 0x9E3779B9u));
        return ((float)(h >> 8) * (1f / 8388608f)) - 1f;
    }

    /// <summary>
    /// The PolyBLEP residual for a unit step at phase 0: a two-sample polynomial that, added to a
    /// naive discontinuous wave, approximates a band-limited step. <paramref name="t"/> is the phase
    /// since the step (0..1), <paramref name="dt"/> the phase advance per sample.
    /// </summary>
    public static float PolyBlep(float t, float dt)
    {
        if (t < dt)
        {
            float x = t / dt;
            return x + x - (x * x) - 1f;
        }

        if (t > 1f - dt)
        {
            float x = (t - 1f) / dt;
            return (x * x) + x + x + 1f;
        }

        return 0f;
    }

    /// <summary>A 32-bit integer hash (lowbias32): every input bit affects every output bit.</summary>
    public static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }
}
