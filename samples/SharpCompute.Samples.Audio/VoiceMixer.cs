// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Threading;

namespace SharpCompute.Samples.Audio;

/// <summary>
/// A request to start a clip on a voice (or, with no clip, to stop the loop). Created on the game
/// thread, handed to the audio thread through an <see cref="Interlocked.Exchange{T}(ref T, T)"/>
/// and never changed afterwards, except for <see cref="IsDone"/>, which the audio thread sets when it
/// stops playing it (or the game thread, if it was replaced before the audio thread took it).
/// </summary>
internal sealed unsafe class VoiceRequest
{
    private int _done;

    public VoiceRequest(AudioClip? clip, double step, float gainLeft, float gainRight)
    {
        Clip = clip;
        Data = clip is null ? null : clip.Data;
        Length = clip?.Length ?? 0;
        Step = step;
        GainLeft = gainLeft;
        GainRight = gainRight;
    }

    /// <summary>The clip, which holds a lease while the request is outstanding; null for "stop the loop".</summary>
    public AudioClip? Clip { get; }

    public float* Data { get; }

    public int Length { get; }

    /// <summary>Clip samples per output frame (clip rate / output rate).</summary>
    public double Step { get; }

    public float GainLeft { get; }

    public float GainRight { get; }

    public bool IsDone => Volatile.Read(ref _done) != 0;

    public void MarkDone() => Volatile.Write(ref _done, 1);
}

/// <summary>
/// Mixes up to N one-shot voices and one loop into interleaved stereo float frames. The game thread
/// posts requests; the audio thread mixes them. The audio thread never blocks or allocates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Hand-off.</b> Each voice slot has a <c>pending</c> request field. The game thread publishes a
/// request with <see cref="Interlocked.Exchange{T}(ref T, T)"/>; at the start of each buffer the audio
/// thread takes it with another exchange and makes it the slot's active voice. Exactly one side ever
/// owns a given request, so there is nothing to lock. A request the game thread overwrites before
/// the audio thread took it was never played and is retired on the spot.
/// </para>
/// <para>
/// <b>Lifetime.</b> The game thread keeps every request it posted in an outstanding list (each holds
/// a lease on its clip) and returns the leases of finished ones on its next call (<see cref="Sweep"/>),
/// so a clip's memory is never released, nor the clip collected, while the audio thread reads it.
/// </para>
/// <para>
/// <b>Threads.</b> <see cref="Play"/>, <see cref="PlayLoop"/>, <see cref="StopLoop"/> and
/// <see cref="ReleaseAll"/> may be called from any thread (they serialize on a lock the audio thread
/// never takes). <see cref="Mix"/> must be called by one thread at a time: the audio thread.
/// </para>
/// </remarks>
internal sealed unsafe class VoiceMixer
{
    // Shared between the game thread (writes) and the audio thread (takes).
    private readonly VoiceRequest?[] _pending;
    private VoiceRequest? _loopPending;
    private float _masterVolume = 1f;
    private int _stopping;

    // Audio thread only.
    private readonly VoiceRequest?[] _active;
    private readonly double[] _position;
    private VoiceRequest? _loopActive;
    private double _loopPosition;
    private long _mixedFrames;

    // Game side, under _gate.
    private readonly object _gate = new();
    private readonly VoiceRequest?[] _latest;
    private readonly long[] _startOrder;
    private readonly List<VoiceRequest> _outstanding = [];
    private long _order;
    private bool _closed;

    public VoiceMixer(int sampleRate, int voices)
    {
        SampleRate = sampleRate;
        VoiceCount = voices;
        _pending = new VoiceRequest?[voices];
        _active = new VoiceRequest?[voices];
        _position = new double[voices];
        _latest = new VoiceRequest?[voices];
        _startOrder = new long[voices];
    }

    public int SampleRate { get; }

    public int VoiceCount { get; }

    public float MasterVolume
    {
        get => Volatile.Read(ref _masterVolume);
        set => Volatile.Write(ref _masterVolume, value);
    }

    /// <summary>Set before the audio queue is torn down, so a late callback doesn't re-enqueue its buffer.</summary>
    public bool IsStopping
    {
        get => Volatile.Read(ref _stopping) != 0;
        set => Volatile.Write(ref _stopping, value ? 1 : 0);
    }

    /// <summary>Frames mixed so far (written by the audio thread; for tests and diagnostics).</summary>
    public long MixedFrames => Volatile.Read(ref _mixedFrames);

    /// <summary>Requests posted and not yet known to have finished (game side; for tests and diagnostics).</summary>
    public int OutstandingCount
    {
        get
        {
            lock (_gate)
            {
                Sweep();
                return _outstanding.Count;
            }
        }
    }

    /// <summary>Starts <paramref name="clip"/> on a free voice, or on the voice started longest ago.</summary>
    public void Play(AudioClip clip, float volume, float pan)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            Sweep();

            if (!clip.TryAcquire())
            {
                throw new ObjectDisposedException(nameof(AudioClip), "The clip was disposed and can't be played.");
            }

            int slot = 0;
            long oldest = long.MaxValue;

            for (int s = 0; s < _latest.Length; s++)
            {
                VoiceRequest? latest = _latest[s];

                if (latest is null || latest.IsDone)
                {
                    slot = s;
                    break;
                }

                if (_startOrder[s] < oldest)
                {
                    oldest = _startOrder[s];
                    slot = s;
                }
            }

