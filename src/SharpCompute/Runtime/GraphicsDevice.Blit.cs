using System;

namespace SharpCompute;

// GPU copies and fills (MTLBlitCommandEncoder). The synchronous methods here commit one command
// buffer each and wait for it; ComputeContext records the same operations in order with its dispatches.
public sealed unsafe partial class GraphicsDevice
{
    /// <summary>
    /// Copies the whole of <paramref name="source"/> to the start of <paramref name="destination"/> on
    /// the GPU and waits for completion.
    /// </summary>
    /// <inheritdoc cref="Copy{T}(Buffer{T}, int, Buffer{T}, int, int)" path="/remarks"/>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is shorter than <paramref name="source"/>, a buffer belongs to a
    /// different device, or the two ranges overlap (they are the same buffer or overlapping views of one).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The device or a buffer has been disposed.</exception>
    /// <exception cref="NotSupportedException"><paramref name="destination"/> maps read-only memory.</exception>
    /// <exception cref="MetalException">The GPU failed to execute the copy.</exception>
    public void Copy<T>(Buffer<T> source, Buffer<T> destination)
        where T : unmanaged
    {
        if (ValidateCopy(source, destination))
        {
            RunBlit(source, 0, destination, 0, source.Length);
        }
    }

    /// <summary>
    /// Copies <paramref name="count"/> elements of <paramref name="source"/> from
    /// <paramref name="sourceOffset"/> to <paramref name="destination"/> at
    /// <paramref name="destinationOffset"/> on the GPU and waits for completion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The copy is a blit (<c>copyFromBuffer:sourceOffset:toBuffer:destinationOffset:size:</c>) in its
    /// own command buffer, ordered with everything else committed to the device: it sees the writes of
    /// work submitted before it, including asynchronous dispatches that are still running. Offsets and
    /// <paramref name="count"/> are in elements, relative to each buffer (so relative to a view's
    /// start); either buffer may be a view, read-only or read-write.
    /// </para>
    /// <para>
    /// Every buffer is unified memory, so <see cref="Buffer{T}.CopyTo"/> or a span copy does the same
    /// job on the CPU and is faster when the data is not waiting on the GPU: this call pays a command
    /// buffer round trip. Use the GPU copy when the source is still being produced by GPU work, and
    /// above all inside a <see cref="ComputeContext"/>, where it runs between dispatches without
    /// bringing the CPU into it.
    /// </para>
    /// <para>
    /// The two ranges must not overlap. They may be disjoint ranges of one buffer.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">An offset or <paramref name="count"/> is negative, or a range is not inside its buffer.</exception>
    /// <exception cref="ArgumentException">A buffer belongs to a different device, or the two ranges overlap.</exception>
    /// <exception cref="ObjectDisposedException">The device or a buffer has been disposed.</exception>
    /// <exception cref="NotSupportedException"><paramref name="destination"/> maps read-only memory.</exception>
    /// <exception cref="MetalException">The GPU failed to execute the copy.</exception>
    public void Copy<T>(Buffer<T> source, int sourceOffset, Buffer<T> destination, int destinationOffset, int count)
        where T : unmanaged
    {
        if (ValidateCopy(source, sourceOffset, destination, destinationOffset, count))
        {
            RunBlit(source, sourceOffset, destination, destinationOffset, count);
        }
    }

    /// <summary>
    /// Copies every pixel of <paramref name="source"/> to <paramref name="destination"/>, a texture of
    /// the same size, on the GPU and waits for completion.
    /// </summary>
    /// <remarks>
    /// A blit (<c>copyFromTexture:toTexture:</c>), ordered with everything else committed to the device.
    /// Either texture may be read-only or read-write, or a view made by
    /// <see cref="ReadWriteTexture2D{T}.AsReadOnly"/>; the pixels are copied as stored, with no conversion.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The textures differ in size, belong to a different device, or are the same texture (one is a view of the other).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The device or a texture has been disposed.</exception>
    /// <exception cref="MetalException">The GPU failed to execute the copy.</exception>
    public void Copy<T>(Texture2D<T> source, Texture2D<T> destination)
        where T : unmanaged
    {
        ValidateCopy(source, destination);

        CommandBatch batch = new(this, commandCapacity: 1);

        try
        {
            batch.RecordCopy(source, destination);
        }
        catch
        {
            batch.Abandon();
            throw;
        }

        batch.Commit();
    }

