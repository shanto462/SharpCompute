using System;
using System.Collections.Generic;
using SharpCompute.Interop;

namespace SharpCompute;

// CPU execution of shaders: the same shader struct and buffers, run as plain C# on worker threads.
public sealed unsafe partial class GraphicsDevice
{
    /// <summary>
    /// The thread limit used to size default threadgroups on the CPU (where <see cref="GroupIds"/> and
    /// <see cref="ThreadGroupIds"/> are derived from them). It is the usual limit of a compiled kernel,
    /// so the CPU picks the same default size as the GPU unless a kernel is too large for 1024 threads.
    /// </summary>
    private const int CpuMaxThreadsPerThreadgroup = 1024;

    /// <summary>Runs <paramref name="x"/> threads of <paramref name="shader"/> on the CPU and returns when all have run.</summary>
    /// <inheritdoc cref="ForCpu{T}(int, int, int, in T)" path="/remarks"/>
    /// <inheritdoc cref="ForCpu{T}(int, int, int, in T)" path="/exception"/>
    public void ForCpu<T>(int x, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => DispatchCpu(x, 1, 1, in shader);

    /// <summary>Runs an <paramref name="x"/> × <paramref name="y"/> grid of <paramref name="shader"/> on the CPU and returns when all threads have run.</summary>
    /// <inheritdoc cref="ForCpu{T}(int, int, int, in T)" path="/remarks"/>
    /// <inheritdoc cref="ForCpu{T}(int, int, int, in T)" path="/exception"/>
    public void ForCpu<T>(int x, int y, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => DispatchCpu(x, y, 1, in shader);

    /// <summary>
    /// Runs an <paramref name="x"/> × <paramref name="y"/> × <paramref name="z"/> grid of
    /// <paramref name="shader"/> on the CPU and returns when all threads have run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IComputeShader.Execute"/> runs as ordinary C# once per thread of the grid, spread over
    /// the thread pool with <c>Parallel.For</c> (in chunks of consecutive threads, x fastest). Inside it,
    /// <see cref="ThreadIds"/>, <see cref="DispatchSize"/>, <see cref="GroupIds"/>,
    /// <see cref="ThreadGroupIds"/>, <see cref="GroupSize"/> and <see cref="ThreadGroupCount"/> return
    /// the values the GPU would, buffers and textures are read and
    /// written in place in the shared memory the GPU uses, and the <see cref="Gpu"/> intrinsics use their
    /// CPU implementations. The grid is the same as the GPU's, and so is the order of threads: none.
    /// </para>
    /// <para>
    /// Results match a GPU dispatch up to floating-point rounding: the GPU contracts <c>a * b + c</c>
    /// into fused multiply-adds, flushes subnormals to zero and evaluates transcendental functions to
    /// within a few ULP, so compare float results with a tolerance. Integer results are identical
    /// (conversions from float saturate, shift counts are masked and arithmetic wraps on both), except
    /// for the two divisions below. Texture reads and writes convert pixels as the GPU does;
    /// <see cref="ReadOnlyTexture2D{T}.Sample"/> with a linear filter and reads of <see cref="Rgba32Srgb"/>
    /// are deliberately exact rather than as coarse as the GPU's, so a bilinear sample written to an
    /// 8-bit format can differ by one step between the two, and a point sample with Repeat or Mirror
    /// outside [0, 1) can land on the neighbouring pixel right at a pixel boundary (see those members).
    /// </para>
    /// <para>
    /// Differences from the GPU: buffer and texture accesses are bounds-checked (an out-of-range index
    /// or pixel throws <see cref="ArgumentOutOfRangeException"/> instead of being undefined), integer division by zero
    /// throws <see cref="DivideByZeroException"/> and <c>int.MinValue / -1</c> throws
    /// <see cref="OverflowException"/> (the GPU returns -1 and <c>int.MinValue</c> for those), and an exception in any thread stops the dispatch and
    /// is rethrown (the first one, if several threads throw). After an exception the output is
    /// undefined: threads that ran before or alongside the failing one may already have written their
    /// elements, and the rest have not run. On the GPU the same out-of-range access or division is not
    /// reported, and its result is undefined.
    /// </para>
    /// <para>
    /// <b>Shaders with threadgroup memory or barriers</b> (<see cref="GroupSharedAttribute"/>,
    /// <see cref="Gpu.GroupMemoryBarrierWithGroupSync"/> and its siblings) run differently, because their
    /// threads cooperate: the threads of one threadgroup run concurrently, each on a dedicated thread
    /// of its own (not the thread pool), and meet at every barrier; the threadgroups run one after
    /// another, in row-major order, with the shader's <see cref="GroupSharedAttribute"/> arrays as the
    /// memory of the group that is running. Dispatches of one such shader type are serialized
    /// process-wide for the same reason. A threadgroup can have at most 1024 threads (Metal's limit as
    /// well; a larger one throws <see cref="NotSupportedException"/>). This is built to be correct, not
    /// fast: every barrier parks and wakes every thread of the group, so expect microseconds per thread
    /// per barrier, thousands of times slower than the GPU. It is meant for checking such a shader
    /// against the GPU and for debugging it, not for bulk work. A thread that returns from
    /// <c>Execute</c> stops taking part in its group's barriers, so a shader whose threads disagree
    /// about the barriers they call (undefined on the GPU) still finishes. If a thread throws, the
    /// threads of its group stop at the next barrier they reach, no further group runs, and the
    /// exception is rethrown.
    /// </para>
    /// <para>
    /// The shader's buffers and textures must not be disposed during the dispatch (an access then throws
    /// <see cref="ObjectDisposedException"/>; the memory itself stays valid until the dispatch returns).
    /// Do not dispatch shaders that write the same buffers or textures concurrently, on the CPU or the GPU.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is negative.</exception>
    /// <exception cref="ArgumentNullException">A buffer or texture of <paramref name="shader"/> is null, e.g. for <c>default(T)</c>.</exception>
    /// <exception cref="ArgumentException">A buffer or texture of <paramref name="shader"/> was allocated from a different device.</exception>
    /// <exception cref="ObjectDisposedException">The device or a buffer or texture of <paramref name="shader"/> has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The shader's <see cref="ThreadGroupSizeAttribute"/> is invalid.</exception>
    /// <exception cref="NotSupportedException">
    /// The shader uses threadgroup memory or barriers and its threadgroups have more than 1024 threads, or
    /// it binds a native buffer or texture the CPU cannot access (<see cref="Buffer{T}.IsCpuAccessible"/>, <see cref="Texture2D{T}.IsCpuAccessible"/>).
    /// </exception>
    public void ForCpu<T>(int x, int y, int z, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => DispatchCpu(x, y, z, in shader);

    /// <summary>
    /// Runs a pixel shader once per pixel of <paramref name="texture"/> on the CPU, storing each returned
    /// color in the texture's shared memory, and returns when all pixels have been written.
    /// </summary>
    /// <remarks>
    /// The grid, built-ins and threading are those of <see cref="ForCpu{T}(int, int, int, in T)"/>, with
    /// a <c>Width × Height × 1</c> grid as in <see cref="ForEach{T}"/>. Colors are converted to
    /// <c>RGBA8Unorm</c> as the GPU does (NaN → 0, clamp to [0, 1], round the exact <c>× 255</c> to nearest),
    /// so the same color gives the same bytes on both; colors that differ by float rounding can land on
    /// neighbouring byte values.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> or a buffer of <paramref name="shader"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="texture"/> or a buffer of <paramref name="shader"/> was allocated from a different device.</exception>
    /// <exception cref="ObjectDisposedException">The device, <paramref name="texture"/> or a buffer of <paramref name="shader"/> has been disposed.</exception>
    /// <exception cref="NotSupportedException"><paramref name="texture"/> or a resource of <paramref name="shader"/> is a native one the CPU cannot access.</exception>
    public void ForEachCpu<T>(ReadWriteTexture2D<Rgba32> texture, in T shader)
        where T : struct, IComputeShader<Float4>, IComputeShaderDescriptor<T>
    {
        ValidateOutput(texture);

        Extent3 grid = new(texture.Width, texture.Height, 1);
        Extent3 groupSize = ResolveCpuThreadGroupSize<T>(rank: 2);

        using CpuLeases leases = new();
        leases.Add(texture.BufferHandle, texture);
        LeaseArguments(in shader, leases);

        PixelKernel<T> kernel = new(shader, texture.Contents, texture.BytesPerRow);
        RunCpu<T, PixelKernel<T>>(kernel, grid, groupSize, grid);
    }

    /// <summary>Checks a pixel shader's output texture like <see cref="ForEach{T}"/> does.</summary>
    private void ValidateOutput(ReadWriteTexture2D<Rgba32> texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);

        if (!IsSameDevice(texture.Device))
        {
            throw new ArgumentException(
                "The texture was allocated from a different GraphicsDevice than the one dispatching the shader.", nameof(texture));
        }

        // The CPU writes the pixels straight into the texture's memory.
        texture.ThrowIfNotCpuAccessible();
    }

    private void DispatchCpu<T>(int x, int y, int z, in T shader)
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

        RunCpu(x, y, z, ResolveCpuThreadGroupSize<T>(InferRank(y, z)), in shader);
    }

    /// <summary>Runs a validated grid (possibly empty) of <paramref name="shader"/> on the CPU with the given threadgroup size.</summary>
    private void RunCpu<T>(int x, int y, int z, Extent3 groupSize, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T>
    {
        // The arguments are checked and leased even for an empty grid, as a GPU dispatch of 0 groups checks them.
        using CpuLeases leases = new();
        LeaseArguments(in shader, leases);

        Extent3 grid = new(x, y, z);
        RunCpu<T, ComputeKernel<T>>(new ComputeKernel<T>(shader), grid, groupSize, grid);
    }

    /// <summary>
    /// Runs the leading <paramref name="count"/> threads of <paramref name="grid"/> on the CPU: spread
    /// over the thread pool when the shader's threads are independent, threadgroup by threadgroup on
    /// dedicated threads when they cooperate through group-shared memory or barriers.
    /// </summary>
    private static void RunCpu<T, TKernel>(TKernel kernel, Extent3 grid, Extent3 groupSize, Extent3 count)
        where T : struct, IComputeShaderDescriptor<T>
        where TKernel : struct, ICpuKernel
    {
        if (T.UsesThreadgroupMemory)
        {
            CpuGroupDispatcher.Run(kernel, grid.X, grid.Y, grid.Z, groupSize, default, count, ThreadgroupGate<T>.Lock, typeof(T));
        }
        else
        {
            CpuDispatcher.Run(kernel, grid.X, grid.Y, grid.Z, groupSize, default, count);
        }
    }

    /// <summary>
    /// Runs the shader's <c>SetArguments</c> in CPU mode: the same null, disposed and wrong-device checks
    /// as a GPU dispatch, and a lease on every buffer and texture until <paramref name="leases"/> is disposed.
    /// </summary>
    private void LeaseArguments<T>(in T shader, CpuLeases leases)
        where T : struct, IComputeShaderDescriptor<T>
    {
        ShaderArgumentWriter writer = new(this, leases.Handles);
        T.SetArguments(in shader, ref writer);
    }

    private static Extent3 ResolveCpuThreadGroupSize<T>(int rank)
        where T : struct, IComputeShaderDescriptor<T> =>
        ToExtent(ResolveThreadGroupSize(T.ThreadGroupSizeX, T.ThreadGroupSizeY, T.ThreadGroupSizeZ, rank, CpuMaxThreadsPerThreadgroup, typeof(T)));

    private static Extent3 ToExtent(MTLSize size) => new((int)size.Width, (int)size.Height, (int)size.Depth);

    /// <summary>Leases held for the duration of a CPU dispatch (the buffers and output texture it accesses).</summary>
    private sealed class CpuLeases : IDisposable
    {
        internal List<ObjCHandle> Handles { get; } = [];

        internal void Add(ObjCHandle handle, object owner)
        {
            handle.AddLease(owner);
            Handles.Add(handle);
        }

        public void Dispose()
        {
            foreach (ObjCHandle handle in Handles)
            {
                handle.DangerousRelease();
            }

            Handles.Clear();
        }
    }
}
