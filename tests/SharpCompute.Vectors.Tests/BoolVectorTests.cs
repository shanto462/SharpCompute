using System;
using System.Linq;
using System.Reflection;

namespace SharpCompute.Vectors.Tests;

/// <summary>
/// <see cref="Bool2"/>, <see cref="Bool3"/> and <see cref="Bool4"/>, and everything that produces or
/// consumes one: the ordering operators of the numeric vectors, <c>Gpu.Equal</c>/<c>NotEqual</c>,
/// the vector overloads of <c>Gpu.IsNaN</c>/<c>IsInfinity</c>/<c>IsFinite</c>, and
/// <c>Gpu.Any</c>/<c>All</c>/<c>Select</c>.
/// </summary>
public class BoolVectorTests
{
    private static readonly Half NegativeZeroHalf = BitConverter.UInt16BitsToHalf(0x8000);

    // ---- the type itself --------------------------------------------------------------------------

    [Fact]
    public void DefaultIsAllFalse()
    {
        Assert.Equal(new Bool2(false, false), default);
        Assert.Equal(new Bool3(false), new Bool3());
        Assert.False(Gpu.Any(default(Bool4)));
    }

    [Fact]
    public void LogicalOperatorsAreComponentwise()
    {
        var a = new Bool4(true, true, false, false);
        var b = new Bool4(true, false, true, false);

        Assert.Equal(new Bool4(true, false, false, false), a & b);
        Assert.Equal(new Bool4(true, true, true, false), a | b);
        Assert.Equal(new Bool4(false, true, true, false), a ^ b);
        Assert.Equal(new Bool4(false, false, true, true), !a);
        Assert.Equal(new Bool3(false, true, false), !new Bool3(true, false, true));
        Assert.Equal(new Bool2(false, true), new Bool2(true, true) ^ new Bool2(true, false));
    }

    [Fact]
    public void CompoundAssignmentUsesTheOperators()
    {
        var mask = new Bool3(true, true, false);
        mask &= new Bool3(true, false, false);
        mask |= new Bool3(false, false, true);
        mask ^= new Bool3(true, true, true);
        Assert.Equal(new Bool3(false, true, false), mask);
    }

    /// <summary>
    /// Exactly <c>!</c>, <c>&amp;</c>, <c>|</c> and <c>^</c>. In particular no <c>true</c>/<c>false</c>
    /// operators (which would allow <c>&amp;&amp;</c>, <c>||</c> and <c>if (mask)</c>), no conversion to
    /// or from anything, and no <c>==</c>/<c>!=</c>.
    /// </summary>
    [Theory]
    [InlineData(typeof(Bool2))]
    [InlineData(typeof(Bool3))]
    [InlineData(typeof(Bool4))]
    public void OperatorSurface(Type t)
    {
        string[] operators = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.IsSpecialName)
            .Select(m => $"{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})")
            .Order()
            .ToArray();