    /// <summary>
    /// Copies the first <c>Width × Height</c> elements of <paramref name="source"/>, read as tightly
    /// packed, row-major pixels, into <paramref name="destination"/> on the GPU and waits for completion.
    /// </summary>
    /// <remarks>
    /// A blit (<c>copyFromBuffer:...toTexture:...</c>), ordered with everything else committed to the
    /// device; it is the GPU counterpart of <see cref="Texture2D{T}.CopyFrom"/>, for pixels a shader
    /// produced in a buffer. The buffer may be a view. The pixels are copied as stored, with no conversion.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="source"/> holds fewer than <c>Width × Height</c> elements, or a resource belongs to a different device.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The device, the buffer or the texture has been disposed.</exception>
    /// <exception cref="MetalException">The GPU failed to execute the copy.</exception>
    public void Copy<T>(Buffer<T> source, Texture2D<T> destination)
        where T : unmanaged
    {
        ValidateCopy(source, destination);

        CommandBatch batch = new(this, commandCapacity: 1);

        try
        {
            batch.RecordCopy(source.Handle, source, source.ByteOffset, destination);
        }
        catch
        {
            batch.Abandon();
            throw;
        }

        batch.Commit();
    }

    /// <summary>
    /// Copies every pixel of <paramref name="source"/> to the start of <paramref name="destination"/>
    /// as tightly packed, row-major pixels (<c>Width × Height</c> elements) on the GPU and waits for
    /// completion.
    /// </summary>
    /// <remarks>
    /// A blit (<c>copyFromTexture:...toBuffer:...</c>), ordered with everything else committed to the
    /// device; it is the GPU counterpart of <see cref="Texture2D{T}.CopyTo"/>, for shaders that go on to
    /// read the pixels from a buffer. The buffer may be a view. The pixels are copied as stored, with
    /// no conversion.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> holds fewer than <c>Width × Height</c> elements, or a resource belongs to a different device.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The device, the texture or the buffer has been disposed.</exception>
    /// <exception cref="NotSupportedException"><paramref name="destination"/> maps read-only memory.</exception>
    /// <exception cref="MetalException">The GPU failed to execute the copy.</exception>
    public void Copy<T>(Texture2D<T> source, Buffer<T> destination)
        where T : unmanaged
    {
        ValidateCopy(source, destination);

        CommandBatch batch = new(this, commandCapacity: 1);

        try
        {
            batch.RecordCopy(source, destination.Handle, destination, destination.ByteOffset);
        }
        catch
        {
            batch.Abandon();
            throw;
        }

        batch.Commit();
    }

    /// <summary>
    /// Sets every <b>byte</b> of <paramref name="buffer"/> to <paramref name="value"/> on the GPU
    /// (<c>fillBuffer:range:value:</c>) and waits for completion.
    /// </summary>
    /// <remarks>
    /// The value is a byte pattern, not an element: <c>Fill(buffer, 0xFF)</c> makes every <c>int</c> -1
    /// and every <c>float</c> a NaN, and only 0 gives the same element for every type (see
    /// <see cref="Clear{T}"/>). A view is filled over its own range only. Like <c>Copy</c>, the fill is
    /// ordered with everything else committed to the device, and on the CPU
    /// <c>buffer.AsSpan().Fill(...)</c> is faster when nothing on the GPU is pending.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="buffer"/> belongs to a different device.</exception>
    /// <exception cref="ObjectDisposedException">The device or the buffer has been disposed.</exception>
    /// <exception cref="NotSupportedException"><paramref name="buffer"/> maps read-only memory.</exception>
    /// <exception cref="MetalException">The GPU failed to execute the fill.</exception>
    public void Fill<T>(Buffer<T> buffer, byte value)
        where T : unmanaged
    {
        if (!ValidateFill(buffer))
        {
            return;
        }

        CommandBatch batch = new(this, commandCapacity: 1);

        try
        {
            RecordFill(batch, buffer, value);
        }
        catch
        {
            batch.Abandon();
            throw;
        }

        batch.Commit();
    }