            // Pan law: the near side stays at full level and the far side fades out linearly, so a
            // centered voice plays at exactly `volume` on both sides.
            float p = Math.Clamp(float.IsNaN(pan) ? 0f : pan, -1f, 1f);
            float v = Gain(volume);
            VoiceRequest request = new(clip, (double)clip.SampleRate / SampleRate, v * Math.Min(1f, 1f - p), v * Math.Min(1f, 1f + p));
            _outstanding.Add(request);
            _latest[slot] = request;
            _startOrder[slot] = ++_order;
            Interlocked.Exchange(ref _pending[slot], request)?.MarkDone();
        }
    }

    /// <summary>Replaces the loop with <paramref name="clip"/>, from its start.</summary>
    public void PlayLoop(AudioClip clip, float volume)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            Sweep();

            if (!clip.TryAcquire())
            {
                throw new ObjectDisposedException(nameof(AudioClip), "The clip was disposed and can't be played.");
            }

            float v = Gain(volume);
            VoiceRequest request = new(clip, (double)clip.SampleRate / SampleRate, v, v);
            _outstanding.Add(request);
            Interlocked.Exchange(ref _loopPending, request)?.MarkDone();
        }
    }

    public void StopLoop()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            Sweep();
            Interlocked.Exchange(ref _loopPending, new VoiceRequest(null, 0, 0, 0))?.MarkDone();
        }
    }

    /// <summary>
    /// Retires every request and returns every lease. Call only once the audio thread has stopped
    /// for good; later posts are ignored.
    /// </summary>
    public void ReleaseAll()
    {
        lock (_gate)
        {
            _closed = true;

            foreach (VoiceRequest request in _outstanding)
            {
                request.MarkDone();
            }

            Sweep();
        }
    }

    /// <summary>
    /// Mixes <paramref name="frames"/> interleaved stereo frames into <paramref name="output"/>
    /// (overwriting it). Audio thread: no locks, no allocations.
    /// </summary>
    public void Mix(float* output, int frames)
    {
        new Span<float>(output, frames * 2).Clear();

        for (int slot = 0; slot < _active.Length; slot++)
        {
            if (Volatile.Read(ref _pending[slot]) is not null && Interlocked.Exchange(ref _pending[slot], null) is { } next)
            {
                _active[slot]?.MarkDone();
                _active[slot] = next;
                _position[slot] = 0;
            }

            if (_active[slot] is { } voice)
            {
                _position[slot] = MixVoice(voice, _position[slot], output, frames, loop: false, out bool finished);

                if (finished)
                {
                    voice.MarkDone();
                    _active[slot] = null;
                }
            }
        }

        if (Volatile.Read(ref _loopPending) is not null && Interlocked.Exchange(ref _loopPending, null) is { } loop)
        {
            _loopActive?.MarkDone();
            _loopPosition = 0;

            if (loop.Clip is null)
            {
                loop.MarkDone();
                _loopActive = null;
            }
            else
            {
                _loopActive = loop;
            }
        }

        if (_loopActive is { } music)
        {
            _loopPosition = MixVoice(music, _loopPosition, output, frames, loop: true, out _);
        }

        float master = MasterVolume;

        for (int i = 0; i < frames * 2; i++)
        {
            // Math.Clamp passes NaN through; a bad sample from a CPU-made clip must not reach the device.
            float v = output[i] * master;
            output[i] = v >= -1f ? (v <= 1f ? v : 1f) : (v < -1f ? -1f : 0f);
        }

        Volatile.Write(ref _mixedFrames, _mixedFrames + frames);
    }

    /// <summary>Returns the leases of finished requests. Game side, under <see cref="_gate"/>.</summary>
    private void Sweep()
    {
        for (int i = _outstanding.Count - 1; i >= 0; i--)
        {
            VoiceRequest request = _outstanding[i];

            if (request.IsDone)
            {
                request.Clip?.ReleaseLease();
                _outstanding[i] = _outstanding[^1];
                _outstanding.RemoveAt(_outstanding.Count - 1);
            }
        }
    }

    private static float Gain(float volume) => float.IsNaN(volume) ? 0f : Math.Max(0f, volume);

    /// <summary>
    /// Adds one voice into the output from clip position <paramref name="position"/> and returns the
    /// new position. Linear interpolation when the clip's rate differs from the output's; a loop
    /// wraps (and interpolates across the seam) without a gap.
    /// </summary>
    private static double MixVoice(VoiceRequest voice, double position, float* output, int frames, bool loop, out bool finished)
    {
        float* data = voice.Data;
        int length = voice.Length;
        double step = voice.Step;
        float left = voice.GainLeft;
        float right = voice.GainRight;
        finished = false;

        for (int f = 0; f < frames; f++)
        {
            if (position >= length)
            {
                if (!loop)
                {
                    finished = true;
                    return position;
                }

                position -= length * Math.Floor(position / length);
            }

            int index = (int)position;
            float sample = data[index];
            float fraction = (float)(position - index);

            if (fraction != 0f)
            {
                int nextIndex = index + 1 < length ? index + 1 : loop ? 0 : -1;
                float nextSample = nextIndex >= 0 ? data[nextIndex] : 0f;
                sample += (nextSample - sample) * fraction;
            }

            output[2 * f] += sample * left;
            output[(2 * f) + 1] += sample * right;
            position += step;
        }

        if (!loop && position >= length)
        {
            finished = true;
        }

        return position;
    }
}
