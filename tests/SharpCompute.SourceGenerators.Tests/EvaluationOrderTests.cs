using System;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// SCG0021 for side effects in resource memory: buffers, <c>[GroupShared]</c> arrays and textures,
/// written directly or inside helpers (instance helpers, static helpers of the shader and of other
/// types, struct methods) and atomics. Each refused case computes something else on the GPU than in
/// C#; each accepted one computes the same (the GPU tests check both against <c>ForCpu</c>).
/// </summary>
public class EvaluationOrderTests
{
    /// <summary>
    /// The shader's state and helpers. <c>Bump</c> is the helper of the measured cases: it increments
    /// <c>data[slot]</c> and returns 10.
    /// </summary>
    private const string Members = """
            public readonly ReadWriteBuffer<int> data;
            public readonly ReadWriteBuffer<int> result;
            public readonly ReadWriteBuffer<int> counter;
            public readonly ReadWriteBuffer<Cell> cells;
            public readonly ReadWriteTexture2D<Float4> image;
            public readonly ReadWriteTexture2D<Float4> other;

            [GroupShared]
            private static readonly int[] tile = new int[64];

            private int Bump(int slot)
            {
                data[slot] += 1;
                return 10;
            }

            private int Outer(int slot) => Bump(slot) + 1;

            private int BumpResult() { result[0] += 1; return 10; }

            private static int BumpTile(int slot) { tile[slot] += 1; return 10; }

            private int Take() => Gpu.AtomicAdd(ref counter[0], 1);

            private int Paint(int x) { image[x, 0] = new Float4(1f, 2f, 3f, 4f); return 10; }

            private int PaintAlias(int x) { ReadWriteTexture2D<Float4> t = image; t[x, 0] = new Float4(1f, 2f, 3f, 4f); return 10; }

            private static int PaintP(ReadWriteTexture2D<Float4> t, int x) { t[x, 0] = new Float4(1f, 2f, 3f, 4f); return 10; }

            private int Next { get { data[0] += 1; return data[0]; } }

            private static int Inc(ReadWriteBuffer<int> b, int i) { b[i] += 1; return 10; }

            private static int Forward(ReadWriteBuffer<int> b, int i) => Inc(b, i) + 1;

            private static int Pair(int a, int b) => (a * 1000) + b;

            private static void Twice(ReadWriteBuffer<int> b) { b[0] += Inc(b, 0); }

            private static int Read(ReadWriteBuffer<int> b, int i) => b[i];
        """;

    private const string Types = """

        public struct Cell
        {
            public int V;

            public int Grow() { V++; return 10; }
        }

        public static class SharedOps
        {
            public static int Touch(ReadWriteBuffer<int> buffer, int i) => Deeper(buffer, i);

            private static int Deeper(ReadWriteBuffer<int> buffer, int i) { buffer[i]++; return 10; }
        }
        """;

