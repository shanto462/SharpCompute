using System;

namespace SharpCompute;

internal static class ShaderOnly
{
    public static InvalidOperationException Error() =>
        new("This member can only be used inside a compute shader's Execute method.");
}

/// <summary>Position of the current thread in the dispatch grid (<c>thread_position_in_grid</c>).</summary>
public static class ThreadIds
{
    public static int X => throw ShaderOnly.Error();

    public static int Y => throw ShaderOnly.Error();

    public static int Z => throw ShaderOnly.Error();

    public static Int2 XY => throw ShaderOnly.Error();

    public static Int3 XYZ => throw ShaderOnly.Error();

    /// <summary>
    /// Thread position normalized to [0, 1] <b>inclusive</b>: <c>tid / max(1, grid - 1)</c> per
    /// component. Same definition as ComputeSharp's <c>ThreadIds.Normalized</c>.
    /// </summary>
    public static class Normalized
    {
        public static float X => throw ShaderOnly.Error();

        public static float Y => throw ShaderOnly.Error();

        public static float Z => throw ShaderOnly.Error();

        public static Float2 XY => throw ShaderOnly.Error();

        public static Float3 XYZ => throw ShaderOnly.Error();
    }
}

/// <summary>Size of the dispatch grid (<c>threads_per_grid</c>).</summary>
public static class DispatchSize
{
    public static int X => throw ShaderOnly.Error();

    public static int Y => throw ShaderOnly.Error();

    public static int Z => throw ShaderOnly.Error();

    public static Int2 XY => throw ShaderOnly.Error();

    public static Int3 XYZ => throw ShaderOnly.Error();
}

/// <summary>Position of the current thread within its threadgroup (<c>thread_position_in_threadgroup</c>).</summary>
public static class GroupIds
{
    public static int X => throw ShaderOnly.Error();

    public static int Y => throw ShaderOnly.Error();

    public static int Z => throw ShaderOnly.Error();

    public static Int2 XY => throw ShaderOnly.Error();

    public static Int3 XYZ => throw ShaderOnly.Error();
}

/// <summary>Position of the current threadgroup in the grid (<c>threadgroup_position_in_grid</c>).</summary>
public static class ThreadGroupIds
{
    public static int X => throw ShaderOnly.Error();

    public static int Y => throw ShaderOnly.Error();

    public static int Z => throw ShaderOnly.Error();

    public static Int2 XY => throw ShaderOnly.Error();

    public static Int3 XYZ => throw ShaderOnly.Error();
}
