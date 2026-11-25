using System.Linq;
using System.Text.RegularExpressions;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// Which captured read-write buffers and textures a shader is found never to store to. Those are
/// bound with <c>written: false</c>, which lets the runtime accept a dispatch that binds one of them
/// twice (or overlapping another binding); everything else keeps the two-argument <c>SetBuffer</c> and
/// <c>SetTexture</c>, which take a read-write resource as written. The answer has to err towards
/// "written", so each way of storing through a buffer element is a case here.
/// </summary>
public partial class ResourceAccessTests
{
    private const string Shader = "Tests.TestShader";

    private const string Captures = """
            public readonly ReadWriteBuffer<float> a;
            public readonly ReadWriteBuffer<float> b;
            public readonly ReadWriteBuffer<Cell> cells;
            public readonly ReadWriteBuffer<int> counts;
            public readonly ReadOnlyBuffer<float> frozen;
            public readonly ReadWriteTexture2D<float> canvas;
            public readonly ReadWriteTexture2D<float> paper;
            public readonly ReadOnlyTexture2D<float> picture;
        """;

    private const string Cell = """

        public struct Cell
        {
            public int Count;
            public float Weight;
            public Inner Nested;

            public void Bump() { Count++; }

            public int Twice() => Count * 2;

            public int Next => Count + 1;
        }

        public struct Inner
        {
            public int Value;

            public void Reset() { Value = 0; }
        }
        """;

    /// <summary>Every read-write capture of the test shader, in the order <see cref="NeverWritten"/> lists them.</summary>
    private const string AllReadWrite = "a,b,canvas,cells,counts,paper";

    private static GeneratorRun Run(string body, string extraMembers = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader($$"""
            {{Captures}}
            {{extraMembers}}

                public void Execute()
                {
                    {{body}}
                }
            """) + Cell);

    [GeneratedRegex(@"writer\.Set(?:Buffer|Texture)\(\d+, shader\.(\w+) \?\? throw [^\n]*?(, written: false)?\);")]
    private static partial Regex Binding();

    /// <summary>The captures bound with <c>written: false</c>, sorted and comma-separated; every shader here is also compiled by Metal.</summary>
    private static string NeverWritten(string body, string extraMembers = "")
    {
        GeneratorRun run = Run(body, extraMembers);
        run.Verify(Shader);

        MatchCollection bindings = Binding().Matches(run.Source("Tests.TestShader.SharpCompute.g.cs"));

        // All eight captures are bound, written or not.
        Assert.Equal(8, bindings.Count);

        return string.Join(",", bindings.Where(static m => m.Groups[2].Success).Select(static m => m.Groups[1].Value).Order(System.StringComparer.Ordinal));
    }

    [Theory]

    // Nothing but reads: element values, fields of an element, Length, pixels, sizes, samples.
    [InlineData("float x = a[0] + b[1] + frozen[2];")]
    [InlineData("float w = cells[0].Weight + cells[1].Nested.Value; int n = counts[0];")]
    [InlineData("int n = a.Length + cells.Length + canvas.Width + paper.Height;")]
    [InlineData("Float4 c = canvas[0, 0] + paper[1, 1] + picture[0, 0];")]
    [InlineData("Float4 c = picture.Sample(Sampler.LinearClamp, new Float2(0.5f, 0.5f));")]
    [InlineData("Cell copy = cells[0]; copy.Count = 5; copy.Bump();")]
    [InlineData("int x = cells[0].Twice() + cells[1].Next;")]
    [InlineData("float x = a[counts[0]];")]
    [InlineData("")]
    public void ACaptureTheShaderOnlyReads_IsBoundAsNotWritten(string body)
    {
        Assert.Equal(AllReadWrite, NeverWritten(body));
    }

    [Theory]