    private static GeneratorRun RunBody(string body) =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(Members + $$"""

                public void Execute()
                {
                    int i = ThreadIds.X;
                    int j = i + 1;
                    int x = i + 3;
                    {{body}}
                }
            """, attributes: "[ThreadGroupSize(64)]") + Types);

    [Theory]
    // The four measured cases: on the GPU the value is evaluated before the target.
    [InlineData("data[0] += Bump(0);", "data[0] += Bump(0)", "data")]
    [InlineData("data[data[8]] = Bump(8);", "data[data[8]] = Bump(8)", "data")]
    [InlineData("data[24] += data[24] = 5;", "data[24] += data[24] = 5", "data")]
    [InlineData("data[data[32]] += Bump(32);", "data[data[32]] += Bump(32)", "data")]
    // The target writes what the value reads.
    [InlineData("data[Bump(0)] = data[0];", "data[Bump(0)] = data[0]", "data")]
    [InlineData("data[data[0]++] = data[0];", "data[data[0]++] = data[0]", "data")]
    // Two direct accesses without an order.
    [InlineData("result[0] = data[0] + data[0]++;", "result[0] = data[0] + data[0]++", "data")]
    [InlineData("result[0] = Pair(data[0] = 1, data[0]);", "result[0] = Pair(data[0] = 1, data[0])", "data")]
    // Through nested helpers, and through buffers passed as arguments to static helpers.
    [InlineData("data[0] += Outer(0);", "data[0] += Outer(0)", "data")]
    [InlineData("data[0] += Inc(data, 0);", "data[0] += Inc(data, 0)", "data")]
    [InlineData("data[data[1]] = Forward(data, 1);", "data[data[1]] = Forward(data, 1)", "data")]
    [InlineData("result[0] += SharedOps.Touch(result, 0);", "result[0] += SharedOps.Touch(result, 0)", "result")]
    [InlineData("result[0] += Outer(0) + BumpResult();", "result[0] += Outer(0) + BumpResult()", "result")]
    // [GroupShared] arrays.
    [InlineData("tile[0] += BumpTile(0);", "tile[0] += BumpTile(0)", "tile")]
    [InlineData("data[tile[1]] = BumpTile(1);", "data[tile[1]] = BumpTile(1)", "tile")]
    [InlineData("tile[i] += tile[i] = 2;", "tile[i] += tile[i] = 2", "tile")]
    // Textures: a helper writes a pixel, the target's index reads one.
    [InlineData("data[(int)image[0, 0].X] = Paint(0);", "data[(int)image[0, 0].X] = Paint(0)", "image")]
    // Atomics write their location, directly and in a helper.
    [InlineData("data[0] += Gpu.AtomicAdd(ref data[0], 1);", "data[0] += Gpu.AtomicAdd(ref data[0], 1)", "data")]
    [InlineData("data[counter[0]] = Take();", "data[counter[0]] = Take()", "counter")]
    [InlineData("tile[0] += Gpu.AtomicAdd(ref tile[0], 1);", "tile[0] += Gpu.AtomicAdd(ref tile[0], 1)", "tile")]
    // A method that modifies a struct element writes the buffer.
    [InlineData("cells[0].V += cells[0].Grow();", "cells[0].V += cells[0].Grow()", "cells")]
    public void SideEffectsInResources_ThatDependOnEvaluationOrder_AreSCG0021(string statement, string location, string resource)
    {
        Diagnostic diagnostic = RunBody(statement).AssertOnlyDiagnostic("SCG0021", location);
        string message = diagnostic.GetMessage();

        Assert.StartsWith($"'{resource}' is modified and used again within one expression; C# evaluates it left to right", message);
        Assert.Contains($"'{resource}' is GPU memory, and any two of its elements are treated as the same one", message);
        Assert.Contains("Do the write in a statement of its own and keep its result in a local", message);
    }

    [Theory]
    // MSL leaves the order of operands and arguments open, so a write in one (directly or in a call)
    // and a use of the same resource in another is refused, even where Metal's compiler happens to
    // evaluate them left to right today.
    [InlineData("result[0] = data[16] + Bump(16);", "Bump(16)", "data")]
    [InlineData("result[1] = Bump(17) + data[17];", "Bump(17)", "data")]
    [InlineData("result[2] = Pair(data[18], Bump(18));", "Bump(18)", "data")]
    [InlineData("result[3] = Pair(Bump(19), data[19]);", "Bump(19)", "data")]
    [InlineData("result[0] = Bump(0) + Bump(1);", "Bump(0)", "data")]
    [InlineData("result[0] = Take() + Take();", "Take()", "counter")]
    [InlineData("result[0] = data[0]++ + Read(data, 0);", "Read(data, 0)", "data")]
    [InlineData("result[0] = Next + Next;", "Next", "data")]
    [InlineData("result[0] = Gpu.AtomicAdd(ref counter[0], 1) + counter[1];", "Gpu.AtomicAdd(ref counter[0], 1)", "counter")]
    [InlineData("result[0] = tile[0] + BumpTile(0);", "BumpTile(0)", "tile")]
    [InlineData("result[0] = (int)image[0, 0].X + Paint(0);", "Paint(0)", "image")]
    public void ACallBesideAnOperandThatUsesWhatItWrites_IsSCG0021(string statement, string call, string resource)
    {
        Diagnostic diagnostic = RunBody(statement).AssertOnlyDiagnostic("SCG0021", statement.TrimEnd(';'));
        string message = diagnostic.GetMessage();

        Assert.StartsWith($"'{resource}' is modified and used again within one expression", message);
        Assert.EndsWith($"The order of operands and arguments is unspecified in MSL: evaluate the call into a local first, as in 'var t = {call};', and use the local in the expression.", message);
    }

    [Theory]
    // A texture under a local name is the texture it was initialized from, here and in helpers.
    [InlineData("ReadWriteTexture2D<Float4> t = image; data[(int)t[0, 0].X] = Paint(0);", "data[(int)t[0, 0].X] = Paint(0)")]
    [InlineData("ReadWriteTexture2D<Float4> t = image; data[(int)image[0, 0].X] = PaintP(t, 0);", "data[(int)image[0, 0].X] = PaintP(t, 0)")]
    [InlineData("data[(int)image[0, 0].X] = PaintAlias(0);", "data[(int)image[0, 0].X] = PaintAlias(0)")]
    [InlineData("ReadWriteTexture2D<Float4> t = image; ReadWriteTexture2D<Float4> u = t; data[(int)u[0, 0].X] = Paint(0);", "data[(int)u[0, 0].X] = Paint(0)")]
    [InlineData("data[(int)image[0, 0].X] = PaintP(image, 0);", "data[(int)image[0, 0].X] = PaintP(image, 0)")]
    // Assigned again: it may be any texture of its type.
    [InlineData("ReadWriteTexture2D<Float4> t = other; if (i > 0) { t = image; } data[(int)t[0, 0].X] = Paint(0);", "data[(int)t[0, 0].X] = Paint(0)")]
    public void ATextureUnderALocalName_IsTheSameTexture(string statements, string location)
    {
        Diagnostic diagnostic = RunBody(statements).AssertOnlyDiagnostic("SCG0021", location);

        Assert.StartsWith("'image' is modified and used again within one expression", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("data[0] += Bump(0);", "var t = Bump(0); data[0] += t;")]
    [InlineData("data[data[8]] = Bump(8);", "var t = Bump(8); data[data[8]] = t;")]
    [InlineData("data[24] += data[24] = 5;", "var t = data[24] = 5; data[24] += t;")]
    public void TheMessage_ShowsHowToSplitTheAssignment(string statement, string example)
    {
        Diagnostic diagnostic = RunBody(statement).AssertOnlyDiagnostic("SCG0021", statement.TrimEnd(';'));

        Assert.EndsWith($", as in '{example}'.", diagnostic.GetMessage());
    }

    [Fact]
    public void TheMessage_WithoutAnAssignmentToSplit_HasNoExample()
    {
        Diagnostic diagnostic = RunBody("result[0] = data[0] + data[0]++;").AssertOnlyDiagnostic("SCG0021", "result[0] = data[0] + data[0]++");

        Assert.EndsWith("keep its result in a local.", diagnostic.GetMessage());
    }

    [Fact]
    public void InsideAHelper_ItsBufferParameterIsTracked()
    {
        GeneratorRun run = RunBody("Twice(data);");

        run.AssertOnlyDiagnostic("SCG0021", "b[0] += Inc(b, 0)");
    }

    [Fact]
    public void ForVariables_TheMessageIsUnchanged()
    {
        Diagnostic diagnostic = RunBody("int k = i++ + i;").AssertOnlyDiagnostic("SCG0021", "i++ + i");

        Assert.Equal(
            "'i' is modified and used again within one expression; C# evaluates it left to right, but Metal Shading Language leaves the order undefined. Split the expression into separate statements.",
            diagnostic.GetMessage());
    }

    [Theory]
    // A callee's effects come after its own arguments, and `?:`, `&&` and `||` are sequenced.
    [InlineData("result[0] = Inc(data, data[16]);")]
    [InlineData("result[0] = data[0] > 0 ? Bump(0) : data[0];")]
    [InlineData("result[0] = data[0] > 0 && Bump(0) == 10 ? 1 : 0;")]
    [InlineData("result[0] = Bump(0) == 10 || data[0] > 0 ? 1 : 0;")]
    // A texture local of another texture, and a getter that writes the buffer it indexes.
    [InlineData("ReadWriteTexture2D<Float4> t = other; data[(int)t[0, 0].X] = Paint(0);")]
    [InlineData("data[Next] += 5;")]
    [InlineData("data[(int)image[0, 0].X] = Bump(0);")]
    // Reads and writes of the target itself.
    [InlineData("data[i] = data[i] + 1;")]
    [InlineData("data[i] += x;")]
    [InlineData("data[i]++; ++data[i]; data[i] -= data[j];")]
    [InlineData("data[i] = data[j] * data[i];")]
    [InlineData("tile[i] += tile[i + 1];")]
    // The store comes last in both languages.
    [InlineData("data[0] = Bump(0);")]
    [InlineData("data[i] = data[j] = 5;")]
    [InlineData("data[data.Length - 1] = Bump(0);")]
    // Different resources.
    [InlineData("data[i] += result[i];")]
    [InlineData("data[i] += BumpResult();")]
    [InlineData("result[data[i]] = Inc(counter, 0);")]
    [InlineData("result[0] += SharedOps.Touch(data, 0);")]
    [InlineData("tile[0] += Bump(0);")]
    [InlineData("data[(int)image[0, 0].X] = BumpResult();")]
    // Atomics whose location the rest of the expression does not use.
    [InlineData("result[Gpu.AtomicAdd(ref counter[0], 1)] = i;")]
    [InlineData("data[i] = Take();")]
    [InlineData("Gpu.AtomicAdd(ref data[0], data[1]);")]
    [InlineData("int before = Gpu.AtomicAdd(ref tile[0], tile[1]);")]
    // Helpers that only read.
    [InlineData("data[0] += Read(data, 0) + Read(data, 1);")]
    [InlineData("cells[i].V += cells[j].V; cells[i].Grow();")]
    public void OrderIndependentExpressions_AreNotReported(string statements)
    {
        RunBody(statements).Verify("Tests.TestShader");
    }

    // ---- wave matrices: Load reads its source and Store writes its destination -------------------

    /// <summary>
    /// Helpers that store a matrix (<c>Save*</c>), load one (<c>Fetch*</c>) and write an element
    /// (<c>Poke*</c>) of a buffer, a buffer parameter and a <c>[GroupShared]</c> array.
    /// </summary>
    private const string MatrixMembers = """
            public readonly ReadWriteBuffer<float> data;
            public readonly ReadWriteBuffer<float> result;

            [GroupShared]
            private static readonly float[] tile = new float[256];

            private int Save(WaveMatrix8x8 m) { m.Store(data, 0, 8); return 1; }

            private static int SaveTile(WaveMatrix8x8 m) { m.Store(tile, 0, 8); return 1; }

            private static int SaveP(ReadWriteBuffer<float> b, WaveMatrix8x8 m) { m.Store(b, 0, 8); return 1; }

            private WaveMatrix8x8 Fetch() => WaveMatrix8x8.Load(data, 0, 8);

            private static WaveMatrix8x8 FetchP(ReadWriteBuffer<float> b) => WaveMatrix8x8.Load(b, 0, 8);

            private int Poke() { data[0] = 5f; return 0; }

            private static int PokeTile() { tile[0] = 5f; return 0; }

            private static int Pair(int a, int b) => (a * 1000) + b;
        """;

    private static GeneratorRun RunMatrixBody(string body) =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(MatrixMembers + $$"""

                public void Execute()
                {
                    WaveMatrix8x8 m = WaveMatrix8x8.Filled(1f);
                    {{body}}
                }
            """, attributes: "[ThreadGroupSize(64)]"));

    [Theory]
    // A helper stores a matrix where the target's index, an operand or a sibling argument reads: the
    // GPU stores first (result[7], not result[0], when the matrix is filled with 7).
    [InlineData("result[(int)data[0]] = Save(m);", "data")]
    [InlineData("result[0] = Pair((int)data[0], Save(m));", "data")]
    [InlineData("result[0] = data[0] + Save(m);", "data")]
    [InlineData("result[(int)data[0]] = SaveP(data, m);", "data")]
    [InlineData("result[0] = Pair((int)data[0], SaveP(data, m));", "data")]
    [InlineData("result[(int)tile[0]] = SaveTile(m);", "tile")]
    [InlineData("tile[(int)tile[0]] = SaveTile(m);", "tile")]
    // One argument loads what a sibling writes. The calls are on two lines: SCG0035 is about one.
    [InlineData("WaveMatrix8x8 p = WaveMatrix8x8.Multiply(WaveMatrix8x8.Load(data, 0, 8),\n WaveMatrix8x8.Load(data, Poke(), 8));", "data")]
    [InlineData("WaveMatrix8x8 p = WaveMatrix8x8.Multiply(WaveMatrix8x8.Load(tile, 0, 8),\n WaveMatrix8x8.Load(tile, PokeTile(), 8));", "tile")]
    [InlineData("WaveMatrix8x8 p = WaveMatrix8x8.Multiply(WaveMatrix8x8.Load(data, Poke(), 8),\n WaveMatrix8x8.Load(data, 0, 8));", "data")]
    // The load is in a helper, of the shader's buffer and of a buffer parameter.
    [InlineData("WaveMatrix8x8 p = WaveMatrix8x8.Multiply(Fetch(),\n WaveMatrix8x8.Filled(Poke()));", "data")]
    [InlineData("WaveMatrix8x8 p = WaveMatrix8x8.Multiply(FetchP(data),\n WaveMatrix8x8.Filled(Poke()));", "data")]
    // A load beside a direct write of an element.
    [InlineData("WaveMatrix8x8 p = WaveMatrix8x8.Multiply(WaveMatrix8x8.Load(data, 0, 8),\n WaveMatrix8x8.Filled(data[0]++));", "data")]
    public void AMatrixLoadOrStore_ThatDependsOnEvaluationOrder_IsSCG0021(string statement, string resource)
    {
        Diagnostic diagnostic = RunMatrixBody(statement).AssertOnlyDiagnostic("SCG0021", statement.TrimEnd(';').Replace("WaveMatrix8x8 p = ", string.Empty, StringComparison.Ordinal));
        string message = diagnostic.GetMessage();

        Assert.StartsWith($"'{resource}' is modified and used again within one expression; C# evaluates it left to right", message);
        Assert.Contains($"'{resource}' is GPU memory, and any two of its elements are treated as the same one", message);
    }

    [Fact]
    public void TheMessage_ForTwoLoads_SaysToDoTheFirstLoadFirst()
    {
        const string Product = "WaveMatrix8x8.Multiply(WaveMatrix8x8.Load(data, 0, 8),\n WaveMatrix8x8.Load(data, Poke(), 8))";
        Diagnostic diagnostic = RunMatrixBody($"WaveMatrix8x8 p = {Product};").AssertOnlyDiagnostic("SCG0021", Product);

        Assert.EndsWith("evaluate the call into a local first, as in 'var t = WaveMatrix8x8.Load(data, 0, 8);', and use the local in the expression.", diagnostic.GetMessage());
    }

    [Theory]
    // The split forms of the refused ones.
    [InlineData("int at = (int)data[0];\nint saved = Save(m);\nresult[at] = saved;")]
    [InlineData("WaveMatrix8x8 first = WaveMatrix8x8.Load(data, 0, 8);\nint at = Poke();\nWaveMatrix8x8 second = WaveMatrix8x8.Load(data, at, 8);\nWaveMatrix8x8 p = WaveMatrix8x8.Multiply(first, second);\np.Store(result, 0, 8);")]
    // A load or store comes after its own arguments, in both languages.
    [InlineData("m.Store(data, (int)data[0], 8);")]
    [InlineData("WaveMatrix8x8 p = WaveMatrix8x8.Load(data, Poke(), 8);\np.Store(result, 0, 8);")]
    [InlineData("m.Store(tile, PokeTile(), 8);")]
    // The store of the assignment comes last in both languages.
    [InlineData("data[0] = Save(m);")]
    // Loads only read, and another resource is another variable.
    [InlineData("WaveMatrix8x8 p = WaveMatrix8x8.Multiply(WaveMatrix8x8.Load(data, 0, 8),\n WaveMatrix8x8.Load(data, 64, 8));\np.Store(data, 128, 8);")]
    [InlineData("WaveMatrix8x8 p = WaveMatrix8x8.Multiply(Fetch(),\n FetchP(data));\np.Store(result, 0, 8);")]
    [InlineData("result[(int)result[0]] = Save(m);")]
    [InlineData("result[(int)data[0]] = SaveTile(m);")]
    [InlineData("result[(int)data[0]] = SaveP(result, m);")]
    public void AMatrixLoadOrStore_ThatIsOrderIndependent_IsNotReported(string statements)
    {
        RunMatrixBody(statements).Verify("Tests.TestShader");
    }
}
