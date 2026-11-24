// Original SharpCompute sample (MIT).
using System;
using System.Runtime.InteropServices;
using System.Threading;
using SharpCompute.Samples.Audio.Interop;

namespace SharpCompute.Samples.Audio;

/// <summary>
/// Plays <see cref="AudioClip"/>s through macOS AudioToolbox: an AudioQueue output in float32
/// stereo, mixing up to <see cref="Voices"/> one-shot sounds and one music loop on the audio thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>No device, no problem.</b> When <see cref="IsSupported"/> is false (no output device, a headless
/// CI machine, or <c>SHARPCOMPUTE_NO_AUDIO=1</c>), or the queue fails to start, the player is
/// inactive (<see cref="IsActive"/>) and every call is a silent no-op. Argument errors (a null or
/// disposed clip) still throw, so code behaves the same with and without sound.
/// </para>
/// <para>
/// <b>Real-time safety.</b> The queue's callback mixes on AudioToolbox's own thread and never takes
/// a lock or allocates: the game thread hands requests over through a fixed array of voice slots
/// updated with <see cref="Interlocked"/> (see <c>VoiceMixer</c>). Latency is about three buffers of
/// <see cref="BufferFrames"/> frames (≈ 32 ms at 48 kHz).
/// </para>
/// <para>
/// <b>Clip lifetime.</b> A playing voice holds a lease on its clip, so a clip can be disposed (or go
/// out of scope) while it plays: its memory is released when the voice finishes, at the player's
/// next call or when the player is disposed.
/// </para>
/// <para>
/// <b>Thread safety.</b> All members may be called from any thread. Dispose the player to stop the
/// queue; an undisposed player keeps playing until the process exits.
/// </para>
/// <para>
/// <b>Latency and the GC.</b> The audio callback takes no lock and allocates nothing, but it is
/// managed code: a garbage-collection pause longer than the queued audio (three 512-frame buffers,
/// about 32 ms at 48 kHz) is audible as a dropout.
/// </para>
/// </remarks>
public sealed unsafe class AudioPlayer : IDisposable
{
    /// <summary>Frames per AudioQueue buffer.</summary>
    public const int BufferFrames = 512;

    private const int BufferCount = 3;

    private static readonly Lazy<bool> s_isSupported = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly VoiceMixer? _mixer;
    private GCHandle _mixerHandle;
    private nint _queue;
    private float _masterVolume = 1f;
    private int _disposed;

    /// <param name="sampleRate">Output frames per second, 8 000..192 000. Clips at other rates are resampled (linear interpolation).</param>
    /// <param name="voices">How many one-shot sounds can play at once (1..256); when all are busy the oldest is cut off.</param>
    public AudioPlayer(int sampleRate = 48000, int voices = 16)
        : this(sampleRate, voices, openOutput: IsSupported)
    {
    }

    /// <summary>With <paramref name="openOutput"/> false, an inactive player (tests use this so they never make sound).</summary>
    internal AudioPlayer(int sampleRate, int voices, bool openOutput)
    {
        ChipSynth.ValidateSampleRate(sampleRate);
        ArgumentOutOfRangeException.ThrowIfLessThan(voices, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(voices, 256);
        SampleRate = sampleRate;
        Voices = voices;

        if (openOutput)
        {
            VoiceMixer mixer = new(sampleRate, voices);
            _mixerHandle = GCHandle.Alloc(mixer);

            if (TryOpen(GCHandle.ToIntPtr(_mixerHandle), sampleRate, mixer, out _queue))
            {
                _mixer = mixer;
            }
            else
            {
                _mixerHandle.Free();
            }
        }
    }

    /// <summary>
    /// True when the machine has a default audio output device and an AudioQueue can be created for
    /// it. Checked once. Set the environment variable <c>SHARPCOMPUTE_NO_AUDIO=1</c> to force false.
    /// </summary>
    public static bool IsSupported => s_isSupported.Value;

    /// <summary>True when this player has a running output queue; false means every call is a no-op.</summary>
    public bool IsActive => _mixer is not null && Volatile.Read(ref _disposed) == 0;

    public int SampleRate { get; }

    /// <summary>Frames the audio thread has mixed so far, including the priming buffers (0 when inactive).</summary>
    internal long MixedFrames => _mixer?.MixedFrames ?? 0;

    public int Voices { get; }

    /// <summary>A gain applied to the whole mix (sound effects and loop), from 0; the mix is clamped to −1..1 after it.</summary>
    public float MasterVolume
    {
        get => Volatile.Read(ref _masterVolume);
        set
        {
            float v = float.IsNaN(value) ? 0f : Math.Max(0f, value);
            Volatile.Write(ref _masterVolume, v);

            if (_mixer is not null)
            {
                _mixer.MasterVolume = v;
            }
        }
    }

    /// <summary>Plays <paramref name="clip"/> once, fire-and-forget. When every voice is busy, the one started longest ago is cut off.</summary>
    /// <param name="clip">The sound. It may be disposed while it plays (see the remarks on <see cref="AudioPlayer"/>).</param>
    /// <param name="volume">Gain, from 0.</param>
    /// <param name="pan">−1 (left) .. 1 (right). The far side fades out; the near side stays at <paramref name="volume"/>.</param>
    /// <exception cref="ObjectDisposedException"><paramref name="clip"/> has been disposed.</exception>
    public void Play(AudioClip clip, float volume = 1f, float pan = 0f)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ObjectDisposedException.ThrowIf(clip.IsDisposed, clip);

        if (IsActive)
        {
            _mixer!.Play(clip, volume, pan);
        }
    }

