using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// The same input must give the same output, whatever else is being compiled at the same time and
/// whatever the culture of the thread: the generator keeps no state between runs, emits textures in
/// declaration order and samplers in the order of <c>Sampler</c>'s properties, and formats nothing
/// with the current culture.
/// </summary>
/// <remarks>
/// The first three inputs are those of the texture tests that once failed together in one full,
/// parallel run of the suite and never again. Snapshot tests fail that way when emission order comes
/// from a hash table, so this runs them (and one that would expose such an order) many times at once.
/// </remarks>
public class DeterminismTests
{
    private const string Shader = "TestShader.SharpCompute.g.cs";

    private static string Body(string body, string extraMembers = "") => TestSources.Shader($$"""
            public readonly ReadOnlyTexture2D<Rgba32> source;
            public readonly ReadWriteTexture2D<Float4> target;
            public readonly ReadWriteBuffer<Float4> output;
            {{extraMembers}}

            public void Execute()
            {
                {{body}}
            }
        """);

    private static string ManyTextures()
    {
        var builder = new StringBuilder();
        builder.AppendLine("using SharpCompute;");
        builder.AppendLine("namespace Tests;");
        builder.AppendLine("[GeneratedComputeShaderDescriptor]");
        builder.AppendLine("public readonly partial struct TestShader : IComputeShader");
        builder.AppendLine("{");

        for (int i = 0; i < 31; i++)
        {
            builder.AppendLine($"    public readonly ReadOnlyTexture2D<float> t{i};");
        }

        builder.AppendLine("    public readonly ReadWriteBuffer<Float4> output;");
        builder.AppendLine("    public void Execute() => output[0] = t0[0, 0] + t30[0, 0];");
        builder.AppendLine("}");
        return builder.ToString();
    }

    /// <summary>Textures declared out of alphabetical order, and every sampler, used in reverse order and from helpers.</summary>
    private static string EveryOrder() => TestSources.Shader("""
            public readonly ReadOnlyTexture2D<Rgba32> zeta;
            public readonly ReadWriteBuffer<Float4> output;
            public readonly ReadOnlyTexture2D<R8> alpha;
            public readonly float scale;
            public readonly ReadWriteTexture2D<Float4> middle;
            public readonly ReadOnlyTexture2D<Rgba16Float> beta;

            public void Execute()
            {
                Float2 uv = new Float2(0.25f, 0.75f) * scale;
                Float4 color = beta.Sample(Sampler.LinearMirror, uv) + alpha.Sample(Sampler.LinearRepeat, uv) + Third(uv);
                middle[ThreadIds.XY] = color + zeta.Sample(Sampler.PointMirror, uv);
                output[ThreadIds.X] = Rest(uv) * 1.5f;
            }

            private Float4 Third(Float2 uv) => zeta.Sample(Sampler.LinearClamp, uv);

            private Float4 Rest(Float2 uv) => alpha.Sample(Sampler.PointRepeat, uv) + beta.Sample(Sampler.PointClamp, uv) + middle[1, 2];
        """);

    private static string[][] Inputs() =>
    [
        [
            TestSources.Vectors,
            Body("""
                float level = 0.25f;
                float access = 2;
                float texture2d = 3;
                ReadOnlyTexture2D<Rgba32> sampler = source;
                output[0] = sampler.Sample(Sampler.LinearMirror, new Float2(level)) * access * texture2d;
                """),
        ],
        [
            TestSources.Vectors,
            Body("""
                ReadOnlyTexture2D<Rgba32> image = source;
                ReadWriteTexture2D<Float4> canvas = target;
                canvas[0, 0] = image[0, 0] + image.Sample(Sampler.PointClamp, new Float2(0.5f));
                """),
        ],
        [ManyTextures()],
        [TestSources.Vectors, EveryOrder()],
    ];

    private static string Generate(string[] input)
    {
        GeneratorRun run = GeneratorHarness.Run(input);

        Assert.True(run.Diagnostics.IsEmpty, "Unexpected generator diagnostics:\n" + string.Join("\n", run.Diagnostics));
        return run.Source(Shader);
    }

