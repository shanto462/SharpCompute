using System;

namespace SharpCompute;

internal static class ShaderOnly
{
    public static InvalidOperationException Error() =>
        new("This member can only be used inside a compute shader's Execute method: on the GPU (where it is translated to MSL), " +
            "or while the shader runs on the CPU through GraphicsDevice.ForCpu, ForEachCpu, ForSplit or ForEachSplit.");
}

// On the GPU every member below is translated to a Metal built-in by the source generator, which
// recognises them by name; their C# bodies never run there. On the CPU they read the calling worker
// thread's CpuThread, which the CPU dispatcher keeps up to date for the thread being executed.
// Outside a CPU dispatch CpuThread.Current throws ShaderOnly.Error().

/// <summary>Position of the current thread in the dispatch grid (<c>thread_position_in_grid</c>).</summary>
public static class ThreadIds
{
    public static int X => CpuThread.Current.X;

    public static int Y => CpuThread.Current.Y;

    public static int Z => CpuThread.Current.Z;

    public static Int2 XY
    {
        get
        {
            CpuThread thread = CpuThread.Current;
            return new Int2(thread.X, thread.Y);
        }
    }

    public static Int3 XYZ
    {
        get
        {
            CpuThread thread = CpuThread.Current;
            return new Int3(thread.X, thread.Y, thread.Z);
        }
    }

    /// <summary>
    /// Thread position normalized to [0, 1] <b>inclusive</b>: <c>tid / max(1, grid - 1)</c> per
    /// component. Same definition as ComputeSharp's <c>ThreadIds.Normalized</c>.
    /// </summary>
    public static class Normalized
    {
        public static float X
        {
            get
            {
                CpuThread thread = CpuThread.Current;
                return Normalize(thread.X, thread.GridX);
            }
        }

        public static float Y
        {
            get
            {
                CpuThread thread = CpuThread.Current;
                return Normalize(thread.Y, thread.GridY);
            }
        }

        public static float Z
        {
            get
            {
                CpuThread thread = CpuThread.Current;
                return Normalize(thread.Z, thread.GridZ);
            }
        }

        public static Float2 XY
        {
            get
            {
                CpuThread thread = CpuThread.Current;
                return new Float2(Normalize(thread.X, thread.GridX), Normalize(thread.Y, thread.GridY));
            }
        }

        public static Float3 XYZ
        {
            get
            {
                CpuThread thread = CpuThread.Current;
                return new Float3(Normalize(thread.X, thread.GridX), Normalize(thread.Y, thread.GridY), Normalize(thread.Z, thread.GridZ));
            }
        }

        // The MSL is `float(tid) / float(max(1, int(grid) - 1))`; both divisions are correctly rounded.
        private static float Normalize(int id, int grid) => id / (float)Math.Max(1, grid - 1);
    }
}

/// <summary>Size of the dispatch grid (<c>threads_per_grid</c>).</summary>
/// <remarks>Always the size of the whole grid, also for each part of a CPU+GPU split dispatch.</remarks>
public static class DispatchSize
{
    public static int X => CpuThread.Current.GridX;

    public static int Y => CpuThread.Current.GridY;

    public static int Z => CpuThread.Current.GridZ;

    public static Int2 XY
    {
        get
        {
            CpuThread thread = CpuThread.Current;
            return new Int2(thread.GridX, thread.GridY);
        }
    }

    public static Int3 XYZ
    {
        get
        {
            CpuThread thread = CpuThread.Current;
            return new Int3(thread.GridX, thread.GridY, thread.GridZ);
        }
    }
}

/// <summary>Position of the current thread within its threadgroup (<c>thread_position_in_threadgroup</c>).</summary>
/// <remarks>
/// On the CPU, threadgroups are the shader's <see cref="ThreadGroupSizeAttribute"/> size, or the runtime's
/// default for the dispatch rank (256, 16×16 or 8×8×4), the same size the GPU uses unless the compiled
/// kernel supports fewer threads per threadgroup than the default.
/// </remarks>
public static class GroupIds
{
    public static int X => CpuThread.Current.GroupX;

    public static int Y => CpuThread.Current.GroupY;

    public static int Z => CpuThread.Current.GroupZ;

    public static Int2 XY
    {
        get
        {
            CpuThread thread = CpuThread.Current;
            return new Int2(thread.GroupX, thread.GroupY);
        }
    }

    public static Int3 XYZ
    {
        get
        {
            CpuThread thread = CpuThread.Current;
            return new Int3(thread.GroupX, thread.GroupY, thread.GroupZ);
        }
    }

    /// <summary>
    /// The flat index of the current thread within its threadgroup (<c>thread_index_in_threadgroup</c>):
    /// <c>(Z * GroupSize.Y + Y) * GroupSize.X + X</c>, from 0 to <see cref="GroupSize.Count"/> - 1.
    /// </summary>
    /// <remarks>
    /// The index is laid out with the size of the <i>current</i> group (see <see cref="GroupSize"/>), so
    /// the threads of a partial group at the edge of the grid are still numbered without gaps.
    /// </remarks>
    public static int Index => CpuThread.Current.GroupIndex;
}

