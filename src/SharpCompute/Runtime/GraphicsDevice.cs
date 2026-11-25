using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using SharpCompute.Interop;

namespace SharpCompute;

/// <summary>
/// A Metal GPU device (<c>id&lt;MTLDevice&gt;</c>) plus its command queue and pipeline cache.
/// </summary>
/// <remarks>
/// <para>
/// All members are thread-safe. Every dispatch uses its own command buffer and encoder, and the
/// device, queue and pipeline states they share are safe to use concurrently.
/// </para>
/// <para>
/// <b>Same device.</b> Buffers and textures belong to the <c>MTLDevice</c>, not to the
/// <see cref="GraphicsDevice"/> they were allocated or wrapped through, so the "different device"
/// checks of dispatches, copies and presentation compare the underlying <c>id&lt;MTLDevice&gt;</c>.
/// <c>MTLCreateSystemDefaultDevice</c> returns the same object every time (measured on an Apple M4
/// Pro), so <see cref="GetDefault"/> and a device made by <see cref="FromNative"/> from the system
/// default device are the same device and share resources; each still has its own queue.
/// </para>
/// </remarks>
public sealed unsafe partial class GraphicsDevice : IDisposable
{
    private static readonly Lazy<GraphicsDevice?> s_default =
        new(static () => TryCreateSystemDefault(isDefault: true), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly ObjCHandle _device;
    private readonly ObjCHandle _queue;

    // One pipeline per shader type, specialization and compile options. The windowed specializations
    // (function constant 0 = true) are the ones the GPU part of ForSplit uses. The options are a
    // constant of the shader type today; they are in the key so that the cache stays right if the
    // same source is ever compiled two ways.
    private readonly ConcurrentDictionary<PipelineKey, Lazy<ComputePipeline>> _pipelines = new();
    private readonly bool _isDefault;
    private int _pipelineCompileCount;
    private int _disposed;

    /// <param name="device">A +1 reference to the <c>id&lt;MTLDevice&gt;</c>; ownership transfers to the new object.</param>
    /// <param name="isDefault">True for the shared <see cref="GetDefault"/> instance, which Dispose leaves alone.</param>
    /// <param name="queue">A +1 reference to a queue of <paramref name="device"/> to use, or 0 to create one.</param>
    /// <param name="identity">What this device compares as (see <see cref="Identity"/>); 0 for <paramref name="device"/> itself.</param>
    private GraphicsDevice(nint device, bool isDefault, nint queue = 0, nint identity = 0)
    {
        _isDefault = isDefault;
        _usePrecompiledShaders = PrecompiledShadersEnabled;
        _device = new ObjCHandle(device);
        Identity = identity == 0 ? device : identity;

        try
        {
            using AutoreleasePool pool = AutoreleasePool.Push();

            if (queue == 0)
            {
                queue = ObjC.SendIntPtr(device, Selectors.NewCommandQueue);

                if (queue == 0)
                {
                    throw new InvalidOperationException("Metal failed to create a command queue.");
                }
            }

            _queue = new ObjCHandle(queue);
            queue = 0;
            Name = NSString.ToManaged(ObjC.SendIntPtr(device, Selectors.Name)) ?? string.Empty;
            MaxBufferLength = ObjC.SendNUInt(device, Selectors.MaxBufferLength);
            MaxThreadgroupMemoryLength = ObjC.SendNUInt(device, Selectors.MaxThreadgroupMemoryLength);
        }
        catch
        {
            ObjC.Release(queue);
            _queue?.Dispose();
            _device.Dispose();
            throw;
        }
    }

    /// <summary>True when a Metal device is available on this machine.</summary>
    public static bool IsSupported => s_default.Value is not null;

    /// <summary>The system default device (<c>MTLCreateSystemDefaultDevice</c>). Shared instance; disposing it is a no-op.</summary>
    /// <exception cref="PlatformNotSupportedException">No usable Metal device; see <see cref="IsSupported"/>.</exception>
    public static GraphicsDevice GetDefault() => s_default.Value ?? throw NotSupported();

    /// <summary>The device name, e.g. "Apple M4 Pro".</summary>
    public string Name { get; }

    /// <summary>The device's <c>maxBufferLength</c>, in bytes.</summary>
    internal nuint MaxBufferLength { get; }

    /// <summary>The device's <c>maxThreadgroupMemoryLength</c>: the bytes of threadgroup memory one kernel may declare (32,768 on Apple GPUs).</summary>
    internal nuint MaxThreadgroupMemoryLength { get; }

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// The <c>id&lt;MTLDevice&gt;</c> this object wraps, as a number: what "the same device" means. Two
    /// <see cref="GraphicsDevice"/> objects over one <c>MTLDevice</c> (<see cref="GetDefault"/> and
    /// <see cref="FromNative"/> of the system default device, say) are the same device, and resources
    /// of one can be used with the other. The value stays valid after Dispose, for the comparison only.
    /// </summary>
    internal nint Identity { get; }

    /// <summary>
    /// True when a resource of <paramref name="other"/> can be used with this device: both wrap the
    /// same <c>MTLDevice</c>. A buffer or texture belongs to an <c>MTLDevice</c>, not to the wrapper it
    /// was allocated through, and Metal accepts it on any queue of that device.
    /// </summary>
    internal bool IsSameDevice(GraphicsDevice other) => other.Identity == Identity;

    /// <summary>The <c>id&lt;MTLDevice&gt;</c>. Lease it (<see cref="ObjCHandle.Acquire"/>) for the duration of a native call.</summary>
    internal ObjCHandle DeviceHandle => _device;

    /// <summary>The device's <c>id&lt;MTLCommandQueue&gt;</c>, shared by every dispatch and by presentation.</summary>
    internal ObjCHandle QueueHandle => _queue;

    /// <summary>
    /// A new, independently disposable wrapper around the system default device. It has its own
    /// command queue and pipeline cache. It is the same device as <see cref="GetDefault"/> (see
    /// <see cref="Identity"/>), so their buffers and textures are interchangeable.
    /// </summary>
    internal static GraphicsDevice CreateSystemDefault() => TryCreateSystemDefault(isDefault: false) ?? throw NotSupported();

    /// <summary>
    /// Tests only: a wrapper around the system default device that counts as a different
    /// <c>MTLDevice</c>, standing in for a second GPU. Apple silicon Macs have one GPU, and
    /// <c>MTLCreateSystemDefaultDevice</c> returns the same object every time, so the wrong-device
    /// checks cannot be reached with real devices here. Its identity is a negative number, which no
    /// object address is.
    /// </summary>
    internal static GraphicsDevice CreateStandInForAnotherDevice()
    {
        nint device = Metal.CreateSystemDefaultDevice();

        if (device == 0)
        {
            throw NotSupported();
        }

        return new GraphicsDevice(device, isDefault: false, identity: -(nint)Interlocked.Increment(ref s_standInIdentities));
    }

    private static long s_standInIdentities;

    public ReadWriteBuffer<T> AllocateReadWriteBuffer<T>(int length)
        where T : unmanaged => new(this, length);

    public ReadWriteBuffer<T> AllocateReadWriteBuffer<T>(ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ReadWriteBuffer<T> buffer = new(this, data.Length);
        data.CopyTo(buffer.AsSpan());
        return buffer;
    }

    /// <summary>
    /// Allocates a zero-filled read-only buffer (<c>const device T*</c> in shaders) of
    /// <paramref name="length"/> elements, to be filled from the CPU through
    /// <see cref="Buffer{T}.AsSpan"/>, e.g. by reading a file straight into it.
    /// </summary>
    public ReadOnlyBuffer<T> AllocateReadOnlyBuffer<T>(int length)
        where T : unmanaged => new(this, length);

    public ReadOnlyBuffer<T> AllocateReadOnlyBuffer<T>(ReadOnlySpan<T> data)
        where T : unmanaged
    {
        ReadOnlyBuffer<T> buffer = new(this, data.Length);
        data.CopyTo(buffer.AsSpan());
        return buffer;
    }

    /// <summary>
    /// The VM page size (16 KB on Apple Silicon). <see cref="WrapReadWriteBuffer{T}"/> and
    /// <see cref="WrapReadOnlyBuffer{T}"/> need memory that starts on a page boundary and lies in one
    /// virtual-memory region. Allocate whole pages, rounding the byte size up to a multiple of this,
    /// e.g. <c>NativeMemory.AlignedAlloc((byteCount + PageSize - 1) / PageSize * PageSize, (nuint)PageSize)</c>;
    /// Metal accepts other lengths in practice, but Apple documents the region as page-aligned.
    /// </summary>
    public static int PageSize => Environment.SystemPageSize;

    /// <summary>
    /// Makes a buffer over <paramref name="length"/> elements of existing memory at
    /// <paramref name="pointer"/>, without copying it (<c>newBufferWithBytesNoCopy</c>): the CPU and
    /// the GPU both use that memory directly.
    /// </summary>
    /// <param name="pointer">Start of the memory; must be a multiple of <see cref="PageSize"/>.</param>
    /// <param name="length">Number of elements; must be positive.</param>
    /// <param name="release">
    /// Called once when the buffer is released, after any dispatch that was using it has finished,
    /// to free the memory. It runs on the thread that drops the last reference: the one calling
    /// Dispose, the finalizer thread, or a Metal completion thread when an asynchronous or batched
    /// dispatch finishes after the buffer was disposed. It must not throw. If this method throws, it
    /// is not called and the caller still owns the memory.
    /// </param>
    /// <remarks>
    /// The memory must stay valid and must not be freed until <paramref name="release"/> runs (or,
    /// without a callback, until the buffer is disposed and no dispatch is using it).
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="pointer"/> is null or not page-aligned.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is not positive or exceeds the device's maximum buffer size.</exception>
    public ReadWriteBuffer<T> WrapReadWriteBuffer<T>(nint pointer, int length, Action? release = null)
        where T : unmanaged
    {
        ObjCHandle handle = NewSharedBufferNoCopy<T>(pointer, length, release);
        return new ReadWriteBuffer<T>(this, handle, (T*)pointer, length);
    }

    /// <summary>
    /// Like <see cref="WrapReadWriteBuffer{T}"/>, but the buffer binds as read-only
    /// (<c>const device T*</c>) in shaders.
    /// </summary>
    /// <param name="pointer">Start of the memory; must be a multiple of <see cref="PageSize"/>.</param>
    /// <param name="length">Number of elements; must be positive.</param>
    /// <param name="release">As for <see cref="WrapReadWriteBuffer{T}"/>.</param>
    /// <param name="isCpuWritable">
    /// False when the memory is read-only for this process (e.g. a <c>PROT_READ</c> mapping): then
    /// <see cref="Buffer{T}.AsSpan"/> and <see cref="Buffer{T}.CopyFrom"/> throw instead of faulting.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="pointer"/> is null or not page-aligned.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is not positive or exceeds the device's maximum buffer size.</exception>
    public ReadOnlyBuffer<T> WrapReadOnlyBuffer<T>(nint pointer, int length, Action? release = null, bool isCpuWritable = true)
        where T : unmanaged
    {
        ObjCHandle handle = NewSharedBufferNoCopy<T>(pointer, length, release);
        return new ReadOnlyBuffer<T>(this, handle, (T*)pointer, length, isCpuWritable);
    }

    /// <summary>
    /// Memory-maps the file at <paramref name="path"/> read-only and makes a buffer over it without
    /// copying: the GPU reads the file's pages straight from the OS page cache, so a large file is
    /// not first read into managed memory and then copied again into a buffer.
    /// </summary>
    /// <remarks>
    /// The file is read as raw <typeparamref name="T"/> elements in this machine's byte order; its
    /// size must be a multiple of <c>sizeof(T)</c>. The mapping is released with the buffer. The
    /// memory is read-only on the CPU too: <see cref="Buffer{T}.IsCpuWritable"/> is false, so read it with
    /// <see cref="Buffer{T}.AsReadOnlySpan"/> or the indexer. Changes other processes make to the file
    /// while it is mapped may be visible through the buffer, and truncating the file while it is
    /// mapped makes reads past the new end fault, as with any memory-mapped file.
    /// </remarks>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    /// <exception cref="ArgumentException">The file size is not a multiple of <c>sizeof(T)</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The file holds more than <see cref="int.MaxValue"/> elements or exceeds the device's maximum buffer size.</exception>
    public ReadOnlyBuffer<T> MapReadOnlyBuffer<T>(string path)
        where T : unmanaged
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        long byteLength = new FileInfo(path) is { Exists: true } file
            ? file.Length
            : throw new FileNotFoundException($"No such file: {Path.GetFullPath(path)}", path);

        if (byteLength % sizeof(T) != 0)
        {
            throw new ArgumentException($"'{path}' is {byteLength} bytes, which is not a whole number of {sizeof(T)}-byte '{typeof(T)}' elements.", nameof(path));
        }

        if (byteLength / sizeof(T) > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(path), byteLength, $"'{path}' holds more than int.MaxValue '{typeof(T)}' elements.");
        }

