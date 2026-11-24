using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpCompute.Vectors.Tests;

public class MatrixTests
{
    public static readonly Type[] All = [typeof(Float2x2), typeof(Float3x3)];

    public static TheoryData<Type> AllData => new(All);

    [Fact]
    public void LayoutIsTightRowMajorFloats()
    {
        Assert.Equal(16, Unsafe.SizeOf<Float2x2>());
        Assert.Equal(36, Unsafe.SizeOf<Float3x3>());
        var m = new Float3x3(1, 2, 3, 4, 5, 6, 7, 8, 9);
        ref float first = ref Unsafe.As<Float3x3, float>(ref m);
        for (int i = 0; i < 9; i++)
        {
            Assert.Equal(i + 1, Unsafe.Add(ref first, i));
        }
    }

    [Theory]
    [MemberData(nameof(AllData))]
    public void StorageIsInternalAndRowMajor(Type t)
    {
        int n = t == typeof(Float2x2) ? 2 : 3;
        FieldInfo[] fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Equal(n * n, fields.Length);
        Assert.All(fields, f => Assert.True(f.IsAssembly && f.FieldType == typeof(float), $"{f.Name} must be an internal float"));
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                Assert.Equal(4 * ((r * n) + c), (int)Marshal.OffsetOf(t, $"_m{r}{c}"));
            }
        }
    }

    [Fact]
    public void ConstructorsTakeRows()
    {
        Assert.Equal(new float[,] { { 1, 2 }, { 3, 4 } }, Elements(new Float2x2(1, 2, 3, 4)));
        Assert.Equal(new float[,] { { 1, 2 }, { 3, 4 } }, Elements(new Float2x2(new Float2(1, 2), new Float2(3, 4))));
        float[,] expected = { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 } };
        Assert.Equal(expected, Elements(new Float3x3(1, 2, 3, 4, 5, 6, 7, 8, 9)));
        Assert.Equal(expected, Elements(new Float3x3(new Float3(1, 2, 3), new Float3(4, 5, 6), new Float3(7, 8, 9))));
    }

    [Fact]
    public void ComponentwiseOperators()
    {
        var a = new Float2x2(1, 2, 3, 4);
        var b = new Float2x2(10, 20, 30, 40);
        Assert.Equal(new float[,] { { 11, 22 }, { 33, 44 } }, Elements(a + b));
        Assert.Equal(new float[,] { { 9, 18 }, { 27, 36 } }, Elements(b - a));
        Assert.Equal(new float[,] { { 2, 4 }, { 6, 8 } }, Elements(a * 2f));
        Assert.Equal(new float[,] { { 2, 4 }, { 6, 8 } }, Elements(2f * a));
        Assert.Equal(new float[,] { { 0.5f, 1 }, { 1.5f, 2 } }, Elements(a / 2f));
        Assert.Equal(new float[,] { { -1, -2 }, { -3, -4 } }, Elements(a * -1f)); // the spelling of unary minus

        var c = new Float3x3(1, 2, 3, 4, 5, 6, 7, 8, 9);
        c += c;
        c -= new Float3x3(1, 1, 1, 1, 1, 1, 1, 1, 1);
        c *= 0.5f;
        c /= 0.25f;
        Assert.Equal(new float[,] { { 2, 6, 10 }, { 14, 18, 22 }, { 26, 30, 34 } }, Elements(c));
    }

    [Fact]
    public void MulFollowsHlslRowVectorConvention()
    {
        var m = new Float2x2(1, 2, 3, 4);
        var v = new Float2(5, 6);
        FloatAssert.SameVector(new Float2(23, 34), Gpu.Mul(v, m)); // v * M: (5*1 + 6*3, 5*2 + 6*4)
        FloatAssert.SameVector(new Float2(17, 39), Gpu.Mul(m, v)); // M * v: (1*5 + 2*6, 3*5 + 4*6)

        var b = new Float2x2(5, 6, 7, 8);
        Assert.Equal(new float[,] { { 19, 22 }, { 43, 50 } }, Elements(Gpu.Mul(m, b)));
        Assert.Equal(new float[,] { { 23, 34 }, { 31, 46 } }, Elements(Gpu.Mul(b, m)));

        var m3 = new Float3x3(1, 2, 3, 4, 5, 6, 7, 8, 10);
        var v3 = new Float3(1, -1, 2);
        FloatAssert.SameVector(new Float3(11, 13, 17), Gpu.Mul(v3, m3));
        FloatAssert.SameVector(new Float3(5, 11, 19), Gpu.Mul(m3, v3));
        Assert.Equal(
            new float[,] { { 30, 36, 45 }, { 66, 81, 102 }, { 109, 134, 169 } },
            Elements(Gpu.Mul(m3, m3)));
    }

    [Fact]
    public void MulIsAssociativeAndTransposeConsistentOnExactValues()
    {
        var a = new Float3x3(1, 2, 0, -1, 3, 1, 2, 0, 1);
        var b = new Float3x3(0, 1, 2, 1, 0, -1, 3, 1, 1);
        var v = new Float3(2, -1, 3);
        FloatAssert.SameVector(Gpu.Mul(Gpu.Mul(v, a), b), Gpu.Mul(v, Gpu.Mul(a, b)));
        FloatAssert.SameVector(Gpu.Mul(a, Gpu.Mul(b, v)), Gpu.Mul(Gpu.Mul(a, b), v));
        FloatAssert.SameVector(Gpu.Mul(a, v), Gpu.Mul(v, Transpose(a)));

        var identity = new Float3x3(1, 0, 0, 0, 1, 0, 0, 0, 1);
        Assert.Equal(Elements(a), Elements(Gpu.Mul(a, identity)));
        Assert.Equal(Elements(a), Elements(Gpu.Mul(identity, a)));
        FloatAssert.SameVector(v, Gpu.Mul(v, identity));
    }

    [Fact]
    public void MulSumsAreFmaChainsInIndexOrder()
    {
        // (1 + 2^-12)^2 = 1 + 2^-11 + 2^-24 is not representable; the fma keeps the 2^-24 term, like the GPU.
        float a = 1f + MathF.ScaleB(1f, -12);
        float expected = MathF.ScaleB(1f, -11) + MathF.ScaleB(1f, -24);
        Assert.Equal(expected, Gpu.Mul(new Float2(1, a), new Float2x2(-1, 0, a, 0)).X);
        Assert.Equal(expected, Gpu.Mul(new Float2x2(-1, a, 0, 0), new Float2(1, a)).X);
        Assert.Equal(expected, Elements(Gpu.Mul(new Float2x2(1, a, 0, 0), new Float2x2(-1, 0, a, 0)))[0, 0]);
        Assert.Equal(expected, Gpu.Mul(new Float3(1, 0, a), new Float3x3(-1, 0, 0, 0, 0, 0, a, 0, 0)).X);
    }

    /// <summary>
    /// No unary minus (invalid MSL), no matrix * matrix operator (HLSL is componentwise, MSL is a
    /// product), no element accessors or other public instance members besides ToString.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllData))]
    public void SurfaceIsExactlyTheContract(Type t)
    {
        string[] operators = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.IsSpecialName)
            .Select(m => $"{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})")
            .Order()
            .ToArray();
        string[] expected =
        [
            $"op_Addition({t.Name},{t.Name})",
            $"op_Division({t.Name},Single)",
            $"op_Multiply(Single,{t.Name})",
            $"op_Multiply({t.Name},Single)",
            $"op_Subtraction({t.Name},{t.Name})",
        ];
        Assert.Equal(expected.Order(), operators);

        Assert.Empty(t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        Assert.Empty(t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        Assert.Equal(
            ["ToString"],
            t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName).Select(m => m.Name));

        int n = t == typeof(Float2x2) ? 2 : 3;
        Type row = VectorTypes.Vector(typeof(float), n);
        ConstructorInfo[] ctors = t.GetConstructors();
        Assert.Equal(2, ctors.Length);
        Assert.Contains(ctors, c => c.GetParameters().Select(p => p.ParameterType).SequenceEqual(Enumerable.Repeat(typeof(float), n * n)));
        Assert.Contains(ctors, c => c.GetParameters().Select(p => p.ParameterType).SequenceEqual(Enumerable.Repeat(row, n)));
    }

    [Fact]
    public void ToStringListsRowsInvariantly()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("[(1.5, 2), (3, 4)]", new Float2x2(1.5f, 2, 3, 4).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    internal static float[,] Elements(Float2x2 m) => Elements(m, 2);

    internal static float[,] Elements(Float3x3 m) => Elements(m, 3);

    private static float[,] Elements(object m, int n)
    {
        var result = new float[n, n];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                result[r, c] = (float)m.GetType().GetField($"_m{r}{c}", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(m)!;
            }
        }

        return result;
    }

    private static Float3x3 Transpose(Float3x3 m)
    {
        float[,] e = Elements(m);
        return new Float3x3(e[0, 0], e[1, 0], e[2, 0], e[0, 1], e[1, 1], e[2, 1], e[0, 2], e[1, 2], e[2, 2]);
    }
}
