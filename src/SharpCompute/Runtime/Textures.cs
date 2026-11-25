using System;
using System.Runtime.CompilerServices;
using System.Threading;
using SharpCompute.Interop;

namespace SharpCompute;

/// <summary>
/// A 2D texture: the storage, CPU access and lifetime shared by <see cref="ReadOnlyTexture2D{T}"/> and
/// <see cref="ReadWriteTexture2D{T}"/>. <typeparamref name="T"/> is the pixel as it is stored, and
/// selects the Metal pixel format:
/// <list type="table">
/// <item><term><see cref="Rgba32"/></term><description><c>RGBA8Unorm</c></description></item>
/// <item><term><see cref="Rgba32Srgb"/></term><description><c>RGBA8Unorm_sRGB</c> (read-only)</description></item>
/// <item><term><see cref="R8"/></term><description><c>R8Unorm</c></description></item>
/// <item><term><see cref="Rg16"/></term><description><c>RG8Unorm</c> (read-only)</description></item>
/// <item><term><see cref="Half"/></term><description><c>R16Float</c></description></item>
/// <item><term><see cref="Rgba16Float"/></term><description><c>RGBA16Float</c></description></item>
/// <item><term><see cref="float"/></term><description><c>R32Float</c></description></item>
/// <item><term><see cref="Float4"/></term><description><c>RGBA32Float</c></description></item>
/// </list>
/// Whatever the format, shaders see a pixel as a <see cref="Float4"/> (<c>texture2d&lt;float&gt;</c> in
/// MSL): normalized formats read as <c>c / 255</c>, sRGB is decoded to linear, and channels the format
/// does not store read as <c>(0, 0, 1)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The texture is a linear view over a shared-storage <c>MTLBuffer</c>
/// (<c>newTextureWithDescriptor:offset:bytesPerRow:</c>), so the CPU reads the pixels the GPU wrote
/// straight out of unified memory with no blit. Rows in that buffer are padded to the device's
/// linear-texture alignment for the format; <see cref="CopyTo"/>, <see cref="CopyFrom"/> and
/// <see cref="ToArray"/> hide the padding and always use tightly packed, row-major pixels
/// (<c>Width * Height</c> elements).
/// </para>
/// <para>
/// Lifetime follows <see cref="Buffer{T}"/>: <see cref="Dispose"/> releases the GPU memory, any later
/// use throws <see cref="ObjectDisposedException"/>, and a texture that is never disposed is released
/// when its handles are finalized. A dispatch that binds a texture leases it, so disposing a texture
/// while a batched or asynchronous dispatch that uses it is pending is safe: the memory is released
/// when the GPU is done.
/// </para>
/// <para>
/// Thread safety: like buffers, a texture may be read from several threads and bound to dispatches on
/// several threads, but the CPU copy methods and indexers do not synchronize with a dispatch that
/// writes the texture; do not use them while one is in flight.
/// </para>
/// </remarks>
public abstract unsafe class Texture2D<T> : IDisposable
    where T : unmanaged
{
    private readonly ObjCHandle _buffer;
    private readonly ObjCHandle _texture;
    private readonly byte* _contents;
    private readonly nuint _bytesPerRow;
    private readonly int _width;
    private readonly int _height;

    // The texture that owns the storage: this object, or for a view (AsReadOnly) the texture it was made from.
    private readonly Texture2D<T> _root;

    // _width and _height while this object is alive, 0 once it is disposed. The indexers compare x
    // with this object's width and y with the root's height, so two comparisons reject an
    // out-of-range pixel, a disposed texture and a view whose source was disposed.
    private int _accessibleWidth;
    private int _accessibleHeight;
    private int _disposed;

    /// <summary>Allocates zero-filled storage and a texture over it.</summary>
    /// <param name="device">The device to allocate from.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="readWrite">
    /// True for a texture shaders also write (<c>MTLTextureUsageShaderWrite</c>), which needs a format
    /// the GPU can read and write in one shader.
    /// </param>
    private protected Texture2D(GraphicsDevice device, int width, int height, bool readWrite)
    {
        ArgumentNullException.ThrowIfNull(device);

        TextureFormat format = TextureFormats.Get<T>() ?? throw TextureFormats.Unsupported<T>();

        if (readWrite && !format.SupportsReadWrite)
        {
            throw new NotSupportedException(
                $"ReadWriteTexture2D<{typeof(T).Name}> is not supported: Apple GPUs cannot read and write {format.Name} in the same shader. " +
                $"Use ReadOnlyTexture2D<{typeof(T).Name}>, or one of {TextureFormats.ReadWriteTypes} for a read-write texture.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, Metal.MaxTexture2DDimension);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, Metal.MaxTexture2DDimension);

        // Each row starts on the device's linear-texture alignment, which Metal requires of bytesPerRow.
        nuint alignment = device.GetMinimumLinearTextureAlignment(format.PixelFormat);
        nuint packedRowBytes = (nuint)width * (nuint)sizeof(T);
        _bytesPerRow = (packedRowBytes + alignment - 1) / alignment * alignment;

        ulong byteLength = (ulong)_bytesPerRow * (ulong)height;

        if (byteLength > device.MaxBufferLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                $"A {width}×{height} {format.Name} texture needs {byteLength} bytes, but '{device.Name}' supports at most {device.MaxBufferLength} bytes per buffer.");
        }

        _buffer = device.NewSharedBuffer((nuint)byteLength);

        try
        {
            _contents = (byte*)ObjC.SendIntPtr(_buffer.DangerousGetHandle(), Selectors.Contents);

            if (_contents == null)
            {
                throw new InvalidOperationException("Metal returned a buffer without CPU-accessible contents.");
            }

            nuint usage = readWrite ? Metal.TextureUsageShaderReadWrite : Metal.TextureUsageShaderRead;
            _texture = NewLinearTexture(_buffer, format, width, height, _bytesPerRow, usage, this);
        }
        catch
        {
            _buffer.Dispose();
            throw;
        }

        Device = device;
        _width = width;
        _height = height;
        _accessibleWidth = width;
        _accessibleHeight = height;
        _root = this;
    }

    /// <summary>
    /// A view of <paramref name="source"/>: the same <c>MTLTexture</c> and storage, owned by
    /// <paramref name="source"/>. The view shares the source's handles, so binding it leases them.
    /// </summary>
    private protected Texture2D(Texture2D<T> source)
    {
        ObjectDisposedException.ThrowIf(source.IsDisposed, source);

        Device = source.Device;
        _buffer = source._buffer;
        _texture = source._texture;
        _contents = source._contents;
        _bytesPerRow = source._bytesPerRow;
        _width = source._width;
        _height = source._height;
        _accessibleWidth = source._width;
        _root = source._root;
    }

    /// <summary>The device this texture was allocated from. It can only be used with shaders dispatched on that device.</summary>
    public GraphicsDevice Device { get; }

    /// <summary>Width in pixels. In a shader this is <c>get_width()</c>.</summary>
    public int Width => _width;

    /// <summary>Height in pixels. In a shader this is <c>get_height()</c>.</summary>
    public int Height => _height;

    /// <summary>The underlying <c>id&lt;MTLTexture&gt;</c>. For a view, the source's handle.</summary>
    internal ObjCHandle Handle => _texture;

    /// <summary>The shared <c>id&lt;MTLBuffer&gt;</c> that stores the texture's pixels. For a view, the source's handle.</summary>
    internal ObjCHandle BufferHandle => _buffer;

    /// <summary>The CPU address of pixel (0, 0) in the backing buffer. Valid while <see cref="BufferHandle"/> is leased.</summary>
    internal byte* Contents => _contents;

    /// <summary>Distance in bytes between the starts of consecutive rows in the backing buffer.</summary>
    internal nuint BytesPerRow => _bytesPerRow;

    /// <summary>True when this texture owns nothing: it is a view made by <see cref="ReadWriteTexture2D{T}.AsReadOnly"/>.</summary>
    internal bool IsView => !ReferenceEquals(_root, this);

    /// <summary>True once the texture is disposed. A view also counts as disposed once its source is.</summary>
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0 || (IsView && _root.IsDisposed);

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

        if (_bytesPerRow == (nuint)_width * (nuint)sizeof(T))
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

    /// <summary>
    /// Releases the <c>MTLTexture</c> and its backing <c>MTLBuffer</c>. A dispatch that is already encoded
    /// or recorded keeps them alive until it completes. Disposing a view
    /// (<see cref="ReadWriteTexture2D{T}.AsReadOnly"/>) releases nothing: its source owns the texture.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _accessibleWidth, 0);

        if (IsView)
        {
            return;
        }

        Volatile.Write(ref _accessibleHeight, 0);
        _texture.Dispose();
        _buffer.Dispose();
    }

    /// <summary>The address of pixel (<paramref name="x"/>, <paramref name="y"/>), bounds- and dispose-checked.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected T* GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)_accessibleWidth || (uint)y >= (uint)_root._accessibleHeight)
        {
            ThrowPixelFailure(x, y);
        }

        return (T*)(_contents + ((nuint)y * _bytesPerRow)) + x;
    }

    /// <summary>
    /// The CPU implementation of <c>sample(sampler, uv, level(0))</c>: normalized coordinates, texel
    /// centers at <c>(i + 0.5) / size</c>, and the address mode applied to each texel index.
    /// </summary>
    private protected Float4 SampleCore(Sampler sampler, Float2 uv)
    {
        int width = _accessibleWidth;
        int height = _root._accessibleHeight;

        if (width == 0 || height == 0)
        {
            ThrowIfDisposed();
        }

        TextureAddressMode mode = sampler.AddressMode;

        // Float-to-int conversions saturate (and NaN converts to 0), as they do in MSL.
        if (sampler.Filter == TextureFilter.Point)
        {
            int x = Address((int)MathF.Floor(uv.X * width), width, mode);
            int y = Address((int)MathF.Floor(uv.Y * height), height, mode);
            return Load(x, y);
        }

        float fx = (uv.X * width) - 0.5f;
        float fy = (uv.Y * height) - 0.5f;
        float floorX = MathF.Floor(fx);
        float floorY = MathF.Floor(fy);
        float weightX = fx - floorX;
        float weightY = fy - floorY;

        // 64-bit so that the second tap of a saturated index cannot overflow.
        long left = (int)floorX;
        long top = (int)floorY;
        int x0 = Address(left, width, mode);
        int x1 = Address(left + 1, width, mode);
        int y0 = Address(top, height, mode);
        int y1 = Address(top + 1, height, mode);

        Float4 upper = Lerp(Load(x0, y0), Load(x1, y0), weightX);
        Float4 lower = Lerp(Load(x0, y1), Load(x1, y1), weightX);
        return Lerp(upper, lower, weightY);
    }

    /// <summary>Maps a texel index outside the texture back into it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int Address(long index, int size, TextureAddressMode mode)
    {
        switch (mode)
        {
            case TextureAddressMode.Repeat:
            {
                long wrapped = index % size;
                return (int)(wrapped < 0 ? wrapped + size : wrapped);
            }

            case TextureAddressMode.Mirror:
            {
                // mirrored_repeat: the texture followed by its mirror image, tiled with period 2 * size.
                long period = 2L * size;
                long wrapped = index % period;

                if (wrapped < 0)
                {
                    wrapped += period;
                }

                return (int)(wrapped < size ? wrapped : period - 1 - wrapped);
            }

            default:
                return (int)Math.Clamp(index, 0, size - 1);
        }
    }

    // a + (b - a) * t rather than a * (1 - t) + b * t: equal texels then interpolate to exactly that value.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Float4 Lerp(Float4 a, Float4 b, float t) =>
        new(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t), a.Z + ((b.Z - a.Z) * t), a.W + ((b.W - a.W) * t));

    /// <summary>Reads a pixel whose coordinates are known to be in range (a sampler tap).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Float4 Load(int x, int y) => TextureFormats.Load((T*)(_contents + ((nuint)y * _bytesPerRow)) + x);

    private protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    private void ThrowPixelFailure(int x, int y)
    {
        ThrowIfDisposed();

        if ((uint)x >= (uint)_width)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, $"x must be non-negative and less than the texture width ({_width}).");
        }

        throw new ArgumentOutOfRangeException(nameof(y), y, $"y must be non-negative and less than the texture height ({_height}).");
    }

    private static ObjCHandle NewLinearTexture(ObjCHandle buffer, TextureFormat format, int width, int height, nuint bytesPerRow, nuint usage, object owner)
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
            ObjC.SendVoid(descriptor, Selectors.SetPixelFormat, format.PixelFormat);
            ObjC.SendVoid(descriptor, Selectors.SetWidth, (nuint)width);
            ObjC.SendVoid(descriptor, Selectors.SetHeight, (nuint)height);
            ObjC.SendVoid(descriptor, Selectors.SetUsage, usage);
            ObjC.SendVoid(descriptor, Selectors.SetStorageMode, Metal.StorageModeShared);

            using ObjCHandle.Lease lease = buffer.Acquire(owner);

            nint texture = ObjC.SendIntPtr(lease.Pointer, Selectors.NewTextureWithDescriptorOffsetBytesPerRow, descriptor, (nuint)0, bytesPerRow);

            if (texture == 0)
            {
                throw new InvalidOperationException($"Metal failed to create a {width}×{height} {format.Name} texture over a shared buffer.");
            }

            return new ObjCHandle(texture);
        }
        finally
        {
            ObjC.Release(descriptor);
        }
    }
}

