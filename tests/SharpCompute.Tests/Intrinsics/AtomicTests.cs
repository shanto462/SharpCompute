using System;
using System.Linq;

namespace SharpCompute.Tests.Intrinsics;

/// <summary>
/// <c>Gpu.Atomic*</c> under the heaviest contention: every thread of a large dispatch on the same
/// element. The GPU, the parallel CPU dispatcher and a single-threaded reference must agree on
/// everything that does not depend on the order the threads ran in.
/// </summary>
public class AtomicTests
{
    public enum Target
    {
        Gpu,
        Cpu,
    }

    private const int Threads = 1 << 20;

    private static void Run<T>(GraphicsDevice device, Target target, int threads, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T>
    {
        if (target == Target.Gpu)
        {
            device.For(threads, in shader);
        }
        else
        {
            device.ForCpu(threads, in shader);
        }
    }

    [MetalTheory]
    [InlineData(Target.Gpu, Threads)]
    [InlineData(Target.Cpu, Threads)]
    [InlineData(Target.Gpu, 1)]
    [InlineData(Target.Cpu, 1)]
    [InlineData(Target.Gpu, 1000)]
    public void EveryOperation_AllThreadsOnOneElement(Target target, int threads)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        // Initial values chosen so every operation has work to do: min starts high, max low, and all ones, or and xor zero.
        int[] initialInts = [0, 0, int.MaxValue, int.MinValue, -1, 0, 0, 0, 0];
        uint[] initialUInts = [0, 0, uint.MaxValue, 0, uint.MaxValue, 0, 0, 0, 0];
        using ReadWriteBuffer<int> ints = device.AllocateReadWriteBuffer<int>(initialInts);
        using ReadWriteBuffer<uint> uints = device.AllocateReadWriteBuffer<uint>(initialUInts);
        using ReadWriteBuffer<int> addOrder = device.AllocateReadWriteBuffer<int>(new int[threads]);
        using ReadWriteBuffer<uint> uintAddOrder = device.AllocateReadWriteBuffer<uint>(new uint[threads]);

        Run(device, target, threads, new AtomicEveryOperation(ints, uints, addOrder, uintAddOrder));

        // The single-threaded reference.
        int min = int.MaxValue, max = int.MinValue, and = -1, or = 0, xor = 0;
        uint umin = uint.MaxValue, umax = 0, uand = uint.MaxValue, uor = 0, uxor = 0, exchangedSum = 0;

        for (int i = 0; i < threads; i++)
        {
            int mixed = (int)AtomicEveryOperation.Mix((uint)i);
            min = Math.Min(min, mixed);
            max = Math.Max(max, mixed);
            umin = Math.Min(umin, (uint)mixed);
            umax = Math.Max(umax, (uint)mixed);
            and &= ~(1 << (i % 12));
            uand &= ~(1u << (i % 12));
            or |= 1 << (i % 20);
            uor |= 1u << (i % 20);
            xor ^= mixed;
            uxor ^= (uint)mixed;
            exchangedSum += (uint)i + 1u;
        }

        int[] gotInts = ints.ToArray();
        uint[] gotUInts = uints.ToArray();

        Assert.Equal(threads, gotInts[0]);
        Assert.Equal((uint)threads, gotUInts[0]);
        Assert.Equal(unchecked(-3 * threads), gotInts[1]);
        Assert.Equal(unchecked(0u - (3u * (uint)threads)), gotUInts[1]); // wraps below zero
        Assert.Equal(min, gotInts[2]);
        Assert.Equal(umin, gotUInts[2]);
        Assert.Equal(max, gotInts[3]);
        Assert.Equal(umax, gotUInts[3]);
        Assert.Equal(and, gotInts[4]);
        Assert.Equal(uand, gotUInts[4]);
        Assert.Equal(or, gotInts[5]);
        Assert.Equal(uor, gotUInts[5]);
        Assert.Equal(xor, gotInts[6]);
        Assert.Equal(uxor, gotUInts[6]);

        // Add returns the original value: with n adds of 1 those are 0..n-1, each exactly once.
        Assert.All(addOrder.ToArray(), static count => Assert.Equal(1, count));
        Assert.All(uintAddOrder.ToArray(), static count => Assert.Equal(1u, count));

        // Exchange: every value stored (i + 1 for each thread, plus the initial 0) is either returned
        // to exactly one thread or still there at the end. Which one is last depends on the schedule.
        Assert.InRange(gotInts[7], 1, threads);
        Assert.InRange(gotUInts[7], 1u, (uint)threads);
        Assert.Equal(exchangedSum, unchecked((uint)gotInts[8] + (uint)gotInts[7]));
        Assert.Equal(exchangedSum, unchecked(gotUInts[8] + gotUInts[7]));
    }

