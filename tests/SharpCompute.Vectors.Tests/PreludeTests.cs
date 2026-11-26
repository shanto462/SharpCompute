using System;
using System.Globalization;
using System.Linq;
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

    [Fact]
    public void VectorArithmetic_IsInlineAndNamespaced_AndDefinesEachHelperOnce()
    {
        // The block a shader that adds or subtracts integer vectors gets after the prelude: its
        // functions are inline and named __ms_*, like the prelude's, and none repeats one of them.
        string[] lines = MslPrelude.VectorArithmetic.Split('\n');
        string[] functions = lines.Where(static line => line.StartsWith("inline ", StringComparison.Ordinal)).ToArray();
        string[] templates = lines.Where(static line => line.StartsWith("template ", StringComparison.Ordinal)).ToArray();

        Assert.StartsWith("// ", lines[0], StringComparison.Ordinal);
        Assert.Equal(lines.Length - 1, functions.Length + templates.Length);

        // Sum and difference, for the vectors of 2 to 4 lanes of the 8 integer types.
        Assert.Equal(2 * 8 * 3, functions.Length);
        Assert.All(functions, static line => Assert.Matches(@"^inline (\w+) __ms_(add|sub)\(\1 a, \1 b\) \{ return \1\(", line));
        Assert.Equal(functions.Length, functions.Select(static line => line[..line.IndexOf(')')]).Distinct().Count());

        // The by-reference forms, for a target in each address space a shader can write.
        Assert.Equal(2 * 3, templates.Length);
        Assert.All(templates, static line => Assert.Matches(@"^template <typename T, typename V> inline V __ms_(add_to|sub_from)\((thread|device|threadgroup) T& target, V value\) ", line));
        Assert.Equal(templates.Length, templates.Select(static line => line[..line.IndexOf(')')]).Distinct().Count());

        foreach (string name in new[] { "__ms_add", "__ms_sub", "__ms_add_to", "__ms_sub_from" })
        {
            Assert.DoesNotContain(name + "(", MslPrelude.Source, StringComparison.Ordinal);
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
