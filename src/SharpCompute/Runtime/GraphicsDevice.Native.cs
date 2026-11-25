using System;
using SharpCompute.Interop;

namespace SharpCompute;

// Interop with other Metal code: devices, buffers and textures created outside SharpCompute, and the
// native handles of SharpCompute's own objects. Encoding into a caller's command buffer is
// ComputeContext.EncodeInto.
public sealed unsafe partial class GraphicsDevice
{
    /// <summary>
    /// Wraps an existing <c>id&lt;MTLDevice&gt;</c>, and optionally the caller's
    /// <c>id&lt;MTLCommandQueue&gt;</c>, so that SharpCompute dispatches on the device an app already
    /// renders with, in order with the app's own work.
    /// </summary>
    /// <param name="mtlDevice">The <c>id&lt;MTLDevice&gt;</c>. It is retained, and released by <see cref="Dispose"/>.</param>
    /// <param name="mtlCommandQueue">
    /// A queue of <paramref name="mtlDevice"/> for every command buffer SharpCompute commits (dispatches,
    /// copies, <see cref="Presentation.MetalWindow.Present"/>), so they run in submission order with
    /// what the app commits to the same queue. Retained and released like the device. 0 (the default)
    /// creates a queue of SharpCompute's own.
    /// </param>
    /// <remarks>
    /// <para>
    /// The result is a new, disposable <see cref="GraphicsDevice"/> with its own pipeline cache.
    /// Disposing it releases its references to the device and the queue (not the app's) and its
    /// pipelines; buffers and textures made from it stay usable, as for any device.
    /// </para>
    /// <para>
    /// <b>Same device.</b> Resources belong to the <c>MTLDevice</c>, not to the wrapper: every check
    /// that a buffer or texture "belongs to this device" compares the underlying <c>id</c>.
    /// <c>MTLCreateSystemDefaultDevice</c> returns the same object every time (measured on macOS 27 on
    /// an Apple M4 Pro, which has one GPU), so when the app renders on the system default device,
    /// <see cref="GetDefault"/> is that same device: its buffers and textures can be used with the
    /// wrapper made here and the other way round, and the app may simply use <see cref="GetDefault"/>.
    /// Work committed through two wrappers goes to two queues, though, which Metal does not order
    /// with each other; pass the app's queue here when SharpCompute's work must be ordered with it.
    /// </para>
    /// </remarks>
    /// <exception cref="PlatformNotSupportedException">Not macOS 15 or later on Apple silicon (MSL 3.2 needs macOS 15).</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="mtlDevice"/> is 0 or not an <c>id&lt;MTLDevice&gt;</c>, or <paramref name="mtlCommandQueue"/>
    /// is not an <c>id&lt;MTLCommandQueue&gt;</c> of that device.
    /// </exception>
    public static GraphicsDevice FromNative(nint mtlDevice, nint mtlCommandQueue = 0)
    {
        if (!IsPlatformSupported)
        {
            throw NotSupported();
        }

        using AutoreleasePool pool = AutoreleasePool.Push();

        ThrowIfNot(mtlDevice, MetalProtocols.Device, "id<MTLDevice>", nameof(mtlDevice));

        if (mtlCommandQueue != 0)
        {
            ThrowIfNot(mtlCommandQueue, MetalProtocols.CommandQueue, "id<MTLCommandQueue>", nameof(mtlCommandQueue));

            if (ObjC.SendIntPtr(mtlCommandQueue, Selectors.Device) != mtlDevice)
            {
                throw new ArgumentException("The command queue belongs to a different MTLDevice than the one passed with it.", nameof(mtlCommandQueue));
            }
        }

        // The constructor owns both references from here on, and releases them if it throws.
        nint device = ObjC.Retain(mtlDevice);
        nint queue = mtlCommandQueue == 0 ? 0 : ObjC.Retain(mtlCommandQueue);
        return new GraphicsDevice(device, isDefault: false, queue);
    }