    [MetalTheory]
    [InlineData(Target.Gpu)]
    [InlineData(Target.Cpu)]
    public void EveryOperation_ReturnsTheOriginalValue_AndWrapsAtTheLimits(Target target)
    {
        // One thread, so nothing depends on scheduling: every returned value and every final element
        // is a literal, the same for the GPU and the CPU.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> ints = device.AllocateReadWriteBuffer<int>([5, 7, int.MinValue, 0, -5, 0x0F0F0F0F, 42, 1, 1, -2]);
        using ReadWriteBuffer<uint> uints = device.AllocateReadWriteBuffer<uint>([0u, 0u, 5u, 5u, 0xF0F0F0F0u, 9u, 0xC0000000u]);
        using ReadWriteBuffer<float> floats = device.AllocateReadWriteBuffer<float>([1.5f]);
        using ReadWriteBuffer<int> returned = device.AllocateReadWriteBuffer<int>(19);
        using ReadWriteBuffer<uint> returnedUInts = device.AllocateReadWriteBuffer<uint>(16);
        using ReadWriteBuffer<float> returnedFloats = device.AllocateReadWriteBuffer<float>(2);

        Run(device, target, 1, new AtomicSequence(ints, uints, floats, returned, returnedUInts, returnedFloats, int.MaxValue, 0xC0000000u));

        int[] expectedReturned =
        [
            5,              // add int.MaxValue to 5: wraps to -2147483644
            -2147483644,    // add 1
            7,              // subtract int.MinValue from 7: wraps to -2147483641
            int.MinValue,   // subtract 1 from int.MinValue: wraps to int.MaxValue
            int.MaxValue,   // subtract -1: wraps back to int.MinValue
            0,              // min(0, -1) stores -1
            -1,             // min(-1, 5) stores nothing new
            -5,             // max(-5, int.MinValue): signed, so -5 stays
            -5,             // max(-5, 9) stores 9
            0x0F0F0F0F,     // and 0x00FF00FF: 0x000F000F
            0x000F000F,     // or the sign bit: 0x800F000F
            -2146500593,    // (0x800F000F) xor -1: 0x7FF0FFF0
            42,             // exchange with int.MinValue
            1,              // compare-exchange expecting 3: fails, 1 stays
            1,              // compare-exchange expecting 1: succeeds, stores 11
            1,              // add big (int.MaxValue) to 1: wraps to int.MinValue
            int.MinValue,   // add big again: -1
            -2,             // subtract big from -2: wraps to int.MaxValue
            int.MaxValue,   // subtract int.MinValue (-big - 1): wraps to -1
        ];
        uint[] expectedReturnedUInts =
        [
            0u,             // add 0xFFFFFFFF to 0
            0xFFFFFFFFu,    // add 2: through zero to 1
            0u,             // subtract 1 from 0: through zero to 0xFFFFFFFF
            0xFFFFFFFFu,    // subtract ubig (0xC0000000): 0x3FFFFFFF
            0x3FFFFFFFu,    // subtract ubig again: through zero to 0x7FFFFFFF
            5u,             // min(5, 0xFFFFFFFF): unsigned, so 5 stays
            5u,             // min(5, 3) stores 3
            5u,             // max(5, 0x80000000): unsigned, so it is stored
            0x80000000u,    // max(0x80000000, 7) stores nothing new
            0xF0F0F0F0u,    // and 0xFF00FF00: 0xF000F000
            0xF000F000u,    // or 0xF: 0xF000F00F
            0xF000F00Fu,    // xor 0xFFFFFFFF: 0x0FFF0FF0
            9u,             // exchange with 0xDEADBEEF
            0xC0000000u,    // compare-exchange expecting 1: fails
            0xC0000000u,    // compare-exchange expecting ubig: succeeds, stores 1
            1u,             // max(1, ubig) stores ubig
        ];

        Assert.Equal(expectedReturned, returned.ToArray());
        Assert.Equal(expectedReturnedUInts, returnedUInts.ToArray());
        Assert.Equal([1.5f, 1.75f], returnedFloats.ToArray());

        Assert.Equal([-2147483643, -2147483641, int.MinValue, -1, 9, 0x7FF0FFF0, int.MinValue, 11, -1, -1], ints.ToArray());
        Assert.Equal([1u, 0x7FFFFFFFu, 3u, 0x80000000u, 0x0FFF0FF0u, 0xDEADBEEFu, 0xC0000000u], uints.ToArray());
        Assert.Equal([-0.25f], floats.ToArray());
    }

