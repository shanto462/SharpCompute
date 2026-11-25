using System.Linq;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests.Conformance;

/// <summary>
/// A corpus of statements with side effects in buffers, group-shared memory and textures, on both sides
/// of SCG0021: every statement whose result depends on an evaluation order that MSL leaves open or
/// orders differently must be refused, and every everyday statement whose order is the same in both
/// languages must stay accepted and produce MSL that Metal compiles without warnings.
/// </summary>
[Trait("Category", "Conformance")]
public class EvaluationOrderCorpusTests
{
    private const string Members = """
            public readonly ReadWriteBuffer<int> data;
            public readonly ReadWriteBuffer<int> result;
            public readonly ReadWriteBuffer<int> counter;
            public readonly ReadWriteBuffer<Cell> cells;
            public readonly ReadWriteTexture2D<Float4> image;

            [GroupShared]
            private static readonly int[] tile = new int[64];

            private int Bump(int slot) { data[slot] += 1; return 10; }

            private int Next { get { data[0] += 1; return data[0]; } }

            private int Take() => Gpu.AtomicAdd(ref counter[0], 1);

            private int Paint(int x) { image[x, 0] = new Float4(1f, 2f, 3f, 4f); return 10; }

            private int PaintAlias(int x) { ReadWriteTexture2D<Float4> t = image; t[x, 0] = new Float4(1f, 2f, 3f, 4f); return 10; }

            private static int Inc(ReadWriteBuffer<int> b, int i) { b[i] += 1; return 10; }

            private static int IncAlias(ReadWriteBuffer<int> b, int i) { ReadWriteBuffer<int> c = b; c[i] += 1; return 10; }

            private static int Swapped(int i, ReadWriteBuffer<int> b) => Inc(b, i);

            private static int Two(ReadWriteBuffer<int> first, ReadWriteBuffer<int> second) { second[0] += 1; return first[0]; }

            private static int GrowAt(ReadWriteBuffer<Cell> c, int i) => c[i].Grow();

            private static int Read(ReadWriteBuffer<int> b, int i) => b[i];

            private static int Pair(int a, int b) => (a * 1000) + b;

            private static int PaintP(ReadWriteTexture2D<Float4> t, int x) { t[x, 0] = new Float4(1f, 2f, 3f, 4f); return 10; }

            private static int PaintDeep(int x, ReadWriteTexture2D<Float4> t) => PaintP(t, x) + 1;

            private static float Peek(ReadWriteTexture2D<Float4> t) => t[0, 0].X;
        """;

    private const string Types = """

        public struct Cell
        {
            public int V;

            public int Grow() { V++; return 10; }

            public int Poke(ReadWriteBuffer<int> b) { b[0] += 1; return 10; }

            public static Cell operator +(Cell a, int b) { a.V += b; return a; }
        }

        public readonly struct Marker
        {
            public readonly int V;

            public Marker(ReadWriteBuffer<int> b, int i) { b[i] += 1; V = 10; }
        }

        public static class SharedOps
        {
            public static int BumpExt(this ReadWriteBuffer<int> buffer, int i) { buffer[i] += 1; return 10; }
        }
        """;

    /// <summary>Statements that must be SCG0021: the value or another operand writes what the target or an operand uses.</summary>
    public static TheoryData<int, string> Refused() => new()
    {
        { 1, "data[0] += Bump(0);" },
        { 2, "data[data[8]] = Bump(8);" },
        { 3, "result[0] = data[16] + Bump(16);" },
        { 4, "result[1] = Bump(17) + data[17];" },
        { 5, "result[2] = Pair(data[18], Bump(18));" },
        { 6, "data[24] += data[24] = 5;" },
        { 7, "data[data[32]] += Bump(32);" },
        { 8, "data[data[8]] = Next;" },
        { 9, "data[0] += Next;" },
        { 10, "data[Next] = data[0];" },
        { 11, "data[data[8]] = data.BumpExt(8);" },
        { 12, "data[data[8]] = SharedOps.BumpExt(data, 8);" },
        { 13, "data[data[1]] = Inc(i: 1, b: data);" },
        { 14, "data[0] += Swapped(0, data);" },
        { 15, "data[0] += Two(result, data);" },
        { 16, "result[0] += Two(data, result);" },
        { 17, "data[0] += cells[1].Poke(data);" },
        { 18, "data[0] += new Marker(data, 0).V;" },
        { 19, "data[(int)image[0, 0].X] = PaintAlias(0);" },
        { 20, "data[0] += IncAlias(data, 0);" },
        { 21, "cells[cells[0].V].V = GrowAt(cells, 0);" },
        { 22, "cells[0].V += GrowAt(cells, 0);" },
        { 23, "static int Local(ReadWriteBuffer<int> b) { b[0] += 1; return 10; } data[0] += Local(data);" },
        { 24, "data[0] += i > 0 ? Bump(0) : 0;" },
        { 25, "int z = data[0] += Bump(0);" },
        { 26, "if ((data[0] += Bump(0)) > 0) { x = 1; }" },
        { 27, "for (int k = 0; k < 2; data[0] += Bump(0)) { k++; }" },
        { 28, "result[0] = Pair(data[0] += Bump(0), 1);" },
        { 29, "while ((data[data[8]] = Bump(8)) < 0) { x = 1; }" },
        { 30, "image[(int)image[0, 0].X, 0] = new Float4((float)Paint(0));" },
        { 31, "tile[tile[0]] += Gpu.AtomicAdd(ref tile[1], 1);" },
        { 32, "data[1] += data[2]++;" },
        { 33, "result[0] = data[16] + Bump(16);" },
        { 34, "result[0] = Pair(Bump(19), data[19]);" },
        { 35, "result[0] = Bump(0) + Bump(1);" },
        { 36, "result[0] = Take() + Take();" },
        { 37, "result[0] = data[0]++ + Read(data, 0);" },
        { 38, "result[0] = Next + Next;" },
        { 39, "result[0] = Gpu.AtomicAdd(ref counter[0], 1) + counter[1];" },
        { 40, "data[(int)image[0, 0].X] = PaintP(image, 0);" },
        { 41, "data[(int)image[0, 0].X] = PaintDeep(0, image);" },
        { 42, "data[(int)Peek(image)] = PaintP(image, 0);" },
        { 43, "image[0, 0] += new Float4((float)PaintP(image, 0));" },
        { 44, "ReadWriteTexture2D<Float4> t = image; data[(int)t[0, 0].X] = Paint(0);" },
        { 45, "ReadWriteTexture2D<Float4> t = image; data[(int)image[0, 0].X] = PaintP(t, 0);" },
        { 46, "ReadWriteTexture2D<Float4> t = image; ReadWriteTexture2D<Float4> u = t; data[(int)u[0, 0].X] = Paint(0);" },
        { 47, "ReadWriteTexture2D<Float4> t = image; t = image; data[(int)t[0, 0].X] = Paint(0);" },
        { 48, "result[0] = Pair(Take(), Take());" },
        { 49, "result[0] = Bump(0) * Bump(0);" },
        { 50, "if (Bump(0) > data[0]) { x = 1; }" },
        { 51, "result[0] = data[Bump(0)] + data[1];" },
    };

