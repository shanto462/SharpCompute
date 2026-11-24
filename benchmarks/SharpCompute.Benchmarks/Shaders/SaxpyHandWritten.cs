using System;
using System.Runtime.InteropServices;
using SharpCompute;

namespace SharpCompute.Benchmarks.Shaders;

/// <summary>
/// SAXPY written directly in MSL, implementing the descriptor by hand. It is the control for
/// <see cref="SaxpyGenerated"/>: the same kernel and threadgroup size, so any gap between the two
/// would be overhead from the generated code or its binding.
/// </summary>
public readonly struct SaxpyHandWritten(ReadOnlyBuffer<float> x, ReadWriteBuffer<float> y, float a)
    : IComputeShader, IComputeShaderDescriptor<SaxpyHandWritten>
{
    private readonly ReadOnlyBuffer<float> _x = x;
    private readonly ReadWriteBuffer<float> _y = y;
    private readonly float _a = a;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Constants
    {
        public float A;
    }

    public static string EntryPoint => "saxpy_hand_written";

    public static string MslSource => """
        #include <metal_stdlib>
        using namespace metal;

        kernel void saxpy_hand_written(
            const device float* x [[buffer(0)]],
            device float* y [[buffer(1)]],
            constant float& a [[buffer(2)]],
            uint i [[thread_position_in_grid]])
        {
            y[i] = a * x[i] + y[i];
        }
        """;

    public static int ThreadGroupSizeX => 256;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public void Execute() => throw new NotSupportedException("Hand-written MSL; dispatch it on the GPU.");

    public static void SetArguments(in SaxpyHandWritten shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._x);
        writer.SetBuffer(1, shader._y);
        writer.SetBytes(2, new Constants { A = shader._a });
    }
}

/// <summary>Reads a descriptor's static members, which the generator implements explicitly.</summary>
public static class ShaderInfo
{
    public static string MslSource<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.MslSource;

    public static string EntryPoint<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.EntryPoint;

    /// <summary>
    /// Fails unless the generator ran for <see cref="SaxpyGenerated"/> in this build. BenchmarkDotNet
    /// rebuilds the project for each run, and without this check a broken analyzer reference would
    /// make "generated vs hand-written" compare nothing, or the hand-written kernel with itself.
    /// </summary>
    public static string CheckGeneratorRan()
    {
        string generated = MslSource<SaxpyGenerated>();
        string entry = EntryPoint<SaxpyGenerated>();

        if (generated == MslSource<SaxpyHandWritten>() || entry == EntryPoint<SaxpyHandWritten>()
            || !generated.Contains($"kernel void {entry}(", StringComparison.Ordinal)
            || !generated.Contains("[[thread_position_in_grid]]", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The generated SaxpyGenerated descriptor is missing or identical to the hand-written one; is the source generator referenced as an analyzer?");
        }

        return entry;
    }
}