/// <summary>The size of the current thread's threadgroup (<c>threads_per_threadgroup</c>).</summary>
/// <remarks>
/// This is the size of the group the thread is in, which is the shader's <see cref="ThreadGroupSizeAttribute"/>
/// size (or the default described for <see cref="GroupIds"/>) except at the far edges of a grid that is
/// not a multiple of it: there the last group along an axis is cut off at the edge of the grid and
/// reports the threads it really has (Metal's non-uniform threadgroups). A 1D dispatch of 1000 threads
/// in groups of 256 has three groups of 256 and one of 232. Shaders that fill a
/// <see cref="GroupSharedAttribute"/> array from every thread should therefore combine only the first
/// <see cref="Count"/> elements, or be dispatched over a multiple of the group size and skip the
/// threads past the end of their data.
/// </remarks>
public static class GroupSize
{
    public static int X => CpuThread.Current.GroupExtentX;

    public static int Y => CpuThread.Current.GroupExtentY;

    public static int Z => CpuThread.Current.GroupExtentZ;

    public static Int2 XY
    {
        get
        {
            CpuThread thread = CpuThread.Current;
            return new Int2(thread.GroupExtentX, thread.GroupExtentY);
        }
    }

    public static Int3 XYZ
    {
        get
        {
            CpuThread thread = CpuThread.Current;
            return new Int3(thread.GroupExtentX, thread.GroupExtentY, thread.GroupExtentZ);
        }
    }

    /// <summary>The number of threads in the current threadgroup: <c>X * Y * Z</c>.</summary>
    public static int Count
    {
        get
        {
            CpuThread thread = CpuThread.Current;
            return thread.GroupExtentX * thread.GroupExtentY * thread.GroupExtentZ;
        }
    }
}

/// <summary>The number of threadgroups in the dispatch grid along each axis (<c>threadgroups_per_grid</c>).</summary>
/// <remarks>
/// Counts a partial group at the edge of the grid as a group: <c>ceil(DispatchSize / threadgroup size)</c>.
/// Like <see cref="DispatchSize"/>, always the count of the whole grid, also for each part of a CPU+GPU
/// split dispatch.
/// </remarks>
public static class ThreadGroupCount
{
    public static int X => CpuThread.Current.GroupCountX;

    public static int Y => CpuThread.Current.GroupCountY;

    public static int Z => CpuThread.Current.GroupCountZ;

    public static Int2 XY
    {
        get
        {
            CpuThread thread = CpuThread.Current;
            return new Int2(thread.GroupCountX, thread.GroupCountY);
        }
    }

    public static Int3 XYZ
    {
        get
        {
            CpuThread thread = CpuThread.Current;
            return new Int3(thread.GroupCountX, thread.GroupCountY, thread.GroupCountZ);
        }
    }
}

/// <summary>Position of the current threadgroup in the grid (<c>threadgroup_position_in_grid</c>).</summary>
/// <remarks>On the CPU, threadgroups are sized as described for <see cref="GroupIds"/>.</remarks>
public static class ThreadGroupIds
{
    public static int X => CpuThread.Current.ThreadGroupX;

    public static int Y => CpuThread.Current.ThreadGroupY;

    public static int Z => CpuThread.Current.ThreadGroupZ;

    public static Int2 XY
    {
        get
        {
            CpuThread thread = CpuThread.Current;
            return new Int2(thread.ThreadGroupX, thread.ThreadGroupY);
        }
    }

    public static Int3 XYZ
    {
        get
        {
            CpuThread thread = CpuThread.Current;
            return new Int3(thread.ThreadGroupX, thread.ThreadGroupY, thread.ThreadGroupZ);
        }
    }
}

/// <summary>The SIMD group of the current thread within its threadgroup (<c>simdgroup_index_in_threadgroup</c>, <c>simdgroups_per_threadgroup</c>).</summary>
/// <remarks>
/// <para>
/// An Apple GPU executes a threadgroup in SIMD groups of 32 consecutive threads (by their linear index
/// in the threadgroup, <see cref="GroupIds.Index"/>; see the SIMD-group operations of <see cref="Gpu"/>,
/// such as <see cref="Gpu.WaveActiveSum(float, string, int)"/>). <see cref="Index"/> says which of them
/// the thread is in, and <see cref="Gpu.WaveGetLaneIndex"/> which lane of it the thread is.
/// </para>
/// <para>
/// On the CPU (<c>GraphicsDevice.ForCpu</c>, <c>ForSplit</c>) both are computed from the thread's
/// position the same way, so they agree with the GPU for every grid and threadgroup size.
/// </para>
/// </remarks>
public static class SimdGroupIds
{
    /// <summary>
    /// The index of the current thread's SIMD group within its threadgroup: the thread's linear index
    /// in the threadgroup divided by 32, from 0 to <see cref="Count"/> - 1.
    /// </summary>
    public static int Index => CpuSimdGroup.GroupIndex();

    /// <summary>
    /// The number of SIMD groups in the current threadgroup: its thread count divided by 32, rounded
    /// up. A partial threadgroup at the edge of the grid has as many as its real thread count needs
    /// (a threadgroup of 256 has 8; the last one of a 1000-thread dispatch, with 232 threads, also 8;
    /// one cut down to 40 threads has 2).
    /// </summary>
    public static int Count => CpuSimdGroup.GroupCount();
}
