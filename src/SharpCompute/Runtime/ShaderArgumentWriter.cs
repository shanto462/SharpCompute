using System;
using System.ComponentModel;
using SharpCompute.Interop;

namespace SharpCompute;

/// <summary>
/// Passed to <see cref="IComputeShaderDescriptor{TSelf}.SetArguments"/>; wraps the active
/// <c>id&lt;MTLComputeCommandEncoder&gt;</c>. Signatures are part of the generator↔runtime ABI.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public unsafe ref struct ShaderArgumentWriter
{
    internal readonly nint Encoder;

    private readonly GraphicsDevice? _device;

    internal ShaderArgumentWriter(nint encoder, GraphicsDevice device)
    {
        Encoder = encoder;
        _device = device;
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

        fixed (T* bytes = &value)
        {
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
        if (Encoder == 0 || _device is null)
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
