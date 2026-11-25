using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using SharpCompute.Interop;

namespace SharpCompute;

/// <summary>
/// A GPU buffer backed by an <c>MTLBuffer</c>, in <c>MTLStorageModeShared</c> (unified memory, zero-copy)
/// unless it wraps a native buffer in another storage mode.
/// </summary>
/// <remarks>
/// The memory is shared with the GPU, not copied: spans and element references point straight into
/// the <c>MTLBuffer</c> and must not be used after <see cref="Dispose"/>. They do not keep the
/// buffer alive either: keep the <see cref="Buffer{T}"/> referenced (e.g. with <c>using</c>) for as
/// long as a span or reference obtained from it is in use. A buffer that is never disposed is
/// released when its handle is finalized.
/// <para>
/// Most buffers own their memory. <see cref="GraphicsDevice.WrapReadWriteBuffer{T}"/> and
/// <see cref="GraphicsDevice.MapReadOnlyBuffer{T}"/> make buffers over existing memory (caller-owned
/// pages or a memory-mapped file) without copying it.
/// </para>
/// <para>
/// <b>Native buffers.</b> <see cref="GraphicsDevice.WrapNativeReadWriteBuffer{T}"/> and
/// <see cref="GraphicsDevice.WrapNativeReadOnlyBuffer{T}"/> make a buffer over an <c>MTLBuffer</c>
/// that other Metal code created, in any storage mode. A buffer in private (GPU-only) storage works
/// with shaders, <c>Copy</c>, <c>Fill</c> and <c>Clear</c>, but has no CPU access:
/// <see cref="IsCpuAccessible"/> is false and the CPU accessors and <c>ForCpu</c> throw
/// <see cref="NotSupportedException"/>. <see cref="NativeHandle"/> and <see cref="NativeByteOffset"/>
/// hand the <c>MTLBuffer</c> of any buffer to other Metal code.
/// </para>
/// <para>
/// <b>Views.</b> <see cref="ReadWriteBuffer{T}.Slice(int, int)"/> and
/// <see cref="ReadOnlyBuffer{T}.Slice(int, int)"/> make a buffer over a range of another one. A view
/// is the same <c>MTLBuffer</c> bound at an offset, so nothing is copied and it can be used wherever
/// a buffer can: bound to a shader (on the GPU, the CPU or both), read and written from the CPU, and
/// passed to <c>Copy</c>, <c>Fill</c> and <c>Clear</c>. Everything about a view is relative to its
/// start: index 0 is its first element and <see cref="Length"/> is its own length, in C# and in the
/// shader alike. A view owns no memory: disposing it releases nothing, and it counts as disposed
/// once the buffer that owns the memory is (for a view of a view, that is the buffer at the root, not
/// the view in between). Making a view allocates a small object and registers it with that buffer;
/// keep a view you bind every frame rather than slicing again each time.
/// </para>
/// </remarks>
public abstract unsafe class Buffer<T> : IDisposable
    where T : unmanaged
{
    private readonly ObjCHandle _handle;
    private readonly T* _contents;
    private readonly int _length;

    // False for memory this process maps read-only (MapReadOnlyBuffer): neither the CPU nor the GPU may write it.
    private readonly bool _isMemoryWritable;

    // False for a native buffer in private storage: the CPU has no access, and _contents is null.
    private readonly bool _isCpuAccessible;

    // The buffer that owns the memory: this object, or for a view (Slice) the buffer at the root of
    // the chain of slices it was made from. _start is this buffer's first element in the root.
    private readonly Buffer<T> _root;
    private readonly int _start;

    // Equal to _length while the buffer is usable and 0 once it is not, so the indexers need a single
    // comparison to reject an out-of-range index, a disposed buffer and a view whose memory was
    // released: disposing the buffer that owns the memory zeroes this field in every view of it (see
    // ViewRegistry). The indexers are the inner loop of CPU dispatches, where a second comparison
    // against the root measured about 25 % on a kernel that does little else.
    private int _accessibleLength;
    private int _disposed;

    // The views of this buffer, when it owns its memory and has ever been sliced.
    private ViewRegistry? _views;

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
        _isMemoryWritable = true;
        _isCpuAccessible = true;
        _root = this;
    }

    /// <summary>
    /// Wraps an existing <c>MTLBuffer</c> whose CPU address is <paramref name="contents"/> and takes
    /// ownership of <paramref name="handle"/>. <paramref name="isMemoryWritable"/> is false when the
    /// memory is mapped read-only, so <see cref="AsSpan"/> must not hand out a writable view.
    /// <paramref name="contents"/> is null for a buffer the CPU cannot access (private storage).
    /// </summary>
    private protected Buffer(GraphicsDevice device, ObjCHandle handle, T* contents, int length, bool isMemoryWritable)
    {
        Device = device;
        _handle = handle;
        _contents = contents;
        _length = length;
        _isMemoryWritable = isMemoryWritable;
        _isCpuAccessible = contents != null;

        // 0 makes the indexers reject every index of a buffer the CPU cannot access, at no extra cost.
        _accessibleLength = _isCpuAccessible ? length : 0;
        _root = this;
    }

    /// <summary>
    /// A view of <paramref name="length"/> elements of <paramref name="source"/> from
    /// <paramref name="start"/>: the same <c>MTLBuffer</c> and memory, owned by the buffer at the root
    /// of <paramref name="source"/>. The view shares that buffer's handle, so binding it leases the
    /// root's memory.
    /// </summary>
    private protected Buffer(Buffer<T> source, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(source.IsDisposed, source);

        if ((uint)start > (uint)source._length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(start), start, $"The start of a slice must be in 0..{source._length} (the length of the buffer it is taken from).");
        }

        if ((uint)length > (uint)(source._length - start))
        {
            throw new ArgumentOutOfRangeException(
                nameof(length), length, $"A slice starting at {start} of a buffer of {source._length} elements can hold at most {source._length - start} elements.");
        }

        Device = source.Device;
        _handle = source._handle;
        _contents = source._contents == null ? null : source._contents + start;
        _length = length;
        _isMemoryWritable = source._isMemoryWritable;
        _isCpuAccessible = source._isCpuAccessible;
        _accessibleLength = _isCpuAccessible ? length : 0;
        _root = source._root;
        _start = source._start + start;

        // From here on, disposing the root makes this view unusable. If the root was disposed since
        // the check above, the view was never usable at all.
        if (!_root.Register(this))
        {
            _accessibleLength = 0;
            throw new ObjectDisposedException(source.GetType().FullName);
        }
    }

    /// <summary>The device this buffer was allocated from. It can only be bound to shaders dispatched on that device.</summary>
    public GraphicsDevice Device { get; }

    /// <summary>Number of elements. Inside a shader, maps to a hidden length constant.</summary>
    public int Length => _length;

    /// <summary>The underlying <c>id&lt;MTLBuffer&gt;</c>. For a view, the handle of the buffer that owns the memory.</summary>
    internal ObjCHandle Handle => _handle;

    /// <summary>True when this buffer owns nothing: it is a view made by <c>Slice</c>.</summary>
    internal bool IsView => !ReferenceEquals(_root, this);

    /// <summary>The buffer that owns the memory: this buffer, or the one a view was (transitively) sliced from.</summary>
    internal Buffer<T> Root => _root;

    /// <summary>The index of this buffer's first element in <see cref="Root"/>: 0 unless it is a view.</summary>
    internal int StartInRoot => _start;

    /// <summary>Where this buffer starts in the <c>MTLBuffer</c>, in bytes: 0 unless it is a view.</summary>
    /// <remarks>
    /// Always a multiple of <c>sizeof(T)</c>, and so of the alignment of the element's MSL type, which
    /// is what Metal requires of the offset of a <c>device</c> buffer binding.
    /// </remarks>
    internal nuint ByteOffset => (nuint)_start * (nuint)sizeof(T);

    /// <summary>
    /// The offset to bind this buffer at (<c>setBuffer:offset:atIndex:</c>). An empty view binds the
    /// start of the buffer instead of its own position: Metal rejects an offset at the very end of a
    /// buffer, and a shader has no element of an empty buffer it may touch anyway.
    /// </summary>
    internal nuint BindOffset => _length == 0 ? 0 : ByteOffset;

    /// <summary>True once the buffer is disposed. A view also counts as disposed once the buffer that owns its memory is.</summary>
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0 || (IsView && _root.IsDisposed);

    /// <summary>
    /// False for a buffer over read-only memory (<see cref="GraphicsDevice.MapReadOnlyBuffer{T}"/>):
    /// its contents can be read with <see cref="AsReadOnlySpan"/>, the indexer and the copy methods,
    /// but <see cref="AsSpan"/> and <see cref="CopyFrom"/> throw, since writing would fault. Also
    /// false when the CPU has no access at all (<see cref="IsCpuAccessible"/>).
    /// </summary>
    public bool IsCpuWritable => _isMemoryWritable && _isCpuAccessible;

    /// <summary>
    /// False for a native buffer in private (GPU-only) storage (<see cref="GraphicsDevice.WrapNativeReadWriteBuffer{T}"/>):
    /// shaders, <c>Copy</c>, <c>Fill</c> and <c>Clear</c> work, but the spans, indexers, copy methods
    /// and <c>ForCpu</c> throw <see cref="NotSupportedException"/>. True for every buffer SharpCompute allocates.
    /// </summary>
    public bool IsCpuAccessible => _isCpuAccessible;

    /// <summary>
    /// The <c>id&lt;MTLBuffer&gt;</c>, for other Metal code: for a view (<c>Slice</c>), the buffer that
    /// owns the memory, with the view starting <see cref="NativeByteOffset"/> bytes into it.
    /// </summary>
    /// <remarks>
    /// The reference is not owned by the caller: it is valid while this buffer (for a view, the buffer
    /// that owns the memory) is not disposed, and for as long as GPU work that uses it is pending.
    /// Code that keeps it longer must retain it (<c>[buffer retain]</c>) and release it when done.
    /// The memory behind a buffer made by <see cref="GraphicsDevice.WrapReadWriteBuffer{T}"/> is freed
    /// when this buffer releases it, retained or not.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public nint NativeHandle
    {
        get
        {
            ThrowIfDisposed();
            return _handle.DangerousGetHandle();
        }
    }

    /// <summary>
    /// Where this buffer starts in <see cref="NativeHandle"/>, in bytes: 0 unless it is a view, and
    /// then <c>start * sizeof(T)</c> from the buffer that owns the memory. Bind it at this offset
    /// (<c>setBuffer:offset:atIndex:</c>) to see what this buffer sees.
    /// </summary>
    public nuint NativeByteOffset => ByteOffset;

    /// <summary>
    /// False only for memory this process maps read-only (<see cref="GraphicsDevice.MapReadOnlyBuffer{T}"/>),
    /// which the GPU must not write either. A private native buffer is writable by the GPU.
    /// </summary>
    internal bool IsMemoryWritable => _isMemoryWritable;

    /// <summary>Direct view of the shared memory. Do not use while a dispatch that writes it is in flight.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    /// <exception cref="NotSupportedException">
    /// The buffer maps read-only memory (<see cref="IsCpuWritable"/> is false; use <see cref="AsReadOnlySpan"/>),
    /// or is GPU-only (<see cref="IsCpuAccessible"/> is false).
    /// </exception>
    public Span<T> AsSpan()
    {
        ThrowIfDisposed();
        ThrowIfNotCpuAccessible();

        if (!_isMemoryWritable)
        {
            throw new NotSupportedException("This buffer maps read-only memory (a memory-mapped file); use AsReadOnlySpan() to read it.");
        }

        return new Span<T>(_contents, _length);
    }

    /// <summary>Direct read-only view of the shared memory. Do not use while a dispatch that writes it is in flight.</summary>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    /// <exception cref="NotSupportedException">The buffer is GPU-only (<see cref="IsCpuAccessible"/> is false).</exception>
    public ReadOnlySpan<T> AsReadOnlySpan()
    {
        ThrowIfDisposed();
        ThrowIfNotCpuAccessible();
        return new ReadOnlySpan<T>(_contents, _length);
    }

    /// <summary>Copies the whole buffer into <paramref name="destination"/>, which must be at least <see cref="Length"/> long.</summary>
    public void CopyTo(Span<T> destination)
    {
        ReadOnlySpan<T> source = AsReadOnlySpan();

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
        T[] result = AsReadOnlySpan().ToArray();
        GC.KeepAlive(this);
        return result;
    }

    /// <summary>
    /// Releases the <c>MTLBuffer</c>. A dispatch that is already encoded keeps the memory alive until it
    /// completes. Disposing a view (<c>Slice</c>) releases nothing: the buffer it was made from owns the
    /// memory. Disposing that buffer disposes every view of it.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _accessibleLength, 0);

        if (!IsView)
        {
            // Before the memory goes: a view's indexer checks nothing but its own length.
            Volatile.Read(ref _views)?.DisposeAll();
            _handle.Dispose();
        }
    }

    /// <summary>
    /// Makes <paramref name="view"/> depend on this buffer, which owns its memory: when this buffer is
    /// disposed, the view's accessible length is zeroed. False when this buffer is already disposed.
    /// </summary>
    private bool Register(Buffer<T> view)
    {
        ViewRegistry views = Volatile.Read(ref _views) ?? Interlocked.CompareExchange(ref _views, new ViewRegistry(), null) ?? _views!;

        // The registry exists before the disposed check, and Dispose sets the flag before it looks for
        // the registry, so one of the two always sees the other: a view is either refused here or
        // found by DisposeAll.
        return views.Add(view, this);
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

    /// <summary>The exception for CPU access to a GPU-only buffer, which every CPU accessor and <c>ForCpu</c> throw.</summary>
    internal void ThrowIfNotCpuAccessible()
    {
        if (!_isCpuAccessible)
        {
            throw new NotSupportedException(
                "This buffer is in private (GPU-only) storage, so the CPU cannot read or write it, and ForCpu cannot use it. " +
                "Shaders, Copy, Fill and Clear work on it; to read it on the CPU, Copy it into a buffer SharpCompute allocated.");
        }
    }

    private void ThrowIndexFailure(int index)
    {
        ThrowIfDisposed();
        ThrowIfNotCpuAccessible();

        throw new ArgumentOutOfRangeException(
            nameof(index), index, $"Index must be non-negative and less than the buffer length ({_length}).");
    }

    /// <summary>
    /// The views of one buffer, held weakly: a view that is dropped without being disposed is simply
    /// collected, and its entry is swept out as the list grows. Thread-safe.
    /// </summary>
    private sealed class ViewRegistry
    {
        private const int FirstSweep = 16;

        private readonly Lock _lock = new();
        private List<WeakReference<Buffer<T>>>? _entries = [];
        private int _sweepAt = FirstSweep;

        /// <summary>Adds <paramref name="view"/>; false when <paramref name="owner"/> is disposed (the view must not be used).</summary>
        internal bool Add(Buffer<T> view, Buffer<T> owner)
        {
            lock (_lock)
            {
                if (_entries is null || Volatile.Read(ref owner._disposed) != 0)
                {
                    return false;
                }

                // Dead entries are swept when the list has doubled since the last sweep, so a loop that
                // slices every frame keeps a list about as long as the number of views still alive.
                if (_entries.Count >= _sweepAt)
                {
                    _entries.RemoveAll(static entry => !entry.TryGetTarget(out _));
                    _sweepAt = Math.Max(FirstSweep, _entries.Count * 2);
                }

                _entries.Add(new WeakReference<Buffer<T>>(view));
                return true;
            }
        }

        /// <summary>Makes every registered view unusable; later <see cref="Add"/> calls are refused.</summary>
        internal void DisposeAll()
        {
            lock (_lock)
            {
                if (_entries is null)
                {
                    return;
                }

                foreach (WeakReference<Buffer<T>> entry in _entries)
                {
                    if (entry.TryGetTarget(out Buffer<T>? view))
                    {
                        Volatile.Write(ref view._accessibleLength, 0);
                    }
                }

                _entries = null;
            }
        }

        /// <summary>Tests only: the number of entries, dead ones included until the next sweep.</summary>
        internal int Count
        {
            get
            {
                lock (_lock)
                {
                    return _entries?.Count ?? 0;
                }
            }
        }
    }

    /// <summary>Tests only: how many view entries this buffer's registry holds (0 when it was never sliced).</summary>
    internal int RegisteredViewCount => Volatile.Read(ref _views)?.Count ?? 0;
}

