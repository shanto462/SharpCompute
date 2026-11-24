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
