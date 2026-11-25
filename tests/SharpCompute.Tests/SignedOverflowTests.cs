using System;
using System.Linq;
using System.Runtime.InteropServices;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// Signed integer arithmetic wraps on the GPU exactly as in unchecked C#. Every shader runs on the GPU and with <c>ForCpu</c> over all pairs of edge
/// values, and both are compared with the same expressions evaluated here in plain C#: the three
/// must agree bit for bit. Without the generator's unsigned arithmetic the Metal compiler folds
/// <c>(a * b) / b</c> to <c>a</c> and <c>a + 1 &gt; a</c> to true (see the shaders' slots 1 to 5).
/// </summary>
public class SignedOverflowTests
{
    private static readonly int[] IntEdges =
    [
        0, 1, -1, 2, -2, 3, 7, 127, 128, 255, 256, -128, -129, 32767, 32768, 65535, 65536, 46341, -46341, 1 << 30,
        int.MaxValue, int.MinValue, int.MaxValue - 1, int.MinValue + 1, 0x55555555, unchecked((int)0xAAAAAAAA), 123456789, -987654321,
    ];

    private static readonly long[] LongEdges =
    [
        0, 1, -1, 2, -2, 3, 7, 255, 65536, -65536, 1L << 31, int.MaxValue, int.MinValue, uint.MaxValue, 1L << 32, 3037000500, -3037000500,
        1L << 62, -(1L << 62), long.MaxValue, long.MinValue, long.MaxValue - 1, long.MinValue + 1, 0x5555555555555555,
        unchecked((long)0xAAAAAAAAAAAAAAAA), 123456789012345, -987654321098765, 0x0123456789ABCDEF,
    ];

    private static readonly short[] ShortEdges =
    [
        0, 1, -1, 2, -2, 3, 7, 127, 128, -128, -129, 255, 256, 181, -181, 1000, -1000, 16384, -16384,
        short.MaxValue, short.MinValue, short.MaxValue - 1, short.MinValue + 1, 0x5555, unchecked((short)0xAAAA), 12345, -12345, 200,
    ];

    private static readonly sbyte[] SbyteEdges =
    [
        0, 1, -1, 2, -2, 3, 7, 11, -11, 12, -12, 15, 16, -16, 63, 64, -64, 100, -100,
        sbyte.MaxValue - 1, sbyte.MaxValue, sbyte.MinValue, sbyte.MinValue + 1, 0x55, unchecked((sbyte)0xAA), 33, -33, 85,
    ];

    /// <summary>Every ordered pair of the edge values, flattened: a0, b0, a1, b1, ...</summary>
    private static T[] Pairs<T>(T[] edges) =>
        [.. from a in edges from b in edges from value in new[] { a, b } select value];

    private static void AssertIdentical(long[] expected, long[] cpu, long[] gpu, int slots, Func<int, string> describe, string what)
    {
        string[] mismatches = Enumerable.Range(0, expected.Length)
            .Where(k => expected[k] != cpu[k] || expected[k] != gpu[k])
            .Select(k => $"{describe(k / slots)} slot {k % slots}: C# {expected[k]}, CPU {cpu[k]}, GPU {gpu[k]}")
            .ToArray();

        Assert.True(mismatches.Length == 0, $"{what}: {mismatches.Length} of {expected.Length} values differ:\n" + string.Join("\n", mismatches.Take(40)));
    }

    // ---- the four idioms and the rest, per type ------------------------------------------------

