using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace SharpCompute;

/// <summary>
/// The state behind <see cref="ThreadIds"/>, <see cref="DispatchSize"/>, <see cref="GroupIds"/>,
/// <see cref="ThreadGroupIds"/>, <see cref="GroupSize"/> and <see cref="ThreadGroupCount"/> while a
/// shader runs on the CPU: one instance per chunk of a CPU dispatch (or per thread of a threadgroup,
/// for shaders whose threads cooperate), published in a thread-static for the worker thread that runs it.
/// </summary>
/// <remarks>
/// Not thread-safe by design: an instance is only touched by the worker that owns it. The dispatcher
/// saves and restores the previous value, so a shader that (unusually) dispatches another CPU shader
/// from its <c>Execute</c> gets its own built-ins back afterwards.
/// </remarks>
internal sealed class CpuThread
{
    [ThreadStatic]
    private static CpuThread? t_current;

    internal int X;
    internal int Y;
    internal int Z;

    internal CpuThread(int gridX, int gridY, int gridZ, Extent3 groupSize)
    {
        GridX = gridX;
        GridY = gridY;
        GridZ = gridZ;
        GroupSizeX = groupSize.X;
        GroupSizeY = groupSize.Y;
        GroupSizeZ = groupSize.Z;
    }

    /// <summary>The worker's current shader thread; throws <see cref="ShaderOnly.Error"/> outside a CPU dispatch.</summary>
    internal static CpuThread Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => t_current ?? ThrowOutsideDispatch();
    }

    internal int GridX { get; }

    internal int GridY { get; }

    internal int GridZ { get; }

    internal int GroupSizeX { get; }

    internal int GroupSizeY { get; }

    internal int GroupSizeZ { get; }

    // Threadgroups tile the grid from the origin (the last one may be partial, as with Metal's
    // non-uniform threadgroups), so these are plain division and remainder.
    internal int GroupX => X % GroupSizeX;

    internal int GroupY => Y % GroupSizeY;

    internal int GroupZ => Z % GroupSizeZ;

    internal int ThreadGroupX => X / GroupSizeX;

    internal int ThreadGroupY => Y / GroupSizeY;

    internal int ThreadGroupZ => Z / GroupSizeZ;

    // The size of the group this thread is in: the nominal size, cut off where the grid ends. Metal
    // reports the same for the partial groups of a non-uniform dispatch (measured on Apple GPUs), and
    // numbers the threads of such a group with its real size.
    internal int GroupExtentX => Math.Min(GroupSizeX, GridX - (X - GroupX));

    internal int GroupExtentY => Math.Min(GroupSizeY, GridY - (Y - GroupY));

    internal int GroupExtentZ => Math.Min(GroupSizeZ, GridZ - (Z - GroupZ));

    internal int GroupIndex => (((GroupZ * GroupExtentY) + GroupY) * GroupExtentX) + GroupX;

    // ceil(grid / group size), written so that it cannot overflow for a grid near int.MaxValue.
    internal int GroupCountX => ((GridX - 1) / GroupSizeX) + 1;

    internal int GroupCountY => ((GridY - 1) / GroupSizeY) + 1;

    internal int GroupCountZ => ((GridZ - 1) / GroupSizeZ) + 1;

    /// <summary>
    /// The threadgroup this thread runs in when the shader's threads cooperate (see
    /// <see cref="CpuGroupDispatcher"/>); null in an ordinary dispatch, where threads are independent.
    /// </summary>
    internal CpuThreadgroup? Group { get; init; }

    /// <summary>This thread's index among the worker threads of <see cref="Group"/>.</summary>
    internal int Slot { get; init; }

    /// <summary>The CPU side of the threadgroup barriers on <see cref="Gpu"/>.</summary>
    internal void Barrier()
    {
        if (Group is null)
        {
            // Generated descriptors report barriers, so this is a hand-written one that does not.
            throw new InvalidOperationException(
                "A threadgroup barrier was called by a shader whose descriptor does not report UsesThreadgroupMemory, so its threads " +
                "were not run as a threadgroup on the CPU. Let the generator write the descriptor ([GeneratedComputeShaderDescriptor]), " +
                "or return true from IComputeShaderDescriptor<T>.UsesThreadgroupMemory.");
        }

        Group.Barrier(Slot);
    }

    /// <summary>Makes <paramref name="thread"/> the current one and returns the one it replaces.</summary>
    internal static CpuThread? Enter(CpuThread thread)
    {
        CpuThread? previous = t_current;
        t_current = thread;
        return previous;
    }

    internal static void Exit(CpuThread? previous) => t_current = previous;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CpuThread ThrowOutsideDispatch() => throw ShaderOnly.Error();
}

/// <summary>Three <see cref="int"/> extents or coordinates: a threadgroup size, or a box of the grid.</summary>
internal readonly record struct Extent3(int X, int Y, int Z);

/// <summary>Runs one shader thread on the CPU. A struct, so the dispatch loop is specialized per shader with no indirect calls.</summary>
internal interface ICpuKernel
{
    void Invoke(CpuThread thread);
}

/// <summary>An <see cref="IComputeShader"/>: just <c>Execute()</c>.</summary>
internal struct ComputeKernel<T>(T shader) : ICpuKernel
    where T : struct, IComputeShader
{
    private T _shader = shader;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke(CpuThread thread) => _shader.Execute();
}

