using System;
using System.Linq;

namespace SharpCompute.Tests.Conformance.Semantics;

/// <summary>
/// In-place stores by storage: every pair of 28 wrap-edge values through the statements of
/// <see cref="InPlaceStorageShader"/> and <see cref="VectorViewShader"/>, on the GPU and with
/// <c>ForCpu</c>. Every output slot and every buffer the shaders write must be equal.
/// </summary>
[Trait("Category", "Conformance")]
public class InPlaceStorageTests
{
    private static readonly int[] Edges =
    [
        0, 1, -1, 2, -2, 3, 7, 127, 128, 255, 256, -128, -129, 32767, 32768, 65535, 65536, 46341, -46341,
        1 << 30, int.MaxValue, int.MinValue, int.MaxValue - 1, int.MinValue + 1, 0x55555555, unchecked((int)0xAAAAAAAA), 123456789, -987654321,
    ];

    private static readonly int Threads = Edges.Length * Edges.Length;

    private static int[] Pairs() => Enumerable.Range(0, Threads).SelectMany(static k => new[] { Edges[k / Edges.Length], Edges[k % Edges.Length] }).ToArray();

    private static string Describe(int[] pairs, int thread) => $"(a={pairs[2 * thread]}, b={pairs[(2 * thread) + 1]})";

    [MetalFact]
    public void ScalarAndPackedTargets_InEveryAddressSpace_MatchForCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] pairs = Pairs();
        using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer<int>(pairs);
        var sides = new (long[] Output, int[] Data, long[] Wide, Int3[] Vec, InPlaceCell[] Cells)[2];

        for (int side = 0; side < 2; side++)
        {
            using ReadWriteBuffer<int> data = device.AllocateReadWriteBuffer<int>(Threads * InPlaceStorageShader.Stride);
            using ReadWriteBuffer<long> wide = device.AllocateReadWriteBuffer<long>(Threads);
            using ReadWriteBuffer<Int3> vec = device.AllocateReadWriteBuffer<Int3>(Threads);
            using ReadWriteBuffer<InPlaceCell> cells = device.AllocateReadWriteBuffer<InPlaceCell>(Threads);
            using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(Threads);
            using ReadWriteBuffer<long> output = device.AllocateReadWriteBuffer<long>(Threads * InPlaceStorageShader.Slots);
            InPlaceStorageShader shader = new(input, data, wide, vec, cells, counts, output);

            if (side == 0)
            {
                device.For(Threads, shader);
            }
            else
            {
                device.ForCpu(Threads, shader);
            }

            sides[side] = (output.ToArray(), data.ToArray(), wide.ToArray(), vec.ToArray(), cells.ToArray());
        }

        SlotComparison.AssertEqual("outputs", sides[0].Output, sides[1].Output, InPlaceStorageShader.Slots, describe: t => Describe(pairs, t));
        SlotComparison.AssertEqual("data", sides[0].Data, sides[1].Data, InPlaceStorageShader.Stride, describe: t => Describe(pairs, t));
        SlotComparison.AssertEqual("wide", sides[0].Wide, sides[1].Wide, 1, describe: t => Describe(pairs, t));
        SlotComparison.AssertEqual("vec", Flatten(sides[0].Vec), Flatten(sides[1].Vec), 3, describe: t => Describe(pairs, t));
        SlotComparison.AssertEqual("cells", Flatten(sides[0].Cells), Flatten(sides[1].Cells), 7, describe: t => Describe(pairs, t));
    }

    [MetalFact]
    public void VectorTargets_ByStorage_MatchForCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] pairs = Pairs();
        using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer<int>(pairs);
        long[][] outputs = new long[2][];

        for (int side = 0; side < 2; side++)
        {
            using ReadWriteBuffer<Int2> twos = device.AllocateReadWriteBuffer<Int2>(Threads);
            using ReadWriteBuffer<Int3> threes = device.AllocateReadWriteBuffer<Int3>(Threads);
            using ReadWriteBuffer<Int4> fours = device.AllocateReadWriteBuffer<Int4>(Threads);
            using ReadWriteBuffer<VectorCell> cells = device.AllocateReadWriteBuffer<VectorCell>(Threads);
            using ReadWriteBuffer<long> output = device.AllocateReadWriteBuffer<long>(Threads * VectorViewShader.Slots);
            VectorViewShader shader = new(input, twos, threes, fours, cells, output);

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

        SlotComparison.AssertEqual("outputs", outputs[0], outputs[1], VectorViewShader.Slots, describe: t => Describe(pairs, t));
    }

    private static int[] Flatten(Int3[] values) => values.SelectMany(static v => new[] { v.X, v.Y, v.Z }).ToArray();

    private static long[] Flatten(InPlaceCell[] values) =>
        values.SelectMany(static c => new long[] { c.Count, c.Total, c.V.X, c.V.Y, c.V.Z, c.P.X, c.P.Y }).ToArray();
}