    [MetalFact]
    public void Int_WrapsAsInCSharp_ForEveryPairOfEdgeValues()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] pairs = Pairs(IntEdges);
        int n = pairs.Length / 2;
        using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer(pairs);
        using ReadWriteBuffer<long> gpu = device.AllocateReadWriteBuffer<long>(n * IntOverflow.Slots);
        using ReadWriteBuffer<long> cpu = device.AllocateReadWriteBuffer<long>(n * IntOverflow.Slots);

        device.For(n, new IntOverflow(input, gpu));
        device.ForCpu(n, new IntOverflow(input, cpu));

        long[] expected = [.. Enumerable.Range(0, n).SelectMany(k => IntReference(pairs[2 * k], pairs[(2 * k) + 1]))];
        AssertIdentical(expected, cpu.ToArray(), gpu.ToArray(), IntOverflow.Slots, k => $"a={pairs[2 * k]} b={pairs[(2 * k) + 1]}", "int");
        Assert.Equal(784, n);
    }

    private static long[] IntReference(int a, int b)
    {
        int x = ((a + b) * b) - a;
        int z = a + 1;
        return
        [
            a * b, b > 1 ? a * b / b : 0, a + 1 > a ? 1 : 0, b > 0 && a + b < a ? 1 : 0, a - 1 < a ? 1 : 0, a * 2 / 2,
            a + b, a - b, -a, (long)(a + b) + (a - b), a * -3, (a * b * a) - b, (a * 1103515245) + 12345,
            x, a + 1, a - 1, ((long)a << 32) | (uint)z, z + 1 + b, -(a * b), b > 0 && a > int.MaxValue - b ? 1 : 0,
        ];
    }

    [MetalFact]
    public void Long_WrapsAsInCSharp_ForEveryPairOfEdgeValues()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        long[] pairs = Pairs(LongEdges);
        int n = pairs.Length / 2;
        using ReadOnlyBuffer<long> input = device.AllocateReadOnlyBuffer(pairs);
        using ReadWriteBuffer<long> gpu = device.AllocateReadWriteBuffer<long>(n * LongOverflow.Slots);
        using ReadWriteBuffer<long> cpu = device.AllocateReadWriteBuffer<long>(n * LongOverflow.Slots);

        device.For(n, new LongOverflow(input, gpu));
        device.ForCpu(n, new LongOverflow(input, cpu));

        long[] expected = [.. Enumerable.Range(0, n).SelectMany(k => LongReference(pairs[2 * k], pairs[(2 * k) + 1]))];
        AssertIdentical(expected, cpu.ToArray(), gpu.ToArray(), LongOverflow.Slots, k => $"a={pairs[2 * k]} b={pairs[(2 * k) + 1]}", "long");
    }

    private static long[] LongReference(long a, long b) =>
    [
        a * b, b > 1 ? a * b / b : 0, a + 1 > a ? 1 : 0, b > 0 && a + b < a ? 1 : 0, a - 1 < a ? 1 : 0, a * 2 / 2,
        a + b, a - b, -a, (a * 6364136223846793005L) + 1442695040888963407L, (int)a * (long)(int)b,
        ((a + b) * b) - a, a + 1, a - 1, a + ((a - 1) * 3), (a + b) * (a - b),
    ];

    [MetalFact]
    public void Short_PromotesToIntAndWrapsAsInCSharp_ForEveryPairOfEdgeValues()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        short[] pairs = Pairs(ShortEdges);
        int n = pairs.Length / 2;
        using ReadOnlyBuffer<short> input = device.AllocateReadOnlyBuffer(pairs);
        using ReadWriteBuffer<long> gpu = device.AllocateReadWriteBuffer<long>(n * ShortOverflow.Slots);
        using ReadWriteBuffer<long> cpu = device.AllocateReadWriteBuffer<long>(n * ShortOverflow.Slots);

        device.For(n, new ShortOverflow(input, gpu));
        device.ForCpu(n, new ShortOverflow(input, cpu));

        long[] expected = [.. Enumerable.Range(0, n).SelectMany(k => ShortReference(pairs[2 * k], pairs[(2 * k) + 1]))];
        AssertIdentical(expected, cpu.ToArray(), gpu.ToArray(), ShortOverflow.Slots, k => $"a={pairs[2 * k]} b={pairs[(2 * k) + 1]}", "short");
    }

    private static long[] ShortReference(short a, short b) =>
    [
        (short)(a * b), b > 1 ? (short)(a * b) / b : 0, (short)(a + 1) > a ? 1 : 0, b > 0 && (short)(a + b) < a ? 1 : 0, (short)(a * 2) / 2,
        a * a * a * a, -a, a * 65537 * b, (short)((short)((short)(a + b) * b) - a), (short)(a + 1), (short)(a - 1), (short)(a + 32767),
    ];

    [MetalFact]
    public void Sbyte_PromotesToIntAndWrapsAsInCSharp_ForEveryPairOfEdgeValues()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        sbyte[] pairs = Pairs(SbyteEdges);
        int n = pairs.Length / 2;
        using ReadOnlyBuffer<sbyte> input = device.AllocateReadOnlyBuffer(pairs);
        using ReadWriteBuffer<long> gpu = device.AllocateReadWriteBuffer<long>(n * SbyteOverflow.Slots);
        using ReadWriteBuffer<long> cpu = device.AllocateReadWriteBuffer<long>(n * SbyteOverflow.Slots);

        device.For(n, new SbyteOverflow(input, gpu));
        device.ForCpu(n, new SbyteOverflow(input, cpu));

        long[] expected = [.. Enumerable.Range(0, n).SelectMany(k => SbyteReference(pairs[2 * k], pairs[(2 * k) + 1]))];
        AssertIdentical(expected, cpu.ToArray(), gpu.ToArray(), SbyteOverflow.Slots, k => $"a={pairs[2 * k]} b={pairs[(2 * k) + 1]}", "sbyte");
    }

    private static long[] SbyteReference(sbyte a, sbyte b) =>
    [
        (sbyte)(a * b), b > 1 ? (sbyte)(a * b) / b : 0, (sbyte)(a + 1) > a ? 1 : 0, b > 0 && (sbyte)(a + b) < a ? 1 : 0, (sbyte)(a * 2) / 2,
        a * a * a * a * a, -a, a * 16843009 * b, (sbyte)((sbyte)((sbyte)(a + b) * b) - a), (sbyte)(a + 1), (sbyte)(a - 1), (sbyte)(a * -127),
    ];

    // ---- vectors ---------------------------------------------------------------------------------

    /// <summary>
    /// The <c>Int2</c>/<c>Int3</c>/<c>Int4</c> shaders take the 784 pairs as components: thread k's
    /// vector component c is pair <c>(k * width + c) % 784</c>, so every pair is in some component.
    /// </summary>
    private static (int[] A, int[] B, int[] D) VectorInputs(int width, int threads)
    {
        int[] pairs = Pairs(IntEdges);
        int count = pairs.Length / 2;
        int[] a = new int[threads * width], b = new int[threads * width], d = new int[threads * width];

        for (int k = 0; k < threads * width; k++)
        {
            int p = k % count;
            a[k] = pairs[2 * p];
            b[k] = pairs[(2 * p) + 1];
            d[k] = b[k] > 1 ? b[k] : 2;
        }

        return (a, b, d);
    }

    private static int[] VectorReference(int width, int[] a, int[] b, int[] d, int slots)
    {
        int threads = a.Length / width;
        int[] expected = new int[threads * slots * width];

        for (int t = 0; t < threads; t++)
        {
            for (int c = 0; c < width; c++)
            {
                int k = (t * width) + c, x = a[k], y = b[k], q = d[k];
                int[] values = [x * y, x * q / q, x * 2 / 2, x + y, x - y, -x, (x * 1103515245) + 12345, ((x + y) * y) - x, 3 - (x * y), -(x + 1)];

                for (int s = 0; s < slots; s++)
                {
                    expected[(((t * slots) + s) * width) + c] = values[s];
                }
            }
        }

        return expected;
    }

    private static void AssertVectors(int width, int[] expected, int[] cpu, int[] gpu, int slots)
    {
        string[] mismatches = Enumerable.Range(0, expected.Length)
            .Where(k => expected[k] != cpu[k] || expected[k] != gpu[k])
            .Select(k => $"thread {k / width / slots} slot {k / width % slots} component {k % width}: C# {expected[k]}, CPU {cpu[k]}, GPU {gpu[k]}")
            .ToArray();

        Assert.True(mismatches.Length == 0, $"Int{width}: {mismatches.Length} components differ:\n" + string.Join("\n", mismatches.Take(40)));
    }

    [MetalFact]
    public void Int2_WrapsComponentWise_AsInCSharp()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int threads = 392;
        (int[] a, int[] b, int[] d) = VectorInputs(2, threads);
        using ReadOnlyBuffer<Int2> ab = device.AllocateReadOnlyBuffer<Int2>(MemoryMarshal.Cast<int, Int2>(a).ToArray());
        using ReadOnlyBuffer<Int2> bb = device.AllocateReadOnlyBuffer<Int2>(MemoryMarshal.Cast<int, Int2>(b).ToArray());
        using ReadOnlyBuffer<Int2> db = device.AllocateReadOnlyBuffer<Int2>(MemoryMarshal.Cast<int, Int2>(d).ToArray());
        using ReadWriteBuffer<Int2> gpu = device.AllocateReadWriteBuffer<Int2>(threads * Int2Overflow.Slots);
        using ReadWriteBuffer<Int2> cpu = device.AllocateReadWriteBuffer<Int2>(threads * Int2Overflow.Slots);

        device.For(threads, new Int2Overflow(ab, bb, db, gpu));
        device.ForCpu(threads, new Int2Overflow(ab, bb, db, cpu));

        AssertVectors(2, VectorReference(2, a, b, d, Int2Overflow.Slots), MemoryMarshal.Cast<Int2, int>(cpu.ToArray()).ToArray(), MemoryMarshal.Cast<Int2, int>(gpu.ToArray()).ToArray(), Int2Overflow.Slots);
    }

    [MetalFact]
    public void Int3_WrapsComponentWise_AsInCSharp_InPackedBufferElementsToo()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int threads = 262; // 786 components: every pair at least once
        (int[] a, int[] b, int[] d) = VectorInputs(3, threads);
        using ReadOnlyBuffer<Int3> ab = device.AllocateReadOnlyBuffer<Int3>(MemoryMarshal.Cast<int, Int3>(a).ToArray());
        using ReadOnlyBuffer<Int3> bb = device.AllocateReadOnlyBuffer<Int3>(MemoryMarshal.Cast<int, Int3>(b).ToArray());
        using ReadOnlyBuffer<Int3> db = device.AllocateReadOnlyBuffer<Int3>(MemoryMarshal.Cast<int, Int3>(d).ToArray());
        using ReadWriteBuffer<Int3> gpu = device.AllocateReadWriteBuffer<Int3>(threads * Int3Overflow.Slots);
        using ReadWriteBuffer<Int3> cpu = device.AllocateReadWriteBuffer<Int3>(threads * Int3Overflow.Slots);

        device.For(threads, new Int3Overflow(ab, bb, db, gpu));
        device.ForCpu(threads, new Int3Overflow(ab, bb, db, cpu));

        AssertVectors(3, VectorReference(3, a, b, d, Int3Overflow.Slots), MemoryMarshal.Cast<Int3, int>(cpu.ToArray()).ToArray(), MemoryMarshal.Cast<Int3, int>(gpu.ToArray()).ToArray(), Int3Overflow.Slots);
    }

    [MetalFact]
    public void Int4_WrapsComponentWise_AsInCSharp()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int threads = 196;
        (int[] a, int[] b, int[] d) = VectorInputs(4, threads);
        using ReadOnlyBuffer<Int4> ab = device.AllocateReadOnlyBuffer<Int4>(MemoryMarshal.Cast<int, Int4>(a).ToArray());
        using ReadOnlyBuffer<Int4> bb = device.AllocateReadOnlyBuffer<Int4>(MemoryMarshal.Cast<int, Int4>(b).ToArray());
        using ReadOnlyBuffer<Int4> db = device.AllocateReadOnlyBuffer<Int4>(MemoryMarshal.Cast<int, Int4>(d).ToArray());
        using ReadWriteBuffer<Int4> gpu = device.AllocateReadWriteBuffer<Int4>(threads * Int4Overflow.Slots);
        using ReadWriteBuffer<Int4> cpu = device.AllocateReadWriteBuffer<Int4>(threads * Int4Overflow.Slots);

        device.For(threads, new Int4Overflow(ab, bb, db, gpu));
        device.ForCpu(threads, new Int4Overflow(ab, bb, db, cpu));

        AssertVectors(4, VectorReference(4, a, b, d, Int4Overflow.Slots), MemoryMarshal.Cast<Int4, int>(cpu.ToArray()).ToArray(), MemoryMarshal.Cast<Int4, int>(gpu.ToArray()).ToArray(), Int4Overflow.Slots);
    }

    // ---- compound assignments whose target is evaluated once --------------------------------------

    [MetalFact]
    public void CompoundAssignments_OnTargetsWithSideEffects_WrapInEveryAddressSpace()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] pairs = Pairs(IntEdges);
        const int threads = 768; // twelve threadgroups of 64
        using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer(pairs);
        using ReadWriteBuffer<int> gpuInts = device.AllocateReadWriteBuffer<int>(threads * OverflowInPlace.Slots);
        using ReadWriteBuffer<long> gpuLongs = device.AllocateReadWriteBuffer<long>(threads * OverflowInPlace.Slots);
        using ReadWriteBuffer<Int3> gpuVectors = device.AllocateReadWriteBuffer<Int3>(threads * OverflowInPlace.Slots);
        using ReadWriteBuffer<int> cpuInts = device.AllocateReadWriteBuffer<int>(threads * OverflowInPlace.Slots);
        using ReadWriteBuffer<long> cpuLongs = device.AllocateReadWriteBuffer<long>(threads * OverflowInPlace.Slots);
        using ReadWriteBuffer<Int3> cpuVectors = device.AllocateReadWriteBuffer<Int3>(threads * OverflowInPlace.Slots);

        device.For(threads, new OverflowInPlace(input, gpuInts, gpuLongs, gpuVectors));
        device.ForCpu(threads, new OverflowInPlace(input, cpuInts, cpuLongs, cpuVectors));

        long[] expected = new long[threads * 12];
        long[] cpu = new long[threads * 12], gpu = new long[threads * 12];
        int[] ci = cpuInts.ToArray(), gi = gpuInts.ToArray();
        long[] cl = cpuLongs.ToArray(), gl = gpuLongs.ToArray();
        Int3[] cv = cpuVectors.ToArray(), gv = gpuVectors.ToArray();

        for (int t = 0; t < threads; t++)
        {
            int a = pairs[2 * t], b = pairs[(2 * t) + 1];
            int bumped = ((a + b) * b) + 1;
            long counter = (long)b << 33;
            long[] values =
            [
                a + b, a * b, a - b, a + 1, a - 1, a + (b * 2), (a + b) * 3, bumped,
                ((long)a << 32) + long.MaxValue, a * (long.MinValue + b), counter + counter + 1,
                (a + b) * 3,
            ];
            int s = t * OverflowInPlace.Slots;
            long[] cpuValues = [.. ci.Skip(s).Take(8).Select(static v => (long)v), cl[s], cl[s + 1], cl[s + 2], cv[s].X];
            long[] gpuValues = [.. gi.Skip(s).Take(8).Select(static v => (long)v), gl[s], gl[s + 1], gl[s + 2], gv[s].X];
            values.CopyTo(expected, t * 12);
            cpuValues.CopyTo(cpu, t * 12);
            gpuValues.CopyTo(gpu, t * 12);

            Assert.Equal(new Int3((a + b) * 3, b * 2 * 3, (a + b) * 3), gv[s]);
            Assert.Equal(new Int3(a * b, a + b, a * b), gv[s + 1]);
            Assert.Equal(cv[s + 1], gv[s + 1]);
        }

        AssertIdentical(expected, cpu, gpu, 12, k => $"a={pairs[2 * k]} b={pairs[(2 * k) + 1]}", "in place");
    }

    // ---- constants -------------------------------------------------------------------------------

    [MetalFact]
    public void ProgramScopeConstants_WrapAsInCSharp_WhetherEmittedOrFolded()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<long> gpu = device.AllocateReadWriteBuffer<long>(OverflowingConstants.Slots);
        using ReadWriteBuffer<long> cpu = device.AllocateReadWriteBuffer<long>(OverflowingConstants.Slots);

        device.For(1, new OverflowingConstants(gpu));
        device.ForCpu(1, new OverflowingConstants(cpu));

        int big = int.MaxValue, wrapped = unchecked(big + 1);
        long[] expected =
        [
            wrapped, unchecked(big * 3), unchecked(-wrapped), unchecked(long.MaxValue + big), unchecked((long)big * big * 5), unchecked(wrapped - big),
            unchecked(big * 2), unchecked(wrapped * 2), Math.Abs(unchecked(big + 2)), Math.Max(unchecked(big * 2), unchecked(wrapped * 3)),
            Math.Min(unchecked(big + 5), unchecked(wrapped * 7)), Math.Min(unchecked(wrapped + 5), unchecked(wrapped * 7)),
        ];

        long[] onCpu = cpu.ToArray(), onGpu = gpu.ToArray();
        Assert.True(expected.SequenceEqual(onCpu), $"CPU: {string.Join(", ", onCpu)}\nC#:  {string.Join(", ", expected)}");
        Assert.True(expected.SequenceEqual(onGpu), $"GPU: {string.Join(", ", onGpu)}\nC#:  {string.Join(", ", expected)}");
    }

    // ---- user code in a target -------------------------------------------------------------------

    [MetalFact]
    public void AGetterWithSideEffectsInATarget_IsEvaluatedOnce()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] initial = [0, 10, 20, 30, 40, 50, 60, 70, 0, 0, 0, 0, 0, 0, 0, 0];
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(initial);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(initial);

        device.For(1, new GetterInTarget(gpu));
        device.ForCpu(1, new GetterInTarget(cpu));

        // Next returns 1, 2, 3 and 4: data[1] += 5, data[2]++, data[3] *= 3, q = 7 * 4.
        int[] expected = [4, 15, 21, 90, 40, 50, 60, 70, 0, 0, 0, 0, 28, 0, 0, 0];
        Assert.Equal(expected, cpu.ToArray());
        Assert.Equal(expected, gpu.ToArray());
    }

    [MetalFact]
    public void SmallIntegerProducts_InCompoundAssignments_WrapAsInCSharp()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ushort[] edges = [0, 1, 2, 3, 255, 256, 257, 32767, 32768, 32769, 46341, 65534, 65535, 12345];
        ushort[] pairs = Pairs(edges);
        int n = pairs.Length / 2;
        using ReadOnlyBuffer<ushort> input = device.AllocateReadOnlyBuffer(pairs);
        using ReadWriteBuffer<ushort> gpu = device.AllocateReadWriteBuffer<ushort>(n * SmallProducts.Slots);
        using ReadWriteBuffer<ushort> cpu = device.AllocateReadWriteBuffer<ushort>(n * SmallProducts.Slots);
        using ReadWriteBuffer<short> gpuSigned = device.AllocateReadWriteBuffer<short>(n);
        using ReadWriteBuffer<short> cpuSigned = device.AllocateReadWriteBuffer<short>(n);

        device.For(n, new SmallProducts(input, gpu, gpuSigned));
        device.ForCpu(n, new SmallProducts(input, cpu, cpuSigned));

        ushort[] expected = [.. Enumerable.Range(0, n).SelectMany(k => new[]
        {
            unchecked((ushort)(pairs[2 * k] * pairs[(2 * k) + 1])),
            unchecked((ushort)(pairs[2 * k] * pairs[(2 * k) + 1])),
            unchecked((ushort)(pairs[2 * k] * 65535)),
        })];
        short[] expectedSigned = [.. Enumerable.Range(0, n).Select(k => unchecked((short)((short)pairs[2 * k] * (short)pairs[(2 * k) + 1])))];

        Assert.Equal(expected, cpu.ToArray());
        Assert.Equal(expected, gpu.ToArray());
        Assert.Equal(expectedSigned, cpuSigned.ToArray());
        Assert.Equal(expectedSigned, gpuSigned.ToArray());
    }
}
