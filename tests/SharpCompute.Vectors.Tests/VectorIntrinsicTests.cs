using System;
using System.Linq;
using System.Reflection;

namespace SharpCompute.Vectors.Tests;

public class VectorIntrinsicTests
{
    // Not "the same function per component": geometric functions, those that change the element
    // type (bit reinterpretation, unorm8 packing; see BitIntrinsicTests), and those that produce or
    // consume a bool vector (see BoolVectorTests).
    private static readonly string[] Geometric =
    [
        "Dot", "Cross", "Length", "Distance", "Normalize", "Reflect", "Mul", "AsFloat", "AsInt", "AsUInt", "PackUnorm4x8",
        "IsNaN", "IsInfinity", "IsFinite", "Equal", "NotEqual", "Any", "All", "Select",
    ];

    /// <summary>
    /// Every vector overload of a componentwise intrinsic equals the scalar overload applied per
    /// component (scalar parameters of the mixed overloads are broadcast).
    /// </summary>
    [Fact]
    public void ComponentwiseOverloadsMatchScalarOverloads()
    {
        MethodInfo[] methods = typeof(Gpu).GetMethods(BindingFlags.Public | BindingFlags.Static);
        // The SIMD-group operations are not componentwise calls of a scalar overload: a lane's result
        // comes from the other lanes, and their vector overloads cannot run outside a shader.
        MethodInfo[] vectorMethods = methods
            .Where(m => !Geometric.Contains(m.Name) && !m.Name.StartsWith("Wave", StringComparison.Ordinal) && !m.Name.StartsWith("Quad", StringComparison.Ordinal))
            .Where(m => m.GetParameters().Any(p => VectorTypes.All.Contains(p.ParameterType)))
            .ToArray();
        Assert.NotEmpty(vectorMethods);

        foreach (MethodInfo m in vectorMethods)
        {
            Type vector = m.ReturnType;
            Assert.True(VectorTypes.IsVector(vector), $"{m}: componentwise overload must return a vector");
            Type element = VectorTypes.Element(vector);
            ParameterInfo[] ps = m.GetParameters();
            Assert.All(ps, p => Assert.True(p.ParameterType == vector || p.ParameterType == element, $"{m}: parameter {p.Name}"));

            MethodInfo scalar = typeof(Gpu).GetMethod(m.Name, Enumerable.Repeat(element, ps.Length).ToArray())
                ?? throw new InvalidOperationException($"{m}: no scalar overload");
            Assert.Equal(m.GetCustomAttribute<MslIntrinsicAttribute>()!.Name, scalar.GetCustomAttribute<MslIntrinsicAttribute>()!.Name);

            object[] args = ps.Select((p, i) => p.ParameterType == vector ? VectorTypes.SampleVector(vector, i) : VectorTypes.Sample(element, i, 1)).ToArray();
            object result = m.Invoke(null, args)!;
            for (int c = 0; c < VectorTypes.Width(vector); c++)
            {
                object[] scalarArgs = args.Select(a => a.GetType() == vector ? VectorTypes.Get(a, c) : a).ToArray();
                object expected = scalar.Invoke(null, scalarArgs)!;
                object actual = VectorTypes.Get(result, c);
                Assert.True(VectorTypes.SameValue(expected, actual), $"{m} component {c}: expected {VectorTypes.Format(expected)}, got {VectorTypes.Format(actual)}");
            }
        }
    }