        int length = (int)(byteLength / sizeof(T));

        // An empty file can't be mapped, and Metal rejects zero-length buffers anyway.
        if (length == 0)
        {
            return new ReadOnlyBuffer<T>(this, 0);
        }

        MemoryMappedFile? map = null;
        MemoryMappedViewAccessor? view = null;
        byte* pointer = null;

        try
        {
            map = MemoryMappedFile.CreateFromFile(path, FileMode.Open, mapName: null, capacity: 0, MemoryMappedFileAccess.Read);
            view = map.CreateViewAccessor(0, byteLength, MemoryMappedFileAccess.Read);
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);

            // A view at offset 0 starts at the mapping's base, which mmap page-aligns.
            nint start = (nint)(pointer + view.PointerOffset);
            MemoryMappedFile ownedMap = map;
            MemoryMappedViewAccessor ownedView = view;

            ObjCHandle handle = NewSharedBufferNoCopy<T>(start, length, () =>
            {
                ownedView.SafeMemoryMappedViewHandle.ReleasePointer();
                ownedView.Dispose();
                ownedMap.Dispose();
            });

            map = null;
            view = null;
            return new ReadOnlyBuffer<T>(this, handle, (T*)start, length, isMemoryWritable: false);
        }
        finally
        {
            if (view is not null)
            {
                if (pointer != null)
                {
                    view.SafeMemoryMappedViewHandle.ReleasePointer();
                }

                view.Dispose();
            }

            map?.Dispose();
        }
    }

    /// <summary>
    /// Allocates a zero-filled <paramref name="width"/> × <paramref name="height"/> texture that shaders
    /// read and write (<c>access::read_write</c>), and that pixel shaders render into when
    /// <typeparamref name="T"/> is <see cref="Rgba32"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The pixel: <see cref="Rgba32"/>, <see cref="R8"/>, <see cref="Half"/>, <see cref="Rgba16Float"/>,
    /// <see cref="float"/> or <see cref="Float4"/>.
    /// </typeparam>
    /// <exception cref="NotSupportedException">
    /// <typeparamref name="T"/> is not a texture element type, or is one the GPU cannot read and write
    /// in one shader (<see cref="Rg16"/>, <see cref="Rgba32Srgb"/>).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not in 1..16384.</exception>
    public ReadWriteTexture2D<T> AllocateReadWriteTexture2D<T>(int width, int height)
        where T : unmanaged => new(this, width, height);

    /// <summary>
    /// Allocates a read-write texture and copies <paramref name="pixels"/> (row-major, tightly packed)
    /// into it, starting at pixel (0, 0); pixels it does not cover stay zero.
    /// </summary>
    /// <inheritdoc cref="AllocateReadWriteTexture2D{T}(int, int)" path="/typeparam"/>
    /// <inheritdoc cref="AllocateReadWriteTexture2D{T}(int, int)" path="/exception"/>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is longer than <c>width * height</c>.</exception>
    public ReadWriteTexture2D<T> AllocateReadWriteTexture2D<T>(ReadOnlySpan<T> pixels, int width, int height)
        where T : unmanaged => WithPixels(new ReadWriteTexture2D<T>(this, width, height), pixels);

    /// <summary>
    /// Allocates a zero-filled <paramref name="width"/> × <paramref name="height"/> texture that shaders
    /// read and sample (<c>access::sample</c>), to be filled from the CPU with
    /// <see cref="Texture2D{T}.CopyFrom"/>.
    /// </summary>
    /// <typeparam name="T">
    /// The pixel: <see cref="Rgba32"/>, <see cref="Rgba32Srgb"/>, <see cref="R8"/>, <see cref="Rg16"/>,
    /// <see cref="Half"/>, <see cref="Rgba16Float"/>, <see cref="float"/> or <see cref="Float4"/>.
    /// </typeparam>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not a texture element type.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not in 1..16384.</exception>
    public ReadOnlyTexture2D<T> AllocateReadOnlyTexture2D<T>(int width, int height)
        where T : unmanaged => new(this, width, height);

    /// <summary>
    /// Allocates a read-only texture and copies <paramref name="pixels"/> (row-major, tightly packed)
    /// into it, starting at pixel (0, 0); pixels it does not cover stay zero.
    /// </summary>
    /// <inheritdoc cref="AllocateReadOnlyTexture2D{T}(int, int)" path="/typeparam"/>
    /// <inheritdoc cref="AllocateReadOnlyTexture2D{T}(int, int)" path="/exception"/>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is longer than <c>width * height</c>.</exception>
    public ReadOnlyTexture2D<T> AllocateReadOnlyTexture2D<T>(ReadOnlySpan<T> pixels, int width, int height)
        where T : unmanaged => WithPixels(new ReadOnlyTexture2D<T>(this, width, height), pixels);

    /// <summary>
    /// Runs a pixel shader once per pixel of <paramref name="texture"/> and waits for completion.
    /// The runtime binds the texture at <c>[[texture(0)]]</c>; the grid is <c>Width × Height × 1</c>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="texture"/> was allocated from a different device.</exception>
    /// <exception cref="ObjectDisposedException">The device or <paramref name="texture"/> has been disposed.</exception>
    public void ForEach<T>(ReadWriteTexture2D<Rgba32> texture, in T shader)
        where T : struct, IComputeShader<Float4>, IComputeShaderDescriptor<T>
    {
        ArgumentNullException.ThrowIfNull(texture);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);

        if (!IsSameDevice(texture.Device))
        {
            throw new ArgumentException(
                "The texture was allocated from a different GraphicsDevice than the one dispatching the shader.", nameof(texture));
        }

        // Pixel shaders are rank 2 by contract, so a 1-pixel-high texture still gets the 16×16 default.
        Run(new MTLSize((nuint)texture.Width, (nuint)texture.Height, 1), rank: 2, texture, in shader);
    }

    /// <summary>Dispatches <paramref name="x"/> threads and waits for completion.</summary>
    public void For<T>(int x, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => Dispatch(x, 1, 1, in shader);

    /// <summary>Dispatches an <paramref name="x"/> × <paramref name="y"/> grid and waits for completion.</summary>
    public void For<T>(int x, int y, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => Dispatch(x, y, 1, in shader);

    /// <summary>Dispatches an <paramref name="x"/> × <paramref name="y"/> × <paramref name="z"/> grid and waits for completion.</summary>
    public void For<T>(int x, int y, int z, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => Dispatch(x, y, z, in shader);

    /// <summary>Releases the command queue, the cached pipelines and the device. No-op on the default device.</summary>
    public void Dispose()
    {
        if (_isDefault || Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // A dispatch that is already past its disposed check holds leases on the handles it uses,
        // so these releases are deferred until it finishes.
        foreach (Lazy<ComputePipeline> pipeline in _pipelines.Values)
        {
            // A pipeline that is still compiling (PrepareAsync) is not waited for: its handle is
            // released by its finalizer. One whose compilation failed has no value to dispose.
            if (pipeline.IsValueCreated)
            {
                pipeline.Value.Dispose();
            }
        }

        _pipelines.Clear();
        DisposePrecompiledLibraries();
        _queue.Dispose();
        _device.Dispose();
    }

    /// <summary>Allocates a zero-filled shared-storage <c>MTLBuffer</c> of <paramref name="byteLength"/> bytes (must be &gt; 0).</summary>
    internal ObjCHandle NewSharedBuffer(nuint byteLength)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        using AutoreleasePool pool = AutoreleasePool.Push();
        using ObjCHandle.Lease device = _device.Acquire(this);

        nint buffer = ObjC.SendIntPtr(device.Pointer, Selectors.NewBufferWithLengthOptions, byteLength, Metal.ResourceStorageModeShared);

        if (buffer == 0)
        {
            throw new OutOfMemoryException($"Metal failed to allocate a {byteLength}-byte buffer on '{Name}'.");
        }

        return new ObjCHandle(buffer, memoryPressure: checked((long)byteLength));
    }

    /// <summary>
    /// <c>newBufferWithBytesNoCopy:length:options:deallocator:nil</c> over <paramref name="length"/>
    /// elements at <paramref name="pointer"/>. <paramref name="release"/> runs after the handle releases
    /// the buffer. Dispatches lease every bound buffer until the GPU has finished with it
    /// (ShaderArgumentWriter.SetBuffer), so by then no dispatch that used it is still in flight.
    /// </summary>
    private ObjCHandle NewSharedBufferNoCopy<T>(nint pointer, int length, Action? release)
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        if (pointer == 0 || pointer % PageSize != 0)
        {
            throw new ArgumentException($"The memory must start on a page boundary (a multiple of GraphicsDevice.PageSize = {PageSize}).", nameof(pointer));
        }

        ulong byteLength = (ulong)length * (ulong)sizeof(T);

        if (byteLength > MaxBufferLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length), length, $"{length} '{typeof(T)}' elements are {byteLength} bytes, but '{Name}' supports at most {MaxBufferLength} bytes per buffer.");
        }

        using AutoreleasePool pool = AutoreleasePool.Push();
        using ObjCHandle.Lease device = _device.Acquire(this);

        nint buffer = ObjC.SendIntPtr(
            device.Pointer, Selectors.NewBufferWithBytesNoCopyLengthOptionsDeallocator, (void*)pointer, (nuint)byteLength, Metal.ResourceStorageModeShared, 0);

        if (buffer == 0)
        {
            throw new InvalidOperationException($"Metal could not wrap {byteLength} bytes at 0x{pointer:x} as a buffer on '{Name}'.");
        }

        // No memory pressure: the memory is not allocated by this buffer.
        return new ObjCHandle(buffer, afterRelease: release);
    }

    /// <summary>Copies the initial pixels into a new texture, releasing it if they do not fit.</summary>
    private static TTexture WithPixels<TTexture, T>(TTexture texture, ReadOnlySpan<T> pixels)
        where TTexture : Texture2D<T>
        where T : unmanaged
    {
        try
        {
            texture.CopyFrom(pixels);
            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    /// <summary><c>minimumLinearTextureAlignmentForPixelFormat:</c>: the required alignment of a linear texture's offset and <c>bytesPerRow</c>.</summary>
    internal nuint GetMinimumLinearTextureAlignment(nuint pixelFormat)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        using ObjCHandle.Lease device = _device.Acquire(this);

        nuint alignment = ObjC.SendNUInt(device.Pointer, Selectors.MinimumLinearTextureAlignmentForPixelFormat, pixelFormat);

        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new InvalidOperationException($"Metal reported an invalid linear texture alignment ({alignment}) for pixel format {pixelFormat}.");
        }

        return alignment;
    }

    /// <summary>
    /// Allocates a shared-storage <c>MTLBuffer</c> initialised with a copy of <paramref name="bytes"/>
    /// and returns the +1 reference. Used for constants too large for <c>setBytes</c>.
    /// </summary>
    internal nint NewSharedBufferWithBytes(void* bytes, nuint byteLength)
    {
        using ObjCHandle.Lease device = _device.Acquire(this);

        nint buffer = ObjC.SendIntPtr(device.Pointer, Selectors.NewBufferWithBytesLengthOptions, bytes, byteLength, Metal.ResourceStorageModeShared);

        if (buffer == 0)
        {
            throw new OutOfMemoryException($"Metal failed to allocate a {byteLength}-byte buffer on '{Name}'.");
        }

        return buffer;
    }

    /// <summary>
    /// The rank of a dispatch, from its extents rather than from the <c>For</c> overload that was
    /// called: <c>For(n, 1, 1, shader)</c> is a 1D dispatch and gets a 1D threadgroup.
    /// </summary>
    internal static int InferRank(int y, int z) => z != 1 ? 3 : y != 1 ? 2 : 1;

    /// <summary>
    /// Resolves the threadgroup size for a dispatch. A descriptor that reports 0,0,0 has no
    /// <see cref="ThreadGroupSizeAttribute"/>, so the size is picked from the dispatch rank and
    /// shrunk to fit <paramref name="maxTotalThreads"/>; an explicit size is validated, never altered.
    /// </summary>
    internal static MTLSize ResolveThreadGroupSize(int x, int y, int z, int rank, nuint maxTotalThreads, Type shaderType)
    {
        if (x == 0 && y == 0 && z == 0)
        {
            (nuint ax, nuint ay, nuint az) = rank switch
            {
                1 => ((nuint)256, (nuint)1, (nuint)1),
                2 => ((nuint)16, (nuint)16, (nuint)1),
                _ => ((nuint)8, (nuint)8, (nuint)4),
            };

            // Halve the largest dimension until the group fits; this keeps 2D/3D groups close to square.
            while (ax * ay * az > maxTotalThreads && ax * ay * az > 1)
            {
                if (ax >= ay && ax >= az)
                {
                    ax /= 2;
                }
                else if (ay >= az)
                {
                    ay /= 2;
                }
                else
                {
                    az /= 2;
                }
            }

            return new MTLSize(ax, ay, az);
        }

        if (x <= 0 || y <= 0 || z <= 0)
        {
            throw new InvalidOperationException(
                $"Shader '{shaderType}' reports an invalid threadgroup size ({x}, {y}, {z}): every dimension must be positive, or all three 0 to let the runtime choose.");
        }

        // Compared step by step so the product cannot overflow.
        ulong total = (ulong)x * (ulong)y;

        if (total > maxTotalThreads || total * (ulong)z > maxTotalThreads)
        {
            throw new InvalidOperationException(
                $"Shader '{shaderType}' requests a threadgroup size of ({x}, {y}, {z}), but its pipeline supports at most {maxTotalThreads} threads per threadgroup.");
        }

        return new MTLSize((nuint)x, (nuint)y, (nuint)z);
    }

    private static PlatformNotSupportedException NotSupported() =>
        new("SharpCompute requires macOS 15 or later on Apple Silicon with a Metal device. Check GraphicsDevice.IsSupported first.");

    /// <summary>
    /// MSL 3.2 and <c>MTLCompileOptions.mathMode</c> (both fixed by the contract) need macOS 15, and
    /// the <c>objc_msgSend</c> call shapes in Interop are written for the arm64 ABI.
    /// </summary>
    private static bool IsPlatformSupported =>
        OperatingSystem.IsMacOSVersionAtLeast(15) && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    private static GraphicsDevice? TryCreateSystemDefault(bool isDefault)
    {
        if (!IsPlatformSupported)
        {
            return null;
        }

        nint device;

        try
        {
            device = Metal.CreateSystemDefaultDevice();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }

        return device == 0 ? null : new GraphicsDevice(device, isDefault);
    }

    private void Dispatch<T>(int x, int y, int z, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T>
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegative(z);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (x == 0 || y == 0 || z == 0)
        {
            return;
        }

        Run(new MTLSize((nuint)x, (nuint)y, (nuint)z), InferRank(y, z), output: null, in shader);
    }

    /// <summary>
    /// The encode/commit/wait path shared by <c>For</c>, <c>ForEach</c> and the GPU part of <c>ForSplit</c>.
    /// When <paramref name="output"/> is non-null it is bound at texture index 0 before the shader's own
    /// arguments. When <paramref name="window"/> is set, the windowed specialization runs
    /// <paramref name="grid"/> threads as one part of a larger grid. <paramref name="whileGpuRuns"/> (the
    /// CPU part of a split) runs on this thread between commit and wait; the GPU is waited for even when
    /// it throws, and its exception then wins over a GPU error.
    /// </summary>
    /// <remarks>
    /// With <paramref name="indirect"/>, the threadgroup counts come from that buffer when the dispatch
    /// runs and <paramref name="grid"/> is ignored (see <c>ForIndirect</c>).
    /// </remarks>
    /// <returns>The command buffer's <c>GPUStartTime</c> and <c>GPUEndTime</c>.</returns>
    private (double GpuStart, double GpuEnd) Run<T>(
        MTLSize grid,
        int rank,
        ReadWriteTexture2D<Rgba32>? output,
        in T shader,
        GridWindow? window = null,
        Action? whileGpuRuns = null,
        IndirectArguments indirect = default)
        where T : struct, IComputeShaderDescriptor<T>
    {
        ComputePipeline pipeline = window is null ? GetPipeline<T>() : GetWindowedPipeline<T>();
        MTLSize threadsPerThreadgroup = ResolveThreadGroupSize(
            T.ThreadGroupSizeX, T.ThreadGroupSizeY, T.ThreadGroupSizeZ, rank, pipeline.MaxTotalThreadsPerThreadgroup, typeof(T));

        // The command buffer and encoder are autoreleased; the pool drains them when the dispatch ends.
        using AutoreleasePool pool = AutoreleasePool.Push();
        using ObjCHandle.Lease queue = _queue.Acquire(this);
        using ObjCHandle.Lease state = pipeline.State.Acquire(this);

        // The output texture and the buffer that stores its pixels stay leased until the GPU is done,
        // so a concurrent Dispose of the texture cannot free memory the kernel is still writing.
        // (A default lease is a no-op when there is no output texture.)
        using ObjCHandle.Lease outputTexture = output is null ? default : output.Handle.Acquire(output);
        using ObjCHandle.Lease outputStorage = output is null ? default : output.BufferHandle.Acquire(output);

        // The GPU reads the indirect arguments when the dispatch runs, so they are leased like a bound buffer.
        using ObjCHandle.Lease indirectArguments = indirect.Handle is null ? default : indirect.Handle.Acquire(indirect.Owner!);

        // Every bound buffer and texture stays leased until the GPU is done with it (see ShaderArgumentWriter.SetBuffer).
        List<ObjCHandle> leased = [];

        try
        {
            return Encode(
                grid,
                threadsPerThreadgroup,
                queue.Pointer,
                state.Pointer,
                output is null ? 0 : outputTexture.Pointer,
                leased,
                in shader,
                window,
                whileGpuRuns,
                indirect.Handle is null ? 0 : indirectArguments.Pointer,
                indirect.Offset);
        }
        finally
        {
            ReleaseLeases(leased);
        }
    }

    /// <summary>
    /// Releases every lease even if one of them throws (the last release of a wrapped buffer runs the
    /// caller's release callback), then reports the failures.
    /// </summary>
    internal static void ReleaseLeases(List<ObjCHandle> leased)
    {
        List<Exception>? failures = null;

        foreach (ObjCHandle handle in leased)
        {
            try
            {
                handle.DangerousRelease();
            }
            catch (Exception e)
            {
                (failures ??= []).Add(e);
            }
        }

        leased.Clear();

        if (failures is not null)
        {
            throw new AggregateException("A release callback threw while a dispatch's resources were released.", failures);
        }
    }

    /// <summary>
    /// Encodes one dispatch into a new command buffer, commits it and waits for it, running
    /// <paramref name="whileGpuRuns"/> in between (see <see cref="Run{T}"/>).
    /// </summary>
    private (double GpuStart, double GpuEnd) Encode<T>(
        MTLSize grid,
        MTLSize threadsPerThreadgroup,
        nint queue,
        nint state,
        nint outputTexture,
        List<ObjCHandle> leased,
        in T shader,
        GridWindow? window,
        Action? whileGpuRuns,
        nint indirectArguments,
        nuint indirectOffset)
        where T : struct, IComputeShaderDescriptor<T>
    {
        nint commandBuffer = ObjC.SendIntPtr(queue, Selectors.CommandBuffer);

        if (commandBuffer == 0)
        {
            throw new InvalidOperationException("Metal failed to create a command buffer.");
        }

        nint encoder = ObjC.SendIntPtr(commandBuffer, Selectors.ComputeCommandEncoder);

        if (encoder == 0)
        {
            throw new InvalidOperationException("Metal failed to create a compute command encoder.");
        }

        try
        {
            ObjC.SendVoid(encoder, Selectors.SetComputePipelineState, state);

            if (outputTexture != 0)
            {
                ObjC.SendVoid(encoder, Selectors.SetTextureAtIndex, outputTexture, (nuint)0);
            }

            ShaderArgumentWriter writer = new(encoder, this, leased);
            T.SetArguments(in shader, ref writer);

            if (window is { } gridWindow)
            {
                writer.SetBytes(T.GridWindowIndex, in gridWindow);
            }

            if (indirectArguments != 0)
            {
                ObjC.SendVoid(encoder, Selectors.DispatchThreadgroupsWithIndirectBuffer, indirectArguments, indirectOffset, threadsPerThreadgroup);
            }
            else
            {
                ObjC.SendVoid(encoder, Selectors.DispatchThreadsThreadsPerThreadgroup, grid, threadsPerThreadgroup);
            }
        }
        finally
        {
            // Metal aborts the process if an encoder is deallocated without endEncoding, so this
            // must run even when binding arguments throws. The command buffer is then dropped uncommitted.
            ObjC.SendVoid(encoder, Selectors.EndEncoding);
        }

        ObjC.SendVoid(commandBuffer, Selectors.Commit);

        try
        {
            whileGpuRuns?.Invoke();
        }
        finally
        {
            // The GPU may still be using memory the caller is about to release or reuse.
            ObjC.SendVoid(commandBuffer, Selectors.WaitUntilCompleted);
        }

        // Read before the status check: LastGpuTime describes a command buffer that failed as well.
        double gpuStart = ObjC.SendDouble(commandBuffer, Selectors.GPUStartTime);
        double gpuEnd = ObjC.SendDouble(commandBuffer, Selectors.GPUEndTime);
        SetLastGpuTime(GpuTimeTicks(gpuStart, gpuEnd));

        if (ObjC.SendNUInt(commandBuffer, Selectors.Status) == Metal.CommandBufferStatusError)
        {
            string description = NSString.DescribeError(ObjC.SendIntPtr(commandBuffer, Selectors.Error));

            throw new MetalException($"The GPU failed to execute shader '{typeof(T)}': {description}");
        }

        return (gpuStart, gpuEnd);
    }

    private ComputePipeline GetPipeline<T>()
        where T : struct, IComputeShaderDescriptor<T> => GetPipeline<T>(windowed: false);

    /// <summary>The specialization that reads the grid window; compiled on the first split dispatch of <typeparamref name="T"/>.</summary>
    private ComputePipeline GetWindowedPipeline<T>()
        where T : struct, IComputeShaderDescriptor<T> => GetPipeline<T>(windowed: true);

    private ComputePipeline GetPipeline<T>(bool windowed)
        where T : struct, IComputeShaderDescriptor<T>
    {
        // Checked before compiling, so that the error names the shader's [GroupShared] total and the
        // device's limit; Metal itself would only fail the pipeline with a less specific message.
        if (T.ThreadgroupMemoryLength < 0 || (nuint)T.ThreadgroupMemoryLength > MaxThreadgroupMemoryLength)
        {
            throw new NotSupportedException(
                $"Shader '{typeof(T)}' declares {T.ThreadgroupMemoryLength} bytes of threadgroup ([GroupShared]) memory, " +
                $"but '{Name}' supports at most {MaxThreadgroupMemoryLength} bytes per kernel.");
        }

        // Lazy makes concurrent first uses of a shader (dispatches, Prepare) compile it once. A compile
        // error is cached, which is right because the source is a constant of the type; any other
        // failure (out of memory, a dispose race) evicts the entry so a later dispatch can retry.
        // The pipeline comes from the assembly's precompiled library when that holds this very
        // shader, and from its MSL otherwise (GraphicsDevice.Precompiled.cs).
        PipelineKey key = new(typeof(T), windowed, T.CompileOptions);
        Lazy<ComputePipeline> lazy = _pipelines.GetOrAdd(
            key,
            static (key, device) => new Lazy<ComputePipeline>(
                () => device.TryCreatePrecompiledPipeline(T.MslSource, T.EntryPoint, key.Shader, key.Windowed, key.Options)
                    ?? device.CompilePipeline(T.MslSource, T.EntryPoint, key.Shader, key.Windowed, key.Options),
                LazyThreadSafetyMode.ExecutionAndPublication),
            this);

        ComputePipeline pipeline;

        try
        {
            pipeline = lazy.Value;
        }
        catch (Exception e) when (e is not ShaderCompilationException)
        {
            _pipelines.TryRemove(new(key, lazy));
            throw;
        }

        // Gpu.WaveActiveBallot packs one vote per lane into a uint, and the documented lane layout is
        // 32 consecutive threads. Every Apple GPU has SIMD groups of 32; refuse a wider one rather than
        // run a shader whose ballots silently lose lanes.
        if (T.UsesSimdGroups && pipeline.ThreadExecutionWidth > CpuSimdGroup.Width)
        {
            throw new NotSupportedException(
                $"Shader '{typeof(T)}' uses SIMD-group operations, which SharpCompute supports for SIMD groups of up to " +
                $"{CpuSimdGroup.Width} threads, but '{Name}' executes it in groups of {pipeline.ThreadExecutionWidth}.");
        }

        return pipeline;
    }

    /// <summary>
    /// True when the pipeline of <typeparamref name="T"/> (with <paramref name="windowed"/>, its split
    /// specialization) has been compiled and is cached, not merely being compiled.
    /// </summary>
    internal bool IsPipelineCached<T>(bool windowed = false)
        where T : struct, IComputeShaderDescriptor<T> =>
        _pipelines.TryGetValue(new PipelineKey(typeof(T), windowed, T.CompileOptions), out Lazy<ComputePipeline>? lazy) && lazy.IsValueCreated;

    /// <summary>Tests only: how many times this device has compiled MSL into a pipeline (successfully or not).</summary>
    internal int PipelineCompileCount => Volatile.Read(ref _pipelineCompileCount);

    /// <summary>The line the runtime puts ahead of a shader's MSL for <see cref="ShaderCompileOptions.NoFpContract"/>.</summary>
    internal const string FpContractOffPragma = PrecompiledShaderContract.FpContractOffPragma;

    /// <summary>
    /// Compiles <paramref name="mslSource"/> and builds a pipeline for <paramref name="entryPoint"/>.
    /// Not cached: callers that are not shader descriptors (the presentation kernel) own the returned
    /// pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="windowed"/> is the value of bool function constant 0, which generated kernels that
    /// read grid positions declare to select the partial-grid (windowed) code. Such a kernel cannot be
    /// built unspecialized, so the function is always specialized; Metal ignores the value for kernels
    /// that declare no constant.
    /// </para>
    /// <para>
    /// <paramref name="options"/> are the shader's own (<see cref="ShaderOptionsAttribute"/>). Without
    /// any, the compile options are fixed: MSL 3.2, <c>mathMode = Safe</c> and precise math functions.
    /// <see cref="ShaderCompileOptions.FastMath"/> switches both of those to Metal's fast variants.
    /// <see cref="ShaderCompileOptions.NoFpContract"/> has no <c>MTLCompileOptions</c> property, so it is
    /// a pragma on a line put ahead of the source; the pragma holds to the end of the translation unit,
    /// and a compile error then reports the source as it was compiled, pragma included, so its line
    /// numbers match.
    /// </para>
    /// </remarks>
    internal ComputePipeline CompilePipeline(
        string mslSource, string entryPoint, Type shaderType, bool windowed = false, ShaderCompileOptions options = ShaderCompileOptions.None)
    {
        using AutoreleasePool pool = AutoreleasePool.Push();
        using ObjCHandle.Lease device = _device.Acquire(this);

        Interlocked.Increment(ref _pipelineCompileCount);

        // The same text the build tool precompiles: the pragma goes ahead of the source.
        mslSource = PrecompiledShaderContract.CompiledSource(mslSource, (int)options);

        bool fastMath = (options & ShaderCompileOptions.FastMath) != 0;
        nint source = 0, compileOptions = 0, library = 0;

        try
        {
            source = NSString.Create(mslSource);

            compileOptions = ObjC.SendIntPtr(ObjC.SendIntPtr(CompileOptionsClass.Value, Selectors.Alloc), Selectors.Init);
            ObjC.SendVoid(compileOptions, Selectors.SetLanguageVersion, Metal.LanguageVersion3_2);
            ObjC.SendVoid(compileOptions, Selectors.SetMathMode, fastMath ? Metal.MathModeFast : Metal.MathModeSafe);
            ObjC.SendVoid(
                compileOptions,
                Selectors.SetMathFloatingPointFunctions,
                fastMath ? Metal.MathFloatingPointFunctionsFast : Metal.MathFloatingPointFunctionsPrecise);

            // NSError out-parameters are autoreleased, so they are read before the pool drains.
            nint error = 0;
            library = ObjC.SendIntPtr(device.Pointer, Selectors.NewLibraryWithSourceOptionsError, source, compileOptions, &error);

            // A non-nil library with a non-nil error means "compiled with warnings".
            if (library == 0)
            {
                throw new ShaderCompilationException(
                    $"Failed to compile the MSL for shader '{shaderType}': {NSString.DescribeError(error)}", mslSource);
            }

            return BuildPipeline(device.Pointer, library, entryPoint, windowed, isPrecompiled: false, out PipelineStage failed, out string description) ?? throw new ShaderCompilationException(
                failed == PipelineStage.Function
                    // A missing entry point also lands here (as an error rather than a nil-without-error).
                    ? $"The MSL for shader '{shaderType}' compiled, but no function named '{entryPoint}' could be specialized from it: {description}"
                    : $"Failed to create the compute pipeline for shader '{shaderType}' (entry point '{entryPoint}'): {description}",
                mslSource);
        }
        finally
        {
            ObjC.Release(library);
            ObjC.Release(compileOptions);
            ObjC.Release(source);
        }
    }

    /// <summary>
    /// Specializes <paramref name="functionName"/> of <paramref name="library"/> with
    /// <paramref name="windowed"/> as bool function constant 0 and builds its pipeline state. Returns
    /// null when Metal refuses, with the step that <paramref name="failed"/> and Metal's
    /// <paramref name="description"/> of why. The caller holds an autorelease pool.
    /// </summary>
    /// <remarks>
    /// The library is one compiled from source just now or one loaded from a precompiled file: from
    /// here on the two are handled alike, which is why a kernel's windowed specialization needs
    /// nothing of its own in a precompiled library. Function constants are resolved here, not when
    /// the MSL is compiled.
    /// </remarks>
    private static ComputePipeline? BuildPipeline(
        nint device, nint library, string functionName, bool windowed, bool isPrecompiled, out PipelineStage failed, out string description)
    {
        nint name = 0, function = 0, constantValues = 0;

        try
        {
            name = NSString.Create(functionName);
            constantValues = ObjC.SendIntPtr(ObjC.SendIntPtr(FunctionConstantValuesClass.Value, Selectors.Alloc), Selectors.Init);

            byte windowedValue = windowed ? (byte)1 : (byte)0;
            ObjC.SendVoid(constantValues, Selectors.SetConstantValueTypeAtIndex, &windowedValue, Metal.DataTypeBool, (nuint)0);

            nint error = 0;
            function = ObjC.SendIntPtr(library, Selectors.NewFunctionWithNameConstantValuesError, name, constantValues, &error);

            if (function == 0)
            {
                failed = PipelineStage.Function;
                description = NSString.DescribeError(error);
                return null;
            }

            error = 0;
            nint state = ObjC.SendIntPtr(device, Selectors.NewComputePipelineStateWithFunctionError, function, &error);

            if (state == 0)
            {
                failed = PipelineStage.State;
                description = NSString.DescribeError(error);
                return null;
            }

            failed = PipelineStage.None;
            description = string.Empty;

            return new ComputePipeline(
                new ObjCHandle(state),
                ObjC.SendNUInt(state, Selectors.MaxTotalThreadsPerThreadgroup),
                ObjC.SendNUInt(state, Selectors.ThreadExecutionWidth),
                isPrecompiled);
        }
        finally
        {
            ObjC.Release(function);
            ObjC.Release(constantValues);
            ObjC.Release(name);
        }
    }

    /// <summary>The step of <see cref="BuildPipeline"/> that Metal refused.</summary>
    private enum PipelineStage
    {
        None,

        /// <summary><c>newFunctionWithName:constantValues:error:</c>: the library has no such function, or it cannot be specialized.</summary>
        Function,

        /// <summary><c>newComputePipelineStateWithFunction:error:</c>.</summary>
        State,
    }

    /// <summary>What identifies a cached pipeline: the shader, whether it is the windowed specialization, and how it is compiled.</summary>
    private readonly record struct PipelineKey(Type Shader, bool Windowed, ShaderCompileOptions Options);

    /// <summary>
    /// The buffer an indirect dispatch reads its threadgroup counts from: the handle to lease (under
    /// <paramref name="owner"/>, for the error message) and the byte offset of the counts in it.
    /// The default value means a direct dispatch.
    /// </summary>
    private readonly struct IndirectArguments(ObjCHandle handle, object owner, nuint offset)
    {
        public ObjCHandle? Handle { get; } = handle;

        public object? Owner { get; } = owner;

        public nuint Offset { get; } = offset;
    }

    private static class CompileOptionsClass
    {
        // Looked up lazily: the class only exists once Metal.framework is loaded, which creating a device guarantees.
        internal static readonly nint Value = ObjC.GetClass("MTLCompileOptions");
    }

    private static class FunctionConstantValuesClass
    {
        internal static readonly nint Value = ObjC.GetClass("MTLFunctionConstantValues");
    }
}