    [Fact]
    public void TexturesAndSamplers_AreEmittedInDeclarationAndPropertyOrder()
    {
        string[] lines = GeneratorHarness.Run(Inputs()[3]).Verify("Tests.TestShader").MslSource.Split('\n');

        // Declaration order, not name order and not the order of use.
        Assert.Equal(
            ["zeta [[texture(0)]],", "alpha [[texture(1)]],", "middle [[texture(2)]],", "beta [[texture(3)]],"],
            lines.Where(static line => line.Contains("[[texture(", StringComparison.Ordinal)).Select(static line => line.Trim().Split("> ")[1]));

        // The order of Sampler's properties, whatever order (and function) they are used in.
        Assert.Equal(
            ["point_clamp", "point_repeat", "point_mirror", "linear_clamp", "linear_repeat", "linear_mirror"],
            lines.Where(static line => line.StartsWith("constexpr sampler", StringComparison.Ordinal)).Select(static line => line.Split("__ms_sampler_")[1].Split('(')[0]));
    }

    [Fact]
    public void LoadedDescriptor_ReportsTheMslOfItsOwnRun()
    {
        // Descriptor() checks this for every shader it loads; here the check itself is checked: the
        // MSL read from the generated syntax is non-trivial, differs per input, and is what the loaded type returns.
        string[][] inputs = Inputs();
        GeneratorRun[] runs = inputs.Select(static input => GeneratorHarness.Run(input)).ToArray();
        string?[] generated = runs.Select(static run => run.GeneratedMsl("Tests.TestShader")).ToArray();

        Assert.All(generated, static msl => Assert.StartsWith("#include <metal_stdlib>", msl));
        Assert.Equal(inputs.Length, generated.Distinct().Count());

        // Loaded in the reverse order of generation, so a mix-up between runs would show.
        for (int i = runs.Length - 1; i >= 0; i--)
        {
            Assert.Equal(generated[i], runs[i].Descriptor("Tests.TestShader").MslSource);
        }

        Assert.Null(runs[0].GeneratedMsl("Tests.NoSuchShader"));
    }

    [Fact]
    public void SameInput_GivesTheSameOutput_WhenManyCompilationsRunInParallel()
    {
        const int Repetitions = 40;
        string[][] inputs = Inputs();
        string[] expected = inputs.Select(Generate).ToArray();
        var differences = new ConcurrentBag<string>();

        // Every input many times over, interleaved, on every core at once.
        Parallel.For(0, inputs.Length * Repetitions, n =>
        {
            int input = n % inputs.Length;

            try
            {
                string actual = Generate(inputs[input]);

                if (actual != expected[input])
                {
                    differences.Add($"Input {input}, run {n}: the generated source differs.\n--- expected\n{expected[input]}\n--- actual\n{actual}");
                }
            }
            catch (Exception e)
            {
                differences.Add($"Input {input}, run {n}: {e}");
            }
        });

        Assert.True(differences.IsEmpty, $"{differences.Count} of {inputs.Length * Repetitions} runs differ. The first:\n{differences.FirstOrDefault()}");
    }

    [Theory]
    [InlineData("tr-TR")]   // 'I'.ToLower() is a dotless ı
    [InlineData("de-DE")]   // decimal comma
    [InlineData("ar-SA")]   // other digits and calendar
    public void SameInput_GivesTheSameOutput_UnderAnotherCulture(string culture)
    {
        string[][] inputs = Inputs();
        string[] expected = inputs.Select(Generate).ToArray();
        string[]? actual = null;
        Exception? failure = null;

        // On a thread of its own, so the culture cannot leak into other tests.
        var thread = new Thread(() =>
        {
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
                actual = inputs.Select(Generate).ToArray();
            }
            catch (Exception e)
            {
                failure = e;
            }
        });

        thread.Start();
        thread.Join();

        Assert.True(failure is null, failure?.ToString());
        Assert.Equal(expected, actual);
    }
}