    [Fact]
    public void ComponentwiseExamples()
    {
        FloatAssert.SameVector(new Float3(0, 0.5f, 1), Gpu.Saturate(new Float3(-1, 0.5f, 2)));
        FloatAssert.SameVector(new Float4(0, 0.25f, 1, 1), Gpu.Clamp(new Float4(-1, 0.25f, 1, 9), 0f, 1f));
        FloatAssert.SameVector(new Float2(3, 1), Gpu.Lerp(new Float2(2, 0), new Float2(4, 2), 0.5f));
        FloatAssert.SameVector(new Float2(0, 1), Gpu.Step(0.5f, new Float2(0.25f, 0.75f)));
        FloatAssert.SameVector(new Float2(1.5f, -1.5f), Gpu.Fmod(new Float2(5.5f, -5.5f), new Float2(2, 2)));
        FloatAssert.SameVector(new Float3(3, -3, 0), Gpu.Round(new Float3(2.5f, -2.5f, -0.4f)));
        Assert.Equal(new Int3(0, 5, 10), Gpu.Clamp(new Int3(-4, 5, 99), new Int3(0), new Int3(10)));
        Assert.Equal(new UInt2(3, 1), Gpu.Max(new UInt2(3, 0), 1u));
        Assert.Equal(new Int2(int.MinValue, 2), Gpu.Abs(new Int2(int.MinValue, -2)));
    }

    [Fact]
    public void Dot()
    {
        FloatAssert.Same(11f, Gpu.Dot(new Float2(1, 2), new Float2(3, 4)));
        FloatAssert.Same(32f, Gpu.Dot(new Float3(1, 2, 3), new Float3(4, 5, 6)));
        FloatAssert.Same(70f, Gpu.Dot(new Float4(1, 2, 3, 4), new Float4(5, 6, 7, 8)));
        FloatAssert.Same(float.NaN, Gpu.Dot(new Float2(float.NaN, 0), new Float2(1, 1)));
    }

    [Fact]
    public void DotIsAnFmaChainInComponentOrder()
    {
        // (1 + 2^-12)^2 = 1 + 2^-11 + 2^-24 is not representable; the fma keeps the 2^-24 term.
        float a = 1f + MathF.ScaleB(1f, -12);
        float expected = MathF.ScaleB(1f, -11) + MathF.ScaleB(1f, -24);
        FloatAssert.Same(expected, Gpu.Dot(new Float2(1, a), new Float2(-1, a)));
        FloatAssert.Same(expected, Gpu.Dot(new Float3(1, 0, a), new Float3(-1, 0, a)));
        FloatAssert.Same(expected, Gpu.Dot(new Float4(1, 0, 0, a), new Float4(-1, 0, 0, a)));
        Assert.NotEqual(expected, (1f * -1f) + (a * a)); // a plain sum loses it
    }

    [Fact]
    public void Cross()
    {
        var x = new Float3(1, 0, 0);
        var y = new Float3(0, 1, 0);
        FloatAssert.SameVector(new Float3(0, 0, 1), Gpu.Cross(x, y));
        FloatAssert.SameVector(new Float3(0, 0, -1), Gpu.Cross(y, x));
        FloatAssert.SameVector(new Float3(-3, 6, -3), Gpu.Cross(new Float3(1, 2, 3), new Float3(4, 5, 6)));

        // fma(a, b, -(c * d)): the a * b product is not rounded.
        float a = 1f + MathF.ScaleB(1f, -12);
        FloatAssert.Same(MathF.ScaleB(1f, -11) + MathF.ScaleB(1f, -24), Gpu.Cross(new Float3(0, a, 1), new Float3(0, 1, a)).X);
    }

    [Fact]
    public void LengthAndDistance()
    {
        FloatAssert.Same(5f, Gpu.Length(new Float2(3, -4)));
        FloatAssert.Same(3f, Gpu.Length(new Float3(1, 2, 2)));
        FloatAssert.Same(2f, Gpu.Length(new Float4(1, -1, 1, -1)));
        FloatAssert.Same(5f, Gpu.Distance(new Float3(1, 1, 1), new Float3(4, 5, 1)));
        FloatAssert.Same(0f, Gpu.Distance(new Float2(7, 7), new Float2(7, 7)));
        FloatAssert.Same(10f, Gpu.Distance(new Float4(0, 0, 0, -5), new Float4(0, 0, 0, 5)));
    }

