using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCompute.Tests.Intrinsics;

/// <summary>
/// The bit, classification, fused and packing intrinsics on the GPU against their CPU
/// implementations (<c>ForCpu</c> of the same shader), over the edge patterns: 0, all ones, the sign
/// bit, NaNs with payloads, signed zeros, infinities and subnormals.
/// </summary>
public class BitAndPackingTests
{
    private static readonly uint[] Patterns =
    [
        0u, 1u, 2u, 3u, 0x80000000u, 0xFFFFFFFFu, 0x7FFFFFFFu, 0x00FF00FFu, 0xAAAAAAAAu, 0x55555555u, 0x12345678u, 0x00010000u,
        0x7F800000u, 0xFF800000u, 0x7FC00000u, 0xFFC00000u, 0x7FC12345u, 0xFFC12345u, 0x7F800001u, // infinities and NaNs
        0x00000100u, 0x807FFFFFu, 0x00800000u, 0x80800000u, // subnormals and the smallest normals
        0x3F800000u, 0xBF800000u, 0x7F7FFFFFu, 0xFF7FFFFFu,
    ];

    private static uint[] Words(int count, int seed)
    {
        Random random = new(seed);
        var words = new List<uint>(Patterns);

        // Single bits, and runs of ones from either end.
        for (int bit = 0; bit < 32; bit++)
        {
            words.Add(1u << bit);
            words.Add(uint.MaxValue << bit);
            words.Add(uint.MaxValue >> bit);
        }

        while (words.Count < count)
        {
            words.Add((uint)random.NextInt64(0, 1L << 32));
        }

        return [.. words];
    }

    [MetalFact]
    public void BitIntrinsics_AreBitExact_GpuVsCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        uint[] words = Words(4096, seed: 3);
        using ReadOnlyBuffer<uint> input = device.AllocateReadOnlyBuffer<uint>(words);
        using ReadWriteBuffer<uint> gpu = device.AllocateReadWriteBuffer<uint>(words.Length * BitIntrinsics.Stride);
        using ReadWriteBuffer<uint> cpu = device.AllocateReadWriteBuffer<uint>(words.Length * BitIntrinsics.Stride);

        device.For(words.Length, new BitIntrinsics(input, gpu));
        device.ForCpu(words.Length, new BitIntrinsics(input, cpu));

        uint[] expected = cpu.ToArray(), actual = gpu.ToArray();
        string[] mismatches = Enumerable.Range(0, expected.Length)
            .Where(i => expected[i] != actual[i])
            .Select(i => $"input 0x{words[i / BitIntrinsics.Stride]:X8} (thread {i / BitIntrinsics.Stride}), output {i % BitIntrinsics.Stride}: CPU 0x{expected[i]:X8}, GPU 0x{actual[i]:X8}")
            .ToArray();

        Assert.True(mismatches.Length == 0, $"{mismatches.Length} outputs differ:\n" + string.Join("\n", mismatches.Take(40)));