/// <summary>
/// A texture shaders read and sample (<c>texture2d&lt;float, access::sample&gt;</c> in MSL). Every
/// element type of <see cref="Texture2D{T}"/> is supported.
/// </summary>
/// <remarks>
/// Fill it from the CPU with <see cref="Texture2D{T}.CopyFrom"/> (or allocate it from pixels), or get one
/// over a texture a shader wrote with <see cref="ReadWriteTexture2D{T}.AsReadOnly"/>. The indexers and
/// <see cref="Sample"/> are for shader code; their C# bodies are what runs when the shader is
/// dispatched on the CPU (<c>ForCpu</c>), and they can also be called from ordinary code.
/// </remarks>
public sealed unsafe class ReadOnlyTexture2D<T> : Texture2D<T>
    where T : unmanaged
{
    internal ReadOnlyTexture2D(GraphicsDevice device, int width, int height)
        : base(device, width, height, readWrite: false)
    {
    }

    /// <summary>A view of <paramref name="source"/>; see <see cref="ReadWriteTexture2D{T}.AsReadOnly"/>.</summary>
    internal ReadOnlyTexture2D(ReadWriteTexture2D<T> source)
        : base(source)
    {
    }

    /// <summary>
    /// The pixel at (<paramref name="x"/>, <paramref name="y"/>) as a shader sees it (<c>read</c> in MSL).
    /// On the CPU it is bounds-checked; in a shader it is not, and reading outside the texture is undefined.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The pixel is outside the texture (CPU only).</exception>
    /// <exception cref="ObjectDisposedException">The texture has been disposed (CPU only).</exception>
    public Float4 this[int x, int y] => TextureFormats.Load(GetPixel(x, y));

    /// <inheritdoc cref="this[int, int]"/>
    public Float4 this[Int2 xy] => TextureFormats.Load(GetPixel(xy.X, xy.Y));

    /// <summary>
    /// Samples the texture at the normalized coordinates <paramref name="uv"/> (<c>sample(sampler, uv,
    /// level(0))</c> in MSL): <c>(0, 0)</c> is the top-left corner of the texture, <c>(1, 1)</c> the
    /// bottom-right one, and the center of pixel <c>(x, y)</c> is at <c>((x + 0.5) / Width, (y + 0.5) / Height)</c>.
    /// </summary>
    /// <param name="sampler">
    /// In a shader, one of the static properties of <see cref="Sampler"/> written directly as the
    /// argument (<c>Sampler.LinearClamp</c>): the sampler is compiled into the shader.
    /// </param>
    /// <param name="uv">Normalized coordinates; outside <c>[0, 1]</c> the sampler's address mode applies.</param>
    /// <remarks>
    /// <para>
    /// How closely the CPU (<c>ForCpu</c>) follows the GPU, as measured on an Apple M4 Pro:
    /// </para>
    /// <para>
    /// <b>Point filtering</b> picks the same pixel with <see cref="TextureAddressMode.Clamp"/>, for
    /// coordinates in <c>[0, 1)</c> with any address mode, and along an axis whose size is a power of
    /// two. With <see cref="TextureAddressMode.Repeat"/> or <see cref="TextureAddressMode.Mirror"/>
    /// outside <c>[0, 1)</c> on other sizes, a coordinate within float rounding (about 2e-7) of the
    /// boundary between two pixels can pick the neighbouring pixel on the GPU. The CPU uses
    /// <c>floor(u * size)</c> in single precision; the GPU's exact arithmetic there is not known.
    /// </para>
    /// <para>
    /// <b>Linear filtering</b> is close but not equal: the CPU interpolates with full float precision,
    /// while the GPU rounds the two interpolation weights to 1/256 (so each is off by up to 1/512 of
    /// the difference between neighbouring pixels) and, for a half-float texture, rounds the result to
    /// a half. In practice a bilinear sample that is written to an 8-bit format can come out one step
    /// (1/255) different on the CPU and the GPU.
    /// </para>
    /// <para>
    /// Coordinates that are not finite give an unspecified pixel of the texture.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The texture has been disposed (CPU only).</exception>
    public Float4 Sample(Sampler sampler, Float2 uv) => SampleCore(sampler, uv);
}

