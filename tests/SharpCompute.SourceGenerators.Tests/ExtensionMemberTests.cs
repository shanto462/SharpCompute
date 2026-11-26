using System.Linq;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// C# 14 extension members. An instance extension property or indexer is declared in the
/// <c>extension(...)</c> block, not on its receiver's type, so it is rejected (SCG0017) instead of
/// being taken for the built-in member it looks like: <c>v.R</c> is not the component <c>v.r</c>,
/// and <c>buffer[i]</c> through an extension indexer is not the buffer's element. Static extension
/// members and extension operators are static methods, and are translated.
/// </summary>
public class ExtensionMemberTests
{
    private const string Reason = "instance extension properties and indexers (declared in an 'extension(...)' block) are not supported yet";

    private const string Extensions = """

        public struct Pair { public float A; public float B; }

        public static class Extensions
        {
            extension(Float4 v)
            {
                public float R => v.X * 2f;
                public float Zyx => v.X + 100f;
            }

            extension(Float3 v)
            {
                public float LengthSquared => Gpu.Dot(v, v);
                public float this[uint i] => i == 0u ? v.X + 1000f : v.Y;
            }

            extension(Float4x4 m)
            {
                public float Trace => 7f;
            }

            extension(float f)
            {
                public float Squared => f * f;
            }

            extension(Pair p)
            {
                public float Total => p.A + p.B;
            }

            extension(ReadOnlyBuffer<float> b)
            {
                public float this[uint i] => b[(int)i] + 1000f;
                public float this[Int2 p] => b[p.X + p.Y];
            }

            extension(ReadWriteBuffer<float> b)
            {
                public int Count => b.Length;
                public float this[uint i] { get => b[(int)i]; set => b[(int)i] = value + 1000f; }
            }

            extension(ReadWriteBuffer<int> b)
            {
                public ref int this[uint i] => ref b[(int)i + 1];
            }

            extension(ReadOnlyTexture2D<Float4> t)
            {
                public Float4 this[uint x, uint y] => t[(int)x, (int)y] * 2f;
            }

            extension(ReadWriteTexture2D<Float4> t)
            {
                public Float4 this[uint x, uint y] { get => t[(int)x, (int)y]; set => t[(int)x, (int)y] = value * 2f; }
            }

            extension(Span<float> s)
            {
                public float this[uint i] => s[(int)i] + 1000f;
            }
        }
        """;

    private static GeneratorRun RunExecute(string body, string members = "") =>
        GeneratorHarness.Run(TestSources.Shader($$"""
                public readonly ReadOnlyBuffer<float> src;
                public readonly ReadWriteBuffer<float> o;
                public readonly ReadWriteBuffer<int> counts;
                public readonly ReadOnlyTexture2D<Float4> image;
                public readonly ReadWriteTexture2D<Float4> canvas;
                {{members}}

                public void Execute()
                {
                    uint i = (uint)ThreadIds.X;
                    {{body}}
                }
            """) + Extensions);

    [Fact]
    public void TheExtensionBlockKind_IsTheOneOfTheCompiler()
    {
        // The generator builds against a Roslyn without TypeKind.Extension and names it by value.
        Assert.Equal(TypeKind.Extension, TypeResolver.ExtensionTypeKind);
    }

    [Theory]
    // Names made of component letters: MSL would read them as a swizzle (`v.r` is `v.x`).
    [InlineData("Float4 v = new Float4(1f, 2f, 3f, 4f); o[ThreadIds.X] = v.R;", "v.R", "Float4).R")]
    [InlineData("Float4 v = new Float4(1f, 2f, 3f, 4f); o[ThreadIds.X] = v.Zyx;", "v.Zyx", "Float4).Zyx")]
    [InlineData("Float3 v = new Float3(1f, 2f, 3f); o[ThreadIds.X] = v.LengthSquared;", "v.LengthSquared", "Float3).LengthSquared")]
    [InlineData("Float4x4 m = default; o[ThreadIds.X] = m.Trace;", "m.Trace", "Float4x4).Trace")]
    [InlineData("float f = ThreadIds.X; o[ThreadIds.X] = f.Squared;", "f.Squared", "float).Squared")]
    [InlineData("Pair p = default; o[ThreadIds.X] = p.Total;", "p.Total", "Pair).Total")]
    [InlineData("o[ThreadIds.X] = o.Count;", "o.Count", "ReadWriteBuffer<float>).Count")]
    public void AnInstanceExtensionProperty_IsRejected(string body, string location, string member)
    {
        GeneratorRun run = RunExecute(body);

        string message = run.AssertOnlyDiagnostic("SCG0017", location).GetMessage();
        Assert.Contains(Reason, message);
        Assert.Contains("'Extensions.extension(" + member + "'", message);
    }

