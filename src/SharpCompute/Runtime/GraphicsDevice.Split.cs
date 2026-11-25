using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using SharpCompute.Interop;

namespace SharpCompute;

// CPU+GPU split dispatches: one grid, two disjoint parts, run concurrently on the same shared memory.
public sealed unsafe partial class GraphicsDevice
{
    // One adaptive balance per shader type and grid-size bucket (log2 of the thread count): the fixed
    // submission cost makes a small grid look far more expensive per thread than a large one, so
    // estimates from different sizes must not be mixed.
    private readonly ConcurrentDictionary<(Type Shader, int SizeBucket), SplitBalance> _splitBalances = new();

    /// <summary>Runs <paramref name="x"/> threads of <paramref name="shader"/>, part on the CPU and part on the GPU at the same time.</summary>
    /// <inheritdoc cref="ForSplit{T}(int, int, int, in T, double)" path="/remarks"/>
    /// <inheritdoc cref="ForSplit{T}(int, int, int, in T, double)" path="/returns"/>
    /// <inheritdoc cref="ForSplit{T}(int, int, int, in T, double)" path="/exception"/>
    public SplitDispatchInfo ForSplit<T>(int x, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => DispatchSplit(x, 1, 1, in shader, cpuFraction: null);

    /// <summary>Runs an <paramref name="x"/> × <paramref name="y"/> grid of <paramref name="shader"/>, part on the CPU and part on the GPU at the same time.</summary>
    /// <inheritdoc cref="ForSplit{T}(int, int, int, in T, double)" path="/remarks"/>
    /// <inheritdoc cref="ForSplit{T}(int, int, int, in T, double)" path="/returns"/>
    /// <inheritdoc cref="ForSplit{T}(int, int, int, in T, double)" path="/exception"/>
    public SplitDispatchInfo ForSplit<T>(int x, int y, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => DispatchSplit(x, y, 1, in shader, cpuFraction: null);

    /// <summary>Runs an <paramref name="x"/> × <paramref name="y"/> × <paramref name="z"/> grid of <paramref name="shader"/>, part on the CPU and part on the GPU at the same time.</summary>
    /// <inheritdoc cref="ForSplit{T}(int, int, int, in T, double)" path="/remarks"/>
    /// <inheritdoc cref="ForSplit{T}(int, int, int, in T, double)" path="/returns"/>
    /// <inheritdoc cref="ForSplit{T}(int, int, int, in T, double)" path="/exception"/>
    public SplitDispatchInfo ForSplit<T>(int x, int y, int z, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => DispatchSplit(x, y, z, in shader, cpuFraction: null);

    /// <summary>Runs <paramref name="x"/> threads, about <paramref name="cpuFraction"/> of them on the CPU and the rest on the GPU at the same time.</summary>
    /// <inheritdoc cref="ForSplit{T}(int, int, int, in T, double)" path="/remarks"/>
    /// <inheritdoc cref="ForSplit{T}(int, int, int, in T, double)" path="/returns"/>
    /// <inheritdoc cref="ForSplit{T}(int, int, int, in T, double)" path="/exception"/>
    public SplitDispatchInfo ForSplit<T>(int x, in T shader, double cpuFraction)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => DispatchSplit(x, 1, 1, in shader, cpuFraction);

    /// <summary>Runs an <paramref name="x"/> × <paramref name="y"/> grid, about <paramref name="cpuFraction"/> of it on the CPU and the rest on the GPU at the same time.</summary>
    /// <inheritdoc cref="ForSplit{T}(int, int, int, in T, double)" path="/remarks"/>
    /// <inheritdoc cref="ForSplit{T}(int, int, int, in T, double)" path="/returns"/>
    /// <inheritdoc cref="ForSplit{T}(int, int, int, in T, double)" path="/exception"/>
    public SplitDispatchInfo ForSplit<T>(int x, int y, in T shader, double cpuFraction)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => DispatchSplit(x, y, 1, in shader, cpuFraction);

    /// <summary>
    /// Runs an <paramref name="x"/> × <paramref name="y"/> × <paramref name="z"/> grid of
    /// <paramref name="shader"/>, about <paramref name="cpuFraction"/> of it on the CPU and the rest on
    /// the GPU at the same time, and returns when both parts have finished.
    /// </summary>
    /// <param name="x">Threads along x.</param>
    /// <param name="y">Threads along y.</param>
    /// <param name="z">Threads along z.</param>
    /// <param name="shader">The shader; every thread must write only its own elements (see remarks).</param>
    /// <param name="cpuFraction">
    /// The share of the grid to run on the CPU, 0..1 (0 = GPU only, 1 = CPU only). The overloads without
    /// it pick the share adaptively (see remarks).
    /// </param>
    /// <remarks>
    /// <para>
    /// The grid is cut along its slowest axis (x for a 1D grid, rows for 2D, slices for 3D) at a multiple
    /// of the threadgroup size: the CPU runs the leading part with <see cref="ForCpu{T}(int, int, int, in T)"/>'s
    /// machinery while the GPU runs the rest. Both parts see the whole grid: <see cref="ThreadIds"/>,
    /// <see cref="DispatchSize"/>, <see cref="ThreadIds.Normalized"/>, <see cref="GroupIds"/> and
    /// <see cref="ThreadGroupIds"/>, <see cref="GroupSize"/> and <see cref="ThreadGroupCount"/> have the
    /// values they would have in one GPU dispatch of the grid. The
    /// GPU part runs a specialization of the kernel that reads its offset in the grid from a hidden
    /// argument; it is compiled on the first split dispatch of <typeparamref name="T"/>, and normal
    /// dispatches do not pay for it.
    /// </para>
    /// <para>
    /// <b>Only split shaders whose threads write disjoint elements</b>: each thread may write only
    /// elements that no other thread of the dispatch reads or writes (typically its own pixel or index).
    /// The two parts share memory with no ordering between them, so a shader that scatters writes, that
    /// accumulates into a shared element (e.g. a histogram with <c>+=</c>) or that reads what other
    /// threads write is not safe to split, even if it happens to work as one GPU dispatch.
    /// A shader that uses threadgroup memory or barriers can be split when each threadgroup writes
    /// only its own outputs: threadgroups are never divided between the two devices, and the CPU runs
    /// its part with the group executor described for <see cref="ForCpu{T}(int, int, int, in T)"/>,
    /// which is far slower per thread than the GPU, so give it a small explicit share.
    /// </para>
    /// <para>
    /// <b>Shaders that use atomics on buffer elements are never divided.</b> <c>Gpu.Atomic*</c> makes a
    /// shared element safe among the threads of one dispatch on one device, but the GPU's atomics and the
    /// CPU's interlocked operations do not exclude each other on shared memory: a histogram split between
    /// the two loses counts. The generator records whether a shader uses such atomics
    /// (<see cref="IComputeShaderDescriptor{TSelf}.UsesDeviceAtomics"/>). For such a shader a
    /// <paramref name="cpuFraction"/> strictly between 0 and 1 throws <see cref="NotSupportedException"/>
    /// (0 and 1 run on one device and are fine), and the overloads without a fraction run the whole grid
    /// on the GPU and say so in <see cref="SplitDispatchInfo.SingleDeviceOnly"/>.
    /// </para>
    /// <para>
    /// <b>Atomics on threadgroup memory do not prevent a split.</b> An element of a
    /// <see cref="GroupSharedAttribute"/> array is shared only by the threads of one threadgroup, and a
    /// threadgroup runs entirely on one device, so no atomic of the CPU ever meets one of the GPU. A
    /// shader whose only atomics are of that kind is split like any other shader that uses threadgroup
    /// memory; one that also has an atomic on a buffer element (say, to merge each group's result into
    /// a global total) is not.
    /// </para>
    /// <para>
    /// Without <paramref name="cpuFraction"/>, the split adapts per shader type on this device: the CPU's
    /// share comes from running estimates of each side's time per thread, chosen so that both parts
    /// finish together, and the split is only used while it is measured to be faster than running the
    /// whole grid on the GPU, which it often is not: the GPU's part slows down while the CPU is busy.
    /// The first two dispatches of a shader type split (10% to the CPU), the next two run GPU-only, and
    /// after that the faster mode is used, with the other one re-measured occasionally (dispatches 4, 8,
    /// ..., 64 after the winner last changed, then every 64th). Estimates are kept per shader type and
    /// grid size (in powers of two), so dispatches of one shader at very different sizes are balanced
    /// separately. Within one size they assume the cost per thread is similar across the grid and
    /// between dispatches; a shader whose work per thread changes with its constants (an iteration
    /// count, say) is better served by an explicit <paramref name="cpuFraction"/>. Dispatches with an
    /// explicit fraction also feed these estimates for their shader type and size.
    /// </para>
    /// <para>
    /// Results match a GPU dispatch up to floating-point rounding, as described for
    /// <see cref="ForCpu{T}(int, int, int, in T)"/>. Shaders with hand-written descriptors, and generated
    /// shaders that use all 31 buffer indices, cannot be split.
    /// </para>
    /// <para>
    /// A shader with SIMD-group operations (the <c>Wave*</c> and <c>Quad*</c> methods of <see cref="Gpu"/>)
    /// is split like any other. The cut is on a threadgroup boundary, so every SIMD group is whole on
    /// one device and its lanes meet there; the CPU's part runs as <see cref="ForCpu{T}(int, int, int, in T)"/>
    /// describes for such shaders, on dedicated threads, which is far slower per thread than the GPU, so
    /// the adaptive share settles on the GPU alone. The CPU's limits for these operations apply to its
    /// part: divergent control flow there throws <see cref="NotSupportedException"/>.
    /// </para>
    /// <para>
    /// <paramref name="cpuFraction"/> 0 runs the whole grid on the GPU and 1 the whole grid on the CPU,
    /// exactly, for any grid size; other values are rounded to a threadgroup boundary.
    /// </para>
    /// <para>
    /// <b>Not bit-reproducible.</b> The CPU and the GPU do not compute floats bit for bit alike (the
    /// GPU fuses multiply-adds, flushes subnormals and evaluates transcendentals within a few ULP), and
    /// the adaptive share decides which device computes which elements, so two calls with the same
    /// input can return different bits in float results. Pass an explicit <paramref name="cpuFraction"/>
    /// when repeatable output matters: the same fraction on the same grid always cuts at the same
    /// place. Integer results do not depend on the device.
    /// </para>
    /// <para>
    /// <b>Errors.</b> If the CPU part throws (for example an out-of-range index, which the CPU checks and
    /// the GPU does not), the GPU part is still waited for and the CPU's exception is rethrown. The
    /// output is then undefined: the GPU part and part of the CPU part may have been written.
    /// </para>
    /// </remarks>
    /// <returns>How the grid was divided and how long each part took.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is negative, or <paramref name="cpuFraction"/> is not in 0..1.</exception>
    /// <exception cref="NotSupportedException">
    /// The shader cannot run a partial grid, or it uses atomics on buffer elements and <paramref name="cpuFraction"/> is strictly between 0 and 1 (see remarks), or the CPU's part has SIMD-group operations in divergent control flow.
    /// </exception>
    /// <exception cref="ArgumentNullException">A buffer of <paramref name="shader"/> is null, e.g. for <c>default(T)</c>.</exception>
    /// <exception cref="ArgumentException">A buffer of <paramref name="shader"/> was allocated from a different device, or two of the shader's buffers or textures overlap and it writes one of them (see <see cref="ShaderArgumentWriter"/>).</exception>
    /// <exception cref="ObjectDisposedException">The device or a buffer of <paramref name="shader"/> has been disposed.</exception>
    /// <exception cref="MetalException">The GPU failed to execute its part.</exception>
    public SplitDispatchInfo ForSplit<T>(int x, int y, int z, in T shader, double cpuFraction)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => DispatchSplit(x, y, z, in shader, cpuFraction);

    /// <summary>
    /// Runs a pixel shader once per pixel of <paramref name="texture"/>, part of the rows on the CPU and
    /// the rest on the GPU at the same time, with an adaptive share.
    /// </summary>
    /// <inheritdoc cref="ForEachSplit{T}(ReadWriteTexture2D{Rgba32}, in T, double)" path="/remarks"/>
    /// <inheritdoc cref="ForEachSplit{T}(ReadWriteTexture2D{Rgba32}, in T, double)" path="/returns"/>
    /// <inheritdoc cref="ForEachSplit{T}(ReadWriteTexture2D{Rgba32}, in T, double)" path="/exception"/>
    public SplitDispatchInfo ForEachSplit<T>(ReadWriteTexture2D<Rgba32> texture, in T shader)
        where T : struct, IComputeShader<Float4>, IComputeShaderDescriptor<T> => DispatchSplit(texture, in shader, cpuFraction: null);

    /// <summary>
    /// Runs a pixel shader once per pixel of <paramref name="texture"/>, about <paramref name="cpuFraction"/>
    /// of the rows on the CPU and the rest on the GPU at the same time.
    /// </summary>
    /// <param name="texture">The output texture; the grid is its <c>Width × Height</c>.</param>
    /// <param name="shader">The pixel shader.</param>
    /// <param name="cpuFraction">The share of the rows to run on the CPU, 0..1.</param>
    /// <remarks>
    /// The split, its rules and the adaptive share are those of <see cref="ForSplit{T}(int, int, int, in T, double)"/>;
    /// a pixel shader only writes its own pixel, so it is safe to split unless it also updates a buffer
    /// with atomics, in which case it is never divided, as described there. The CPU converts colors to
    /// <c>RGBA8Unorm</c> as described for <see cref="ForEachCpu{T}"/>.
    /// </remarks>
    /// <returns>How the rows were divided and how long each part took.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cpuFraction"/> is not in 0..1.</exception>
    /// <exception cref="NotSupportedException">
    /// The shader cannot run a partial grid (a hand-written descriptor), or it uses atomics on buffer elements and <paramref name="cpuFraction"/> is strictly between 0 and 1, or the CPU's part has SIMD-group operations in divergent control flow.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> or a buffer of <paramref name="shader"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="texture"/> or a buffer of <paramref name="shader"/> was allocated from a different device, or
    /// <paramref name="shader"/> captures <paramref name="texture"/> itself (or its read-only view), or overlapping resources it writes.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The device, <paramref name="texture"/> or a buffer of <paramref name="shader"/> has been disposed.</exception>
    /// <exception cref="MetalException">The GPU failed to execute its part.</exception>
    public SplitDispatchInfo ForEachSplit<T>(ReadWriteTexture2D<Rgba32> texture, in T shader, double cpuFraction)
        where T : struct, IComputeShader<Float4>, IComputeShaderDescriptor<T> => DispatchSplit(texture, in shader, cpuFraction);

    private SplitDispatchInfo DispatchSplit<T>(int x, int y, int z, in T shader, double? cpuFraction)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T>
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegative(z);
        ValidateSplit<T>(cpuFraction);

        if (x == 0 || y == 0 || z == 0)
        {
            return default;
        }

        using CpuLeases leases = new();
        LeaseArguments(in shader, leases);

        return RunSplit(new Extent3(x, y, z), InferRank(y, z), output: null, in shader, new ComputeKernel<T>(shader), cpuFraction);
    }

    private SplitDispatchInfo DispatchSplit<T>(ReadWriteTexture2D<Rgba32> texture, in T shader, double? cpuFraction)
        where T : struct, IComputeShader<Float4>, IComputeShaderDescriptor<T>
    {
        ValidateOutput(texture);
        ValidateSplit<T>(cpuFraction);

        using CpuLeases leases = new();
        leases.Add(texture.BufferHandle, texture);
        LeaseArguments(in shader, leases, texture);

        PixelKernel<T> kernel = new(shader, texture.Contents, texture.BytesPerRow);
        return RunSplit(new Extent3(texture.Width, texture.Height, 1), rank: 2, texture, in shader, kernel, cpuFraction);
    }

    private void ValidateSplit<T>(double? cpuFraction)
        where T : struct, IComputeShaderDescriptor<T>
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (cpuFraction is { } fraction && !(fraction is >= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(cpuFraction), fraction, "The CPU fraction must be between 0 and 1.");
        }

        if (T.GridWindowIndex is < 0 or >= Metal.MaxBufferBindings)
        {
            throw new NotSupportedException(
                $"Shader '{typeof(T)}' cannot run a partial grid, so it cannot be split between the CPU and the GPU. " +
                "Only generated shaders ([GeneratedComputeShaderDescriptor]) with a free buffer index (at most 30 buffers and constants) can be split.");
        }

        // An explicit share that puts threads on both devices. 0 and 1 run on one device, and the
        // adaptive mode (no fraction) keeps such a shader on the GPU: see RunSplit. Atomics on
        // threadgroup memory alone do not count: a threadgroup is never divided.
        if (T.UsesDeviceAtomics && cpuFraction is > 0 and < 1)
        {
            throw new NotSupportedException(
                $"Shader '{typeof(T)}' uses atomic operations (Gpu.Atomic*) on buffer elements, so it cannot be split between the CPU and the GPU: " +
                "atomics are atomic among the threads of one dispatch on one device, and the two parts would lose each other's updates. " +
                "Run it with For or ForCpu, pass a cpuFraction of 0 or 1, or use the ForSplit overload without a fraction, which runs such a shader on the GPU alone.");
        }
    }

    /// <summary>
    /// Divides <paramref name="grid"/> and runs both parts: the GPU part is committed first, then the CPU
    /// part runs on this thread (and the thread pool) while the GPU works, then the GPU is waited for.
    /// The caller has already validated and leased the shader's arguments.
    /// </summary>
    private SplitDispatchInfo RunSplit<T, TKernel>(
        Extent3 grid, int rank, ReadWriteTexture2D<Rgba32>? output, in T shader, TKernel cpuKernel, double? cpuFraction)
        where T : struct, IComputeShaderDescriptor<T>
        where TKernel : struct, ICpuKernel
    {
        // Both parts use the windowed pipeline's threadgroup size, so the cut falls on a threadgroup
        // boundary of the GPU's grid and GroupIds/ThreadGroupIds agree across it.
        ComputePipeline pipeline = GetWindowedPipeline<T>();
        Extent3 groupSize = ToExtent(ResolveThreadGroupSize(
            T.ThreadGroupSizeX, T.ThreadGroupSizeY, T.ThreadGroupSizeZ, rank, pipeline.MaxTotalThreadsPerThreadgroup, typeof(T)));

        int axis = rank - 1;
        int extent = Component(grid, axis);
        int align = Component(groupSize, axis);

        long threads = (long)grid.X * grid.Y * grid.Z;
        SplitBalance balance = _splitBalances.GetOrAdd((typeof(T), BitOperations.Log2((ulong)threads)), static _ => new SplitBalance());

        // A shader with atomics on buffer elements must stay on one device (ValidateSplit has already
        // refused an explicit share that would divide it). There is nothing to adapt then: the whole
        // grid goes to the GPU, and the balance is neither asked nor fed.
        bool singleDeviceOnly = T.UsesDeviceAtomics;
        double fraction = cpuFraction ?? (singleDeviceOnly ? 0 : balance.NextCpuFraction());

        // 0 and 1 are exact (GPU only, CPU only) whatever the grid and threadgroup sizes; anything in
        // between is rounded to a threadgroup boundary.
        int cpuExtent = fraction >= 1 ? extent
            : fraction <= 0 ? 0
            : (int)Math.Clamp(Math.Round(extent * fraction / align) * align, 0, extent);

        // An adaptive split always splits when it decides to: a share that rounds to nothing still gives
        // each side one threadgroup slice, so that the split's wall time (which decides between splitting
        // and GPU-only) is measured for a real split.
        if (cpuFraction is null && fraction > 0 && extent > align)
        {
            cpuExtent = Math.Clamp(cpuExtent, align, (extent - 1) / align * align);
        }

        int gpuExtent = extent - cpuExtent;
        long sliceThreads = (long)grid.X * grid.Y * grid.Z / extent;

        Extent3 cpuCount = With(grid, axis, cpuExtent);
        double cpuSeconds = 0, gpuSeconds = 0;
        long dispatchStart = Stopwatch.GetTimestamp();

        if (gpuExtent == 0)
        {
            long start = Stopwatch.GetTimestamp();
            RunCpu<T, TKernel>(cpuKernel, grid, groupSize, cpuCount);
            cpuSeconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        }
        else
        {
            // Without a CPU part this is a whole-grid dispatch, which needs neither the window nor the
            // windowed specialization.
            GridWindow? window = null;
            double commitTime = 0;

            // Runs right after the commit; the time it starts is the GPU part's start for balancing.
            Action whileGpuRuns = () => commitTime = HostSeconds(Stopwatch.GetTimestamp());

            if (cpuExtent > 0)
            {
                // The GPU runs the trailing part; its thread and threadgroup ids are rebased past the CPU's part.
                window = new GridWindow(
                    With(default, axis, cpuExtent),
                    grid,
                    With(default, axis, cpuExtent / align));

                whileGpuRuns = () =>
                {
                    long start = Stopwatch.GetTimestamp();
                    commitTime = HostSeconds(start);
                    RunCpu<T, TKernel>(cpuKernel, grid, groupSize, cpuCount);
                    cpuSeconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
                };
            }

            MTLSize gpuGrid = new((nuint)grid.X, (nuint)grid.Y, (nuint)grid.Z);
            gpuGrid = axis switch
            {
                0 => new MTLSize((nuint)gpuExtent, gpuGrid.Height, gpuGrid.Depth),
                1 => new MTLSize(gpuGrid.Width, (nuint)gpuExtent, gpuGrid.Depth),
                _ => new MTLSize(gpuGrid.Width, gpuGrid.Height, (nuint)gpuExtent),
            };

            (double gpuStart, double gpuEnd) = Run(gpuGrid, rank, output, in shader, window, whileGpuRuns);

            // For balancing, the GPU's time runs from the commit to the end of its work, so submission
            // latency counts against it. GPUStartTime/GPUEndTime are host times on the same clock as
            // Stopwatch (mach absolute time, in seconds). The fallback to the GPU's own execution time
            // covers a GPU that finished before the callback read the clock, and clocks that disagree.
            gpuSeconds = gpuEnd - gpuStart;

            if (commitTime > 0 && gpuEnd - commitTime >= gpuSeconds && gpuEnd - commitTime < gpuSeconds + 1)
            {
                gpuSeconds = gpuEnd - commitTime;
            }
        }

        if (!singleDeviceOnly)
        {
            balance.Record(cpuExtent * sliceThreads, cpuSeconds, gpuExtent * sliceThreads, gpuSeconds, Stopwatch.GetElapsedTime(dispatchStart).TotalSeconds);
        }

        return new SplitDispatchInfo(axis, cpuExtent, gpuExtent, fraction, TimeSpan.FromSeconds(cpuSeconds), TimeSpan.FromSeconds(gpuSeconds))
        {
            SingleDeviceOnly = singleDeviceOnly,
        };
    }

    private static double HostSeconds(long timestamp) => timestamp / (double)Stopwatch.Frequency;

    private static int Component(Extent3 value, int axis) => axis switch
    {
        0 => value.X,
        1 => value.Y,
        _ => value.Z,
    };

    private static Extent3 With(Extent3 value, int axis, int component) => axis switch
    {
        0 => value with { X = component },
        1 => value with { Y = component },
        _ => value with { Z = component },
    };
}

/// <summary>
/// The adaptive split of one shader type on one device. It answers two questions for the next
/// dispatch: what share of the grid the CPU should take so that both parts finish together, and
/// whether splitting is faster than running the whole grid on the GPU at all. Thread-safe.
/// </summary>
/// <remarks>
/// <para>
/// The share comes from running estimates (exponential moving averages, weight ½) of each side's
/// seconds per thread, measured on split dispatches: both finish together when
/// <c>f · t_cpu = (1 − f) · t_gpu</c>.
/// </para>
/// <para>
/// Equal finishing times are not the same as a win. While the CPU part runs, the GPU's part takes
/// longer than it would alone (submission and completion compete with the CPU work for cores, and the
/// two share memory bandwidth and power), so a balanced split can still be slower than GPU-only. The
/// balance therefore also keeps the wall time per thread of split and of GPU-only dispatches, and
/// uses whichever is lower. The first two dispatches split (with <see cref="InitialCpuFraction"/>),
/// the next two run GPU-only, and from then on the losing mode is probed on the 4th, 8th, ... 64th
/// dispatch since the winner last changed, then every 64th, which bounds the cost of probing while
/// still noticing when the balance shifts.
/// </para>
/// <para>
/// The first sample of each kind is discarded: the CPU's includes JIT-compiling the shader's
/// <c>Execute</c> and waking the thread pool, and the first GPU-only dispatch may include compiling
/// the shader's whole-grid pipeline.
/// </para>
/// </remarks>
internal sealed class SplitBalance
{
    internal const double InitialCpuFraction = 0.1;
    private const int MaxProbeInterval = 64;

