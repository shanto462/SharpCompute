using System;
using System.Runtime.CompilerServices;
using System.Threading;
using SharpCompute.Interop;

namespace SharpCompute;

/// <summary>
/// A GPU buffer backed by an <c>MTLBuffer</c> in <c>MTLStorageModeShared</c> (unified memory, zero-copy).
/// </summary>
/// <remarks>
/// The memory is shared with the GPU, not copied: spans and element references point straight into
/// the <c>MTLBuffer</c> and must not be used after <see cref="Dispose"/>. They do not keep the
/// buffer alive either: keep the <see cref="Buffer{T}"/> referenced (e.g. with <c>using</c>) for as
/// long as a span or reference obtained from it is in use. A buffer that is never disposed is
/// released when its handle is finalized.
/// </remarks>
public abstract unsafe class Buffer<T> : IDisposable
    where T : unmanaged
{
    private readonly ObjCHandle _handle;
    private readonly T* _contents;
    private readonly int _length;

    // Equal to _length while the buffer is alive and 0 once disposed, so the indexers need a single
    // comparison to reject both an out-of-range index and a disposed buffer.
    private int _accessibleLength;
    private int _disposed;

    private protected Buffer(GraphicsDevice device, int length)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        // Metal rejects zero-length buffers, so an empty buffer still owns one element of storage
        // that can be bound to a shader; it is just never exposed on the CPU side.
        ulong byteLength = (ulong)Math.Max(length, 1) * (ulong)sizeof(T);

        if (byteLength > device.MaxBufferLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                length,
                $"A buffer of {length} '{typeof(T)}' elements needs {byteLength} bytes, but '{device.Name}' supports at most {device.MaxBufferLength} bytes per buffer.");
        }

        _handle = device.NewSharedBuffer((nuint)byteLength);
        _contents = (T*)ObjC.SendIntPtr(_handle.DangerousGetHandle(), Selectors.Contents);

        if (_contents == null)
        {
            _handle.Dispose();
            throw new InvalidOperationException("Metal returned a buffer without CPU-accessible contents.");
        }

        Device = device;
        _length = length;
        _accessibleLength = length;
    }

    /// <summary>The device this buffer was allocated from. It can only be bound to shaders dispatched on that device.</summary>
    public GraphicsDevice Device { get; }

    /// <summary>Number of elements. Inside a shader, maps to a hidden length constant.</summary>
    public int Length => _length;

    /// <summary>The underlying <c>id&lt;MTLBuffer&gt;</c>.</summary>
    internal ObjCHandle Handle => _handle;

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Direct view of the shared memory. Do not use while a dispatch that writes it is in flight.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public Span<T> AsSpan()
    {
        ThrowIfDisposed();
        return new Span<T>(_contents, _length);
    }

    /// <summary>Copies the whole buffer into <paramref name="destination"/>, which must be at least <see cref="Length"/> long.</summary>
    public void CopyTo(Span<T> destination)
    {
        Span<T> source = AsSpan();

        if (destination.Length < source.Length)
        {
            throw new ArgumentException(
                $"The destination holds {destination.Length} elements but the buffer has {source.Length}.", nameof(destination));
        }

        source.CopyTo(destination);

        // The span does not root this object; without this a GC during the copy could finalize the
        // handle and free the MTLBuffer under the memcpy.
        GC.KeepAlive(this);
    }

    /// <summary>Copies <paramref name="source"/> to the start of the buffer; it may be shorter than <see cref="Length"/> but not longer.</summary>
    public void CopyFrom(ReadOnlySpan<T> source)
    {
        Span<T> destination = AsSpan();

        if (source.Length > destination.Length)
        {
            throw new ArgumentException(
                $"The source holds {source.Length} elements but the buffer only has room for {destination.Length}.", nameof(source));
        }

        source.CopyTo(destination);
        GC.KeepAlive(this);
    }

    /// <summary>Copies the buffer into a new array.</summary>
    public T[] ToArray()
    {
        T[] result = AsSpan().ToArray();
        GC.KeepAlive(this);
        return result;
    }

    /// <summary>Releases the <c>MTLBuffer</c>. A dispatch that is already encoded keeps the memory alive until it completes.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _accessibleLength, 0);
        _handle.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected ref T GetReference(int index)
    {
        if ((uint)index >= (uint)_accessibleLength)
        {
            ThrowIndexFailure(index);
        }

        return ref _contents[index];
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    private void ThrowIndexFailure(int index)
    {
        ThrowIfDisposed();

        throw new ArgumentOutOfRangeException(
            nameof(index), index, $"Index must be non-negative and less than the buffer length ({_length}).");
    }
}

/// <summary>A read/write buffer; <c>device T*</c> in MSL.</summary>
public sealed class ReadWriteBuffer<T> : Buffer<T>
    where T : unmanaged
{
    internal ReadWriteBuffer(GraphicsDevice device, int length)
        : base(device, length)
    {
    }

    /// <summary>A reference to the element in shared memory. On the CPU it is bounds-checked; in a shader it is not.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the buffer.</exception>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public ref T this[int index] => ref GetReference(index);
}

/// <summary>A read-only buffer; <c>const device T*</c> in MSL.</summary>
public sealed class ReadOnlyBuffer<T> : Buffer<T>
    where T : unmanaged
{
    internal ReadOnlyBuffer(GraphicsDevice device, int length)
        : base(device, length)
    {
    }

    /// <summary>A read-only reference to the element in shared memory. On the CPU it is bounds-checked; in a shader it is not.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the buffer.</exception>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public ref readonly T this[int index] => ref GetReference(index);
}