        Assert.Equal(
            new[] { $"op_BitwiseAnd({t.Name},{t.Name})", $"op_BitwiseOr({t.Name},{t.Name})", $"op_ExclusiveOr({t.Name},{t.Name})", $"op_LogicalNot({t.Name})" },
            operators);
        Assert.All(t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.IsSpecialName), m => Assert.Equal(t, m.ReturnType));
    }

    // ---- any / all ---------------------------------------------------------------------------------

    [Fact]
    public void AnyAndAll()
    {
        Assert.True(Gpu.Any(new Bool2(false, true)));
        Assert.False(Gpu.Any(new Bool2(false, false)));
        Assert.True(Gpu.All(new Bool2(true, true)));
        Assert.False(Gpu.All(new Bool2(true, false)));

        Assert.True(Gpu.Any(new Bool3(false, false, true)));
        Assert.False(Gpu.All(new Bool3(true, true, false)));
        Assert.True(Gpu.All(new Bool3(true)));

        // Each component counts, the last one included.
        Assert.True(Gpu.Any(new Bool4(false, false, false, true)));
        Assert.False(Gpu.All(new Bool4(true, true, true, false)));
        Assert.True(Gpu.All(new Bool4(true)));
        Assert.False(Gpu.Any(new Bool4(false)));
    }

    // ---- comparisons -------------------------------------------------------------------------------

    [Fact]
    public void OrderingOperatorsAnswerPerComponent()
    {
        var a = new Float3(1, 2, 3);
        var b = new Float3(2, 2, 1);

        Assert.Equal(new Bool3(true, false, false), a < b);
        Assert.Equal(new Bool3(true, true, false), a <= b);
        Assert.Equal(new Bool3(false, false, true), a > b);
        Assert.Equal(new Bool3(false, true, true), a >= b);

        Assert.Equal(new Bool2(true, false), new Int2(-5, 5) < new Int2(0, 0));
        Assert.Equal(new Bool4(false, false, true, true), new Int4(1, 2, 3, 4) >= 3);

        // uint compares unsigned: 0xFFFFFFFF is the largest value, not -1.
        Assert.Equal(new Bool2(false, true), new UInt2(uint.MaxValue, 0) < new UInt2(1, 1));
        Assert.Equal(new Bool2(true, false), new UInt2(uint.MaxValue, 0) > 1u);

        Assert.Equal(new Bool2(true, false), new Half2((Half)0.25f, (Half)0.75f) < (Half)0.5f);
        Assert.Equal(new Bool3(false, true, true), (Half3)new Float3(1, 2, 3) >= (Half3)new Float3(2, 2, 1));
    }

    [Fact]
    public void ScalarOnTheLeftKeepsOperandOrder()
    {
        Assert.Equal(new Bool3(false, false, true), 2f < new Float3(1, 2, 3));
        Assert.Equal(new Bool3(false, true, true), 2f <= new Float3(1, 2, 3));
        Assert.Equal(new Bool3(true, false, false), 2 > new Int3(1, 2, 3));
        Assert.Equal(new Bool3(true, true, false), 2u >= new UInt3(1, 2, 3));
        Assert.Equal(new Bool2(true, false), (Half)2 > (Half2)new Float2(1, 3));
    }

    [Fact]
    public void AnyComparisonWithNaNIsFalse_AndZerosCompareEqual()
    {
        var nan = new Float2(float.NaN, 1);
        var one = new Float2(1, float.NaN);

        Assert.Equal(new Bool2(false, false), nan < one);
        Assert.Equal(new Bool2(false, false), nan <= one);
        Assert.Equal(new Bool2(false, false), nan > one);
        Assert.Equal(new Bool2(false, false), nan >= one);

        // So !(a < b) is not a >= b: it is true for NaN.
        Assert.Equal(new Bool2(true, true), !(nan < one));

        Assert.Equal(new Bool2(false, true), new Float2(-0f, -0f) < new Float2(0f, float.Epsilon));
        Assert.Equal(new Bool2(true, true), new Float2(-0f, 0f) >= new Float2(0f, -0f));

        var halfNaN = new Half2(Half.NaN, NegativeZeroHalf);
        Assert.Equal(new Bool2(false, false), halfNaN < new Half2((Half)1f, (Half)0f));
        Assert.Equal(new Bool2(false, true), halfNaN >= new Half2((Half)1f, (Half)0f));
    }

    [Fact]
    public void EqualAndNotEqual()
    {
        Assert.Equal(new Bool3(true, false, true), Gpu.Equal(new Int3(1, 2, 3), new Int3(1, 5, 3)));
        Assert.Equal(new Bool3(false, true, false), Gpu.NotEqual(new Int3(1, 2, 3), new Int3(1, 5, 3)));
        Assert.Equal(new Bool2(true, false), Gpu.Equal(new UInt2(7, 8), 7u));
        Assert.Equal(new Bool4(false, true, true, true), Gpu.NotEqual(new Float4(0, 1, 2, 3), 0f));

        // IEEE equality: a NaN equals nothing, itself included, and -0 equals +0.
        var v = new Float3(float.NaN, -0f, 1.5f);
        Assert.Equal(new Bool3(false, true, true), Gpu.Equal(v, v));
        Assert.Equal(new Bool3(true, false, false), Gpu.NotEqual(v, v));
        Assert.Equal(new Bool3(false, true, false), Gpu.Equal(v, 0f));

        var h = new Half3(Half.NaN, NegativeZeroHalf, (Half)1.5f);
        Assert.Equal(new Bool3(false, true, true), Gpu.Equal(h, h));
        Assert.Equal(new Bool3(true, false, false), Gpu.NotEqual(h, h));
        Assert.Equal(new Bool3(false, false, true), Gpu.Equal(h, (Half)1.5f));
    }

    /// <summary>
    /// The numeric vectors have no <c>==</c>/<c>!=</c> at all: C# would not let them return a bool
    /// vector without breaking <c>if (a == b)</c>, and a <c>bool</c> answer is not what MSL computes.
    /// </summary>
    [Theory]
    [MemberData(nameof(VectorTypes.EveryData), MemberType = typeof(VectorTypes))]
    public void NoVectorHasEqualityOperators(Type t)
    {
        Assert.DoesNotContain(t.GetMethods(BindingFlags.Public | BindingFlags.Static), m => m.Name is "op_Equality" or "op_Inequality" or "op_True" or "op_False" or "op_Implicit");
    }

    // ---- classification ----------------------------------------------------------------------------

    [Fact]
    public void ClassificationOfVectors()
    {
        var v = new Float4(float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.Epsilon);
        Assert.Equal(new Bool4(true, false, false, false), Gpu.IsNaN(v));
        Assert.Equal(new Bool4(false, true, true, false), Gpu.IsInfinity(v));
        Assert.Equal(new Bool4(false, false, false, true), Gpu.IsFinite(v));
        Assert.Equal(new Bool2(true, true), Gpu.IsFinite(new Float2(-0f, float.MaxValue)));
        Assert.Equal(new Bool3(false, true, false), Gpu.IsNaN(new Float3(1, -float.NaN, 0)));

        var h = new Half4(Half.NaN, Half.PositiveInfinity, Half.NegativeInfinity, BitConverter.UInt16BitsToHalf(0x0001));
        Assert.Equal(new Bool4(true, false, false, false), Gpu.IsNaN(h));
        Assert.Equal(new Bool4(false, true, true, false), Gpu.IsInfinity(h));
        Assert.Equal(new Bool4(false, false, false, true), Gpu.IsFinite(h));
        Assert.Equal(new Bool2(true, false), Gpu.IsNaN(new Half2(BitConverter.UInt16BitsToHalf(0xFE01), BitConverter.UInt16BitsToHalf(0x7BFF))));
        Assert.Equal(new Bool3(true, true, false), Gpu.IsFinite(new Half3(NegativeZeroHalf, BitConverter.UInt16BitsToHalf(0x7BFF), Half.NaN)));
    }

    // ---- select ------------------------------------------------------------------------------------

    [Fact]
    public void SelectPicksWhenTrueWhereTheMaskIsTrue()
    {
        var mask = new Bool3(true, false, true);
        FloatAssert.SameVector(new Float3(1, 20, 3), Gpu.Select(mask, new Float3(1, 2, 3), new Float3(10, 20, 30)));
        Assert.Equal(new Int3(1, 20, 3), Gpu.Select(mask, new Int3(1, 2, 3), new Int3(10, 20, 30)));
        Assert.Equal(new UInt3(1, 20, 3), Gpu.Select(mask, new UInt3(1, 2, 3), new UInt3(10, 20, 30)));
        FloatAssert.SameVector((Half3)new Float3(1, 20, 3), Gpu.Select(mask, (Half3)new Float3(1, 2, 3), (Half3)new Float3(10, 20, 30)));
        Assert.Equal(new Bool3(true, true, false), Gpu.Select(mask, new Bool3(true, false, false), new Bool3(false, true, true)));

        // The idiom it is for: a componentwise conditional.
        var color = new Float3(0.2f, 0.9f, 0.5f);
        FloatAssert.SameVector(new Float3(0f, 1f, 0f), Gpu.Select(color > 0.5f, new Float3(1), new Float3(0)));
        Assert.Equal(new Int2(5, 0), Gpu.Select(new Int2(5, -5) > 0, new Int2(5, -5), new Int2(0)));
    }

    [Fact]
    public void SelectCopiesBits_NaNPayloadsAndSignedZerosIncluded()
    {
        float payload = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC12345));
        Float2 picked = Gpu.Select(new Bool2(true, false), new Float2(payload, 1), new Float2(2, -0f));
        Assert.Equal(unchecked((int)0xFFC12345), BitConverter.SingleToInt32Bits(picked.X));
        Assert.Equal(BitConverter.SingleToInt32Bits(-0f), BitConverter.SingleToInt32Bits(picked.Y));

        Half2 half = Gpu.Select(new Bool2(false, true), new Half2((Half)1f, NegativeZeroHalf), new Half2(BitConverter.UInt16BitsToHalf(0x7E55), (Half)2f));
        Assert.Equal(0x7E55, BitConverter.HalfToUInt16Bits(half.X));
        Assert.Equal(0x8000, BitConverter.HalfToUInt16Bits(half.Y));
    }

    /// <summary>Every <c>Select</c> overload, for every mask of its width: component i comes from whenTrue exactly when mask component i is true.</summary>
    [Theory]
    [MemberData(nameof(VectorTypes.EveryData), MemberType = typeof(VectorTypes))]
    public void SelectIsAComponentwiseConditional(Type t)
    {
        int n = VectorTypes.Width(t);
        Type maskType = VectorTypes.Vector(typeof(bool), n);
        MethodInfo select = typeof(Gpu).GetMethod("Select", [maskType, t, t]) ?? throw new InvalidOperationException($"no Select for {t.Name}");
        Assert.Equal(["mask", "whenTrue", "whenFalse"], select.GetParameters().Select(p => p.Name));

        object whenTrue = VectorTypes.SampleVector(t, 0);
        object whenFalse = VectorTypes.SampleVector(t, 1);

        for (int bits = 0; bits < 1 << n; bits++)
        {
            object mask = VectorTypes.Make(maskType, Enumerable.Range(0, n).Select(i => (object)((bits >> i & 1) != 0)));
            object result = select.Invoke(null, [mask, whenTrue, whenFalse])!;
            Assert.Equal(t, result.GetType());

            for (int c = 0; c < n; c++)
            {
                object expected = VectorTypes.Get((bits >> c & 1) != 0 ? whenTrue : whenFalse, c);
                Assert.True(VectorTypes.SameValue(expected, VectorTypes.Get(result, c)), $"{t.Name}, mask {bits:X}, component {c}");
            }
        }
    }

    /// <summary>
    /// <c>Select</c> is HLSL's <c>select(condition, whenTrue, whenFalse)</c>; MSL's is
    /// <c>select(whenFalse, whenTrue, condition)</c>, so the mapping reverses the arguments.
    /// </summary>
    [Fact]
    public void SelectMapsToMetalSelectWithItsArgumentsReversed()
    {
        MethodInfo[] overloads = typeof(Gpu).GetMethods().Where(m => m.Name == "Select").ToArray();
        Assert.Equal(15, overloads.Length);
        Assert.All(overloads, m =>
        {
            MslIntrinsicAttribute attribute = m.GetCustomAttribute<MslIntrinsicAttribute>()!;
            Assert.Equal("metal::select", attribute.Name);
            Assert.True(attribute.ReverseArguments);
        });

        // Nothing else is reversed.
        Assert.All(
            typeof(Gpu).GetMethods().Where(m => m.Name != "Select" && m.GetCustomAttribute<MslIntrinsicAttribute>() is not null),
            m => Assert.False(m.GetCustomAttribute<MslIntrinsicAttribute>()!.ReverseArguments));
    }
}