/// <summary>A read/write buffer; <c>device T*</c> in MSL.</summary>
public sealed class ReadWriteBuffer<T> : Buffer<T>
    where T : unmanaged
{
    internal ReadWriteBuffer(GraphicsDevice device, int length)
        : base(device, length)
    {
    }

    internal unsafe ReadWriteBuffer(GraphicsDevice device, ObjCHandle handle, T* contents, int length)
        : base(device, handle, contents, length, isMemoryWritable: true)
    {
    }

    private ReadWriteBuffer(ReadWriteBuffer<T> source, int start, int length)
        : base(source, start, length)
    {
    }

    /// <summary>
    /// A view of <paramref name="length"/> elements starting at <paramref name="start"/>: a
    /// <see cref="ReadWriteBuffer{T}"/> over the same memory, usable wherever this buffer is. Nothing
    /// is copied; the view is this buffer's <c>MTLBuffer</c> bound at <c>start * sizeof(T)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The view is relative to <paramref name="start"/> everywhere: its index 0 is this buffer's
    /// element <paramref name="start"/>, and its <see cref="Buffer{T}.Length"/> (also inside a shader)
    /// is <paramref name="length"/>. As with any buffer, a shader is not bounds-checked on the GPU:
    /// indexing a view outside 0..<paramref name="length"/> reaches the rest of this buffer.
    /// </para>
    /// <para>
    /// The view owns nothing. Disposing it releases nothing, it counts as disposed once the buffer
    /// that owns the memory is (this buffer, or for a view of a view the buffer at the root), and a
    /// dispatch that binds it leases that memory until the GPU is done. Views may
    /// overlap each other and this buffer; binding two that overlap to one dispatch that writes
    /// either is a data race, exactly like binding the same buffer twice.
    /// </para>
    /// <para>
    /// The offset always satisfies Metal's alignment rule for buffer bindings (a multiple of the
    /// element's alignment), because it is a whole number of elements.
    /// </para>
    /// </remarks>
    /// <param name="start">The first element of the view, 0..<see cref="Buffer{T}.Length"/>.</param>
    /// <param name="length">The number of elements, 0..<c>Length - start</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The range is not inside the buffer.</exception>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public ReadWriteBuffer<T> Slice(int start, int length) => new(this, start, length);

    /// <summary>A view from <paramref name="start"/> to the end of the buffer; see <see cref="Slice(int, int)"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> is not in 0..<see cref="Buffer{T}.Length"/>.</exception>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public ReadWriteBuffer<T> Slice(int start) => new(this, start, Math.Max(0, Length - start));

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

    internal unsafe ReadOnlyBuffer(GraphicsDevice device, ObjCHandle handle, T* contents, int length, bool isMemoryWritable)
        : base(device, handle, contents, length, isMemoryWritable)
    {
    }

    private ReadOnlyBuffer(ReadOnlyBuffer<T> source, int start, int length)
        : base(source, start, length)
    {
    }

    /// <summary>
    /// A view of <paramref name="length"/> elements starting at <paramref name="start"/>: a
    /// <see cref="ReadOnlyBuffer{T}"/> over the same memory, usable wherever this buffer is. Nothing
    /// is copied; the view is this buffer's <c>MTLBuffer</c> bound at <c>start * sizeof(T)</c>.
    /// </summary>
    /// <remarks>
    /// The view is relative to <paramref name="start"/> everywhere, owns nothing and lives as long as
    /// this buffer does; see <see cref="ReadWriteBuffer{T}.Slice(int, int)"/> for the details, which
    /// are the same. A view of a buffer over read-only memory
    /// (<see cref="GraphicsDevice.MapReadOnlyBuffer{T}"/>) is read-only on the CPU as well.
    /// </remarks>
    /// <param name="start">The first element of the view, 0..<see cref="Buffer{T}.Length"/>.</param>
    /// <param name="length">The number of elements, 0..<c>Length - start</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The range is not inside the buffer.</exception>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public ReadOnlyBuffer<T> Slice(int start, int length) => new(this, start, length);

    /// <summary>A view from <paramref name="start"/> to the end of the buffer; see <see cref="Slice(int, int)"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> is not in 0..<see cref="Buffer{T}.Length"/>.</exception>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public ReadOnlyBuffer<T> Slice(int start) => new(this, start, Math.Max(0, Length - start));

    /// <summary>A read-only reference to the element in shared memory. On the CPU it is bounds-checked; in a shader it is not.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the buffer.</exception>
    /// <exception cref="ObjectDisposedException">The buffer has been disposed.</exception>
    public ref readonly T this[int index] => ref GetReference(index);
}
