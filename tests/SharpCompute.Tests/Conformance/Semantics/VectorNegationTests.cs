using System;
using System.Linq;

namespace SharpCompute.Tests.Conformance.Semantics;

/// <summary>
/// The 21 shapes of <see cref="VectorNegationShader"/>, one test case each: the GPU must match
/// <c>ForCpu</c> on every slot of the group, over 256 threads of small operands. The Metal compiler
/// miscompiles some of these negations, so they are skipped until the generator works around it.
/// </summary>
[Trait("Category", "Conformance")]
public class VectorNegationTests
{
    private const string Pending = "Pending the integer vector negation workaround";
    private const int Threads = 256;

    private static readonly Lazy<int[][]> Outputs = new(Run);

    public static TheoryData<int, int, string> Groups() => new()
    {
        { 0, 3, "-v" }, { 4, 7, "0 - v" }, { 8, 11, "v * -1" }, { 12, 15, "-1 * v" }, { 16, 19, "v / -1" }, { 20, 23, "v *= -1" },
        { 24, 31, "Int2 and Int4" }, { 32, 39, "UInt3: 0 - u and u * uint.MaxValue" }, { 40, 43, "packed element of a buffer" },
        { 44, 49, "struct in a buffer" }, { 50, 53, "group-shared" }, { 54, 57, "in a helper" }, { 58, 61, "inside a chain" },
        { 62, 65, "*= -1 on a struct field" }, { 66, 69, "*= -1 on a buffer element" }, { 70, 74, "all lanes read" },
        { 75, 78, "(0, 5, 0) - v" }, { 79, 82, "v * (-1, 7, -1)" }, { 83, 86, "~v + 1" }, { 87, 90, "v - 2 * v" },
        { 91, 95, "floats" },
    };

    [MetalTheory(Skip = Pending)]
    [MemberData(nameof(Groups))]
    public void Negation_MatchesForCpu(int from, int to, string shape)
    {
        int[] gpu = Outputs.Value[0], cpu = Outputs.Value[1];
        const int slots = VectorNegationShader.Slots;
        int[] wrong = Enumerable.Range(0, Threads * slots)
            .Where(k => k % slots >= from && k % slots <= to && gpu[k] != cpu[k])
            .ToArray();

        Assert.True(
            wrong.Length == 0,
            $"{shape}: {wrong.Length} of {Threads * (to - from + 1)} values differ; first: thread {wrong.FirstOrDefault() / slots}, slot {wrong.FirstOrDefault() % slots}, " +
            $"GPU {(wrong.Length > 0 ? gpu[wrong[0]] : 0)}, ForCpu {(wrong.Length > 0 ? cpu[wrong[0]] : 0)}");
    }

    private static int[][] Run()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] pairs = Enumerable.Range(0, Threads * 2).Select(static k => ((k * 7) % 31) - 9).ToArray();
        int[][] outputs = new int[2][];

        for (int side = 0; side < 2; side++)
        {
            using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer<int>(pairs);
            using ReadWriteBuffer<Int3> vectors = device.AllocateReadWriteBuffer<Int3>(Threads);
            using ReadWriteBuffer<NegationCell> cells = device.AllocateReadWriteBuffer<NegationCell>(Threads);
            using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(Threads * VectorNegationShader.Slots);
            VectorNegationShader shader = new(input, vectors, cells, output);

            if (side == 0)
            {
                device.For(Threads, shader);
            }
            else
            {
                device.ForCpu(Threads, shader);
            }

            outputs[side] = output.ToArray();
        }

        return outputs;
    }
}
