using System;
using System.Collections.Generic;
using System.ComponentModel;
using SharpCompute.Interop;

namespace SharpCompute;

/// <summary>
/// Passed to <see cref="IComputeShaderDescriptor{TSelf}.SetArguments"/>; wraps the active
/// <c>id&lt;MTLComputeCommandEncoder&gt;</c>. Signatures are part of the generator↔runtime ABI.
/// </summary>
/// <remarks>
/// <para>
/// Before a CPU dispatch the runtime runs <c>SetArguments</c> with a writer that has no encoder: it
/// performs the same checks (null, disposed and wrong-device buffers and textures fail up front,
/// exactly as on the GPU) and leases each of them for the duration of the dispatch instead of binding it.
/// </para>
/// <para>
/// <b>Overlapping bindings.</b> Every buffer and texture bound through the writer is compared with
/// the ones bound before it, and the dispatch is refused with an <see cref="ArgumentException"/> when
/// two of them reach the same memory and the shader writes through either: the same buffer or texture
/// bound twice, overlapping views of one buffer, a texture and its
/// <see cref="ReadWriteTexture2D{T}.AsReadOnly"/> view, a texture and the buffer that stores its
/// pixels, or buffers wrapping the same memory. On the GPU such a shader reads stale data (Metal
/// compiles a kernel as if its buffer arguments never overlap, and shows a thread its texture writes
/// only through the argument it wrote), so it would not compute what its C# does.
/// Overlap among bindings the shader only reads is fine. Whether a binding is written is what the
/// three-argument overloads say; the two-argument ones take every <see cref="ReadWriteBuffer{T}"/>
/// and <see cref="ReadWriteTexture2D{T}"/> as written.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public unsafe ref struct ShaderArgumentWriter
{
    internal readonly nint Encoder;

    private readonly GraphicsDevice? _device;

    // Set when encoding into a ComputeContext or a ForAsync dispatch: the command buffer is committed
    // after this call returns (or much later), so bound handles are leased for the batch's lifetime
    // instead of for the duration of the call.
    private readonly CommandBatch? _batch;

    // Buffers and textures bound so far, each with a lease the dispatcher releases once the GPU has finished.
    private readonly List<ObjCHandle>? _leased;

    // Non-null only for a CPU dispatch: the buffer and texture handles leased so far, released by the dispatcher.
    private readonly List<ObjCHandle>? _cpuLeases;

    // What this dispatch has bound so far, to refuse a binding that overlaps one of them (see BindingTable).
    private readonly BindingTable? _bindings;

    // leased receives a lease on every bound buffer; the caller releases them (DangerousRelease)
    // after the command buffer completes. Without it, a buffer is leased only for the bind call.
    // bindings records what is bound, to refuse overlapping bindings; the caller rents and returns it.
    internal ShaderArgumentWriter(nint encoder, GraphicsDevice device, List<ObjCHandle>? leased, BindingTable bindings)
    {
        Encoder = encoder;
        _device = device;
        _leased = leased;
        _bindings = bindings;
    }

    internal ShaderArgumentWriter(nint encoder, GraphicsDevice device, CommandBatch batch, BindingTable bindings)
    {
        Encoder = encoder;
        _device = device;
        _batch = batch;
        _bindings = bindings;
    }

    /// <summary>A writer for a CPU dispatch: validates and leases buffers into <paramref name="leases"/>, binds nothing.</summary>
    internal ShaderArgumentWriter(GraphicsDevice device, List<ObjCHandle> leases, BindingTable bindings)
    {
        _device = device;
        _cpuLeases = leases;
        _bindings = bindings;
    }

    /// <summary>
    /// <c>setBuffer:offset:atIndex:index</c>. The offset is 0 for a buffer that owns its memory; a view
    /// (<c>Slice</c>) binds its source's <c>MTLBuffer</c> at the view's start, and leases the source.
    /// </summary>
    /// <remarks>
    /// The buffer counts as written by the shader when it is a <see cref="ReadWriteBuffer{T}"/>; see
    /// <see cref="SetBuffer{T}(int, Buffer{T}, bool)"/> to say otherwise.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not in 0..30.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="buffer"/> was allocated from a different device, or it overlaps another binding
    /// of this dispatch and the shader writes one of the two.
    /// </exception>
    /// <exception cref="ObjectDisposedException"><paramref name="buffer"/> has been disposed.</exception>
    /// <exception cref="NotSupportedException">A CPU dispatch binds a buffer the CPU cannot access (<see cref="Buffer{T}.IsCpuAccessible"/>).</exception>
    public void SetBuffer<T>(int index, Buffer<T> buffer)
        where T : unmanaged => SetBuffer(index, buffer, written: buffer is ReadWriteBuffer<T>);

    /// <summary>
    /// <see cref="SetBuffer{T}(int, Buffer{T})"/>, saying whether the shader can write the buffer
    /// through this binding.
    /// </summary>
    /// <param name="index">The buffer index, 0..30.</param>
    /// <param name="buffer">The buffer to bind.</param>
    /// <param name="written">
    /// False when no code of the kernel stores to an element through this binding, which lets the
    /// buffer overlap other bindings that are not written either. The generator passes false for a
    /// <see cref="ReadWriteBuffer{T}"/> the shader only reads. True is always safe.
    /// </param>
    /// <inheritdoc cref="SetBuffer{T}(int, Buffer{T})" path="/exception"/>
    public void SetBuffer<T>(int index, Buffer<T> buffer, bool written)
        where T : unmanaged
    {
        GraphicsDevice device = Validate(index);

        ArgumentNullException.ThrowIfNull(buffer);
        ObjectDisposedException.ThrowIf(buffer.IsDisposed, buffer);

        if (!device.IsSameDevice(buffer.Device))
        {
            throw new ArgumentException(
                $"The buffer bound at index {index} was allocated from a different GraphicsDevice than the one dispatching the shader.",
                nameof(buffer));
        }

        // Before anything is leased or bound: a dispatch with overlapping bindings is not recorded at all.
        _bindings?.AddBuffer(index, buffer, written);

        if (_cpuLeases is not null)
        {
            buffer.ThrowIfNotCpuAccessible();

            // The CPU reads the buffer through its indexers for the whole dispatch, so the lease
            // must last that long: a concurrent Dispose then fails the indexers instead of freeing
            // memory under them.
            buffer.Handle.AddLease(buffer);
            _cpuLeases.Add(buffer.Handle);
            return;
        }

        if (_batch is not null)
        {
            // Recorded, not encoded: the batch encodes when it is committed. The lease is released when
            // the batch completes, so a Dispose before then cannot free the memory.
            _batch.RecordBuffer(_batch.Lease(buffer.Handle, buffer), index, buffer.BindOffset);
            return;
        }

        if (_leased is not null)
        {
            // Held until the dispatch completes. The command buffer's own retain keeps the MTLBuffer
            // object alive, but not memory the buffer merely wraps (WrapReadWriteBuffer,
            // MapReadOnlyBuffer): that is freed when the handle is released, so the handle must not be
            // released, by Dispose or by the finalizer, while the GPU may still touch it.
            nint pointer = buffer.Handle.AddLease(buffer);
            _leased.Add(buffer.Handle);
            ObjC.SendVoid(Encoder, Selectors.SetBufferOffsetAtIndex, pointer, buffer.BindOffset, (nuint)index);
            return;
        }

        // The lease closes the window where another thread disposes the buffer mid-call. Once bound,
        // the command buffer holds its own reference until the dispatch completes.
        using ObjCHandle.Lease handle = buffer.Handle.Acquire(buffer);

        ObjC.SendVoid(Encoder, Selectors.SetBufferOffsetAtIndex, handle.Pointer, buffer.BindOffset, (nuint)index);
    }

    /// <summary>
    /// <c>setTexture:atIndex:index</c>. A <see cref="ReadOnlyTexture2D{T}"/> made by
    /// <see cref="ReadWriteTexture2D{T}.AsReadOnly"/> binds (and leases) its source's texture.
    /// </summary>
    /// <remarks>
    /// The texture counts as written by the shader when it is a <see cref="ReadWriteTexture2D{T}"/>;
    /// see <see cref="SetTexture{T}(int, Texture2D{T}, bool)"/> to say otherwise.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not in 0..30.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="texture"/> was allocated from a different device, or it is bound to this
    /// dispatch twice (or shares memory with a bound buffer) and the shader writes one of the two.
    /// </exception>
    /// <exception cref="ObjectDisposedException"><paramref name="texture"/> has been disposed.</exception>
    /// <exception cref="NotSupportedException">A CPU dispatch binds a texture the CPU cannot address (<see cref="Texture2D{T}.IsCpuAccessible"/>).</exception>
    public void SetTexture<T>(int index, Texture2D<T> texture)
        where T : unmanaged => SetTexture(index, texture, written: texture is ReadWriteTexture2D<T>);

    /// <summary>
    /// <see cref="SetTexture{T}(int, Texture2D{T})"/>, saying whether the shader can write the texture
    /// through this binding.
    /// </summary>
    /// <param name="index">The texture index, 0..30.</param>
    /// <param name="texture">The texture to bind.</param>
    /// <param name="written">
    /// False when no code of the kernel writes a pixel through this binding, which lets the texture
    /// be bound again (itself or its <see cref="ReadWriteTexture2D{T}.AsReadOnly"/> view) as long as
    /// no binding of it is written. The generator passes false for a
    /// <see cref="ReadWriteTexture2D{T}"/> the shader only reads. True is always safe.
    /// </param>
    /// <inheritdoc cref="SetTexture{T}(int, Texture2D{T})" path="/exception"/>
    public void SetTexture<T>(int index, Texture2D<T> texture, bool written)
        where T : unmanaged
    {
        GraphicsDevice device = Validate(index, Metal.MaxTextureBindings, "Texture");

        ArgumentNullException.ThrowIfNull(texture);
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);

        if (!device.IsSameDevice(texture.Device))
        {
            throw new ArgumentException(
                $"The texture bound at index {index} was allocated from a different GraphicsDevice than the one dispatching the shader.",
                nameof(texture));
        }

        // Before anything is leased or bound, as for a buffer.
        _bindings?.AddTexture(index, texture, written);

        // Both handles are leased everywhere below: the MTLTexture is what is bound, and the MTLBuffer
        // is the memory behind it, which the CPU reads directly and which must not be released (nor
        // its GC memory pressure dropped) while the GPU may still touch it.
        if (_cpuLeases is not null)
        {
            texture.ThrowIfNotCpuAccessible();

            // The CPU reads and writes the pixels through the indexers for the whole dispatch.
            _cpuLeases.EnsureCapacity(_cpuLeases.Count + 2);
            texture.Handle.AddLease(texture);
            _cpuLeases.Add(texture.Handle);
            texture.BufferHandle.AddLease(texture);
            _cpuLeases.Add(texture.BufferHandle);
            return;
        }

        if (_batch is not null)
        {
            // Recorded, not encoded: the leases are released when the batch completes, so a Dispose
            // before then cannot free the texture.
            nint recorded = _batch.Lease(texture.Handle, texture);
            _batch.Lease(texture.BufferHandle, texture);
            _batch.RecordTexture(recorded, index);
            return;
        }

        if (_leased is not null)
        {
            // Held until the dispatch completes (see SetBuffer).
            _leased.EnsureCapacity(_leased.Count + 2);
            nint pointer = texture.Handle.AddLease(texture);
            _leased.Add(texture.Handle);
            texture.BufferHandle.AddLease(texture);
            _leased.Add(texture.BufferHandle);
            ObjC.SendVoid(Encoder, Selectors.SetTextureAtIndex, pointer, (nuint)index);
            return;
        }

        // The leases close the window where another thread disposes the texture mid-call. Once bound,
        // the command buffer holds its own reference until the dispatch completes.
        using ObjCHandle.Lease handle = texture.Handle.Acquire(texture);
        using ObjCHandle.Lease storage = texture.BufferHandle.Acquire(texture);

        ObjC.SendVoid(Encoder, Selectors.SetTextureAtIndex, handle.Pointer, (nuint)index);
    }

    /// <summary><c>setBytes:length:sizeof(T) atIndex:index</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not in 0..30.</exception>
    public void SetBytes<T>(int index, in T value)
        where T : unmanaged
    {
        GraphicsDevice device = Validate(index);

        if (_cpuLeases is not null)
        {
            // On the CPU the shader reads its fields directly; the constants are not needed.
            return;
        }

        fixed (T* bytes = &value)
        {
            if (_batch is not null && sizeof(T) <= Metal.MaxSetBytesLength)
            {
                // Copied now, so a dispatch sees the constants as they were when it was recorded.
                _batch.RecordBytes(bytes, sizeof(T), index);
                return;
            }

            if (sizeof(T) <= Metal.MaxSetBytesLength)
            {
                // Metal copies the bytes into the command buffer before this call returns.
                ObjC.SendVoid(Encoder, Selectors.SetBytesLengthAtIndex, bytes, (nuint)sizeof(T), (nuint)index);
                return;
            }

            // setBytes is capped at 4 KB, so larger constants go through a temporary buffer. The
            // command buffer retains it at bind time and lets go when it completes, so dropping our
            // reference right away gives the buffer exactly the lifetime of the dispatch.
            nint temporary = device.NewSharedBufferWithBytes(bytes, (nuint)sizeof(T));

            if (_batch is not null)
            {
                // A batch may stay uncommitted for a long time; it keeps its own reference until it
                // completes instead of relying on the command buffer's retain from bind time alone.
                _batch.Own(temporary);
                _batch.RecordBuffer(temporary, index, offset: 0);
                return;
            }

            try
            {
                ObjC.SendVoid(Encoder, Selectors.SetBufferOffsetAtIndex, temporary, (nuint)0, (nuint)index);
            }
            finally
            {
                ObjC.Release(temporary);
            }
        }
    }

    /// <summary>
    /// Records the texture a pixel shader writes its result to, which the runtime binds at texture
    /// index 0 itself: a capture of the same texture (or of its read-only view) then fails like any
    /// other texture that is written and bound twice.
    /// </summary>
    internal readonly void NoteOutput(ReadWriteTexture2D<Rgba32> output) => _bindings?.AddTexture(0, output, written: true, isOutput: true);

    private readonly GraphicsDevice Validate(int index, int limit = Metal.MaxBufferBindings, string kind = "Buffer")
    {
        if (_device is null || (Encoder == 0 && _batch is null && _cpuLeases is null))
        {
            throw new InvalidOperationException(
                $"A {nameof(ShaderArgumentWriter)} is only valid inside {nameof(IComputeShaderDescriptor<>)}.SetArguments during a dispatch.");
        }

        if ((uint)index >= (uint)limit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), index, $"{kind} binding indices must be in the range 0..{limit - 1}.");
        }

        return _device;
    }
}
