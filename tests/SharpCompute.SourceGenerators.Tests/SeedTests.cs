using System.Linq;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// Regression seeds from external review, kept verbatim so that line numbers in the assertions match
/// the original files.
/// </summary>
public class SeedTests
{
    /// <summary>The review seed <c>seeds/pos2/Shaders.cs</c>, verbatim.</summary>
    private const string Pos2 = """
        using System;
        using SharpCompute;
        namespace P;

        public static class Consts { public const float Scale = 2.5f; public const int Count = 7; }

        // S5: evaluation order with side effects
        [GeneratedComputeShaderDescriptor] public readonly partial struct EvalOrder(ReadWriteBuffer<int> o) : IComputeShader
        {
            public void Execute()
            {
                int b = ThreadIds.X * 8;
                int j = 0;
                o[b + j++] = j;                 // C#: index 0, value 1
                int a = j++ + j++ * 10;         // C#: 1 + 2*10 = 21, j = 3
                o[b + 1] = a;
                o[b + 2] = j;
                int k = 5;
                k += k++;                       // C#: 5 + 5 = 10
                o[b + 3] = k;
                int m = 2;
                o[b + 4] = m = m + 1;           // 3
                o[b + 5] = (m = 7) + m;         // 14
                int n = 1;
                n = n++ + ++n;                  // C#: 1 + 3 = 4
                o[b + 6] = n;
                o[b + 7] = j-- - --j;           // C#: 3 - 1 = 2
            }
        }

        // S10/S19/S26/S27: ref locals, ++ on elements and swizzles, target-typed new, vector ternary
        [GeneratedComputeShaderDescriptor] public readonly partial struct Refs(ReadWriteBuffer<float> o, ReadWriteBuffer<Float3> v) : IComputeShader
        {
            public void Execute()
            {
                int i = ThreadIds.X;
                o[i]++;
                ++o[i];
                o[i] -= 0.5f;
                Float3 t = new Float3(1f, 2f, 3f);
                t.X++;
                t.Y--;
                Float3 pick = i % 2 == 0 ? t : -t;
                v[i] = pick;
                v[i].Z++;
            }
        }

        // S12: helpers named like metal:: functions, locals named like them too
        [GeneratedComputeShaderDescriptor] public readonly partial struct Shadowing(ReadWriteBuffer<float> o) : IComputeShader
        {
            private static float min(float a, float b) => a < b ? a + 100f : b + 100f;
            private static float sqrt(float a) => a * 1000f;
            private static float length(Float2 a) => a.X - a.Y;
            private static float Clamp(float a) => Gpu.Clamp(a, 0f, 1f) + min(a, a);

            public void Execute()
            {
                int i = ThreadIds.X;
                float max = Gpu.Max(i, 2f), dot = Gpu.Dot(new Float2(i, 1f), new Float2(2f, 3f)), abs = MathF.Abs(-i), step = 0.5f, sign = -1f, clamp = Clamp(i), float3 = 9f;
                o[i] = min(1f, 2f) + sqrt(2f) + length(new Float2(5f, 3f)) + MathF.Sqrt(4f) + Gpu.Min(1f, 2f) + Gpu.Length(new Float2(3f, 4f)) + max + dot + abs + step + sign + clamp + float3;
            }
        }

        // S17/S21/S22: external consts, folded infinities, hash arithmetic
        [GeneratedComputeShaderDescriptor] public readonly partial struct Folding(ReadWriteBuffer<float> o, ReadWriteBuffer<uint> h) : IComputeShader
        {
            private const float Big = float.MaxValue * 2f;
            private const float NotANumber = 0f / 0f;
            public void Execute()
            {
                int i = ThreadIds.X;
                uint x = (uint)i * 2654435761u;
                x ^= x >> 16; x *= 0x85EBCA6Bu; x ^= x >> 13; x *= 0xC2B2AE35u; x ^= x >> 16;
                h[i] = x + 0xFFFFFFFFu + uint.MaxValue;
                float v = Consts.Scale * Consts.Count;
                if (i == 0) v = Big;
                if (i == 1) v = -Big;
                if (i == 2) v = NotANumber;
                if (i == 3) v = 1f / Consts.Count;
                o[i] = v;
            }
        }
        """;

    [Fact]
    public void Pos2_EvalOrder_ReportsExactlyTheSixOrderDependentStatements()
    {
        GeneratorRun run = GeneratorHarness.Run(Pos2);

        Diagnostic[] unsequenced = run.Diagnostics.Where(static d => d.Id == "SCG0021").ToArray();

        // 1-based lines: `o[b + j++] = j;`, `j++ + j++ * 10`, `k += k++;`, `(m = 7) + m`, `n++ + ++n`, `j-- - --j`.
        // Line 22 (`o[b + 4] = m = m + 1;`) is well defined in MSL too and must not be reported.
        Assert.Equal([14, 15, 19, 23, 25, 27], unsequenced.Select(static d => d.Location.GetLineSpan().StartLinePosition.Line + 1).ToArray());
        Assert.All(unsequenced, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
    }

    [Fact]
    public void Pos2_TheOtherShaders_AreValid()
    {
        GeneratorRun run = GeneratorHarness.Run(Pos2);

        Assert.All(run.Diagnostics, static d => Assert.Equal("SCG0021", d.Id));

        foreach (string shader in new[] { "P.Refs", "P.Shadowing", "P.Folding" })
        {
            run.Descriptor(shader).AssertMslCompiles();
        }
    }
}
