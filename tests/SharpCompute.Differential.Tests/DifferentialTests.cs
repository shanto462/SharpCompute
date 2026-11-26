using System.Collections.Generic;
using System.Linq;
using SharpCompute.Tests;
using SharpCompute.Tests.Conformance;
using Xunit.Abstractions;

namespace SharpCompute.Differential.Tests;

/// <summary>
/// Random, well-typed expressions from fixed seeds (<c>tools/gen_differential.py</c>), compiled by
/// the source generator and run on the GPU and with <c>ForCpu</c> over the same inputs. Where the
/// conformance suite checks each operation alone over edge values, these combine them, so they find
/// what only shows in context: a promotion lost between two operators, a conversion folded away, a
/// helper the generator emits wrongly when its operand is itself an expression.
/// <c>scripts/fuzz-differential.sh</c> runs the same over other seeds and shrinks what it finds.
/// </summary>
[Trait("Category", "Differential")]
public class DifferentialTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Shaders() => DifferentialShaders.All.Select(static s => new object[] { s.Name });

    [MetalTheory]
    [MemberData(nameof(Shaders))]
    public void RandomExpressions_GiveTheSameResultsOnTheGpuAsWithForCpu(string name)
    {
        DifferentialShader shader = DifferentialShaders.All.Single(s => s.Name == name);
        DifferentialResult result;

        try
        {
            result = DifferentialHarness.Run(GraphicsDevice.GetDefault(), shader);
        }
        catch (ShaderCompilationException e) when (DifferentialHarness.ReportCompilerCrash(shader, e))
        {
            // The long mode records a shader that crashes Metal's compiler and goes on (see
            // ReportCompilerCrash); the default mode fails on it.
            output.WriteLine($"{shader.Name}: the Metal compiler crashed building its pipeline");
            return;
        }

        output.WriteLine($"{result.Name}: {result.Cases} cases, {result.Comparisons} comparisons");
        Assert.True(result.Passed, "\n" + DifferentialHarness.Report(result));
    }

    /// <summary>The comparisons are not vacuous: a changed GPU value is caught in every case.</summary>
    [MetalTheory]
    [MemberData(nameof(Shaders))]
    public void RandomExpressions_ReportEveryCase_WhenTheGpuResultsAreMutated(string name)
    {
        DifferentialShader shader = DifferentialShaders.All.Single(s => s.Name == name);
        DifferentialResult result = DifferentialHarness.Run(GraphicsDevice.GetDefault(), shader, mutate: true);

        Assert.Equal(shader.Cases.Select(static c => c.Id), result.Mismatches.Select(static m => m.Case.Id));
    }

    [Fact]
    public void Cases_FillTheirShadersOutputs_AndHaveAPolicyThatFitsTheirType()
    {
        Assert.NotEmpty(DifferentialShaders.All);
        Assert.Equal(DifferentialShaders.InputSlots.Length, DifferentialShaders.InputNames.Length);

        foreach (DifferentialShader shader in DifferentialShaders.All)
        {
            int offset = 0, modelOffset = 0;

            foreach (DifferentialCase c in shader.Cases)
            {
                Assert.Equal(offset, c.Offset);
                Assert.Equal(modelOffset, c.ModelOffset);
                Assert.InRange(c.Lanes, 1, 4);
                Assert.InRange(c.Ulps, 0, 4);
                Assert.False(c.Check == DifferentialCheck.Exact && c.Variants > 0, c.Id);
                Assert.False(c.Check == DifferentialCheck.Exact && c.Kind != ResultKind.Integer, c.Id);
                offset += c.Lanes;
                modelOffset += c.Variants * c.Lanes;
            }

            Assert.Equal(shader.Slots, offset);
            Assert.Equal(shader.ModelSlots, modelOffset);
            Assert.Equal(shader.ModelSlots > 0, shader.Model is not null);
        }
    }

    /// <summary>The policy itself: a model variant is accepted, anything else is not.</summary>
    [Fact]
    public void Matches_AcceptsTheCaseOrAModel_UpToFlushingAndNaNSign()
    {
        DifferentialCase model = new("t", "", ResultKind.Float, 1, DifferentialCheck.Model, 0, 0, 0, 1);
        long one = 0x3F800000, two = 0x40000000, subnormal = 0x00000001, nan = 0x7FC00000, negativeNaN = 0xFFC00000;

        Assert.True(DifferentialHarness.Matches(model, one, one, []));
        Assert.True(DifferentialHarness.Matches(model, two, one, [two]));
        Assert.True(DifferentialHarness.Matches(model, 0, subnormal, []));
        Assert.True(DifferentialHarness.Matches(model, nan, negativeNaN, []));
        Assert.False(DifferentialHarness.Matches(model, two, one, [one]));
        Assert.False(DifferentialHarness.Matches(model, 0x80000000, one, []));

        DifferentialCase value = model with { Check = DifferentialCheck.Value };
        Assert.True(DifferentialHarness.Matches(value, 0x80000000, 0, []));
        Assert.False(DifferentialHarness.Matches(model, 0x80000000, 0, []));

        DifferentialCase exact = new("t", "", ResultKind.Integer, 1, DifferentialCheck.Exact, 0, 0, 0, 0);
        Assert.True(DifferentialHarness.Matches(exact, 5, 5, []));
        Assert.False(DifferentialHarness.Matches(exact, 5, 6, [5]));

        DifferentialCase tolerance = new("t", "", ResultKind.Float, 1, DifferentialCheck.Tolerance, 0, 0, 0, 0) { Ulps = 4 };
        Assert.True(DifferentialHarness.Matches(tolerance, one + 4, one, []));
        Assert.False(DifferentialHarness.Matches(tolerance, one + 5, one, []));
    }
}
