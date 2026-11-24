// Original SharpCompute sample (MIT).
using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace SharpCompute.Samples.Audio;

/// <summary>
/// Mono float samples (−1..1) at <see cref="SampleRate"/>, living in a shared GPU buffer. Unified
/// memory: the GPU writes them, <see cref="AudioPlayer"/>'s audio thread reads the same memory, and
/// nothing is copied in between.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime.</b> A clip may be disposed while an <see cref="AudioPlayer"/> is still playing it:
/// every voice holds a lease, and the GPU buffer is released only when the last lease is returned
/// (the player returns them on its next call, or when it is disposed). After <see cref="Dispose"/>,
/// <see cref="Samples"/> and <see cref="SaveWav(string)"/> throw and the clip can't be played again.
/// </para>
/// <para>
/// <b>Thread safety.</b> A clip is immutable once rendered: any number of threads (and players) may
/// read and play it concurrently. <see cref="Dispose"/> is thread-safe.
/// </para>
/// </remarks>
public sealed unsafe class AudioClip : IDisposable
{
    private readonly ReadWriteBuffer<float> _buffer;
    private int _leases;
    private int _disposeRequested;
    private int _released;

    internal AudioClip(ReadWriteBuffer<float> buffer, int sampleRate)
    {
        _buffer = buffer;
        SampleRate = sampleRate;
        Length = buffer.Length;

        // Shared Metal memory never moves, so the address stays valid until the buffer is released.
        Data = (float*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(buffer.AsSpan()));
    }

    /// <summary>Samples per second.</summary>
    public int SampleRate { get; }

    /// <summary>Number of samples.</summary>
    public int Length { get; }

    /// <summary>Length in seconds.</summary>
    public double Duration => (double)Length / SampleRate;

    /// <summary>True once <see cref="Dispose"/> has been called (the memory may still be held by a playing voice).</summary>
    public bool IsDisposed => Volatile.Read(ref _disposeRequested) != 0;

    /// <summary>The samples, read straight from the GPU buffer. Do not hold the span past <see cref="Dispose"/>.</summary>
    /// <exception cref="ObjectDisposedException">The clip has been disposed.</exception>
    public ReadOnlySpan<float> Samples
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _buffer.AsReadOnlySpan();
        }
    }

    /// <summary>The GPU buffer that holds the samples.</summary>
    internal ReadWriteBuffer<float> Buffer => _buffer;

    /// <summary>The first sample, for the audio thread (valid while a lease is held).</summary>
    internal float* Data { get; }

    /// <summary>
    /// A clip of the given samples, copied into a new GPU buffer on <paramref name="device"/>, so
    /// sounds made on the CPU (or loaded from a file) can be played alongside synthesized ones.
    /// </summary>
    public static AudioClip FromSamples(GraphicsDevice device, ReadOnlySpan<float> samples, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(device);
        ChipSynth.ValidateSampleRate(sampleRate);

        if (samples.IsEmpty)
        {
            throw new ArgumentException("A clip needs at least one sample.", nameof(samples));
        }

        return new AudioClip(device.AllocateReadWriteBuffer(samples), sampleRate);
    }

    /// <summary>Writes the clip as a 16-bit PCM mono WAV file (samples clamped to −1..1 and rounded).</summary>
    public void SaveWav(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write);
        SaveWav(stream);
    }

    /// <summary>Writes the clip as a 16-bit PCM mono WAV to <paramref name="stream"/>.</summary>
    public void SaveWav(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ReadOnlySpan<float> samples = Samples;
        const int HeaderSize = 44;
        int dataSize = checked(samples.Length * 2);
        byte[] bytes = new byte[HeaderSize + dataSize];
        Span<byte> b = bytes;

        "RIFF"u8.CopyTo(b);
        BinaryPrimitives.WriteInt32LittleEndian(b[4..], 36 + dataSize);
        "WAVE"u8.CopyTo(b[8..]);
        "fmt "u8.CopyTo(b[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(b[16..], 16);             // fmt chunk size
        BinaryPrimitives.WriteInt16LittleEndian(b[20..], 1);              // PCM
        BinaryPrimitives.WriteInt16LittleEndian(b[22..], 1);              // mono
        BinaryPrimitives.WriteInt32LittleEndian(b[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(b[28..], SampleRate * 2); // byte rate
        BinaryPrimitives.WriteInt16LittleEndian(b[32..], 2);              // block align
        BinaryPrimitives.WriteInt16LittleEndian(b[34..], 16);             // bits per sample
        "data"u8.CopyTo(b[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(b[40..], dataSize);

        for (int i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(b[(HeaderSize + (2 * i))..], ToPcm16(samples[i]));
        }

        GC.KeepAlive(this);
        stream.Write(bytes);
    }

    /// <summary>The 16-bit PCM value of a float sample: clamped to −1..1, scaled by 32767 and rounded to nearest.</summary>
    public static short ToPcm16(float sample) =>
        float.IsNaN(sample) ? (short)0 : (short)MathF.Round(Math.Clamp(sample, -1f, 1f) * 32767f);

    /// <summary>
    /// Releases the GPU buffer, or marks the clip disposed and leaves the release to the last
    /// playing voice's lease.
    /// </summary>
    public void Dispose()
    {
        // Exchange (a full fence) orders the flag before the lease read; see TryAcquire.
        if (Interlocked.Exchange(ref _disposeRequested, 1) == 0 && Volatile.Read(ref _leases) == 0)
        {
            ReleaseBuffer();
        }
    }

    /// <summary>
    /// Takes a lease that keeps the samples alive while a voice plays them. False if the clip has
    /// been disposed. Each successful call must be paired with <see cref="ReleaseLease"/>.
    /// </summary>
    internal bool TryAcquire()
    {
        Interlocked.Increment(ref _leases);

        // A Dispose that set the flag before our increment may have seen no leases and released
        // the buffer; one that sets it after sees our lease and leaves the release to us.
        if (Volatile.Read(ref _disposeRequested) != 0)
        {
            ReleaseLease();
            return false;
        }

        return true;
    }

    internal void ReleaseLease()
    {
        if (Interlocked.Decrement(ref _leases) == 0 && Volatile.Read(ref _disposeRequested) != 0)
        {
            ReleaseBuffer();
        }
    }

    private void ReleaseBuffer()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            _buffer.Dispose();
        }
    }
}