    // ---- atomics and CPU+GPU splits ---------------------------------------------------------------
    // The GPU's atomics and the CPU's interlocked operations do not exclude each other on shared
    // memory (a split histogram loses counts), so a shader with atomics is never divided.

    private const int SplitThreads = 1 << 20;

    private static uint[] SplitValues()
    {
        Random random = new(7);
        return Enumerable.Range(0, SplitThreads).Select(_ => (uint)random.Next()).ToArray();
    }

    private static int[] Histogram(uint[] values, int bins)
    {
        int[] expected = new int[bins];

        foreach (uint value in values)
        {
            expected[value % (uint)bins]++;
        }

        return expected;
    }

    [MetalFact]
    public void GeneratedDescriptors_SayWhichShadersUseAtomics()
    {
        static bool UsesAtomics<T>()
            where T : struct, IComputeShaderDescriptor<T> => T.UsesAtomics;

        Assert.True(UsesAtomics<AtomicHistogram>());
        Assert.True(UsesAtomics<AtomicEveryOperation>());
        Assert.True(UsesAtomics<AtomicCompareExchangeLoop>()); // only a prelude helper and one add
        Assert.True(UsesAtomics<AtomicPixelCount>());
        Assert.False(UsesAtomics<BitIntrinsics>());
        Assert.False(UsesAtomics<MatrixMath>());
        Assert.False(UsesAtomics<Shaders.MultiplyByTwo>()); // hand-written: the interface's default
    }

