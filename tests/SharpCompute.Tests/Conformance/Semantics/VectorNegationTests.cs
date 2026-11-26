using System;
using System.Linq;

namespace SharpCompute.Tests.Conformance.Semantics;

/// <summary>
/// The 21 shapes of <see cref="VectorNegationShader"/> and the 37 of
/// <see cref="VectorNegationShapesShader"/>, one test case each: the GPU must match <c>ForCpu</c> on
/// every slot of the group, over 256 threads of small operands. The Metal compiler miscompiles
/// integer vector negation when a lane of it is never read; the generator works around it, for the
/// negations written as one and for the arithmetic that only amounts to one.
/// </summary>
[Trait("Category", "Conformance")]
public class VectorNegationTests
{
    private const int Threads = 256;

    private static readonly Lazy<int[][]> Outputs = new(static () => Run(
        VectorNegationShader.Slots,
        static (pairs, vectors, cells, output) => new VectorNegationShader(pairs, vectors, cells, output)));

    private static readonly Lazy<int[][]> ShapeOutputs = new(static () => Run(
        VectorNegationShapesShader.Slots,
        static (pairs, vectors, cells, output) => new VectorNegationShapesShader(pairs, vectors, cells, output)));

    public static TheoryData<int, int, string> Groups() => new()
    {
        { 0, 3, "-v" }, { 4, 7, "0 - v" }, { 8, 11, "v * -1" }, { 12, 15, "-1 * v" }, { 16, 19, "v / -1" }, { 20, 23, "v *= -1" },
        { 24, 31, "Int2 and Int4" }, { 32, 39, "UInt3: 0 - u and u * uint.MaxValue" }, { 40, 43, "packed element of a buffer" },
        { 44, 49, "struct in a buffer" }, { 50, 53, "group-shared" }, { 54, 57, "in a helper" }, { 58, 61, "inside a chain" },
        { 62, 65, "*= -1 on a struct field" }, { 66, 69, "*= -1 on a buffer element" }, { 70, 74, "all lanes read" },
        { 75, 78, "(0, 5, 0) - v" }, { 79, 82, "v * (-1, 7, -1)" }, { 91, 95, "floats" },
    };

    /// <summary>Negations written without a negation operator, which only the lane-by-lane sums and differences cover.</summary>
    public static TheoryData<int, int, string> UnrecognizedGroups() => new()
    {
        { 83, 86, "~v + 1" }, { 87, 90, "v - 2 * v" },
    };

    /// <summary>The groups of <see cref="VectorNegationShapesShader"/>, in its order.</summary>
    public static TheoryData<int, int, string> ShapeGroups() => new()
    {
        { 0, 3, "Int3: ~v + 1" }, { 4, 7, "Int3: v - 2 * v" }, { 8, 11, "Int3: v - (v + v)" }, { 12, 15, "Int3: v - (v << 1)" },
        { 16, 19, "Int3: -(-v)" }, { 20, 23, "Int3: v + (-v)" }, { 24, 27, "Int3: (0 - v) - v" },
        { 28, 30, "Int2: ~v + 1" }, { 31, 33, "Int2: v - 2 * v" }, { 34, 36, "Int2: v - (v + v)" }, { 37, 39, "Int2: v - (v << 1)" },
        { 40, 42, "Int2: -(-v)" }, { 43, 45, "Int2: v + (-v)" }, { 46, 48, "Int2: (0 - v) - v" },
        { 49, 53, "Int4: ~v + 1" }, { 54, 58, "Int4: v - 2 * v" }, { 59, 63, "Int4: v - (v + v)" }, { 64, 68, "Int4: v - (v << 1)" },
        { 69, 73, "Int4: -(-v)" }, { 74, 78, "Int4: v + (-v)" }, { 79, 83, "Int4: (0 - v) - v" },
        { 84, 87, "UInt3: ~v + 1" }, { 88, 91, "UInt3: v - 2 * v" }, { 92, 95, "UInt3: v - (v + v)" }, { 96, 99, "UInt3: v - (v << 1)" },
        { 100, 103, "UInt3: -(-v)" }, { 104, 107, "UInt3: v + (-v)" }, { 108, 111, "UInt3: (0 - v) - v" },
        { 112, 115, "Int3: ~(v - 1)" }, { 116, 119, "Int3: v * s, where s is -1" }, { 120, 123, "Int3: v / s, where s is -1" },
        { 124, 127, "Int3: a chain of sums that subtracts through ~v + 1" },
        { 128, 131, "Int3: a chain of compound assignments" },
        { 132, 135, "Int3: a chain of compound assignments whose values have side effects" },
        { 136, 139, "Int3: a chain of compound assignments on a buffer element whose index has side effects" },
        { 140, 143, "Int3: a chain of compound assignments on a struct field in a buffer" },
        { 144, 147, "Int3: a chain of compound assignments on group-shared memory" },
    };

    [MetalTheory]
    [MemberData(nameof(Groups))]
    public void Negation_MatchesForCpu(int from, int to, string shape) => AssertGroup(Outputs.Value, VectorNegationShader.Slots, from, to, shape);

    [MetalTheory]
    [MemberData(nameof(UnrecognizedGroups))]
    public void NegationWithoutTheOperator_MatchesForCpu(int from, int to, string shape) => AssertGroup(Outputs.Value, VectorNegationShader.Slots, from, to, shape);

    [MetalTheory]
    [MemberData(nameof(ShapeGroups))]
    public void ArithmeticThatAmountsToANegation_MatchesForCpu(int from, int to, string shape) =>
        AssertGroup(ShapeOutputs.Value, VectorNegationShapesShader.Slots, from, to, shape);

    [Fact]
    public void ShapeGroups_CoverEverySlot()
    {
        int next = 0;

        foreach (object[] group in ShapeGroups())
        {
            Assert.Equal(next, (int)group[0]);
            next = (int)group[1] + 1;
        }

        Assert.Equal(VectorNegationShapesShader.Slots, next);
    }

    private static void AssertGroup(int[][] outputs, int slots, int from, int to, string shape)
    {
        int[] gpu = outputs[0], cpu = outputs[1];
        int[] wrong = Enumerable.Range(0, Threads * slots)
            .Where(k => k % slots >= from && k % slots <= to && gpu[k] != cpu[k])
            .ToArray();

        Assert.True(
            wrong.Length == 0,
            $"{shape}: {wrong.Length} of {Threads * (to - from + 1)} values differ; first: thread {wrong.FirstOrDefault() / slots}, slot {wrong.FirstOrDefault() % slots}, " +
            $"GPU {(wrong.Length > 0 ? gpu[wrong[0]] : 0)}, ForCpu {(wrong.Length > 0 ? cpu[wrong[0]] : 0)}");
    }

    private static int[][] Run<T>(int slots, Func<ReadOnlyBuffer<int>, ReadWriteBuffer<Int3>, ReadWriteBuffer<NegationCell>, ReadWriteBuffer<int>, T> create)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T>
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] pairs = Enumerable.Range(0, Threads * 2).Select(static k => ((k * 7) % 31) - 9).ToArray();
        int[][] outputs = new int[2][];

        for (int side = 0; side < 2; side++)
        {
            using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer<int>(pairs);
            using ReadWriteBuffer<Int3> vectors = device.AllocateReadWriteBuffer<Int3>(Threads);
            using ReadWriteBuffer<NegationCell> cells = device.AllocateReadWriteBuffer<NegationCell>(Threads);
            using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(Threads * slots);
            T shader = create(input, vectors, cells, output);

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
