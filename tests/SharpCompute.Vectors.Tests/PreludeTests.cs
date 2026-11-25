using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpCompute.Vectors.Tests;

public class PreludeTests
{
    [Fact]
    public void HelpersAreInlineAndNamespaced()
    {
        // Every function the prelude defines is inline and named __ms_*.
        MatchCollection definitions = Regex.Matches(MslPrelude.Source, @"^(\w+) (\w+) (\w+)\(", RegexOptions.Multiline);
        Assert.NotEmpty(definitions);
        foreach (Match d in definitions)
        {
            Assert.Equal("inline", d.Groups[1].Value);
            Assert.StartsWith("__ms_", d.Groups[3].Value, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("__ms_radians", "0.017453292519943295")]
    [InlineData("__ms_degrees", "57.29577951308232")]
    public void PreludeAndCpuUseTheSameConstant(string helper, string literal)
    {
        foreach (string t in new[] { "float", "float2", "float3", "float4" })
        {
            Assert.Contains($"inline {t} {helper}({t} x) {{ return x * {literal}f; }}", MslPrelude.Source, StringComparison.Ordinal);
        }

        // For halves: one multiplication by the same constant rounded to half.
        foreach (string t in new[] { "half", "half2", "half3", "half4" })
        {
            Assert.Contains($"inline {t} {helper}({t} x) {{ return x * half({literal}f); }}", MslPrelude.Source, StringComparison.Ordinal);
        }

        float constant = float.Parse(literal, CultureInfo.InvariantCulture);
        Func<float, float> cpu = helper == "__ms_radians" ? Gpu.Radians : Gpu.Degrees;
        Assert.Equal(constant, cpu(1f));
        foreach (float x in new[] { 0f, -0f, 1f, -45f, 180f, 123.456f, 1e30f, float.NaN, float.PositiveInfinity })
        {
            FloatAssert.Same(x * constant, cpu(x));
        }

        Func<Half, Half> halfCpu = helper == "__ms_radians" ? Gpu.Radians : Gpu.Degrees;
        Assert.Equal((Half)constant, halfCpu((Half)1f));
        Assert.Equal((Half)(-45f) * (Half)constant, halfCpu((Half)(-45f)));
    }

    [Fact]
    public void RadiansAndDegrees()
    {
        FloatAssert.Near(MathF.PI, Gpu.Radians(180f));
        FloatAssert.Near(MathF.PI / 2, Gpu.Radians(90f));
        FloatAssert.Near(180f, Gpu.Degrees(MathF.PI));
        FloatAssert.Same(-0f, Gpu.Radians(-0f));
        FloatAssert.SameVector(new Float3(Gpu.Radians(30f), Gpu.Radians(60f), Gpu.Radians(-90f)), Gpu.Radians(new Float3(30, 60, -90)));
        FloatAssert.SameVector(new Float2(Gpu.Degrees(1f), Gpu.Degrees(-2f)), Gpu.Degrees(new Float2(1, -2)));
    }
}