/// <summary>
/// A texture shaders read and write (<c>texture2d&lt;float, access::read_write&gt;</c> in MSL), and the
/// output of pixel shaders (<c>ForEach</c>, with <typeparamref name="T"/> = <see cref="Rgba32"/>).
/// </summary>
/// <remarks>
/// <para>
/// Supported for the element types an Apple GPU can read and write in one shader: <see cref="Rgba32"/>,
/// <see cref="R8"/>, <see cref="Half"/>, <see cref="Rgba16Float"/>, <see cref="float"/> and
/// <see cref="Float4"/> (not <see cref="Rg16"/> or <see cref="Rgba32Srgb"/>).
/// </para>
/// <para>
/// A read-write texture cannot be sampled (Metal has no sampling of <c>access::read_write</c>
/// textures). To sample what one pass wrote, bind <see cref="AsReadOnly"/> in the next pass. As with
/// buffers, threads of one dispatch must not write a pixel another thread reads or writes.
/// </para>
/// </remarks>
public sealed unsafe class ReadWriteTexture2D<T> : Texture2D<T>
    where T : unmanaged
{
    internal ReadWriteTexture2D(GraphicsDevice device, int width, int height)
        : base(device, width, height, readWrite: true)
    {
    }

    /// <summary>
    /// The pixel at (<paramref name="x"/>, <paramref name="y"/>): reading gives the pixel as a shader
    /// sees it (<c>read</c> in MSL), writing stores a color with the format's conversion (<c>write</c>):
    /// normalized formats clamp to [0, 1] and round, half-float formats round toward zero (what an
    /// Apple M4 Pro was measured to do), and channels the format does not store are dropped.
    /// On the CPU it is bounds-checked; in a shader it is not, and an access outside the texture is undefined.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The pixel is outside the texture (CPU only).</exception>
    /// <exception cref="ObjectDisposedException">The texture has been disposed (CPU only).</exception>
    public Float4 this[int x, int y]
    {
        get => TextureFormats.Load(GetPixel(x, y));
        set => TextureFormats.Store(GetPixel(x, y), value);
    }

    /// <inheritdoc cref="this[int, int]"/>
    public Float4 this[Int2 xy]
    {
        get => TextureFormats.Load(GetPixel(xy.X, xy.Y));
        set => TextureFormats.Store(GetPixel(xy.X, xy.Y), value);
    }

    /// <summary>
    /// A <see cref="ReadOnlyTexture2D{T}"/> over the same pixels, so that a texture one dispatch wrote
    /// can be sampled by the next one. Nothing is copied: the view is the same <c>MTLTexture</c>.
    /// </summary>
    /// <remarks>
    /// The view owns nothing. Disposing it releases nothing, it counts as disposed once this texture
    /// is, and a dispatch that binds it leases this texture's memory. Do not bind the view and this
    /// texture to the same dispatch when that dispatch writes the texture.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The texture has been disposed.</exception>
    public ReadOnlyTexture2D<T> AsReadOnly() => new(this);
}

file static class TextureDescriptorClass
{
    // Looked up lazily: the class only exists once Metal.framework is loaded, which creating a device guarantees.
    internal static readonly nint Value = ObjC.GetClass("MTLTextureDescriptor");
}
