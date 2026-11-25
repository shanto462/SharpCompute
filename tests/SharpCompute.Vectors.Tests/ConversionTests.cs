using System;
using System.Linq;
using System.Reflection;

namespace SharpCompute.Vectors.Tests;

public class ConversionTests
{
    [Fact]
    public void FloatToIntTruncatesSaturatesAndMapsNaNToZero()
    {
        Assert.Equal(new Int4(1, -1, 0, 0), (Int4)new Float4(1.9f, -1.9f, float.NaN, -0f));
        Assert.Equal(new Int2(int.MaxValue, int.MinValue), (Int2)new Float2(3e10f, float.NegativeInfinity));
    }

    [Fact]
    public void FloatToUIntSaturatesNegativesToZero()
    {
        Assert.Equal(new UInt3(0, uint.MaxValue, 2), (UInt3)new Float3(-1f, 1e20f, 2.99f));
        Assert.Equal(new UInt2(0, 0), (UInt2)new Float2(float.NaN, float.NegativeInfinity));
    }

    [Fact]
    public void IntUIntConversionsReinterpretBits()
    {
        Assert.Equal(new UInt2(uint.MaxValue, 5), (UInt2)new Int2(-1, 5));
        Assert.Equal(new Int3(-1, int.MinValue, 7), (Int3)new UInt3(uint.MaxValue, 0x80000000, 7));
    }

    [Fact]
    public void IntegerToFloatRoundsToNearest()
    {
        FloatAssert.SameVector(new Float2(4294967296f, 0f), (Float2)new UInt2(uint.MaxValue, 0));
        FloatAssert.SameVector(new Float4(-1, 16777216f, int.MinValue, 3), (Float4)new Int4(-1, 16777217, int.MinValue, 3));
    }

    [Fact]
    public void ConversionsWorkInCheckedContext()
    {
        checked
        {
            Assert.Equal(new UInt2(uint.MaxValue, 0), (UInt2)new Int2(-1, 0));
            Assert.Equal(new Int2(int.MaxValue, 0), (Int2)new Float2(1e20f, 0));
        }
    }

    /// <summary>
    /// Exactly the explicit conversions from the other three element kinds of the same width;
    /// no implicit conversions and no equality operators (MSL <c>==</c> returns a bool vector, which
    /// <c>Gpu.Equal</c> and <c>Gpu.NotEqual</c> return instead).
    /// </summary>
    [Theory]
    [MemberData(nameof(VectorTypes.NumericData), MemberType = typeof(VectorTypes))]
    public void ConversionAndEqualityOperatorSurface(Type t)
    {
        MethodInfo[] ops = t.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.IsSpecialName).ToArray();
        Assert.DoesNotContain(ops, m => m.Name is "op_Implicit" or "op_Equality" or "op_Inequality");

        Type[] expectedSources = new[] { typeof(float), typeof(Half), typeof(int), typeof(uint) }
            .Where(e => e != VectorTypes.Element(t))
            .Select(e => VectorTypes.Vector(e, VectorTypes.Width(t)))
            .OrderBy(x => x.Name)
            .ToArray();
        Type[] actualSources = ops.Where(m => m.Name == "op_Explicit")
            .Select(m =>
            {
                Assert.Equal(t, m.ReturnType);
                return m.GetParameters().Single().ParameterType;
            })
            .OrderBy(x => x.Name)
            .ToArray();
        Assert.Equal(expectedSources, actualSources);
    }
}