    // Each way C# stores through a buffer element marks its buffer, and only that one.
    [InlineData("a[0] = b[0];", "b,canvas,cells,counts,paper")]
    [InlineData("a[0] += 1f;", "b,canvas,cells,counts,paper")]
    [InlineData("b[0] *= a[0];", "a,canvas,cells,counts,paper")]
    [InlineData("counts[0]++;", "a,b,canvas,cells,paper")]
    [InlineData("--counts[0];", "a,b,canvas,cells,paper")]
    [InlineData("int x = counts[0]++ + 1;", "a,b,canvas,cells,paper")]
    [InlineData("cells[0].Weight = a[0];", "a,b,canvas,counts,paper")]
    [InlineData("cells[0].Nested.Value += 2;", "a,b,canvas,counts,paper")]
    [InlineData("cells[0] = cells[1];", "a,b,canvas,counts,paper")]
    [InlineData("Gpu.AtomicAdd(ref counts[0], 1);", "a,b,canvas,cells,paper")]
    [InlineData("int old = Gpu.AtomicExchange(ref counts[0], 5);", "a,b,canvas,cells,paper")]

    // A method that modifies its struct, called on the element in place (also on a field of it).
    [InlineData("cells[0].Bump();", "a,b,canvas,counts,paper")]
    [InlineData("cells[0].Nested.Reset();", "a,b,canvas,counts,paper")]

    // A store anywhere counts: in a branch, a loop, the header of a for, a nested expression.
    [InlineData("if (counts[0] > 0) { a[0] = 1f; }", "b,canvas,cells,counts,paper")]
    [InlineData("for (int i = 0; i < 2; a[i] = 0f, i++) { }", "b,canvas,cells,counts,paper")]
    [InlineData("float x = (a[0] = 2f) + b[0];", "b,canvas,cells,counts,paper")]
    [InlineData("a[0] = 1f; b[0] = 2f; counts[0] = 3; cells[0].Count = 4;", "canvas,paper")]

    // Pixels: only an assignment writes one.
    [InlineData("canvas[0, 0] = paper[0, 0];", "a,b,cells,counts,paper")]
    [InlineData("canvas[0, 0] = picture[0, 0]; paper[1, 1] = canvas[0, 0];", "a,b,cells,counts")]
    public void AStoreThroughACapture_MarksThatCaptureAsWritten(string body, string expected)
    {
        Assert.Equal(expected, NeverWritten(body));
    }

    [Fact]
    public void StoresAndReadsInHelpers_CountForTheCaptureTheyUse()
    {
        // A helper that reads a capture leaves it unwritten; one that stores to a capture marks it.
        Assert.Equal(
            "a,canvas,cells,counts,paper",
            NeverWritten("b[0] = Get(1);", "float Get(int i) => a[i];"));

        Assert.Equal(
            "b,canvas,cells,counts,paper",
            NeverWritten("Put(0);", "void Put(int i) { a[i] = b[i]; }"));

        // Through a helper two calls deep, and from a helper nothing else names the capture in.
        Assert.Equal(
            "a,b,canvas,cells,paper",
            NeverWritten("Outer();", "void Outer() { Inner(3); }\nvoid Inner(int i) { counts[i] += 1; }"));

        Assert.Equal(
            "a,b,cells,counts,paper",
            NeverWritten("Paint();", "void Paint() { canvas[0, 0] = picture[0, 0]; }"));
    }

    [Fact]
    public void AStoreThroughAParameter_CouldBeToAnyCaptureOfItsKind()
    {
        // Which buffer `target` is depends on the call, so every read-write buffer counts as written;
        // textures are unaffected.
        Assert.Equal(
            "canvas,paper",
            NeverWritten("Store(a, 0);", "static void Store(ReadWriteBuffer<float> target, int i) { target[i] = 1f; }"));

        // The same for a texture parameter, which leaves the buffers alone.
        Assert.Equal(
            "a,b,cells,counts",
            NeverWritten("Paint(canvas);", "static void Paint(ReadWriteTexture2D<float> target) { target[0, 0] = default; }"));

        // A parameter that is only read marks nothing.
        Assert.Equal(
            AllReadWrite,
            NeverWritten(
                "float x = Load(a, 0) + Peek(canvas);",
                "static float Load(ReadWriteBuffer<float> source, int i) => source[i];\nstatic float Peek(ReadWriteTexture2D<float> source) => source[0, 0].X;"));
    }

