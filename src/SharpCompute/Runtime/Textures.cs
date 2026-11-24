using System;
using System.Threading;
using SharpCompute.Interop;

namespace SharpCompute;

/// <summary>
/// A 2D texture the GPU can write (<c>texture2d&lt;float, access::write&gt;</c> in MSL). v1 supports
/// <typeparamref name="T"/> = <see cref="Rgba32"/> (<c>RGBA8Unorm</c>); shaders write normalized
/// <see cref="Float4"/> colors.
/// </summary>
/// <remarks>
/// <para>
/// The texture is a linear view over a shared-storage <c>MTLBuffer</c>
/// (<c>newTextureWithDescriptor:offset:bytesPerRow:</c>), so the CPU reads the pixels the GPU wrote
/// straight out of unified memory with no blit. Rows in that buffer are padded to the device's
/// linear-texture alignment; <see cref="CopyTo"/>, <see cref="CopyFrom"/> and <see cref="ToArray"/>
/// hide the padding and always use tightly packed, row-major pixels (<c>Width * Height</c> elements).
/// </para>
/// <para>
/// Lifetime follows <see cref="Buffer{T}"/>: <see cref="Dispose"/> releases the GPU memory, any later
/// use throws <see cref="ObjectDisposedException"/>, and a texture that is never disposed is released
/// when its handles are finalized.
/// </para>
/// </remarks>
public sealed unsafe class ReadWriteTexture2D<T> : IDisposable
    where T : unmanaged
{
    private readonly ObjCHandle _buffer;
    private readonly ObjCHandle _texture;
    private readonly byte* _contents;
    private readonly nuint _bytesPerRow;
    private readonly int _width;
    private readonly int _height;
    private int _disposed;

    internal ReadWriteTexture2D(GraphicsDevice device, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (typeof(T) != typeof(Rgba32))
        {
            throw new NotSupportedException(
                $"ReadWriteTexture2D<{typeof(T).Name}> is not supported; v1 textures must use {nameof(Rgba32)} (RGBA8Unorm).");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, Metal.MaxTexture2DDimension);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, Metal.MaxTexture2DDimension);

        // Each row starts on the device's linear-texture alignment, which Metal requires of bytesPerRow.
        nuint alignment = device.GetMinimumLinearTextureAlignment(Metal.PixelFormatRGBA8Unorm);
        nuint packedRowBytes = (nuint)width * (nuint)sizeof(T);
        _bytesPerRow = (packedRowBytes + alignment - 1) / alignment * alignment;

        ulong byteLength = (ulong)_bytesPerRow * (ulong)height;

        if (byteLength > device.MaxBufferLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"A {width}×{height} texture needs {byteLength} bytes, but '{device.Name}' supports at most {device.MaxBufferLength} bytes per buffer.");
        }

        _buffer = device.NewSharedBuffer((nuint)byteLength);

        try
        {
            _contents = (byte*)ObjC.SendIntPtr(_buffer.DangerousGetHandle(), Selectors.Contents);

            if (_contents == null)
            {
                throw new InvalidOperationException("Metal returned a buffer without CPU-accessible contents.");
            }

            _texture = NewLinearTexture(_buffer, width, height, _bytesPerRow, this);
        }
        catch
        {
            _buffer.Dispose();
            throw;
        }

        Device = device;
        _width = width;
        _height = height;
    }

    /// <summary>The device this texture was allocated from. It can only be used with shaders dispatched on that device.</summary>
    public GraphicsDevice Device { get; }

    /// <summary>Width in pixels.</summary>
    public int Width => _width;

    /// <summary>Height in pixels.</summary>
    public int Height => _height;

    /// <summary>The underlying <c>id&lt;MTLTexture&gt;</c>.</summary>
    internal ObjCHandle Handle => _texture;

    /// <summary>The shared <c>id&lt;MTLBuffer&gt;</c> that stores the texture's pixels.</summary>
    internal ObjCHandle BufferHandle => _buffer;

    /// <summary>The CPU address of pixel (0, 0) in the backing buffer. Valid while <see cref="BufferHandle"/> is leased.</summary>
    internal byte* Contents => _contents;

    /// <summary>Distance in bytes between the starts of consecutive rows in the backing buffer.</summary>
    internal nuint BytesPerRow => _bytesPerRow;

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Copies all pixels, row-major and tightly packed (<c>Width * Height</c> elements).</summary>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <c>Width * Height</c>.</exception>
    /// <exception cref="ObjectDisposedException">The texture has been disposed.</exception>
    public void CopyTo(Span<T> destination)
    {
        ThrowIfDisposed();

        int pixelCount = _width * _height;

        if (destination.Length < pixelCount)
        {
            throw new ArgumentException(
                $"The destination holds {destination.Length} pixels but the {_width}×{_height} texture has {pixelCount}.", nameof(destination));
        }

        // The lease keeps the MTLBuffer alive even if another thread disposes the texture mid-copy.
        using ObjCHandle.Lease lease = _buffer.Acquire(this);

        if (_bytesPerRow == (nuint)(_width * sizeof(T)))
        {
            new ReadOnlySpan<T>(_contents, pixelCount).CopyTo(destination);
            return;
        }

        for (int y = 0; y < _height; y++)
        {
            new ReadOnlySpan<T>(_contents + (nuint)y * _bytesPerRow, _width).CopyTo(destination.Slice(y * _width, _width));
        }
    }

    /// <summary>
    /// Copies <paramref name="source"/> (row-major, tightly packed) into the texture, starting at
    /// pixel (0, 0). It may be shorter than <c>Width * Height</c> but not longer; the remaining pixels keep their values.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="source"/> is longer than <c>Width * Height</c>.</exception>
    /// <exception cref="ObjectDisposedException">The texture has been disposed.</exception>
    public void CopyFrom(ReadOnlySpan<T> source)
    {
        ThrowIfDisposed();

        int pixelCount = _width * _height;

        if (source.Length > pixelCount)
        {
            throw new ArgumentException(
                $"The source holds {source.Length} pixels but the {_width}×{_height} texture only has room for {pixelCount}.", nameof(source));
        }

        using ObjCHandle.Lease lease = _buffer.Acquire(this);

        for (int y = 0, start = 0; start < source.Length; y++, start += _width)
        {
            ReadOnlySpan<T> row = source.Slice(start, Math.Min(_width, source.Length - start));
            row.CopyTo(new Span<T>(_contents + (nuint)y * _bytesPerRow, _width));
        }
    }

    /// <summary>Copies all pixels into a new array (row-major, <c>Width * Height</c> elements).</summary>
    /// <exception cref="ObjectDisposedException">The texture has been disposed.</exception>
    public T[] ToArray()
    {
        ThrowIfDisposed();

        T[] result = GC.AllocateUninitializedArray<T>(_width * _height);
        CopyTo(result);
        return result;
    }

    /// <summary>Releases the <c>MTLTexture</c> and its backing <c>MTLBuffer</c>. A dispatch that is already encoded keeps them alive until it completes.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _texture.Dispose();
        _buffer.Dispose();
    }

    private static ObjCHandle NewLinearTexture(ObjCHandle buffer, int width, int height, nuint bytesPerRow, object owner)
    {
        using AutoreleasePool pool = AutoreleasePool.Push();

        nint descriptor = ObjC.SendIntPtr(ObjC.SendIntPtr(TextureDescriptorClass.Value, Selectors.Alloc), Selectors.Init);

        if (descriptor == 0)
        {
            throw new InvalidOperationException("Metal failed to create a texture descriptor.");
        }

        try
        {
            // storageMode (and the default cpuCacheMode / hazard tracking) must match the buffer's
            // resource options, which NewSharedBuffer sets to MTLResourceStorageModeShared.
            ObjC.SendVoid(descriptor, Selectors.SetTextureType, Metal.TextureType2D);
            ObjC.SendVoid(descriptor, Selectors.SetPixelFormat, Metal.PixelFormatRGBA8Unorm);
            ObjC.SendVoid(descriptor, Selectors.SetWidth, (nuint)width);
            ObjC.SendVoid(descriptor, Selectors.SetHeight, (nuint)height);
            ObjC.SendVoid(descriptor, Selectors.SetUsage, Metal.TextureUsageShaderReadWrite);
            ObjC.SendVoid(descriptor, Selectors.SetStorageMode, Metal.StorageModeShared);

            using ObjCHandle.Lease lease = buffer.Acquire(owner);

            nint texture = ObjC.SendIntPtr(lease.Pointer, Selectors.NewTextureWithDescriptorOffsetBytesPerRow, descriptor, (nuint)0, bytesPerRow);

            if (texture == 0)
            {
                throw new InvalidOperationException($"Metal failed to create a {width}×{height} RGBA8Unorm texture over a shared buffer.");
            }

            return new ObjCHandle(texture);
        }
        finally
        {
            ObjC.Release(descriptor);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);
}

file static class TextureDescriptorClass
{
    // Looked up lazily: the class only exists once Metal.framework is loaded, which creating a device guarantees.
    internal static readonly nint Value = ObjC.GetClass("MTLTextureDescriptor");
}