    /// <summary>The <c>id&lt;MTLDevice&gt;</c>, for other Metal code (to create resources SharpCompute can wrap, say).</summary>
    /// <remarks>
    /// The reference is not owned by the caller: it is valid while this device is not disposed (and,
    /// in practice, for as long as anything made from it is alive, since every Metal resource retains
    /// its device). Code that keeps it longer must retain it and release it when done.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    public nint NativeHandle
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _device.DangerousGetHandle();
        }
    }

    /// <summary>
    /// The <c>id&lt;MTLCommandQueue&gt;</c> SharpCompute commits to: the app's own queue for a device
    /// made by <see cref="FromNative"/> with one, otherwise SharpCompute's. Command buffers the app
    /// commits to it run in order with SharpCompute's.
    /// </summary>
    /// <remarks>Not owned by the caller, like <see cref="NativeHandle"/>: valid while this device is not disposed.</remarks>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    public nint NativeCommandQueueHandle
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _queue.DangerousGetHandle();
        }
    }

    /// <summary>
    /// Makes a <see cref="ReadWriteBuffer{T}"/> (<c>device T*</c> in shaders) over an existing
    /// <c>id&lt;MTLBuffer&gt;</c> of this device, without copying it.
    /// </summary>
    /// <param name="mtlBuffer">The buffer. It is retained, and released when the result is disposed (or finalized).</param>
    /// <remarks>
    /// <para>
    /// The length is <c>buffer.length / sizeof(T)</c>, which must be a whole number of elements. The
    /// result is an ordinary buffer: it can be bound to shaders, sliced, copied and filled, and every
    /// dispatch that uses it keeps the <c>MTLBuffer</c> alive until the GPU is done, even if the buffer
    /// is disposed in between.
    /// </para>
    /// <para>
    /// <b>Storage.</b> A buffer in shared (or managed, which on Apple silicon is the same thing) storage
    /// has full CPU access through its spans and indexers. One in private storage is GPU-only:
    /// shaders, <c>Copy</c>, <c>Fill</c> and <c>Clear</c> work, and the CPU accessors and <c>ForCpu</c>
    /// throw <see cref="NotSupportedException"/> (see <see cref="Buffer{T}.IsCpuAccessible"/>).
    /// </para>
    /// <para>
    /// The buffer must use Metal's default (tracked) hazard tracking, which is what SharpCompute's
    /// ordering guarantees rest on: an untracked buffer (one from an <c>MTLHeap</c>, typically) is rejected.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="mtlBuffer"/> is 0, not an <c>id&lt;MTLBuffer&gt;</c>, belongs to another
    /// <c>MTLDevice</c>, has untracked hazard tracking, or its length is not a whole number of elements.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The buffer holds more than <see cref="int.MaxValue"/> elements.</exception>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    public ReadWriteBuffer<T> WrapNativeReadWriteBuffer<T>(nint mtlBuffer)
        where T : unmanaged
    {
        NativeBuffer native = WrapNativeBuffer<T>(mtlBuffer);
        return new ReadWriteBuffer<T>(this, native.Handle, (T*)native.Contents, native.Length);
    }

    /// <summary>
    /// Like <see cref="WrapNativeReadWriteBuffer{T}"/>, but the buffer binds as read-only
    /// (<c>const device T*</c>) in shaders.
    /// </summary>
    /// <param name="mtlBuffer">The buffer. It is retained, and released when the result is disposed (or finalized).</param>
    /// <remarks>
    /// Read-only describes how shaders bind it; the memory itself is not read-only, so the CPU can
    /// write a shared one through <see cref="Buffer{T}.AsSpan"/>, and GPU copies can write it.
    /// </remarks>
    /// <inheritdoc cref="WrapNativeReadWriteBuffer{T}(nint)" path="/exception"/>
    public ReadOnlyBuffer<T> WrapNativeReadOnlyBuffer<T>(nint mtlBuffer)
        where T : unmanaged
    {
        NativeBuffer native = WrapNativeBuffer<T>(mtlBuffer);
        return new ReadOnlyBuffer<T>(this, native.Handle, (T*)native.Contents, native.Length, isMemoryWritable: true);
    }

    /// <summary>
    /// Makes a <see cref="ReadOnlyTexture2D{T}"/> (read and sampled by shaders) over an existing
    /// <c>id&lt;MTLTexture&gt;</c> of this device, without copying it.
    /// </summary>
    /// <param name="mtlTexture">The texture. It is retained, and released when the result is disposed (or finalized).</param>
    /// <typeparam name="T">The element type whose pixel format the texture has (see <see cref="Texture2D{T}"/>).</typeparam>
    /// <remarks>
    /// <para>
    /// The texture must be a 2D texture (<c>MTLTextureType2D</c>, one sample per pixel) in the pixel
    /// format of <typeparamref name="T"/>, with <c>MTLTextureUsageShaderRead</c>, tracked hazard
    /// tracking (see <see cref="WrapNativeReadWriteBuffer{T}"/>) and storage other than memoryless.
    /// A mipmapped texture is accepted, but only level 0 is used: its size is
    /// <see cref="Texture2D{T}.Width"/> × <see cref="Texture2D{T}.Height"/>, shaders read level 0, and
    /// the copies copy level 0.
    /// </para>
    /// <para>
    /// <b>CPU access.</b> Unless it is a linear view of a shared buffer, the CPU cannot address the
    /// pixels (<see cref="Texture2D{T}.IsCpuAccessible"/> is false): the indexers and <c>ForCpu</c>
    /// throw <see cref="NotSupportedException"/>, while <see cref="Texture2D{T}.CopyTo"/>,
    /// <see cref="Texture2D{T}.CopyFrom"/> and <see cref="Texture2D{T}.ToArray"/> work in shared storage and
    /// throw in private storage. GPU copies work in every case.
    /// </para>
    /// </remarks>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not a texture element type.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="mtlTexture"/> is 0, not an <c>id&lt;MTLTexture&gt;</c>, belongs to another
    /// <c>MTLDevice</c>, or is not a single-sample 2D texture in the pixel format of
    /// <typeparamref name="T"/> with the usage, storage and hazard tracking described above.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    public ReadOnlyTexture2D<T> WrapNativeReadOnlyTexture2D<T>(nint mtlTexture)
        where T : unmanaged
    {
        NativeTexture native = WrapNativeTexture<T>(mtlTexture, readWrite: false);
        return new ReadOnlyTexture2D<T>(this, native.Texture, native.Storage, native.Contents, native.BytesPerRow, native.Width, native.Height, native.CpuAccess);
    }

    /// <summary>
    /// Makes a <see cref="ReadWriteTexture2D{T}"/> (read and written by shaders; with
    /// <see cref="Rgba32"/>, the output of pixel shaders and of <see cref="Presentation.MetalWindow.Present"/>)
    /// over an existing <c>id&lt;MTLTexture&gt;</c> of this device, without copying it.
    /// </summary>
    /// <param name="mtlTexture">The texture. It is retained, and released when the result is disposed (or finalized).</param>
    /// <typeparam name="T">
    /// The element type whose pixel format the texture has; one the GPU can read and write in one
    /// shader (see <see cref="ReadWriteTexture2D{T}"/>).
    /// </typeparam>
    /// <remarks>
    /// Everything said for <see cref="WrapNativeReadOnlyTexture2D{T}"/> applies; the texture also needs
    /// <c>MTLTextureUsageShaderWrite</c>.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// <typeparamref name="T"/> is not a texture element type, or is one the GPU cannot read and write in one shader.
    /// </exception>
    /// <inheritdoc cref="WrapNativeReadOnlyTexture2D{T}(nint)" path="/exception"/>
    public ReadWriteTexture2D<T> WrapNativeReadWriteTexture2D<T>(nint mtlTexture)
        where T : unmanaged
    {
        NativeTexture native = WrapNativeTexture<T>(mtlTexture, readWrite: true);
        return new ReadWriteTexture2D<T>(this, native.Texture, native.Storage, native.Contents, native.BytesPerRow, native.Width, native.Height, native.CpuAccess);
    }

    /// <summary>
    /// The check every <c>id</c> handed in goes through before anything is sent to it: not nil, and an
    /// object of the right protocol (a texture passed for a buffer would otherwise be sent selectors
    /// it does not implement, which Objective-C reports by terminating the process).
    /// </summary>
    internal static void ThrowIfNot(nint obj, nint protocol, string kind, string parameterName)
    {
        if (obj == 0)
        {
            throw new ArgumentException($"The {kind} is nil (0).", parameterName);
        }

        if (protocol != 0 && !ObjC.ConformsTo(obj, protocol))
        {
            throw new ArgumentException($"The object at 0x{obj:x} is not an {kind}.", parameterName);
        }
    }

    /// <summary>
    /// The <c>device</c> of a native resource, command queue or command buffer is this device, or the
    /// exception that says it is not.
    /// </summary>
    internal void ThrowIfOtherDevice(nint obj, string kind, string parameterName)
    {
        if (ObjC.SendIntPtr(obj, Selectors.Device) != Identity)
        {
            throw new ArgumentException(
                $"The {kind} belongs to a different MTLDevice than this GraphicsDevice ('{Name}'), which Metal does not allow it to be used with.",
                parameterName);
        }
    }

    private NativeBuffer WrapNativeBuffer<T>(nint mtlBuffer)
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        using AutoreleasePool pool = AutoreleasePool.Push();

        ThrowIfNot(mtlBuffer, MetalProtocols.Buffer, "id<MTLBuffer>", nameof(mtlBuffer));
        ThrowIfOtherDevice(mtlBuffer, "buffer", nameof(mtlBuffer));
        ThrowIfUntracked(mtlBuffer, "buffer", nameof(mtlBuffer));

        nuint byteLength = ObjC.SendNUInt(mtlBuffer, Selectors.Length);

        if (byteLength % (nuint)sizeof(T) != 0)
        {
            throw new ArgumentException(
                $"The buffer is {byteLength} bytes, which is not a whole number of {sizeof(T)}-byte '{typeof(T)}' elements.", nameof(mtlBuffer));
        }

        if (byteLength / (nuint)sizeof(T) > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mtlBuffer), byteLength, $"The buffer holds {byteLength / (nuint)sizeof(T)} '{typeof(T)}' elements, more than int.MaxValue.");
        }

        nuint storageMode = ObjC.SendNUInt(mtlBuffer, Selectors.StorageMode);
        void* contents = null;

        // Private storage is GPU-only. On Apple silicon `contents` of such a buffer has been seen to
        // return an address anyway, so the storage mode decides, never a null check.
        if (storageMode != Metal.StorageModePrivate)
        {
            contents = (void*)ObjC.SendIntPtr(mtlBuffer, Selectors.Contents);

            if (contents == null)
            {
                throw new ArgumentException("The buffer is in CPU-visible storage but has no contents.", nameof(mtlBuffer));
            }
        }

        // No memory pressure: SharpCompute did not allocate the memory, and it lives as long as the app keeps it anyway.
        return new NativeBuffer(new ObjCHandle(ObjC.Retain(mtlBuffer)), contents, (int)(byteLength / (nuint)sizeof(T)));
    }

    private NativeTexture WrapNativeTexture<T>(nint mtlTexture, bool readWrite)
        where T : unmanaged
    {
        TextureFormat format = TextureFormats.Get<T>() ?? throw TextureFormats.Unsupported<T>();
        string type = readWrite ? $"ReadWriteTexture2D<{typeof(T).Name}>" : $"ReadOnlyTexture2D<{typeof(T).Name}>";

        if (readWrite && !format.SupportsReadWrite)
        {
            throw new NotSupportedException(
                $"{type} is not supported: Apple GPUs cannot read and write {format.Name} in the same shader. " +
                $"Wrap it with WrapNativeReadOnlyTexture2D, or use one of {TextureFormats.ReadWriteTypes} for a read-write texture.");
        }

        ObjectDisposedException.ThrowIf(IsDisposed, this);

        using AutoreleasePool pool = AutoreleasePool.Push();

        const string parameter = nameof(mtlTexture);
        ThrowIfNot(mtlTexture, MetalProtocols.Texture, "id<MTLTexture>", parameter);
        ThrowIfOtherDevice(mtlTexture, "texture", parameter);

        nuint textureType = ObjC.SendNUInt(mtlTexture, Selectors.TextureType);

        if (textureType != Metal.TextureType2D)
        {
            throw new ArgumentException(
                $"The texture's type is {DescribeTextureType(textureType)}, but a {type} needs MTLTextureType2D.", parameter);
        }

        nuint sampleCount = ObjC.SendNUInt(mtlTexture, Selectors.SampleCount);

        if (sampleCount != 1)
        {
            throw new ArgumentException($"The texture has {sampleCount} samples per pixel; a {type} needs exactly 1.", parameter);
        }

        nuint pixelFormat = ObjC.SendNUInt(mtlTexture, Selectors.PixelFormat);

        if (pixelFormat != format.PixelFormat)
        {
            throw new ArgumentException(
                $"The texture's pixel format is {TextureFormats.Describe(pixelFormat)}, but a {type} needs {format.Name}.", parameter);
        }

        nuint usage = ObjC.SendNUInt(mtlTexture, Selectors.Usage);
        nuint required = readWrite ? Metal.TextureUsageShaderReadWrite : Metal.TextureUsageShaderRead;

        if ((usage & required) != required)
        {
            throw new ArgumentException(
                $"The texture's usage (0x{usage:x}) lacks {(readWrite ? "MTLTextureUsageShaderRead | MTLTextureUsageShaderWrite" : "MTLTextureUsageShaderRead")}, " +
                $"which a {type} needs: shaders {(readWrite ? "read and write" : "read")} it.",
                parameter);
        }

        nuint width = ObjC.SendNUInt(mtlTexture, Selectors.Width);
        nuint height = ObjC.SendNUInt(mtlTexture, Selectors.Height);

        // Metal itself never makes a 2D texture larger than this on Apple GPUs; checked so that the
        // int conversions below can never wrap.
        if (width is 0 or > Metal.MaxTexture2DDimension || height is 0 or > Metal.MaxTexture2DDimension)
        {
            throw new ArgumentException(
                $"The texture is {width}×{height}; a {type} must be 1 to {Metal.MaxTexture2DDimension} pixels in each dimension.", parameter);
        }

        ThrowIfUntracked(mtlTexture, "texture", parameter);

        nuint storageMode = ObjC.SendNUInt(mtlTexture, Selectors.StorageMode);

        if (storageMode == Metal.StorageModeMemoryless)
        {
            throw new ArgumentException(
                "The texture is memoryless: it exists only in tile memory during a render pass, so compute shaders cannot use it.", parameter);
        }

        ObjCHandle texture = new(ObjC.Retain(mtlTexture));
        ObjCHandle storage = texture;
        byte* contents = null;
        nuint bytesPerRow = 0;
        TextureCpuAccess cpuAccess = storageMode == Metal.StorageModePrivate ? TextureCpuAccess.None : TextureCpuAccess.Region;

        // A linear view of a buffer (newTextureWithDescriptor:offset:bytesPerRow:): when that buffer is
        // shared, the CPU reads and writes the pixels in place, exactly as for a texture SharpCompute
        // allocated. The buffer is retained and leased with the texture, as there.
        nint buffer = ObjC.SendIntPtr(mtlTexture, Selectors.Buffer);

        if (buffer != 0 && cpuAccess == TextureCpuAccess.Region && ObjC.SendNUInt(buffer, Selectors.StorageMode) != Metal.StorageModePrivate)
        {
            byte* bufferContents = (byte*)ObjC.SendIntPtr(buffer, Selectors.Contents);

            if (bufferContents != null)
            {
                storage = new ObjCHandle(ObjC.Retain(buffer));
                contents = bufferContents + ObjC.SendNUInt(mtlTexture, Selectors.BufferOffset);
                bytesPerRow = ObjC.SendNUInt(mtlTexture, Selectors.BufferBytesPerRow);
                cpuAccess = TextureCpuAccess.Linear;
            }
        }

        return new NativeTexture(texture, storage, contents, bytesPerRow, (int)width, (int)height, cpuAccess);
    }

    /// <summary>
    /// SharpCompute orders the work it encodes by Metal's hazard tracking (see <see cref="ComputeContext"/>),
    /// which does nothing for an untracked resource: a copy after a dispatch could then read what the
    /// dispatch had not yet written. Such resources need fences or barriers SharpCompute does not encode.
    /// </summary>
    private static void ThrowIfUntracked(nint resource, string kind, string parameterName)
    {
        if (ObjC.SendNUInt(resource, Selectors.HazardTrackingMode) == Metal.HazardTrackingModeUntracked)
        {
            throw new ArgumentException(
                $"The {kind} uses MTLHazardTrackingModeUntracked (as resources of an MTLHeap do by default). SharpCompute relies on Metal's " +
                $"hazard tracking to order its work, so only tracked resources can be wrapped: create the {kind} with " +
                "MTLResourceHazardTrackingModeTracked (or a heap with MTLHazardTrackingModeTracked).",
                parameterName);
        }
    }

    private static string DescribeTextureType(nuint textureType) => textureType switch
    {
        0 => "MTLTextureType1D",
        1 => "MTLTextureType1DArray",
        3 => "MTLTextureType2DArray",
        4 => "MTLTextureType2DMultisample",
        5 => "MTLTextureTypeCube",
        6 => "MTLTextureTypeCubeArray",
        7 => "MTLTextureType3D",
        8 => "MTLTextureType2DMultisampleArray",
        9 => "MTLTextureTypeTextureBuffer",
        _ => $"MTLTextureType {textureType}",
    };

    /// <summary>A validated, retained native buffer: its handle, CPU address (null in private storage) and element count.</summary>
    private readonly struct NativeBuffer(ObjCHandle handle, void* contents, int length)
    {
        public ObjCHandle Handle { get; } = handle;

        public void* Contents { get; } = contents;

        public int Length { get; } = length;
    }

    /// <summary>A validated, retained native texture and what the CPU can do with it.</summary>
    private readonly struct NativeTexture(
        ObjCHandle texture, ObjCHandle storage, byte* contents, nuint bytesPerRow, int width, int height, TextureCpuAccess cpuAccess)
    {
        public ObjCHandle Texture { get; } = texture;

        public ObjCHandle Storage { get; } = storage;

        public byte* Contents { get; } = contents;

        public nuint BytesPerRow { get; } = bytesPerRow;

        public int Width { get; } = width;

        public int Height { get; } = height;

        public TextureCpuAccess CpuAccess { get; } = cpuAccess;
    }
}
