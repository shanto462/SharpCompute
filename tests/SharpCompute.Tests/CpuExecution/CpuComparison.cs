using System;
using System.Linq;
using System.Reflection;

namespace SharpCompute.Tests.CpuExecution;

/// <summary>
/// Comparisons between CPU and GPU results. The GPU contracts <c>a * b + c</c> into fmas, flushes
/// subnormals and evaluates transcendental functions to within a few ULP, so floats are compared with
/// a tolerance and colors to within a few unorm8 steps; integers must match exactly.
/// </summary>
internal static class CpuComparison
{
    /// <summary>Every element within <paramref name="relative"/> of the larger magnitude, or within <paramref name="absolute"/>.</summary>
    public static void AssertClose(ReadOnlySpan<float> expected, ReadOnlySpan<float> actual, float relative = 1e-5f, float absolute = 1e-5f)
    {
        Assert.Equal(expected.Length, actual.Length);

        for (int i = 0; i < expected.Length; i++)
        {
            float e = expected[i], a = actual[i];
            float tolerance = Math.Max(absolute, relative * Math.Max(Math.Abs(e), Math.Abs(a)));

            Assert.True(
                e.Equals(a) || Math.Abs(e - a) <= tolerance,
                $"Element {i}: GPU {e:R}, CPU {a:R} (tolerance {tolerance:R}).");
        }
    }

    public static float[] Flatten(Float3[] values) => values.SelectMany(static v => new[] { v.X, v.Y, v.Z }).ToArray();

    public static float[] Flatten(Float4[] values) => values.SelectMany(static v => new[] { v.X, v.Y, v.Z, v.W }).ToArray();

    /// <summary>
    /// Pixel colors: at most <paramref name="maxStep"/> unorm8 steps apart per channel, except for at most
    /// <paramref name="outlierFraction"/> of the pixels (where rounding differences were amplified, e.g.
    /// a ray that hits an edge on one side and misses it on the other).
    /// </summary>
    public static void AssertPixelsClose(Rgba32[] expected, Rgba32[] actual, int maxStep, double outlierFraction, string name)
    {
        Assert.Equal(expected.Length, actual.Length);

        int outliers = 0;

        for (int i = 0; i < expected.Length; i++)
        {
            Rgba32 e = expected[i], a = actual[i];
            int step = Math.Max(Math.Max(Math.Abs(e.R - a.R), Math.Abs(e.G - a.G)), Math.Max(Math.Abs(e.B - a.B), Math.Abs(e.A - a.A)));

            if (step > maxStep)
            {
                outliers++;
            }
        }

        Assert.True(
            outliers <= outlierFraction * expected.Length,
            $"{name}: {outliers} of {expected.Length} pixels differ by more than {maxStep} steps between the GPU and the CPU.");
    }

    /// <summary>Calls a generic <see cref="GraphicsDevice"/> method on a shader type only known at run time (internal sample shaders).</summary>
    public static object? Invoke(GraphicsDevice device, string method, int parameterCount, Type shaderType, params object[] arguments)
    {
        MethodInfo info = typeof(GraphicsDevice)
            .GetMethods()
            .Single(m => m.Name == method && m.GetParameters().Length == parameterCount)
            .MakeGenericMethod(shaderType);

        try
        {
            return info.Invoke(device, arguments);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }
    }
}
