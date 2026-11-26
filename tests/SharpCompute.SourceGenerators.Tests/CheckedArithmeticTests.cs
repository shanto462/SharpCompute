using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// Checked arithmetic is SCG0052: in C# (and in <c>ForCpu</c>) it throws <c>OverflowException</c>,
/// and the GPU cannot throw. Only what a checked context changes is reported, wherever the context
/// comes from: a <c>checked(...)</c> expression, a <c>checked { }</c> block, or the project's
/// <c>CheckForOverflowUnderflow</c>. The first two are reported per operation; the project switch is
/// reported once per shader, on its declaration.
/// </summary>
public class CheckedArithmeticTests
{
    private const string Shader = "Tests.TestShader";

    private static string Source(string body) => $$"""
        using System;
        using SharpCompute;

        namespace Tests;

        [GeneratedComputeShaderDescriptor]
        public readonly partial struct TestShader(ReadWriteBuffer<int> ints, ReadWriteBuffer<long> longs, ReadWriteBuffer<float> floats, ReadWriteBuffer<byte> bytes) : IComputeShader
        {
            public void Execute()
            {
                int i = ThreadIds.X;
                int a = ints[0], b = ints[1];
                long l = longs[0];
                float f = floats[0];
                Half h = (Half)f;
                byte y = bytes[0];
                {{body}}
            }

            private static int Twice(int x) => checked(x * 2);
        }
        """;

    private static GeneratorRun Run(string body, bool checkOverflow = false)
    {
        CSharpCompilation compilation = GeneratorHarness.CreateCompilation(Source(body));

        if (checkOverflow)
        {
            compilation = compilation.WithOptions(compilation.Options.WithOverflowChecks(true));
        }

        GeneratorDriver driver = GeneratorHarness.CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return new GeneratorRun(compilation, (CSharpCompilation)output, driver);
    }