    /// <summary>Everyday statements that must stay accepted: the order is the same in C# and in MSL.</summary>
    public static TheoryData<int, string> Accepted() => new()
    {
        { 1, "data[0] += Two(data, result);" },
        { 2, "data[i] = Gpu.Max(data[i], data[j]);" },
        { 3, "int slot = Gpu.AtomicAdd(ref counter[0], 1); result[slot] = data[i];" },
        { 4, "tile[GroupIds.X] = data[i]; data[i] = tile[GroupIds.X] + tile[0];" },
        { 5, "image[i, 0] = image[i, 0] * 2f;" },
        { 6, "data[i] += Read(data, j);" },
        { 7, "cells[i].V = cells[i].V + cells[j].V;" },
        { 8, "data[i] = data[i] > 0 ? data[i] - 1 : Read(data, j);" },
        { 9, "int t0 = data[i]; data[i] = data[j]; data[j] = t0;" },
        { 10, "data[data[i]] += 1; counter[data[i]]++; data[data[i]] = data[j];" },
        { 11, "result[Take()] = data[i];" },
        { 12, "data[i] = Bump(j);" },
        { 13, "result[i] = Bump(i);" },
        { 14, "result[i] = Inc(data, i) + Read(result, j);" },
        { 15, "Gpu.AtomicMax(ref data[0], data[i]);" },
        { 16, "data[Next] += 5;" },
        { 17, "cells[i] = cells[i] + 1;" },
        { 18, "cells[i].Grow(); cells[j].V += x;" },
        { 19, "data[(int)Peek(image)] = Bump(0);" },
        { 20, "image[i, 0] = new Float4((float)PaintP(image, 1));" },
        { 21, "int t0 = Bump(0); result[0] = t0 + data[0];" },
        { 22, "result[0] = Bump(0) > 5 ? data[0] : data[1];" },
        { 23, "result[0] = Bump(0) > 5 && data[0] > 1 ? 1 : 0;" },
        { 24, "result[0] = Read(data, 0) + Read(data, 1) + data[2];" },
        { 25, "result[i] = Take();" },
        { 26, "result[0] = Pair(Read(data, 0), Inc(counter, 1));" },
        { 27, "data[i] = Pair(data[i], data[j]) + Read(data, 0);" },
        { 28, "result[0] = Gpu.Max(data[i], data[j]) + Gpu.Min(Read(data, 0), 3);" },
        { 29, "cells[i].V = Pair(cells[i].V, cells[j].V);" },
        { 30, "image[i, 0] = image[j, 0] + new Float4(Peek(image));" },
    };

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
    [MemberData(nameof(Refused))]
    public void OrderDependentStatements_AreSCG0021(int row, string statement)
    {
        GeneratorRun run = RunBody(statement);
        Diagnostic[] errors = run.Input.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Diagnostic[] reported = run.Diagnostics.Where(static d => d.Severity >= DiagnosticSeverity.Warning).ToArray();

        // The statement is valid C#: the refusal is the generator's, not the compiler's.
        Assert.True(errors.Length == 0, $"Row {row}: the C# does not compile:\n" + string.Join("\n", errors));
        // A statement may break another rule as well (a texture read and written in one expression, a
        // buffer-typed local in the helper); what matters here is that the order is refused.
        Assert.True(reported.Any(static d => d.Id == "SCG0021"), $"Row {row}: not refused with SCG0021, though the result depends on the evaluation order. Reported:\n" + string.Join("\n", reported));
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void OrderIndependentStatements_AreAcceptedAndCompile(int row, string statement)
    {
        // The row number keeps the names of cases that start alike distinct.
        _ = row;
        RunBody(statement).Verify("Tests.TestShader");
    }
}
