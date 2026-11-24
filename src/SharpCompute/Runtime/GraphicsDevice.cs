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
/// All members are thread-safe. Every dispatch uses its own command buffer and encoder, and the
/// device, queue and pipeline states they share are safe to use concurrently.
/// </remarks>
public sealed unsafe partial class GraphicsDevice : IDisposable
{
    private static readonly Lazy<GraphicsDevice?> s_default =
        new(static () => TryCreateSystemDefault(isDefault: true), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly ObjCHandle _device;
    private readonly ObjCHandle _queue;
    private readonly ConcurrentDictionary<Type, Lazy<ComputePipeline>> _pipelines = new();
    private readonly bool _isDefault;
    private int _disposed;

    private GraphicsDevice(nint device, bool isDefault)
    {
        _isDefault = isDefault;
        _device = new ObjCHandle(device);

        try
        {
            using AutoreleasePool pool = AutoreleasePool.Push();

            nint queue = ObjC.SendIntPtr(device, Selectors.NewCommandQueue);

            if (queue == 0)
            {
                throw new InvalidOperationException("Metal failed to create a command queue.");
            }

            _queue = new ObjCHandle(queue);
            Name = NSString.ToManaged(ObjC.SendIntPtr(device, Selectors.Name)) ?? string.Empty;
            MaxBufferLength = ObjC.SendNUInt(device, Selectors.MaxBufferLength);
        }
        catch
        {
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

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>The <c>id&lt;MTLDevice&gt;</c>. Lease it (<see cref="ObjCHandle.Acquire"/>) for the duration of a native call.</summary>
    internal ObjCHandle DeviceHandle => _device;

    /// <summary>The device's <c>id&lt;MTLCommandQueue&gt;</c>, shared by every dispatch and by presentation.</summary>
    internal ObjCHandle QueueHandle => _queue;

    /// <summary>
    /// A new, independently disposable wrapper around the system default device. It has its own
    /// command queue and pipeline cache, and buffers are not interchangeable with other wrappers.
    /// </summary>
    internal static GraphicsDevice CreateSystemDefault() => TryCreateSystemDefault(isDefault: false) ?? throw NotSupported();

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
    /// <see cref="WrapReadOnlyBuffer{T}"/> need memory that starts on a page boundary, e.g. from
    /// <c>NativeMemory.AlignedAlloc(size, (nuint)GraphicsDevice.PageSize)</c>.
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
            return new ReadOnlyBuffer<T>(this, handle, (T*)start, length, isCpuWritable: false);
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
    /// Allocates a zero-filled <paramref name="width"/> × <paramref name="height"/> GPU-writable texture.
    /// v1: <c>T</c> = <see cref="Rgba32"/>.
    /// </summary>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> is not <see cref="Rgba32"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not in 1..16384.</exception>
    public ReadWriteTexture2D<T> AllocateReadWriteTexture2D<T>(int width, int height)
        where T : unmanaged => new(this, width, height);

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

        if (!ReferenceEquals(texture.Device, this))
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
            if (pipeline.IsValueCreated)
            {
                pipeline.Value.Dispose();
            }
        }

        _pipelines.Clear();
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

    private static GraphicsDevice? TryCreateSystemDefault(bool isDefault)
    {
        // MSL 3.2 and MTLCompileOptions.mathMode (both fixed by the contract) need macOS 15, and the
        // objc_msgSend call shapes in Interop are written for the arm64 ABI.
        if (!OperatingSystem.IsMacOSVersionAtLeast(15) || RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
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
    /// The encode/commit/wait path shared by <c>For</c> and <c>ForEach</c>. When <paramref name="output"/>
    /// is non-null it is bound at texture index 0 before the shader's own arguments.
    /// </summary>
    private void Run<T>(MTLSize grid, int rank, ReadWriteTexture2D<Rgba32>? output, in T shader)
        where T : struct, IComputeShaderDescriptor<T>
    {
        ComputePipeline pipeline = GetPipeline<T>();
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

        // Every bound buffer stays leased until the GPU is done with it (see ShaderArgumentWriter.SetBuffer).
        List<ObjCHandle> leased = [];

        try
        {
            Encode(grid, threadsPerThreadgroup, queue.Pointer, state.Pointer, output is null ? 0 : outputTexture.Pointer, leased, in shader);
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

    /// <summary>Encodes one dispatch into a new command buffer, commits it and waits for it.</summary>
    private void Encode<T>(MTLSize grid, MTLSize threadsPerThreadgroup, nint queue, nint state, nint outputTexture, List<ObjCHandle> leased, in T shader)
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

            ObjC.SendVoid(encoder, Selectors.DispatchThreadsThreadsPerThreadgroup, grid, threadsPerThreadgroup);
        }
        finally
        {
            // Metal aborts the process if an encoder is deallocated without endEncoding, so this
            // must run even when binding arguments throws. The command buffer is then dropped uncommitted.
            ObjC.SendVoid(encoder, Selectors.EndEncoding);
        }

        ObjC.SendVoid(commandBuffer, Selectors.Commit);
        ObjC.SendVoid(commandBuffer, Selectors.WaitUntilCompleted);

        if (ObjC.SendNUInt(commandBuffer, Selectors.Status) == Metal.CommandBufferStatusError)
        {
            string description = NSString.DescribeError(ObjC.SendIntPtr(commandBuffer, Selectors.Error));

            throw new MetalException($"The GPU failed to execute shader '{typeof(T)}': {description}");
        }
    }

    private ComputePipeline GetPipeline<T>()
        where T : struct, IComputeShaderDescriptor<T>
    {
        // Lazy makes concurrent first dispatches of a shader compile it once. A compile error is
        // cached, which is right because the source is a constant of the type; any other failure
        // (out of memory, a dispose race) evicts the entry so a later dispatch can retry.
        Lazy<ComputePipeline> lazy = _pipelines.GetOrAdd(
            typeof(T),
            static (type, device) => new Lazy<ComputePipeline>(
                () => device.CompilePipeline(T.MslSource, T.EntryPoint, type),
                LazyThreadSafetyMode.ExecutionAndPublication),
            this);

        try
        {
            return lazy.Value;
        }
        catch (Exception e) when (e is not ShaderCompilationException)
        {
            _pipelines.TryRemove(new(typeof(T), lazy));
            throw;
        }
    }

    /// <summary>
    /// Compiles <paramref name="mslSource"/> with the fixed compile options and builds a pipeline for
    /// <paramref name="entryPoint"/>. Not cached: callers that are not shader descriptors (the
    /// presentation kernel) own the returned pipeline.
    /// </summary>
    internal ComputePipeline CompilePipeline(string mslSource, string entryPoint, Type shaderType)
    {
        using AutoreleasePool pool = AutoreleasePool.Push();
        using ObjCHandle.Lease device = _device.Acquire(this);

        nint source = 0, options = 0, library = 0, functionName = 0, function = 0;

        try
        {
            source = NSString.Create(mslSource);

            options = ObjC.SendIntPtr(ObjC.SendIntPtr(CompileOptionsClass.Value, Selectors.Alloc), Selectors.Init);
            ObjC.SendVoid(options, Selectors.SetLanguageVersion, Metal.LanguageVersion3_2);
            ObjC.SendVoid(options, Selectors.SetMathMode, Metal.MathModeSafe);
            ObjC.SendVoid(options, Selectors.SetMathFloatingPointFunctions, Metal.MathFloatingPointFunctionsPrecise);

            // NSError out-parameters are autoreleased, so they are read before the pool drains.
            nint error = 0;
            library = ObjC.SendIntPtr(device.Pointer, Selectors.NewLibraryWithSourceOptionsError, source, options, &error);

            // A non-nil library with a non-nil error means "compiled with warnings".
            if (library == 0)
            {
                throw new ShaderCompilationException(
                    $"Failed to compile the MSL for shader '{shaderType}': {NSString.DescribeError(error)}", mslSource);
            }

            functionName = NSString.Create(entryPoint);
            function = ObjC.SendIntPtr(library, Selectors.NewFunctionWithName, functionName);

            if (function == 0)
            {
                throw new ShaderCompilationException(
                    $"The MSL for shader '{shaderType}' compiled, but contains no function named '{entryPoint}'.", mslSource);
            }

            error = 0;
            nint state = ObjC.SendIntPtr(device.Pointer, Selectors.NewComputePipelineStateWithFunctionError, function, &error);

            if (state == 0)
            {
                throw new ShaderCompilationException(
                    $"Failed to create the compute pipeline for shader '{shaderType}' (entry point '{entryPoint}'): {NSString.DescribeError(error)}", mslSource);
            }

            return new ComputePipeline(new ObjCHandle(state), ObjC.SendNUInt(state, Selectors.MaxTotalThreadsPerThreadgroup));
        }
        finally
        {
            ObjC.Release(function);
            ObjC.Release(functionName);
            ObjC.Release(library);
            ObjC.Release(options);
            ObjC.Release(source);
        }
    }

    private static class CompileOptionsClass
    {
        // Looked up lazily: the class only exists once Metal.framework is loaded, which creating a device guarantees.
        internal static readonly nint Value = ObjC.GetClass("MTLCompileOptions");
    }
}