    [Fact]
    public void AHelperNothingCalls_MarksNothing()
    {
        // Only code the kernel reaches is translated, and only that can store.
        Assert.Equal(AllReadWrite, NeverWritten("float x = a[0];", "void Unused() { a[0] = 1f; canvas[0, 0] = default; }"));
    }

    [Fact]
    public void ReadOnlyResources_AndWrittenOnes_KeepTheTwoArgumentBinding()
    {
        GeneratorRun run = Run("a[0] = b[0] + frozen[0]; canvas[0, 0] = picture[0, 0] + paper[0, 0];");
        run.Verify(Shader);
        string code = run.Source("Tests.TestShader.SharpCompute.g.cs");

        // Written: bound as before, and so taken as written by the runtime.
        Assert.Contains("writer.SetBuffer(0, shader.a ?? throw new global::System.ArgumentNullException(\"TestShader.a\", \"The buffer 'TestShader.a' is null. Create the shader with its constructor (not 'default') and pass a buffer.\"));", code);
        Assert.Contains("writer.SetTexture(0, shader.canvas ?? throw new global::System.ArgumentNullException(\"TestShader.canvas\", \"The texture 'TestShader.canvas' is null. Create the shader with its constructor (not 'default') and pass a texture.\"));", code);

        // Read-write but only read: the extra argument.
        Assert.Contains("writer.SetBuffer(1, shader.b ?? throw new global::System.ArgumentNullException(\"TestShader.b\", \"The buffer 'TestShader.b' is null. Create the shader with its constructor (not 'default') and pass a buffer.\"), written: false);", code);
        Assert.Contains("writer.SetTexture(1, shader.paper ?? throw new global::System.ArgumentNullException(\"TestShader.paper\", \"The texture 'TestShader.paper' is null. Create the shader with its constructor (not 'default') and pass a texture.\"), written: false);", code);

        // Read-only types never need it: the runtime knows a shader cannot write them.
        Assert.Contains("pass a buffer.\"));\n", code[code.IndexOf("shader.frozen", System.StringComparison.Ordinal)..]);
        Assert.DoesNotContain("written: false", code[code.IndexOf("shader.frozen", System.StringComparison.Ordinal)..code.IndexOf("shader.canvas", System.StringComparison.Ordinal)]);
        Assert.DoesNotContain("written: false", code[code.IndexOf("shader.picture", System.StringComparison.Ordinal)..]);

        // The MSL does not change with it: a read-write buffer stays a `device T*`.
        Assert.Contains("device float* b [[buffer(1)]]", run.Verify(Shader).MslSource);
    }

    [Fact]
    public void PrimaryConstructorCaptures_AndPixelShaders_AreMarkedTheSameWay()
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, """
            using SharpCompute;
            using TestVectors;

            namespace Tests;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Blend(ReadWriteBuffer<float> weights, ReadWriteBuffer<float> log, ReadWriteTexture2D<float> layer) : IComputeShader<Float4>
            {
                public Float4 Execute()
                {
                    log[0] = weights[0];
                    return layer[ThreadIds.XY] * weights[1];
                }
            }
            """);

        run.Verify("Tests.Blend");
        string code = run.Source("Tests.Blend.SharpCompute.g.cs");

        Assert.Contains("__writer.SetBuffer(0, weights ?? throw", code);
        Assert.Contains("pass a buffer.\"), written: false);", code);
        Assert.Contains("__writer.SetBuffer(1, log ?? throw new global::System.ArgumentNullException(\"Blend.log\", \"The buffer 'Blend.log' is null. Create the shader with its constructor (not 'default') and pass a buffer.\"));", code);

        // A captured texture of a pixel shader binds from index 1; the output at 0 is the runtime's.
        Assert.Contains("__writer.SetTexture(1, layer ?? throw", code);
        Assert.Contains("pass a texture.\"), written: false);", code);
    }
}