        // And the CPU side is what the definitions say, for a few outputs that are easy to restate.
        for (int t = 0; t < words.Length; t++)
        {
            uint u = words[t];
            ReadOnlySpan<uint> o = expected.AsSpan(t * BitIntrinsics.Stride, BitIntrinsics.Stride);

            Assert.Equal(u, o[0]); // a float local keeps every bit, NaN payloads and subnormals included
            Assert.Equal(u, o[1]);
            Assert.Equal(0u, o[2]);
            Assert.Equal((uint)System.Numerics.BitOperations.PopCount(u), o[4]);
            Assert.Equal(u == 0 ? 32u : (uint)System.Numerics.BitOperations.LeadingZeroCount(u), o[6]);
            Assert.Equal(u == 0 ? 32u : (uint)System.Numerics.BitOperations.TrailingZeroCount(u), o[8]);
            Assert.Equal(o[4], o[15]);
            Assert.Equal(System.Numerics.BitOperations.RotateLeft(u, t % 67), o[18]);
            Assert.Equal(System.Numerics.BitOperations.RotateRight(u, (int)u), o[21]);
        }
    }

    [MetalFact]
    public void FusedAndSignIntrinsics_AreBitExact_Log10AndExp10WithinTolerance()
    {
        // Normal floats, zeros, infinities and NaN. Subnormals are left out: Apple GPUs flush them to
        // zero in arithmetic (a documented difference), which fma would show.
        float[] specials = [0f, -0f, 1f, -1f, 2f, 0.5f, 1e10f, -1e10f, 1e-10f, float.MaxValue, float.MinValue, float.PositiveInfinity, float.NegativeInfinity, float.NaN, 10f, 0.1f, 1000f, 3f];
        Random random = new(5);
        var a = new List<float>();
        var b = new List<float>();
        var c = new List<float>();

        foreach (float x in specials)
        {
            foreach (float y in specials)
            {
                a.Add(x);
                b.Add(y);
                c.Add(specials[(a.Count * 7) % specials.Length]);
            }
        }

        while (a.Count < 8192)
        {
            // Magnitudes from 1e-6 to 1e6, both signs: products and sums stay well inside the normal range.
            a.Add(Sample(random));
            b.Add(Sample(random));
            c.Add(Sample(random));
        }

        GraphicsDevice device = GraphicsDevice.GetDefault();
        int n = a.Count;
        using ReadOnlyBuffer<float> aBuffer = device.AllocateReadOnlyBuffer<float>(a.ToArray());
        using ReadOnlyBuffer<float> bBuffer = device.AllocateReadOnlyBuffer<float>(b.ToArray());
        using ReadOnlyBuffer<float> cBuffer = device.AllocateReadOnlyBuffer<float>(c.ToArray());
        using ReadWriteBuffer<uint> gpuExact = device.AllocateReadWriteBuffer<uint>(n * FloatIntrinsics.ExactStride);
        using ReadWriteBuffer<uint> cpuExact = device.AllocateReadWriteBuffer<uint>(n * FloatIntrinsics.ExactStride);
        using ReadWriteBuffer<float> gpuApproximate = device.AllocateReadWriteBuffer<float>(n * FloatIntrinsics.ApproximateStride);
        using ReadWriteBuffer<float> cpuApproximate = device.AllocateReadWriteBuffer<float>(n * FloatIntrinsics.ApproximateStride);

        device.For(n, new FloatIntrinsics(aBuffer, bBuffer, cBuffer, gpuExact, gpuApproximate));
        device.ForCpu(n, new FloatIntrinsics(aBuffer, bBuffer, cBuffer, cpuExact, cpuApproximate));

        // Outputs 0..1 are single fma results: the same bits, or NaN on both sides (which NaN an
        // invalid operation produces is the hardware's choice). Output 3 is pure sign manipulation.
        uint[] expected = cpuExact.ToArray(), actual = gpuExact.ToArray();
        var mismatches = new List<string>();

        for (int i = 0; i < expected.Length; i++)
        {
            int t = i / FloatIntrinsics.ExactStride, output = i % FloatIntrinsics.ExactStride;
            bool nanInvolved = float.IsNaN(a[t]) || float.IsNaN(b[t]) || float.IsNaN(c[t]);
            bool same = output switch
            {
                0 or 1 => expected[i] == actual[i] || (float.IsNaN(BitConverter.UInt32BitsToSingle(expected[i])) && float.IsNaN(BitConverter.UInt32BitsToSingle(actual[i]))),
                2 => expected[i] == actual[i] || nanInvolved || float.IsNaN(Gpu.Fma(a[t], b[t], c[t])) || float.IsNaN(Gpu.Fma(b[t], c[t], a[t])) || float.IsNaN(Gpu.Fma(c[t], a[t], b[t])),
                _ => expected[i] == actual[i],
            };

            if (!same)
            {
                mismatches.Add($"({a[t]:R}, {b[t]:R}, {c[t]:R}) output {output}: CPU 0x{expected[i]:X8}, GPU 0x{actual[i]:X8}");
            }
        }

        Assert.True(mismatches.Count == 0, $"{mismatches.Count} outputs differ:\n" + string.Join("\n", mismatches.Take(40)));

        // Transcendentals: a few ULP, like exp and log. NaN and infinities must agree exactly.
        float[] expectedApproximate = cpuApproximate.ToArray(), actualApproximate = gpuApproximate.ToArray();

        for (int i = 0; i < expectedApproximate.Length; i++)
        {
            float e = expectedApproximate[i], g = actualApproximate[i];
            int t = i / FloatIntrinsics.ApproximateStride;
            bool close = float.IsNaN(e) ? float.IsNaN(g)
                : float.IsInfinity(e) || float.IsInfinity(g) ? e.Equals(g) || MathF.Abs(e) > 1e38f || MathF.Abs(g) > 1e38f
                : MathF.Abs(e - g) <= 1e-5f * MathF.Max(1e-30f, MathF.Max(MathF.Abs(e), MathF.Abs(g)));

            Assert.True(close, $"({a[t]:R}, {b[t]:R}, {c[t]:R}) output {i % FloatIntrinsics.ApproximateStride}: CPU {e:R}, GPU {g:R}");
        }
    }

    private static float Sample(Random random)
    {
        float magnitude = MathF.Pow(10f, (random.NextSingle() * 12f) - 6f);
        return random.Next(2) == 0 ? magnitude : -magnitude;
    }

    [MetalFact]
    public void PackAndUnpack_MatchTheGpuExactly_AtEveryMidpoint()
    {
        // Every unorm8 boundary (k / 255 and (k + 0.5) / 255) with its float neighbours, the
        // out-of-range, infinite and NaN cases, then random values: the inputs that pin the rounding.
        var values = new List<float> { 0f, -0f, 1f, -1f, 2f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.Epsilon, -float.Epsilon, 1e-30f, 0.99999994f };

        for (int k = 0; k <= 255; k++)
        {
            foreach (float center in new[] { k / 255f, (k + 0.5f) / 255f })
            {
                values.Add(center);
                values.Add(MathF.BitDecrement(center));
                values.Add(MathF.BitIncrement(center));
            }
        }

        Random random = new(42);

        while (values.Count % 4 != 0 || values.Count < 8192)
        {
            values.Add((random.NextSingle() * 1.2f) - 0.1f);
        }

        // Each value visits each of the four channels.
        int n = values.Count;
        Float4[] colors = Enumerable.Range(0, n).Select(i => new Float4(values[i], values[(i + 1) % n], values[(i + 2) % n], values[(i + 3) % n])).ToArray();
        uint[] words = Enumerable.Range(0, n).Select(i => i < 256 ? (uint)i * 0x01010101u : (uint)random.NextInt64(0, 1L << 32)).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<Float4> colorBuffer = device.AllocateReadOnlyBuffer<Float4>(colors);
        using ReadOnlyBuffer<uint> wordBuffer = device.AllocateReadOnlyBuffer<uint>(words);
        using ReadWriteBuffer<uint> gpuPacked = device.AllocateReadWriteBuffer<uint>(n);
        using ReadWriteBuffer<uint> cpuPacked = device.AllocateReadWriteBuffer<uint>(n);
        using ReadWriteBuffer<Float4> gpuUnpacked = device.AllocateReadWriteBuffer<Float4>(n);
        using ReadWriteBuffer<Float4> cpuUnpacked = device.AllocateReadWriteBuffer<Float4>(n);

        device.For(n, new Unorm8Packing(colorBuffer, gpuPacked, wordBuffer, gpuUnpacked));
        device.ForCpu(n, new Unorm8Packing(colorBuffer, cpuPacked, wordBuffer, cpuUnpacked));

        uint[] packed = gpuPacked.ToArray();
        string[] mismatches = Enumerable.Range(0, n)
            .Where(i => packed[i] != Pack(colors[i]))
            .Select(i => $"color {colors[i].X:R}, {colors[i].Y:R}, {colors[i].Z:R}, {colors[i].W:R}: GPU 0x{packed[i]:X8}, ToUnorm8 0x{Pack(colors[i]):X8}")
            .ToArray();

        Assert.True(mismatches.Length == 0, $"{mismatches.Length} packed colors differ:\n" + string.Join("\n", mismatches.Take(40)));
        Assert.Equal(packed, cpuPacked.ToArray());

        // Unpacking is byte / 255f, correctly rounded, for all 256 values of every channel; and the
        // GPU packs what it unpacked back into the same word.
        Float4[] unpacked = gpuUnpacked.ToArray();
        Assert.Equal(CpuExecution.CpuComparison.Flatten(cpuUnpacked.ToArray()), CpuExecution.CpuComparison.Flatten(unpacked));

        for (int i = 0; i < n; i++)
        {
            Assert.Equal((words[i] & 0xFF) / 255f, unpacked[i].X);
            Assert.Equal(((words[i] >> 8) & 0xFF) / 255f, unpacked[i].Y);
            Assert.Equal(((words[i] >> 16) & 0xFF) / 255f, unpacked[i].Z);
            Assert.Equal((words[i] >> 24) / 255f, unpacked[i].W);
        }

        using ReadOnlyBuffer<Float4> unpackedBuffer = device.AllocateReadOnlyBuffer<Float4>(unpacked);
        device.For(n, new Unorm8Packing(unpackedBuffer, gpuPacked, wordBuffer, gpuUnpacked));
        Assert.Equal(words, gpuPacked.ToArray());
    }

    /// <summary>The definition: <see cref="CpuDispatcher.ToUnorm8"/> per channel, X in the low byte.</summary>
    private static uint Pack(Float4 color) =>
        CpuDispatcher.ToUnorm8(color.X) | ((uint)CpuDispatcher.ToUnorm8(color.Y) << 8) | ((uint)CpuDispatcher.ToUnorm8(color.Z) << 16) | ((uint)CpuDispatcher.ToUnorm8(color.W) << 24);
}