    [Theory]
    // Every receiver whose own indexer the generator translates by shape. Arrays, threadgroup memory
    // included, are not here: C# never binds an extension indexer on an array.
    [InlineData("o[ThreadIds.X] = src[i];", "src[i]")]
    [InlineData("o[ThreadIds.X] = src[new Int2(1, 2)];", "src[new Int2(1, 2)]")]
    [InlineData("o[i] = 1f;", "o[i]")]
    [InlineData("o[i] += 1f;", "o[i]")]
    [InlineData("o[i]++;", "o[i]")]
    [InlineData("Float3 v = new Float3(1f, 2f, 3f); o[ThreadIds.X] = v[i];", "v[i]")]
    [InlineData("o[ThreadIds.X] = image[i, 0u].X;", "image[i, 0u]")]
    [InlineData("canvas[i, 0u] = new Float4(1f);", "canvas[i, 0u]")]
    [InlineData("Span<float> w = stackalloc float[4]; w[0] = 1f; o[ThreadIds.X] = w[i];", "w[i]")]
    public void AnInstanceExtensionIndexer_IsRejected(string body, string location)
    {
        GeneratorRun run = RunExecute(body);

        Assert.Contains(Reason, run.AssertOnlyDiagnostic("SCG0017", location).GetMessage());
    }

    [Fact]
    public void AnExtensionIndexer_IsNoAtomicLocation()
    {
        GeneratorRun run = RunExecute("Gpu.AtomicAdd(ref counts[i], 1);");

        Assert.Contains("it is not an element of a buffer", run.AssertOnlyDiagnostic("SCG0034", "counts[i]").GetMessage());
        Assert.DoesNotContain("metal_stdlib", Assert.Single(run.Sources).SourceText.ToString());
    }

    [Fact]
    public void AnInstanceExtensionProperty_IsNotFoldedIntoAConstant()
    {
        GeneratorRun run = RunExecute(
            "o[ThreadIds.X] = Scale;",
            "private static readonly float Scale = new Float4(1f, 2f, 3f, 4f).R;");

        // Folding `.R` as the component would give 1 instead of 2.
        Assert.Contains(Reason, run.AssertOnlyDiagnostic("SCG0017", "new Float4(1f, 2f, 3f, 4f).R").GetMessage());
        Assert.DoesNotContain("metal_stdlib", Assert.Single(run.Sources).SourceText.ToString());
    }

    [Fact]
    public void StaticExtensionMembersAndExtensionOperators_AreTranslated()
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Shader("""
                public readonly ReadWriteBuffer<float> o;

                public void Execute()
                {
                    Float3 a = new Float3(ThreadIds.X, 2f, 3f);
                    Float3 c = (a ^ Float3.Unit) + Float3.Make(1f);
                    Pair p = default;
                    p.A = c.X;
                    Pair q = p + p;
                    o[ThreadIds.X] = q.A + q.B + c.Y + c.Z;
                }
            """) + """

            public struct Pair { public float A; public float B; }

            public static class Extensions
            {
                extension(Float3)
                {
                    public static Float3 Unit => new Float3(1f, 0f, 0f);
                    public static Float3 Make(float x) => new Float3(x, x + 1f, x + 2f);
                    public static Float3 operator ^(Float3 a, Float3 b) => new Float3(a.X * b.Y, a.Y * b.Z, a.Z * b.X);
                }

                extension(Pair)
                {
                    public static Pair operator +(Pair a, Pair b) { Pair r = default; r.A = a.A + b.A; r.B = a.B + b.B + 1000f; return r; }
                }
            }
            """);

        Assert.Empty(run.Diagnostics);
        Assert.Contains("metal_stdlib", Assert.Single(run.Sources).SourceText.ToString());
    }
}
