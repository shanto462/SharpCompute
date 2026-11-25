using System;
using System.Linq;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// The negation of an integer vector gives the same result on the GPU as in C#, also in the shape
/// that the Metal compiler miscompiles when the negation is its own operator: one lane of the
/// negation is never read, and the same vector is subtracted in a chain elsewhere. The chain then
/// loses that lane (slot 2 of the shaders, the Y of the chain). The generator emits the prelude's
/// <c>__ms_neg</c> instead, for every integer vector and every operator that is a negation.
/// </summary>
public class VectorNegationTests
{
    private const int Threads = 256;

    /// <summary>Small pairs: no arithmetic in the shaders overflows, so only the negation is under test.</summary>
    private static readonly int[] SmallPairs = [.. Enumerable.Range(0, Threads * 2).Select(static k => (k * 7) % 16)];

    private static readonly int[] IntEdges =
    [
        0, 1, -1, 2, -2, 3, 7, 255, -256, 46341, -46341, 1 << 30, int.MaxValue, int.MinValue, int.MaxValue - 1, int.MinValue + 1,
        0x55555555, unchecked((int)0xAAAAAAAA), 123456789, -987654321,
    ];

    private static void AssertIdentical<T>(T[] cpu, T[] gpu, int slots, int[] pairs, string what)
        where T : IEquatable<T>
    {
        string[] mismatches = Enumerable.Range(0, cpu.Length)
            .Where(k => !cpu[k].Equals(gpu[k]))
            .Select(k => $"a={pairs[2 * (k / slots)]} b={pairs[(2 * (k / slots)) + 1]} slot {k % slots}: CPU {cpu[k]}, GPU {gpu[k]}")
            .ToArray();

        Assert.True(mismatches.Length == 0, $"{what}: {mismatches.Length} of {cpu.Length} values differ:\n" + string.Join("\n", mismatches.Take(40)));
    }

    [MetalFact]
    public void Int3_NegatedWithAnUnusedLane_DoesNotLoseThatLaneOfAChain()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer(SmallPairs);
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(Threads * NegationInt3.Slots);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(Threads * NegationInt3.Slots);

        device.For(Threads, new NegationInt3(input, gpu));
        device.ForCpu(Threads, new NegationInt3(input, cpu));

        int[] expected = cpu.ToArray();
        AssertIdentical(expected, gpu.ToArray(), NegationInt3.Slots, SmallPairs, "Int3");