    private readonly Lock _lock = new();
    private Estimate _cpuSecondsPerThread;
    private Estimate _gpuSecondsPerThread;
    private Estimate _splitWallPerThread;
    private Estimate _gpuOnlyWallPerThread;
    private bool _splitWins;
    private int _dispatchesSinceWinnerChanged;

    /// <summary>The CPU share for an equal finish, from the measured rates (the initial share until both are known).</summary>
    internal double CpuFraction
    {
        get
        {
            lock (_lock)
            {
                return BalancedFraction();
            }
        }
    }

    /// <summary>The share for the next adaptive dispatch: 0 for a GPU-only dispatch, otherwise the balanced share.</summary>
    internal double NextCpuFraction()
    {
        lock (_lock)
        {
            if (!_splitWallPerThread.IsKnown)
            {
                return BalancedFraction();
            }

            if (!_gpuOnlyWallPerThread.IsKnown)
            {
                return 0;
            }

            bool splitWins = _splitWallPerThread.Value < _gpuOnlyWallPerThread.Value;

            if (splitWins != _splitWins)
            {
                _splitWins = splitWins;
                _dispatchesSinceWinnerChanged = 0;
            }

            int n = ++_dispatchesSinceWinnerChanged;
            bool probe = n <= MaxProbeInterval ? n >= 4 && (n & (n - 1)) == 0 : n % MaxProbeInterval == 0;

            return splitWins != probe ? BalancedFraction() : 0;
        }
    }