    [Fact]
    public void LengthNeitherOverflowsNorUnderflows()
    {
        FloatAssert.Same(float.MaxValue, Gpu.Length(new Float3(0, 0, float.MaxValue)));
        FloatAssert.Same(1e30f, Gpu.Length(new Float2(1, 1e30f)));
        FloatAssert.Near(5e-30f, Gpu.Length(new Float3(3e-30f, 4e-30f, 0)));
        FloatAssert.Near(2e30f, Gpu.Length(new Float4(1e30f, 1e30f, 1e30f, 1e30f)));
        FloatAssert.Same(float.PositiveInfinity, Gpu.Length(new Float3(float.MaxValue, float.MaxValue, 0))); // the true result overflows
        FloatAssert.Same(0f, Gpu.Length(new Float2(0, -0f)));
        FloatAssert.Same(float.PositiveInfinity, Gpu.Length(new Float2(float.NegativeInfinity, 1)));
        FloatAssert.Same(float.NaN, Gpu.Length(new Float2(float.NaN, 1)));
    }

    [Fact]
    public void NormalizeNonZero()
    {
        Float3 n = Gpu.Normalize(new Float3(3, 0, 4));
        FloatAssert.Near(0.6f, n.X);
        FloatAssert.Same(0f, n.Y);
        FloatAssert.Near(0.8f, n.Z);
        FloatAssert.Near(1f, Gpu.Length(n));

        FloatAssert.SameVector(new Float2(-1, 0), Gpu.Normalize(new Float2(-2, 0)));
        FloatAssert.SameVector(new Float2(1, 0), Gpu.Normalize(new Float2(1e-10f, 0)));

        Float4 d = Gpu.Normalize(new Float4(1, 1, 1, 1));
        Assert.All(new[] { d.X, d.Y, d.Z, d.W }, c => FloatAssert.Same(0.5f, c));
    }

    [Fact]
    public void NormalizeNeitherOverflowsNorUnderflows()
    {
        FloatAssert.SameVector(new Float3(0, 0, 1), Gpu.Normalize(new Float3(0, 0, 1e30f)));
        FloatAssert.SameVector(new Float2(0, -1), Gpu.Normalize(new Float2(0, -float.MaxValue)));
        Float3 tiny = Gpu.Normalize(new Float3(3e-30f, 4e-30f, 0));
        FloatAssert.Near(0.6f, tiny.X);
        FloatAssert.Near(0.8f, tiny.Y);
    }

    [Fact]
    public void NormalizeZeroReturnsTheZeroVector()
    {
        // As on Apple GPUs: no NaN, and the signs of the zeros are kept.
        FloatAssert.SameVector(new Float3(0, 0, 0), Gpu.Normalize(new Float3(0, 0, 0)));
        FloatAssert.SameVector(new Float2(-0f, 0), Gpu.Normalize(new Float2(-0f, 0)));
        FloatAssert.SameVector(new Float4(0, -0f, 0, -0f), Gpu.Normalize(new Float4(0, -0f, 0, -0f)));
    }

    [Fact]
    public void NormalizeInfiniteOrNaNIsNaN()
    {
        FloatAssert.SameVector(new Float3(float.NaN), Gpu.Normalize(new Float3(0, float.PositiveInfinity, 1)));
        FloatAssert.SameVector(new Float2(float.NaN), Gpu.Normalize(new Float2(float.NegativeInfinity, float.PositiveInfinity)));
        FloatAssert.SameVector(new Float4(float.NaN), Gpu.Normalize(new Float4(1, float.NaN, 0, 0)));
    }

    [Fact]
    public void Reflect()
    {
        FloatAssert.SameVector(new Float3(1, 1, 0), Gpu.Reflect(new Float3(1, -1, 0), new Float3(0, 1, 0)));
        FloatAssert.SameVector(new Float2(-2, 3), Gpu.Reflect(new Float2(2, 3), new Float2(1, 0)));
        FloatAssert.SameVector(new Float4(0, 0, 0, -5), Gpu.Reflect(new Float4(0, 0, 0, 5), new Float4(0, 0, 0, 1)));
    }
}