        // Thread 0 is a = 0, b = 7: v = (0, 7, 7), so -v.X + -v.Z = -7 and ((v + 1000) * 3) - v = (3000, 3014, 3014).
        Assert.Equal([-7, 3000, 3014, 3014], expected[..NegationInt3.Slots]);
    }

    [MetalFact]
    public void Int4_NegatedWithTwoUnusedLanes_DoesNotLoseThoseLanesOfAChain()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer(SmallPairs);
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(Threads * NegationInt4.Slots);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(Threads * NegationInt4.Slots);

        device.For(Threads, new NegationInt4(input, gpu));
        device.ForCpu(Threads, new NegationInt4(input, cpu));

        int[] expected = cpu.ToArray();
        AssertIdentical(expected, gpu.ToArray(), NegationInt4.Slots, SmallPairs, "Int4");
        Assert.Equal([-7, 3000, 3014, 3014, 3014], expected[..NegationInt4.Slots]);
    }

    [MetalFact]
    public void ALongVector_NegatedWithAnUnusedLane_DoesNotLoseThatLaneOfAChain()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer(SmallPairs);
        using ReadWriteBuffer<long> gpu = device.AllocateReadWriteBuffer<long>(Threads * NegationLong3.Slots);
        using ReadWriteBuffer<long> cpu = device.AllocateReadWriteBuffer<long>(Threads * NegationLong3.Slots);

        device.For(Threads, new NegationLong3(input, gpu));
        device.ForCpu(Threads, new NegationLong3(input, cpu));

        long[] expected = cpu.ToArray();
        AssertIdentical(expected, gpu.ToArray(), NegationLong3.Slots, SmallPairs, "long3");
        Assert.Equal([-7L, 3000, 3014, 3014], expected[..NegationLong3.Slots]);
    }

    [MetalFact]
    public void AShortVector_NegatedWithAnUnusedLane_DoesNotLoseThatLaneOfAChain()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer(SmallPairs);
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(Threads * NegationShort3.Slots);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(Threads * NegationShort3.Slots);

        device.For(Threads, new NegationShort3(input, gpu));
        device.ForCpu(Threads, new NegationShort3(input, cpu));

        int[] expected = cpu.ToArray();
        AssertIdentical(expected, gpu.ToArray(), NegationShort3.Slots, SmallPairs, "short3");
        Assert.Equal([-7, 3000, 3014, 3014], expected[..NegationShort3.Slots]);
    }

    [MetalFact]
    public void Int3_NegatedWhereTheArithmeticWraps_AgreesWithCSharp_ForEveryPairOfEdgeValues()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] pairs = [.. from a in IntEdges from b in IntEdges from value in new[] { a, b } select value];
        int n = pairs.Length / 2;
        using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer(pairs);
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(n * NegationWrapped.Slots);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(n * NegationWrapped.Slots);

        device.For(n, new NegationWrapped(input, gpu));
        device.ForCpu(n, new NegationWrapped(input, cpu));

        int[] onCpu = cpu.ToArray();
        AssertIdentical(onCpu, gpu.ToArray(), NegationWrapped.Slots, pairs, "wrapped Int3");

        // And ForCpu agrees with the same arithmetic on scalars, in plain C#.
        int[] expected = [.. Enumerable.Range(0, n).SelectMany(k => WrappedReference(pairs[2 * k], pairs[(2 * k) + 1]))];
        AssertIdentical(expected, onCpu, NegationWrapped.Slots, pairs, "wrapped Int3 on the CPU");
        Assert.Equal(400, n);
    }

    private static int[] WrappedReference(int a, int b)
    {
        unchecked
        {
            int[] v = [a, b, a ^ b];
            int[] t = [.. v.Select(static lane => ((lane + int.MaxValue) * 3) - lane)];
            int[] w = [b, a + b, a - b];
            int[] x = [a + 1, b - 1, a * b];
            int[] s = [.. w.Select((lane, k) => ((lane + int.MinValue) * 5) - (lane * x[k]))];
            int[] y = [b, a, a + b];

            return
            [
                -v[0] + -v[2], t[0], t[1], t[2],
                (-w[0] * x[0]) + (-w[2] * x[2]), s[0], s[1], s[2],
                -y[0], -y[1], -y[2], 0 - y[0], 0 - y[1], 0 - y[2], y[0] * -1, y[1] * -1, y[2] * -1,
                x[0] + -y[0], x[1] + -y[1], x[2] + -y[2],
            ];
        }
    }

    [MetalFact]
    public void NegationsByAConstant_WithAnUnusedLane_DoNotLoseThatLaneOfAChain()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer(SmallPairs);
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(Threads * NegationByConstant.Slots);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(Threads * NegationByConstant.Slots);

        device.For(Threads, new NegationByConstant(input, gpu));
        device.ForCpu(Threads, new NegationByConstant(input, cpu));

        int[] expected = cpu.ToArray();
        AssertIdentical(expected, gpu.ToArray(), NegationByConstant.Slots, SmallPairs, "negation by a constant");

        // Thread 0 again: form k negates (k, 7, 7), so each sum is -(k + 7) and each chain ((v + 1000) * 3) - v.
        for (int form = 0; form < NegationByConstant.Forms; form++)
        {
            Assert.Equal([-(form + 7), 3000 + (2 * form), 3014, 3014], expected[(form * 4)..((form * 4) + 4)]);
        }
    }

    /// <summary>Pairs with negative values too, still small enough that nothing overflows.</summary>
    private static readonly int[] SignedPairs = [.. Enumerable.Range(0, Threads * 2).Select(static k => ((k * 7) % 31) - 9)];

    [MetalFact]
    public void ConstantsThatNegateSomeLanes_WithTheOtherLanesUnused_DoNotLoseThoseLanesOfAChain()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer(SignedPairs);
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(Threads * NegationOfSomeLanes.Slots);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(Threads * NegationOfSomeLanes.Slots);

        device.For(Threads, new NegationOfSomeLanes(input, gpu));
        device.ForCpu(Threads, new NegationOfSomeLanes(input, cpu));

        int[] onCpu = cpu.ToArray();
        AssertIdentical(onCpu, gpu.ToArray(), NegationOfSomeLanes.Slots, SignedPairs, "constants that negate some lanes");

        // And ForCpu agrees with the same arithmetic on scalars, in plain C#.
        int[] expected = [.. Enumerable.Range(0, Threads).SelectMany(k => SomeLanesReference(SignedPairs[2 * k], SignedPairs[(2 * k) + 1]))];
        AssertIdentical(expected, onCpu, NegationOfSomeLanes.Slots, SignedPairs, "constants that negate some lanes on the CPU");
    }

    private static int[] SomeLanesReference(int a, int b)
    {
        static int[] Chain(int[] w, int[] c) => [.. w.Select((lane, k) => ((lane + c[k]) * 3) - lane)];

        int x = a ^ b;
        int[] c = [1000, 2000, 3000];
        int[] w0 = [a, b, x], w1 = [a + 1, b - 1, x + 1], w2 = [a + 2, b - 2, a + b, a - b], w3 = [a + 3, b - 3, x + 3];
        int[] u4 = [a + 4, b + 4, x + 4], u5 = [a + 5, b + 5, x + 5], w6 = [a + 6, b - 6, x + 6];

        return
        [
            -w0[0] + -w0[2], .. Chain(w0, c),
            -w1[0] + -w1[2], .. Chain(w1, c),
            -w2[1] + -w2[2], .. Chain(w2, [10, 20, 30, 40]),
            -w3[0] + -w3[2], .. Chain(w3, c),
            -u4[0] + -u4[2], .. Chain(u4, c),
            -u5[0] + -u5[2], .. Chain(u5, c),
            -w6[0], 5 - w6[1], -w6[2], w6[0], -w6[1], w6[2] * 7,
            1000 + (7 - w6[0]) - w6[0], 2000 - w6[1] - w6[1], 3000 - w6[2] + (w6[2] * 3),
            -a + -a, 5 - a,
        ];
    }

    [MetalFact]
    public void VectorsReadFromStorage_NegatedWithAnUnusedLane_DoNotLoseThatLaneOfAChain()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[][] outputs = new int[2][];

        for (int pass = 0; pass < 2; pass++)
        {
            // The shader writes the vectors it negates, so each pass gets storage of its own.
            using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer(SignedPairs);
            using ReadWriteBuffer<Int3> vectors = device.AllocateReadWriteBuffer<Int3>(Threads);
            using ReadWriteBuffer<NegationCell> cells = device.AllocateReadWriteBuffer<NegationCell>(Threads);
            using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(Threads * NegationInStorage.Slots);

            if (pass == 0)
            {
                device.For(Threads, new NegationInStorage(input, vectors, cells, output));
            }
            else
            {
                device.ForCpu(Threads, new NegationInStorage(input, vectors, cells, output));
            }

            outputs[pass] = output.ToArray();
        }

        AssertIdentical(outputs[1], outputs[0], NegationInStorage.Slots, SignedPairs, "vectors read from storage");

        // Thread 0 is a = -9, b = -2 (a ^ b = 9): the Int2 group is -(a + 6) and ((p + (100, 200)) * 3) - p.
        Assert.Equal([3, 294, 608], outputs[1][..3]);
    }
}