    /// <summary>Records one dispatch: the threads and seconds of each part (0 for a part that did not run) and the whole dispatch's wall time.</summary>
    internal void Record(long cpuThreads, double cpuSeconds, long gpuThreads, double gpuSeconds, double wallSeconds)
    {
        lock (_lock)
        {
            bool cpuRan = cpuThreads > 0 && cpuSeconds > 0;
            bool gpuRan = gpuThreads > 0 && gpuSeconds > 0;

            if (cpuRan && gpuRan)
            {
                _cpuSecondsPerThread.Add(cpuSeconds / cpuThreads);
                _gpuSecondsPerThread.Add(gpuSeconds / gpuThreads);
            }

            if (wallSeconds > 0 && (cpuRan || gpuRan))
            {
                double perThread = wallSeconds / (cpuThreads + gpuThreads);

                if (cpuRan)
                {
                    _splitWallPerThread.Add(perThread);
                }
                else
                {
                    _gpuOnlyWallPerThread.Add(perThread);
                }
            }
        }
    }

    private double BalancedFraction() =>
        _cpuSecondsPerThread.IsKnown && _gpuSecondsPerThread.IsKnown
            ? _gpuSecondsPerThread.Value / (_cpuSecondsPerThread.Value + _gpuSecondsPerThread.Value)
            : InitialCpuFraction;

