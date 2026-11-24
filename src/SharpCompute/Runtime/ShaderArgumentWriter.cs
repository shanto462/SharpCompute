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
/// Before a CPU dispatch the runtime runs <c>SetArguments</c> with a writer that has no encoder: it
/// performs the same checks (null, disposed and wrong-device buffers fail up front, exactly as on the
/// GPU) and leases each buffer for the duration of the dispatch instead of binding it.
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

    // Buffers bound so far, each with a lease the dispatcher releases once the GPU has finished.
    private readonly List<ObjCHandle>? _leased;

    // Non-null only for a CPU dispatch: the buffer handles leased so far, released by the dispatcher.
    private readonly List<ObjCHandle>? _cpuLeases;

    // leased receives a lease on every bound buffer; the caller releases them (DangerousRelease)
    // after the command buffer completes. Without it, a buffer is leased only for the bind call.
    internal ShaderArgumentWriter(nint encoder, GraphicsDevice device, List<ObjCHandle>? leased = null)
    {
        Encoder = encoder;
        _device = device;
        _leased = leased;
    }

    internal ShaderArgumentWriter(nint encoder, GraphicsDevice device, CommandBatch batch)
    {
        Encoder = encoder;
        _device = device;
        _batch = batch;
    }

    /// <summary>A writer for a CPU dispatch: validates and leases buffers into <paramref name="leases"/>, binds nothing.</summary>
    internal ShaderArgumentWriter(GraphicsDevice device, List<ObjCHandle> leases)
    {
        _device = device;
        _cpuLeases = leases;
    }

    /// <summary><c>setBuffer:offset:0 atIndex:index</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not in 0..30.</exception>
    /// <exception cref="ArgumentException"><paramref name="buffer"/> was allocated from a different device.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="buffer"/> has been disposed.</exception>
    public void SetBuffer<T>(int index, Buffer<T> buffer)
        where T : unmanaged
    {
        GraphicsDevice device = Validate(index);

        ArgumentNullException.ThrowIfNull(buffer);
        ObjectDisposedException.ThrowIf(buffer.IsDisposed, buffer);

        if (!ReferenceEquals(buffer.Device, device))
        {
            throw new ArgumentException(
                $"The buffer bound at index {index} was allocated from a different GraphicsDevice than the one dispatching the shader.",
                nameof(buffer));
        }

        if (_cpuLeases is not null)
        {
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
            _batch.RecordBuffer(_batch.Lease(buffer.Handle, buffer), index);
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
            ObjC.SendVoid(Encoder, Selectors.SetBufferOffsetAtIndex, pointer, (nuint)0, (nuint)index);
            return;
        }

        // The lease closes the window where another thread disposes the buffer mid-call. Once bound,
        // the command buffer holds its own reference until the dispatch completes.
        using ObjCHandle.Lease handle = buffer.Handle.Acquire(buffer);

        ObjC.SendVoid(Encoder, Selectors.SetBufferOffsetAtIndex, handle.Pointer, (nuint)0, (nuint)index);
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
                _batch.RecordBuffer(temporary, index);
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

    private readonly GraphicsDevice Validate(int index)
    {
        if (_device is null || (Encoder == 0 && _batch is null && _cpuLeases is null))
        {
            throw new InvalidOperationException(
                $"A {nameof(ShaderArgumentWriter)} is only valid inside {nameof(IComputeShaderDescriptor<>)}.SetArguments during a dispatch.");
        }

        if ((uint)index >= Metal.MaxBufferBindings)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), index, $"Buffer binding indices must be in the range 0..{Metal.MaxBufferBindings - 1}.");
        }

        return _device;
    }
}
