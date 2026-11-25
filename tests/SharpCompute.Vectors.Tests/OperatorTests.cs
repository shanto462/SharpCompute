using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SharpCompute.Vectors.Tests;

public class OperatorTests
{
    private static readonly Dictionary<string, Func<dynamic, dynamic, object>> Binary = new()
    {
        ["op_Addition"] = (a, b) => a + b,
        ["op_Subtraction"] = (a, b) => a - b,
        ["op_Multiply"] = (a, b) => a * b,
        ["op_Division"] = (a, b) => a / b,
        ["op_Modulus"] = (a, b) => a % b,
        ["op_BitwiseAnd"] = (a, b) => a & b,
        ["op_BitwiseOr"] = (a, b) => a | b,
        ["op_ExclusiveOr"] = (a, b) => a ^ b,
        ["op_LeftShift"] = (a, b) => a << b,
        ["op_RightShift"] = (a, b) => a >> b,
        ["op_LessThan"] = (a, b) => a < b,
        ["op_LessThanOrEqual"] = (a, b) => a <= b,
        ["op_GreaterThan"] = (a, b) => a > b,
        ["op_GreaterThanOrEqual"] = (a, b) => a >= b,
    };

    private static readonly string[] Comparisons = ["op_LessThan", "op_LessThanOrEqual", "op_GreaterThan", "op_GreaterThanOrEqual"];

    private static readonly Dictionary<string, Func<dynamic, object>> Unary = new()
    {
        ["op_UnaryNegation"] = a => -a,
        ["op_OnesComplement"] = a => ~a,
    };

    /// <summary>
    /// The exact operator set per element kind (vector∘vector, vector∘scalar, scalar∘vector). The
    /// ordering operators are there for every numeric vector and return the bool vector of the same
    /// width; equality is not an operator (<c>Gpu.Equal</c>, <c>Gpu.NotEqual</c>).
    /// </summary>
    [Theory]
    [MemberData(nameof(VectorTypes.NumericData), MemberType = typeof(VectorTypes))]
    public void OperatorSetMatchesMsl(Type t)
    {
        Type s = VectorTypes.Element(t);
        var expected = new HashSet<string>();
        string[] arithmetic = ["op_Addition", "op_Subtraction", "op_Multiply", "op_Division", "op_Modulus"];
        string[] bitwise = VectorTypes.IsFloating(s) ? [] : ["op_BitwiseAnd", "op_BitwiseOr", "op_ExclusiveOr"];
        foreach (string op in arithmetic.Concat(bitwise).Concat(Comparisons))
        {
            expected.Add($"{op}({t.Name},{t.Name})");
            expected.Add($"{op}({t.Name},{s.Name})");
            expected.Add($"{op}({s.Name},{t.Name})");
        }

        if (s != typeof(uint))
        {
            expected.Add($"op_UnaryNegation({t.Name})");
        }

        if (!VectorTypes.IsFloating(s))
        {
            expected.Add($"op_OnesComplement({t.Name})");
            foreach (string op in new[] { "op_LeftShift", "op_RightShift" })
            {
                expected.Add($"{op}({t.Name},{t.Name})");
                expected.Add($"{op}({t.Name},Int32)");
            }
        }

        var actual = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.IsSpecialName && m.Name.StartsWith("op_", StringComparison.Ordinal) && m.Name != "op_Explicit")
            .Select(m => $"{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})")
            .ToHashSet();

        Assert.Equal(expected.Order(), actual.Order());