    /// <summary>An exponential moving average (weight ½) that ignores its first sample (warm-up).</summary>
    private struct Estimate
    {
        private int _samples;

        public double Value { get; private set; }

        public readonly bool IsKnown => _samples >= 2;

        public void Add(double sample)
        {
            Value = _samples switch
            {
                0 => 0,
                1 => sample,
                _ => (Value + sample) / 2,
            };

            _samples++;
        }
    }
}

/// <summary>
/// The C# mirror of the MSL <c>__GridWindow</c> struct a generated kernel declares when it can run a
/// partial grid. Its layout is pinned on the MSL side by <c>static_assert</c>s (36 bytes; offsets 0, 12, 24).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly struct GridWindow(Extent3 offset, Extent3 size, Extent3 groupOffset)
{
    /// <summary>The position in the whole grid of the part's first thread.</summary>
    public readonly uint OffsetX = (uint)offset.X;
    public readonly uint OffsetY = (uint)offset.Y;
    public readonly uint OffsetZ = (uint)offset.Z;

    /// <summary>The size of the whole grid (what <see cref="DispatchSize"/> returns).</summary>
    public readonly uint SizeX = (uint)size.X;
    public readonly uint SizeY = (uint)size.Y;
    public readonly uint SizeZ = (uint)size.Z;

    /// <summary>The position in the whole grid of the part's first threadgroup (the offset divided by the threadgroup size).</summary>
    public readonly uint GroupOffsetX = (uint)groupOffset.X;
    public readonly uint GroupOffsetY = (uint)groupOffset.Y;
    public readonly uint GroupOffsetZ = (uint)groupOffset.Z;
}

/// <summary>How a CPU+GPU split dispatch (<see cref="GraphicsDevice.ForSplit{T}(int, int, in T)"/>) divided its grid.</summary>
/// <param name="Axis">The axis that was cut: 0 = x (1D grids), 1 = y (2D), 2 = z (3D).</param>
/// <param name="CpuExtent">The slices along <paramref name="Axis"/> the CPU ran: <c>0 .. CpuExtent - 1</c>.</param>
/// <param name="GpuExtent">The slices the GPU ran, the rest of the axis.</param>
/// <param name="CpuFraction">The CPU share that was requested or chosen (0 when an adaptive split ran GPU-only), before rounding to whole threadgroups.</param>
/// <param name="CpuTime">Wall time of the CPU part (zero if it was empty).</param>
/// <param name="GpuTime">Time from commit until the GPU finished its part (zero if it was empty); the GPU's execution time if the host and GPU clocks cannot be related.</param>
public readonly record struct SplitDispatchInfo(int Axis, int CpuExtent, int GpuExtent, double CpuFraction, TimeSpan CpuTime, TimeSpan GpuTime)
{
    /// <summary>
    /// True when the shader may not be divided between the CPU and the GPU because it uses atomics on
    /// buffer elements (<see cref="IComputeShaderDescriptor{TSelf}.UsesDeviceAtomics"/>): the whole grid then ran on one
    /// device (one of the extents is 0), and for a dispatch without an explicit fraction that was not
    /// a measured choice, the GPU was simply the only option considered. False for every other shader,
    /// including when the adaptive mode chose to run GPU-only.
    /// </summary>
    public bool SingleDeviceOnly { get; init; }
}