    [MetalTheory]
    [InlineData(0.5)]
    [InlineData(0.1)]
    [InlineData(0.9)]
    [InlineData(1e-9)]
    [InlineData(0.999999)]
    public void ForSplit_WithAnExplicitShareOnBothDevices_IsRefused_BeforeAnythingRuns(double cpuFraction)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<uint> values = device.AllocateReadOnlyBuffer<uint>(SplitValues());
        using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(new int[4]);
        AtomicHistogram shader = new(values, counts, 4);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.ForSplit(SplitThreads, shader, cpuFraction));

        Assert.Contains("AtomicHistogram", exception.Message);
        Assert.Contains("uses atomic operations", exception.Message);
        Assert.Contains("cannot be split between the CPU and the GPU", exception.Message);
        Assert.Contains("cpuFraction of 0 or 1", exception.Message);
        Assert.Equal([0, 0, 0, 0], counts.ToArray());

        // The same for the 2D and 3D overloads.
        Assert.Throws<NotSupportedException>(() => device.ForSplit(1024, SplitThreads / 1024, shader, cpuFraction));
        Assert.Throws<NotSupportedException>(() => device.ForSplit(64, 64, SplitThreads / 4096, shader, cpuFraction));
        Assert.Equal([0, 0, 0, 0], counts.ToArray());
    }

    [MetalTheory]
    [InlineData(0.0, 256)]
    [InlineData(1.0, 256)]
    [InlineData(0.0, 4)]
    [InlineData(1.0, 4)]
    [InlineData(0.0, 1)]
    [InlineData(1.0, 1)]
    public void ForSplit_WithAllOfTheGridOnOneDevice_IsAllowedAndExact(double cpuFraction, int bins)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        uint[] data = SplitValues();
        int[] expected = Histogram(data, bins);
        using ReadOnlyBuffer<uint> values = device.AllocateReadOnlyBuffer<uint>(data);

        for (int run = 0; run < 3; run++)
        {
            using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(new int[bins]);
            SplitDispatchInfo info = device.ForSplit(SplitThreads, new AtomicHistogram(values, counts, bins), cpuFraction);

            Assert.Equal(expected, counts.ToArray());
            Assert.Equal(cpuFraction == 1 ? SplitThreads : 0, info.CpuExtent);
            Assert.Equal(cpuFraction == 1 ? 0 : SplitThreads, info.GpuExtent);
            Assert.Equal(cpuFraction, info.CpuFraction);
            Assert.True(info.SingleDeviceOnly);
        }
    }

    [MetalTheory]
    [InlineData(256)]
    [InlineData(4)]
    [InlineData(1)]
    public void ForSplit_Adaptive_RunsOnTheGpuAlone_SaysSo_AndIsExact(int bins)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        uint[] data = SplitValues();
        int[] expected = Histogram(data, bins);
        using ReadOnlyBuffer<uint> values = device.AllocateReadOnlyBuffer<uint>(data);

        // An adaptive split of any other shader divides its first two dispatches and probes splitting
        // again on the 4th, 8th, ... after that. Ten dispatches cover all of those.
        for (int run = 0; run < 10; run++)
        {
            using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(new int[bins]);
            SplitDispatchInfo info = device.ForSplit(SplitThreads, new AtomicHistogram(values, counts, bins));

            Assert.Equal(expected, counts.ToArray());
            Assert.True(info.SingleDeviceOnly, $"run {run}");
            Assert.Equal(0, info.CpuExtent);
            Assert.Equal(SplitThreads, info.GpuExtent);
            Assert.Equal(0.0, info.CpuFraction);
            Assert.Equal(TimeSpan.Zero, info.CpuTime);
            Assert.True(info.GpuTime > TimeSpan.Zero);
        }

        // 2D: two rows, each over the first half of the values (the shader indexes by x only).
        using ReadWriteBuffer<int> counts2D = device.AllocateReadWriteBuffer<int>(new int[bins]);
        SplitDispatchInfo info2D = device.ForSplit(SplitThreads / 2, 2, new AtomicHistogram(values, counts2D, bins));
        Assert.Equal(Histogram(data[..(SplitThreads / 2)], bins).Select(static count => count * 2), counts2D.ToArray());
        Assert.True(info2D.SingleDeviceOnly);
        Assert.Equal((1, 0, 2), (info2D.Axis, info2D.CpuExtent, info2D.GpuExtent));
    }

    [MetalFact]
    public void ForEachSplit_OfAPixelShaderWithAtomics_FollowsTheSameRules()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int width = 256, height = 192;
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        using ReadWriteTexture2D<Rgba32> reference = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(new int[2]);
        using ReadWriteBuffer<int> referenceCounts = device.AllocateReadWriteBuffer<int>(new int[2]);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.ForEachSplit(texture, new AtomicPixelCount(counts), 0.5));
        Assert.Contains("uses atomic operations", exception.Message);
        Assert.Equal([0, 0], counts.ToArray());

        device.ForEach(reference, new AtomicPixelCount(referenceCounts));

        for (int run = 0; run < 4; run++)
        {
            SplitDispatchInfo info = device.ForEachSplit(texture, new AtomicPixelCount(counts));
            Assert.True(info.SingleDeviceOnly);
            Assert.Equal((0, height), (info.CpuExtent, info.GpuExtent));
        }

        Assert.Equal([4 * width * height / 2, 4 * width * height / 2], counts.ToArray());
        Assert.Equal(reference.ToArray(), texture.ToArray());

        // All rows on the CPU is one device too.
        SplitDispatchInfo cpuOnly = device.ForEachSplit(texture, new AtomicPixelCount(counts), 1.0);
        Assert.True(cpuOnly.SingleDeviceOnly);
        Assert.Equal((height, 0), (cpuOnly.CpuExtent, cpuOnly.GpuExtent));
        Assert.Equal([5 * width * height / 2, 5 * width * height / 2], counts.ToArray());
    }

    // ---- atomics in a shader that also captures textures -----------------------------------------

    private const int TextureWidth = 320;
    private const int TextureHeight = 200;

    private static Rgba32[] TexturePixels()
    {
        Random random = new(11);
        byte[] bytes = new byte[TextureWidth * TextureHeight * 4];
        random.NextBytes(bytes);

        return Enumerable.Range(0, TextureWidth * TextureHeight)
            .Select(i => new Rgba32(bytes[i * 4], bytes[(i * 4) + 1], bytes[(i * 4) + 2], bytes[(i * 4) + 3]))
            .ToArray();
    }

    /// <summary>Rows (0, 2, 0, 0), (0.5, 0, 0, 0), (0, 0, 0, 1), (0, 0, 1, 0): M * (r, g, b, a) = (2g, r / 2, a, b), exact in floats.</summary>
    private static readonly Float4x4 Tint = new(0f, 2f, 0f, 0f, 0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 1f, 0f);

    private static void AssertTextureHistogram(Rgba32[] pixels, int[] counts, uint seen, Float4[] target)
    {
        int[] expected = new int[AtomicTextureHistogram.Bins + 1];
        uint union = 0;

        foreach (Rgba32 pixel in pixels)
        {
            expected[pixel.R]++;
            union |= pixel.R | ((uint)pixel.G << 8) | ((uint)pixel.B << 16) | ((uint)pixel.A << 24);
        }

        expected[AtomicTextureHistogram.Bins] = expected.Max();

        Assert.Equal(expected, counts);
        Assert.Equal(union, seen);
        Assert.Equal(
            pixels.SelectMany(static p => new[] { 2f * (p.G / 255f), 0.5f * (p.R / 255f), p.A / 255f, p.B / 255f }),
            target.SelectMany(static v => new[] { v.X, v.Y, v.Z, v.W }));
    }

    [MetalTheory]
    [InlineData(Target.Gpu)]
    [InlineData(Target.Cpu)]
    public void Atomics_InAShaderThatReadsAndWritesTextures(Target target)
    {
        // Textures have their own binding indices and their own CPU path; the atomics must still reach
        // their buffers, from the kernel and from a helper that reads the texture, on both devices.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Rgba32[] pixels = TexturePixels();
        using ReadOnlyTexture2D<Rgba32> source = device.AllocateReadOnlyTexture2D<Rgba32>(pixels, TextureWidth, TextureHeight);
        using ReadWriteTexture2D<Float4> output = device.AllocateReadWriteTexture2D<Float4>(TextureWidth, TextureHeight);
        using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(AtomicTextureHistogram.Bins + 1);
        using ReadWriteBuffer<uint> seen = device.AllocateReadWriteBuffer<uint>(1);
        AtomicTextureHistogram shader = new(source, output, counts, seen, Tint);

        if (target == Target.Gpu)
        {
            device.For(TextureWidth, TextureHeight, shader);
        }
        else
        {
            device.ForCpu(TextureWidth, TextureHeight, shader);
        }

        AssertTextureHistogram(pixels, counts.ToArray(), seen[0], output.ToArray());
    }

    [MetalFact]
    public void ForSplit_OfAShaderWithAtomicsAndTextures_IsNotDividedEither()
    {
        static bool UsesAtomics<T>()
            where T : struct, IComputeShaderDescriptor<T> => T.UsesAtomics;

        Assert.True(UsesAtomics<AtomicTextureHistogram>());

        GraphicsDevice device = GraphicsDevice.GetDefault();
        Rgba32[] pixels = TexturePixels();
        using ReadOnlyTexture2D<Rgba32> source = device.AllocateReadOnlyTexture2D<Rgba32>(pixels, TextureWidth, TextureHeight);
        using ReadWriteTexture2D<Float4> output = device.AllocateReadWriteTexture2D<Float4>(TextureWidth, TextureHeight);
        using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(AtomicTextureHistogram.Bins + 1);
        using ReadWriteBuffer<uint> seen = device.AllocateReadWriteBuffer<uint>(1);
        AtomicTextureHistogram shader = new(source, output, counts, seen, Tint);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.ForSplit(TextureWidth, TextureHeight, shader, 0.5));
        Assert.Contains("uses atomic operations", exception.Message);
        Assert.All(counts.ToArray(), static count => Assert.Equal(0, count));

        SplitDispatchInfo info = device.ForSplit(TextureWidth, TextureHeight, shader);

        Assert.True(info.SingleDeviceOnly);
        Assert.Equal((0, TextureHeight), (info.CpuExtent, info.GpuExtent));
        AssertTextureHistogram(pixels, counts.ToArray(), seen[0], output.ToArray());
    }

    [MetalFact]
    public void ShadersWithoutAtomics_AreStillSplit_AndNotMarkedSingleDevice()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        uint[] words = Enumerable.Range(0, 1 << 16).Select(static i => (uint)i * 2654435761u).ToArray();
        using ReadOnlyBuffer<uint> input = device.AllocateReadOnlyBuffer<uint>(words);
        using ReadWriteBuffer<uint> split = device.AllocateReadWriteBuffer<uint>(words.Length * BitIntrinsics.Stride);
        using ReadWriteBuffer<uint> gpu = device.AllocateReadWriteBuffer<uint>(words.Length * BitIntrinsics.Stride);

        device.For(words.Length, new BitIntrinsics(input, gpu));
        SplitDispatchInfo info = device.ForSplit(words.Length, new BitIntrinsics(input, split), 0.5);

        Assert.False(info.SingleDeviceOnly);
        Assert.True(info.CpuExtent > 0 && info.GpuExtent > 0);
        Assert.Equal(gpu.ToArray(), split.ToArray()); // the bit intrinsics are bit-exact, so the cut does not show

        Assert.False(device.ForSplit(words.Length, new BitIntrinsics(input, split)).SingleDeviceOnly);
        Assert.False(device.ForSplit(words.Length, new BitIntrinsics(input, split), 0.0).SingleDeviceOnly);
    }

    [MetalTheory]
    [InlineData(Target.Gpu, 1 << 14)]
    [InlineData(Target.Cpu, 1 << 14)]
    [InlineData(Target.Gpu, 1)]
    public void CompareExchange_IsStrong_OneWinnerAndAnExactCounter(Target target, int threads)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> ints = device.AllocateReadWriteBuffer<int>(new int[3]);
        using ReadWriteBuffer<uint> uints = device.AllocateReadWriteBuffer<uint>(new uint[3]);

        Run(device, target, threads, new AtomicCompareExchangeLoop(ints, uints));

        // Exactly one thread saw the comparand and stored its id; a weak compare-and-swap that fails
        // spuriously could leave the claim to a later thread, but never to two or to none.
        Assert.InRange(ints[0], 1, threads);
        Assert.Equal(1, ints[1]);
        Assert.InRange(uints[0], 1u, (uint)threads);
        Assert.Equal(1u, uints[1]);

        // The retry loop only counts every thread if a failed swap really returns the current value.
        Assert.Equal(threads, ints[2]);
        Assert.Equal((uint)threads, uints[2]);
    }

    [MetalTheory]
    [InlineData(Target.Gpu)]
    [InlineData(Target.Cpu)]
    public void FloatAdd_AppliesEveryAdd_AndReturnsEachPartialSumOnce(Target target)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> sums = device.AllocateReadWriteBuffer<float>(new float[2]);
        using ReadWriteBuffer<int> order = device.AllocateReadWriteBuffer<int>(new int[Threads]);

        Run(device, target, Threads, new AtomicFloatSum(sums, order));

        // 2^20 ones and quarters: every partial sum is exact, so no add may be lost to rounding or a race.
        Assert.Equal((float)Threads, sums[0]);
        Assert.Equal(Threads / 4 * 1.5f, sums[1]);
        Assert.All(order.ToArray(), static count => Assert.Equal(1, count));
    }

    [MetalTheory]
    [InlineData(256)]
    [InlineData(4)]
    [InlineData(1)]
    public void Histogram_MatchesTheCpuReference_WhateverTheContention(int bins)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Random random = new(bins);
        uint[] values = Enumerable.Range(0, Threads).Select(_ => (uint)random.Next()).ToArray();
        int[] expected = new int[bins];

        foreach (uint value in values)
        {
            expected[value % (uint)bins]++;
        }

        using ReadOnlyBuffer<uint> valueBuffer = device.AllocateReadOnlyBuffer<uint>(values);
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(new int[bins]);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(new int[bins]);

        device.For(Threads, new AtomicHistogram(valueBuffer, gpu, bins));
        device.ForCpu(Threads, new AtomicHistogram(valueBuffer, cpu, bins));

        Assert.Equal(expected, gpu.ToArray());
        Assert.Equal(expected, cpu.ToArray());
    }

    [MetalFact]
    public void Atomics_AccumulateAcrossDispatches_AndInsideAComputeContext()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        uint[] values = Enumerable.Range(0, 4096).Select(static i => (uint)i).ToArray();
        using ReadOnlyBuffer<uint> valueBuffer = device.AllocateReadOnlyBuffer<uint>(values);
        using ReadWriteBuffer<int> counts = device.AllocateReadWriteBuffer<int>(new int[8]);

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.For(values.Length, new AtomicHistogram(valueBuffer, counts, 8));
            context.For(values.Length, new AtomicHistogram(valueBuffer, counts, 2));
        }

        device.For(values.Length, new AtomicHistogram(valueBuffer, counts, 8));
        device.ForCpu(values.Length, new AtomicHistogram(valueBuffer, counts, 1));

        // 512 per bin from each 8-bin pass, 2048 more in bins 0 and 1, all 4096 in bin 0.
        Assert.Equal([1024 + 2048 + 4096, 1024 + 2048, 1024, 1024, 1024, 1024, 1024, 1024], counts.ToArray());
    }
}