        // Every comparison answers with the bool vector of the same width; everything else with the vector itself.
        Type mask = VectorTypes.Vector(typeof(bool), VectorTypes.Width(t));
        Assert.All(
            t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.IsSpecialName && m.Name.StartsWith("op_", StringComparison.Ordinal) && m.Name != "op_Explicit"),
            m => Assert.Equal(Comparisons.Contains(m.Name) ? mask : t, m.ReturnType));
    }

    /// <summary>
    /// Every operator overload computes the scalar C# operator per component: the arithmetic of the
    /// element type (for a half vector that of <see cref="Half"/>, one correctly rounded half operation
    /// each), and for a comparison the scalar comparison, into a bool vector.
    /// </summary>
    [Theory]
    [MemberData(nameof(VectorTypes.NumericData), MemberType = typeof(VectorTypes))]
    public void OperatorsAreComponentwise(Type t)
    {
        int n = VectorTypes.Width(t);
        Type mask = VectorTypes.Vector(typeof(bool), n);
        foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.IsSpecialName && m.Name != "op_Explicit"))
        {
            ParameterInfo[] ps = m.GetParameters();
            object[] args = ps.Select((p, i) => p.ParameterType == t
                ? VectorTypes.SampleVector(t, i)
                : p.ParameterType == typeof(int) && m.Name.Contains("Shift", StringComparison.Ordinal) ? 3
                : VectorTypes.Sample(p.ParameterType, i, 0)).ToArray();
            object result = m.Invoke(null, args)!;
            bool comparison = Comparisons.Contains(m.Name);
            Assert.Equal(comparison ? mask : t, result.GetType());

            for (int c = 0; c < n; c++)
            {
                object Component(object arg) => arg.GetType() == t ? VectorTypes.Get(arg, c) : arg;
                object expected = ps.Length == 1
                    ? Unary[m.Name](Component(args[0]))
                    : Binary[m.Name](Component(args[0]), ShiftCount(m.Name, Component(args[1])));
                object actual = VectorTypes.Get(result, c);
                if (m.Name == "op_Modulus" && expected is Half remainder && remainder == (Half)0f && (Half)Component(args[0]) != (Half)0f)
                {
                    // Where C#'s % gives -0 for a negative dividend, a half vector gives +0, as the GPU does.
                    expected = (Half)0f;
                }

                // A comparison is a bool and Half arithmetic a Half already (Half is not IConvertible).
                object typed = comparison || expected is Half ? expected : Convert.ChangeType(expected, VectorTypes.Element(t), null);
                Assert.True(
                    VectorTypes.SameValue(typed, actual),
                    $"{m} component {c}: expected {VectorTypes.Format(expected)}, got {VectorTypes.Format(actual)}");
            }
        }
    }

    [Fact]
    public void ScalarOnTheLeftKeepsOperandOrder()
    {
        FloatAssert.SameVector(new Float2(9, 8), 10f - new Float2(1, 2));
        FloatAssert.SameVector(new Float2(2, 0.5f), 2f / new Float2(1, 4));
        Assert.Equal(new Int2(3, 2), 12 / new Int2(4, 5));
        Assert.Equal(new UInt3(1, 2, 0), 5u % new UInt3(2, 3, 5));
    }

    [Fact]
    public void FloatModulusHasFmodSemantics()
    {
        FloatAssert.SameVector(new Float4(1.5f, -1.5f, 1.5f, -1.5f), new Float4(5.5f, -5.5f, 5.5f, -5.5f) % new Float4(2, 2, -2, -2));
        FloatAssert.SameVector(new Float2(-0f, 0f), new Float2(-4, 4) % 2f);
        FloatAssert.SameVector(new Float3(float.NaN, float.NaN, 1), new Float3(1, float.PositiveInfinity, 1) % new Float3(0, 1, float.PositiveInfinity));
        FloatAssert.SameVector(new Float2(1, 0.5f), 7f % new Float2(3, 1.625f));
    }

    [Fact]
    public void IntegerRemainderTruncatesTowardZero()
    {
        Assert.Equal(new Int4(-1, 1, -1, 1), new Int4(-7, 7, -7, 7) % new Int4(3, 3, -3, -3));
    }

    [Fact]
    public void IntegerAddSubMulNegateWrapEvenInCheckedContext()
    {
        checked
        {
            Assert.Equal(new Int2(int.MinValue, int.MaxValue), new Int2(int.MaxValue, int.MinValue) + new Int2(1, -1));
            Assert.Equal(new UInt2(uint.MaxValue, 0), new UInt2(0, uint.MaxValue) + new UInt2(uint.MaxValue, 1));
            Assert.Equal(new Int2(int.MinValue, 0), -new Int2(int.MinValue, 0));
            Assert.Equal(new UInt2(0xFFFFFFFE, 0), new UInt2(0x7FFFFFFF, 0) * 2u);
        }
    }

    [Fact]
    public void IntegerDivisionTrapsOnCpu()
    {
        // Documented gap: .NET throws even in unchecked code; the GPU silently returns a value.
        Assert.Throws<DivideByZeroException>(() => new Int2(1, 2) / new Int2(1, 0));
        Assert.Throws<DivideByZeroException>(() => new UInt2(1, 2) % 0u);
        Assert.Throws<OverflowException>(() => new Int2(int.MinValue, 1) / -1);
        Assert.Throws<OverflowException>(() => new Int2(int.MinValue, 1) % new Int2(-1, 1));
    }

    [Fact]
    public void ShiftsMaskTheCountAndKeepSignedness()
    {
        Assert.Equal(new Int2(2, -2), new Int2(1, -1) << 33);
        Assert.Equal(new Int2(-1, 1), new Int2(int.MinValue, 0x40000000) >> new Int2(31, 30));
        Assert.Equal(new UInt2(1, 0x7FFFFFFF), new UInt2(0x80000000, uint.MaxValue) >> new UInt2(31, 1));
        Assert.Equal(new UInt3(2, 4, 8), new UInt3(1) << new UInt3(1, 2, 35));
    }

    [Fact]
    public void BitwiseOperators()
    {
        Assert.Equal(new UInt2(0x0F, 0xFF), new UInt2(0xFF, 0xF0) & new UInt2(0x0F, 0xFF) | new UInt2(0, 0x0F));
        Assert.Equal(new Int3(~1, ~0, 0), ~new Int3(1, 0, -1));
        Assert.Equal(new Int2(3, 0), new Int2(5, 6) ^ new Int2(6, 6));
        Assert.Equal(new UInt2(uint.MaxValue, 0), ~new UInt2(0, uint.MaxValue));
    }

    [Fact]
    public void FloatNegationFlipsSignOfZero()
    {
        FloatAssert.SameVector(new Float3(-0f, 0f, -1), -new Float3(0, -0f, 1));
    }

    [Fact]
    public void CompoundAssignmentUsesTheOperators()
    {
        var v = new Float3(1, 2, 3);
        v *= 2f;
        v += new Float3(1);
        v.XY -= new Float2(3, 5);
        FloatAssert.SameVector(new Float3(0, 0, 7), v);

        var i = new Int2(1, 2);
        i <<= 2;
        i |= 1;
        Assert.Equal(new Int2(5, 9), i);
    }

    private static object ShiftCount(string op, object value) =>
        op.Contains("Shift", StringComparison.Ordinal) && value is uint u ? (int)u : value;
}