/// <summary>
/// A pixel shader: <c>Execute()</c>'s color is stored to the texture's shared memory with the
/// conversion the GPU applies when it writes a float4 to an <c>RGBA8Unorm</c> texture.
/// </summary>
internal unsafe struct PixelKernel<T>(T shader, byte* pixels, nuint bytesPerRow) : ICpuKernel
    where T : struct, IComputeShader<Float4>
{
    private T _shader = shader;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke(CpuThread thread)
    {
        Float4 color = _shader.Execute();
        Rgba32* row = (Rgba32*)(pixels + ((nuint)thread.Y * bytesPerRow));
        row[thread.X] = new Rgba32(CpuDispatcher.ToUnorm8(color.X), CpuDispatcher.ToUnorm8(color.Y), CpuDispatcher.ToUnorm8(color.Z), CpuDispatcher.ToUnorm8(color.W));
    }
}

/// <summary>Runs every thread of a box of the grid on the CPU, in parallel.</summary>
internal static class CpuDispatcher
{
    // Chunks per processor: enough for Parallel.For to balance shaders whose cost varies across the
    // grid (a Mandelbrot row through the set costs far more than one outside it), few enough that
    // the per-chunk overhead (a delegate call and a thread-static swap) stays negligible. There is no
    // minimum chunk size: a few hundred N-body threads can each run for tens of microseconds, so even
    // a small grid is spread over every core (only a single thread runs inline).
    private const int ChunksPerProcessor = 16;

    /// <summary>
    /// Runs the threads <paramref name="origin"/> .. <paramref name="origin"/> + <paramref name="count"/>
    /// (exclusive) of a <paramref name="gridX"/> × <paramref name="gridY"/> × <paramref name="gridZ"/> grid.
    /// x varies fastest, as in a row-major image. If threads throw, the dispatch stops scheduling new
    /// chunks and the first exception is rethrown (others are dropped).
    /// </summary>
    internal static void Run<TKernel>(
        TKernel kernel, int gridX, int gridY, int gridZ, Extent3 groupSize, Extent3 origin, Extent3 count)
        where TKernel : struct, ICpuKernel
    {
        long total = (long)count.X * count.Y * count.Z;

        if (total == 0)
        {
            return;
        }

        long targetChunks = (long)Environment.ProcessorCount * ChunksPerProcessor;
        long chunkSize = (total + targetChunks - 1) / targetChunks;
        long chunks = (total + chunkSize - 1) / chunkSize;
        Chunk<TKernel> chunk = new(kernel, gridX, gridY, gridZ, groupSize, origin, count, total, chunkSize);

        if (chunks == 1)
        {
            chunk.Run(0);
            return;
        }

        try
        {
            Parallel.For(0, chunks, chunk.Run);
        }
        catch (AggregateException e) when (e.InnerExceptions.Count > 0)
        {
            // Surface what the shader threw (e.g. an out-of-range buffer index), not the wrapper.
            ExceptionDispatchInfo.Throw(e.InnerExceptions[0]);
        }
    }

    /// <summary>
    /// The conversion Apple GPUs apply when a shader writes a float to a unorm8 texture channel: NaN → 0,
    /// clamp to [0, 1], then the integer nearest to the <i>exact</i> product <c>value × 255</c>. Measured
    /// rather than taken from the spec: rounding a float product (to nearest even or half up) is off by
    /// one at about half of the 255 midpoints, because the float nearest to <c>(k + 0.5) / 255</c> lies
    /// just above or just below it. The product is exact in double (24 + 8 significant bits), and it can
    /// never be exactly halfway, so how ties round does not matter. The FloatToUnorm8 test pins every
    /// midpoint and its float neighbours against the GPU.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static byte ToUnorm8(float value)
    {
        // `!(value > 0)` also catches NaN.
        if (!(value > 0f))
        {
            return 0;
        }

        return value >= 1f ? (byte)255 : (byte)((value * 255.0) + 0.5);
    }

    /// <summary>The closure of one CPU dispatch; <see cref="Run"/> executes one chunk of the linearized box.</summary>
    private sealed class Chunk<TKernel>(
        TKernel kernel, int gridX, int gridY, int gridZ, Extent3 groupSize, Extent3 origin, Extent3 count, long total, long chunkSize)
        where TKernel : struct, ICpuKernel
    {
        public void Run(long index)
        {
            long start = index * chunkSize;
            long end = Math.Min(total, start + chunkSize);

            // Decompose the first linear index once, then step x, y, z like an odometer.
            int x = (int)(start % count.X);
            long rows = start / count.X;
            int y = (int)(rows % count.Y);
            int z = (int)(rows / count.Y);

            TKernel local = kernel;
            CpuThread thread = new(gridX, gridY, gridZ, groupSize);
            CpuThread? previous = CpuThread.Enter(thread);

            try
            {
                for (long n = start; n < end; n++)
                {
                    thread.X = origin.X + x;
                    thread.Y = origin.Y + y;
                    thread.Z = origin.Z + z;
                    local.Invoke(thread);

                    if (++x == count.X)
                    {
                        x = 0;

                        if (++y == count.Y)
                        {
                            y = 0;
                            z++;
                        }
                    }
                }
            }
            finally
            {
                CpuThread.Exit(previous);
            }
        }
    }
}