    /// <summary>Zero-fills <paramref name="buffer"/> on the GPU and waits for completion: <c>Fill(buffer, 0)</c>.</summary>
    /// <inheritdoc cref="Fill{T}" path="/exception"/>
    public void Clear<T>(Buffer<T> buffer)
        where T : unmanaged => Fill(buffer, 0);

    /// <summary>The preconditions of a whole-buffer copy; false when there is nothing to copy.</summary>
    internal bool ValidateCopy<T>(Buffer<T> source, Buffer<T> destination)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        // Checked before the range so that a destination that is too short is reported as such.
        if (destination.Length < source.Length)
        {
            ValidateResource(source, nameof(source));
            ValidateResource(destination, nameof(destination));

            throw new ArgumentException(
                $"The destination holds {destination.Length} elements but the source has {source.Length}.", nameof(destination));
        }

        return ValidateCopy(source, 0, destination, 0, source.Length);
    }

    /// <summary>The preconditions of a buffer copy, in elements; false when <paramref name="count"/> is 0 and there is nothing to copy.</summary>
    internal bool ValidateCopy<T>(Buffer<T> source, int sourceOffset, Buffer<T> destination, int destinationOffset, int count)
        where T : unmanaged
    {
        ValidateResource(source, nameof(source));
        ValidateResource(destination, nameof(destination));
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(destinationOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count > source.Length - sourceOffset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), count, $"The source has {source.Length} elements, so at most {Math.Max(0, source.Length - sourceOffset)} can be copied from offset {sourceOffset}.");
        }

        if (count > destination.Length - destinationOffset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), count, $"The destination has {destination.Length} elements, so at most {Math.Max(0, destination.Length - destinationOffset)} fit at offset {destinationOffset}.");
        }

        ThrowIfReadOnlyMemory(destination);

        if (count == 0)
        {
            return false;
        }

        // Views share their source's MTLBuffer, so positions are compared in that buffer, in elements
        // (a 64-bit sum: two views of a large buffer can each start past 2^31 bytes). The MTLBuffer is
        // compared rather than the wrapper: two wrappers of one native buffer share memory too, and
        // both start at its first byte.
        if (source.Root.Handle.DangerousGetHandle() == destination.Root.Handle.DangerousGetHandle())
        {
            long from = source.StartInRoot + (long)sourceOffset;
            long to = destination.StartInRoot + (long)destinationOffset;

            if (from < to + count && to < from + count)
            {
                throw new ArgumentException(
                    "The source and destination ranges overlap in the same buffer; a GPU copy needs disjoint ranges. Copy through a second buffer, or on the CPU with a span.",
                    nameof(destination));
            }
        }

        return true;
    }

    /// <summary>The preconditions of a texture-to-texture copy.</summary>
    internal void ValidateCopy<T>(Texture2D<T> source, Texture2D<T> destination)
        where T : unmanaged
    {
        ValidateResource(source, nameof(source));
        ValidateResource(destination, nameof(destination));

        if (source.Width != destination.Width || source.Height != destination.Height)
        {
            throw new ArgumentException(
                $"The textures differ in size: the source is {source.Width}×{source.Height} and the destination {destination.Width}×{destination.Height}.", nameof(destination));
        }

        // A view (AsReadOnly) is its source's MTLTexture, and two wrappers of one native texture are one MTLTexture too.
        if (source.Handle.DangerousGetHandle() == destination.Handle.DangerousGetHandle())
        {
            throw new ArgumentException("The source and the destination are the same texture.", nameof(destination));
        }
    }

    /// <summary>The preconditions of a buffer-to-texture copy.</summary>
    internal void ValidateCopy<T>(Buffer<T> source, Texture2D<T> destination)
        where T : unmanaged
    {
        ValidateResource(source, nameof(source));
        ValidateResource(destination, nameof(destination));

        if (source.Length < destination.Width * destination.Height)
        {
            throw new ArgumentException(
                $"The source holds {source.Length} elements but the {destination.Width}×{destination.Height} texture has {destination.Width * destination.Height} pixels.", nameof(source));
        }
    }

    /// <summary>The preconditions of a texture-to-buffer copy.</summary>
    internal void ValidateCopy<T>(Texture2D<T> source, Buffer<T> destination)
        where T : unmanaged
    {
        ValidateResource(source, nameof(source));
        ValidateResource(destination, nameof(destination));

        if (destination.Length < source.Width * source.Height)
        {
            throw new ArgumentException(
                $"The destination holds {destination.Length} elements but the {source.Width}×{source.Height} texture has {source.Width * source.Height} pixels.", nameof(destination));
        }

        ThrowIfReadOnlyMemory(destination);
    }

    /// <summary>The preconditions of a fill; false when the buffer is empty and there is nothing to fill.</summary>
    internal bool ValidateFill<T>(Buffer<T> buffer)
        where T : unmanaged
    {
        ValidateResource(buffer, nameof(buffer));
        ThrowIfReadOnlyMemory(buffer);

        return buffer.Length != 0;
    }

    /// <summary>Records a validated buffer copy; offsets and <paramref name="count"/> are in elements.</summary>
    internal static void RecordCopy<T>(CommandBatch batch, Buffer<T> source, int sourceOffset, Buffer<T> destination, int destinationOffset, int count)
        where T : unmanaged =>
        batch.RecordCopy(
            source.Handle,
            source,
            source.ByteOffset + ((nuint)sourceOffset * (nuint)sizeof(T)),
            destination.Handle,
            destination,
            destination.ByteOffset + ((nuint)destinationOffset * (nuint)sizeof(T)),
            (nuint)count * (nuint)sizeof(T));

    /// <summary>Records a validated fill of the whole of a non-empty buffer (a view: of its own range).</summary>
    internal static void RecordFill<T>(CommandBatch batch, Buffer<T> buffer, byte value)
        where T : unmanaged =>
        batch.RecordFill(buffer.Handle, buffer, buffer.ByteOffset, (nuint)buffer.Length * (nuint)sizeof(T), value);

    /// <summary>A live buffer of this device, or the exception a dispatch would throw for it.</summary>
    internal void ValidateResource<T>(Buffer<T> buffer, string parameterName)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(buffer, parameterName);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ObjectDisposedException.ThrowIf(buffer.IsDisposed, buffer);

        if (!IsSameDevice(buffer.Device))
        {
            throw new ArgumentException("The buffer was allocated from a different GraphicsDevice than the one it is used with.", parameterName);
        }
    }

    /// <summary>A live texture of this device, or the exception a dispatch would throw for it.</summary>
    internal void ValidateResource<T>(Texture2D<T> texture, string parameterName)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(texture, parameterName);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);

        if (!IsSameDevice(texture.Device))
        {
            throw new ArgumentException("The texture was allocated from a different GraphicsDevice than the one it is used with.", parameterName);
        }
    }

    /// <summary>
    /// A buffer over memory this process cannot write (a memory-mapped file) cannot be written by the
    /// GPU either: the pages are mapped read-only, and a blit into them would fault or be dropped.
    /// </summary>
    private static void ThrowIfReadOnlyMemory<T>(Buffer<T> destination)
        where T : unmanaged
    {
        if (!destination.IsMemoryWritable)
        {
            throw new NotSupportedException("The destination buffer maps read-only memory (a memory-mapped file) and cannot be written.");
        }
    }

    private void RunBlit<T>(Buffer<T> source, int sourceOffset, Buffer<T> destination, int destinationOffset, int count)
        where T : unmanaged
    {
        CommandBatch batch = new(this, commandCapacity: 1);

        try
        {
            RecordCopy(batch, source, sourceOffset, destination, destinationOffset, count);
        }
        catch
        {
            batch.Abandon();
            throw;
        }

        batch.Commit();
    }
}