    /// <summary>
    /// Plays <paramref name="music"/> on repeat from its start, replacing the current loop. The wrap is
    /// seamless (sample-exact). The mix is clamped to −1..1, so leave music some headroom (a volume
    /// of about 0.4–0.5) if sound effects play on top of it.
    /// </summary>
    /// <exception cref="ObjectDisposedException"><paramref name="music"/> has been disposed.</exception>
    public void PlayLoop(AudioClip music, float volume = 1f)
    {
        ArgumentNullException.ThrowIfNull(music);
        ObjectDisposedException.ThrowIf(music.IsDisposed, music);

        if (IsActive)
        {
            _mixer!.PlayLoop(music, volume);
        }
    }

    /// <summary>Stops the loop (sound effects keep playing).</summary>
    public void StopLoop()
    {
        if (IsActive)
        {
            _mixer!.StopLoop();
        }
    }

    /// <summary>Stops the queue at once and returns every clip lease.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0 || _mixer is null)
        {
            return;
        }

        _mixer.IsStopping = true;
        _ = AudioToolbox.AudioQueueStop(_queue, 1);
        _ = AudioToolbox.AudioQueueDispose(_queue, 1);
        _queue = 0;
        _mixer.ReleaseAll();
        _mixerHandle.Free();
    }

    private static bool Probe()
    {
        if (Environment.GetEnvironmentVariable("SHARPCOMPUTE_NO_AUDIO") is "1" or "true")
        {
            return false;
        }

        try
        {
            if (AudioToolbox.DefaultOutputDevice() == 0)
            {
                return false;
            }

            // Creating a queue (never started) checks that AudioToolbox accepts the format.
            AudioStreamBasicDescription format = Format(48000);
            nint queue;

            if (AudioToolbox.AudioQueueNewOutput(&format, &OnBufferDone, 0, 0, 0, 0, &queue) != 0)
            {
                return false;
            }

            _ = AudioToolbox.AudioQueueDispose(queue, 1);
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static AudioStreamBasicDescription Format(int sampleRate) => new()
    {
        SampleRate = sampleRate,
        FormatId = AudioToolbox.FormatLinearPcm,
        FormatFlags = AudioToolbox.FormatFlagsPackedFloat,
        BytesPerPacket = 8,
        FramesPerPacket = 1,
        BytesPerFrame = 8,
        ChannelsPerFrame = 2,
        BitsPerChannel = 32,
    };

    /// <summary>Creates, primes and starts the queue; on any failure, tears down what was made and returns false.</summary>
    private static bool TryOpen(nint userData, int sampleRate, VoiceMixer mixer, out nint queue)
    {
        AudioStreamBasicDescription format = Format(sampleRate);
        nint q;

        if (AudioToolbox.AudioQueueNewOutput(&format, &OnBufferDone, userData, 0, 0, 0, &q) != 0)
        {
            queue = 0;
            return false;
        }

        for (int i = 0; i < BufferCount; i++)
        {
            AudioQueueBuffer* buffer;

            if (AudioToolbox.AudioQueueAllocateBuffer(q, BufferFrames * 8, &buffer) != 0)
            {
                _ = AudioToolbox.AudioQueueDispose(q, 1);
                queue = 0;
                return false;
            }

            // Prime with the mixer's (silent) output; the queue isn't running yet, so this thread
            // is the only one mixing.
            Fill(mixer, buffer);
            _ = AudioToolbox.AudioQueueEnqueueBuffer(q, buffer, 0, 0);
        }

        if (AudioToolbox.AudioQueueStart(q, 0) != 0)
        {
            _ = AudioToolbox.AudioQueueDispose(q, 1);
            queue = 0;
            return false;
        }

        queue = q;
        return true;
    }

    private static void Fill(VoiceMixer mixer, AudioQueueBuffer* buffer)
    {
        int frames = (int)(buffer->AudioDataBytesCapacity / 8);
        mixer.Mix((float*)buffer->AudioData, frames);
        buffer->AudioDataByteSize = (uint)(frames * 8);
    }

    /// <summary>The AudioQueue output callback: refill the buffer that just played and queue it again.</summary>
    [UnmanagedCallersOnly]
    private static void OnBufferDone(nint userData, nint queue, AudioQueueBuffer* buffer)
    {
        // An exception must never unwind into AudioToolbox (it would take the process down), so
        // anything unexpected plays silence instead.
        try
        {
            if (userData == 0 || GCHandle.FromIntPtr(userData).Target is not VoiceMixer mixer || mixer.IsStopping)
            {
                return;
            }

            Fill(mixer, buffer);
        }
        catch
        {
            new Span<byte>(buffer->AudioData, (int)buffer->AudioDataBytesCapacity).Clear();
            buffer->AudioDataByteSize = buffer->AudioDataBytesCapacity;
        }

        _ = AudioToolbox.AudioQueueEnqueueBuffer(queue, buffer, 0, 0);
    }
}