    [Theory]
    [InlineData("ints[2] = checked(a + b);", "a + b")]
    [InlineData("ints[2] = checked(a - b);", "a - b")]
    [InlineData("ints[2] = checked(a * b);", "a * b")]
    [InlineData("ints[2] = checked(-a);", "-a")]
    [InlineData("longs[1] = checked(l * l);", "l * l")]
    [InlineData("bytes[1] = checked((byte)(y + y));", "(byte)(y + y)")]
    [InlineData("ints[2] = checked((int)l);", "(int)l")]
    [InlineData("ints[2] = checked((int)f);", "(int)f")]
    [InlineData("bytes[1] = checked((byte)a);", "(byte)a")]
    [InlineData("ints[2] = checked((int)(uint)a);", "(int)(uint)a")]
    [InlineData("longs[1] = checked(y + y);", "y + y")]       // byte + byte is an int addition, checked like any other
    [InlineData("ints[2] = checked((int)h);", "(int)h")]
    [InlineData("checked { ints[2] = a + 1; }", "a + 1")]
    [InlineData("checked { int x = a; x += b; ints[2] = x; }", "x += b")]
    [InlineData("checked { int x = a; x *= b; ints[2] = x; }", "x *= b")]
    [InlineData("checked { int x = a; x++; ints[2] = x; }", "x++")]
    [InlineData("checked { int x = a; --x; ints[2] = x; }", "--x")]
    [InlineData("checked { for (int k = a; k < b; k++) { ints[k] = 0; } }", "k++")]
    [InlineData("checked { ints[0] += b; }", "ints[0] += b")]
    public void CheckedOperations_AreSCG0052(string body, string location)
    {
        GeneratorRun run = Run(body);
        Diagnostic diagnostic = run.AssertDiagnostic("SCG0052", location);

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains($"'{location}' is checked arithmetic", diagnostic.GetMessage());
        Assert.Contains("unchecked(", diagnostic.GetMessage());
        Assert.DoesNotContain("CheckForOverflowUnderflow", diagnostic.GetMessage());
        Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "SCG9999");
    }

    [Fact]
    public void CheckedArithmetic_InAHelper_IsReportedThere()
    {
        GeneratorRun run = Run("ints[2] = Twice(a);");

        run.AssertOnlyDiagnostic("SCG0052", "x * 2");
    }

    [Theory]
    [InlineData("ints[2] = unchecked(a + b);")]
    [InlineData("unchecked { int x = a; x *= b; x++; ints[2] = -x; }")]
    [InlineData("floats[1] = checked(f * f + 1f);")]          // floats are never checked
    [InlineData("longs[1] = checked((long)a);")]              // a widening conversion cannot overflow
    [InlineData("longs[1] = checked(l / 2 % 3);")]            // division is the same in both contexts
    [InlineData("floats[1] = checked((float)a);")]
    [InlineData("floats[1] = checked((float)h);")]
    [InlineData("ints[2] = checked(a << 3 | b >> 1 & ~a);")]  // shifts and bitwise operators are not checked
    [InlineData("ints[2] = checked(int.MaxValue - 1 + 1);")]  // a constant, folded by the C# compiler
    public void WhatACheckedContextDoesNotChange_IsAccepted(string body)
    {
        Run(body).Verify(Shader);
    }

    [Fact]
    public void TheProjectSwitch_IsReportedOnce_OnTheShader_WithTheCountAndTheFirstOperation()
    {
        GeneratorRun run = Run("int x = a + b; x++; ints[2] = x * 3; floats[1] = f * 2f; ints[3] = unchecked(a * b);", checkOverflow: true);
        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0052", "TestShader");
        string message = diagnostic.GetMessage();

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Shader 'TestShader' has 3 checked operations, the first 'a + b' at line 17,", message);
        Assert.Contains("CheckForOverflowUnderflow", message);
        Assert.Contains("'unchecked { }'", message);
        Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "SCG9999");
    }

    [Fact]
    public void TheProjectSwitch_CountsTheHelpers_ButEachOperationOnce()
    {
        // The subtraction's operand is translated too, so the helper's addition is counted; the helper
        // is called twice, and its addition counts once.
        const string source = """
            using SharpCompute;

            namespace Tests;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct TestShader(ReadWriteBuffer<int> ints) : IComputeShader
            {
                public void Execute()
                {
                    ints[0] = Next(ints[1]) - 1;
                    ints[1] = Next(ints[2]);
                }

                private static int Next(int x) => x + 1;
            }
            """;
        CSharpCompilation compilation = GeneratorHarness.CreateCompilation(source);
        compilation = compilation.WithOptions(compilation.Options.WithOverflowChecks(true));
        GeneratorDriver driver = GeneratorHarness.CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        var run = new GeneratorRun(compilation, (CSharpCompilation)output, driver);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0052", "TestShader");
        Assert.Contains("has 2 checked operations, the first 'Next(ints[1]) - 1' at line 10,", diagnostic.GetMessage());
    }

    [Fact]
    public void TheProjectSwitch_WithOneOperation_SaysOne()
    {
        GeneratorRun run = Run("ints[2] = a - b;", checkOverflow: true);

        Assert.Contains("has 1 checked operation, the first 'a - b'", run.AssertOnlyDiagnostic("SCG0052", "TestShader").GetMessage());
    }

    [Theory]
    [InlineData("ints[2] = checked(a + b);", "a + b")]
    [InlineData("checked { int x = a; x++; ints[2] = x; }", "x++")]
    public void TheProjectSwitch_KeepsExplicitCheckedCode_WhereItIsWritten(string body, string location)
    {
        // Explicit checked code is deliberate: it is reported where it is written, as without the
        // switch, and it is not counted on the shader. The shader's other checked arithmetic is.
        GeneratorRun run = Run(body + " ints[3] = a * b;", checkOverflow: true);
        Diagnostic[] reported = run.Diagnostics.Where(static d => d.Id == "SCG0052").ToArray();

        Assert.Equal(2, reported.Length);
        Diagnostic site = Assert.Single(reported, d => run.TextAt(d) == location);
        Assert.Contains($"'{location}' is checked arithmetic", site.GetMessage());
        Assert.DoesNotContain("CheckForOverflowUnderflow", site.GetMessage());
        Diagnostic shader = Assert.Single(reported, static d => d.GetMessage().Contains("CheckForOverflowUnderflow"));
        Assert.Contains("has 1 checked operation, the first 'a * b'", shader.GetMessage());
    }

    [Fact]
    public void TheProjectSwitch_WithUncheckedCode_IsAccepted()
    {
        GeneratorRun run = Run("unchecked { int x = a + b; x++; ints[2] = x * 3; }", checkOverflow: true);

        // Twice, which has a checked(...) of its own, is not called, so it is not translated.
        run.Verify(Shader);
    }
}
