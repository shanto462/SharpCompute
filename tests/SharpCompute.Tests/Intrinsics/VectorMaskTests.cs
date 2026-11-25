using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace SharpCompute.Tests.Intrinsics;

/// <summary>
/// Bool vectors on the GPU against the CPU (<c>ForCpu</c> of the same shader) and against plain C#:
/// the comparisons of every numeric vector, <c>Gpu.Equal</c>/<c>NotEqual</c>, the vector
/// classifications, the logical operators, <c>Any</c>/<c>All</c> and <c>Select</c>. Everything here
/// is exact, so everything must match bit for bit (as it does on the Apple M4 Pro this was written
/// on), with one allowance: a NaN is any NaN. Where <c>Select</c> returns a NaN, the test asks for
/// a NaN, not for its sign or its payload, which no GPU promises.
/// </summary>
public class VectorMaskTests
{
    private const int Count = 4096;

    private static readonly float[] Floats =
    [
        0f, -0f, 1f, -1f, 0.5f, 2f, 3.5f, -3.5f, 1.0000001f, 0.99999994f, float.MaxValue, float.MinValue,
        float.PositiveInfinity, float.NegativeInfinity, float.NaN, -float.NaN, 1e-20f, -1e-20f, 16777216f, 16777218f,
    ];

    private static readonly ushort[] Halves =
    [
        0x0000, 0x8000, 0x3C00, 0xBC00, 0x3800, 0x4000, 0x7C00, 0xFC00, 0x7E00, 0xFE01, 0x7BFF, 0xFBFF,
        0x0001, 0x8001, 0x03FF, 0x0400, 0x3BFF, 0x3C01,
    ];

    private static readonly int[] Ints = [0, 1, -1, 2, -2, 5, int.MaxValue, int.MinValue, 0x40000000, unchecked((int)0x80000001), 255, -256];

    /// <summary>The <c>Select</c> outputs that hold the bits of floats (the others hold integers, which are compared as they are).</summary>
    private static readonly int[] FloatSelects = [0, 3, 4];

    /// <summary>The mask that is the XOR of the bits of two selected floats.</summary>
    private const int FloatBitsMask = 51;

    private static bool IsNaN(uint bits) => (bits & 0x7F800000) == 0x7F800000 && (bits & 0x007FFFFF) != 0;

    private static bool IsNaN(ushort bits) => (bits & 0x7C00) == 0x7C00 && (bits & 0x03FF) != 0;

    /// <summary>The same float, where every NaN is the same, whatever its sign and payload.</summary>
    private static bool SameFloat(uint expected, uint actual) => expected == actual || (IsNaN(expected) && IsNaN(actual));

    /// <summary>The same float, where every NaN is the same, whatever its sign and payload.</summary>
    private static bool SameFloat(float expected, uint actual) => SameFloat(BitConverter.SingleToUInt32Bits(expected), actual);

    /// <summary>The same half, where every NaN is the same, whatever its sign and payload.</summary>
    private static bool SameHalf(ushort expected, ushort actual) => expected == actual || (IsNaN(expected) && IsNaN(actual));

    /// <summary>The same half, where every NaN is the same, whatever its sign and payload.</summary>
    private static bool SameHalf(Half expected, Half actual) => SameHalf(BitConverter.HalfToUInt16Bits(expected), BitConverter.HalfToUInt16Bits(actual));

    /// <summary>
    /// Whether one of the two floats whose bits <see cref="FloatBitsMask"/> combines is a NaN: the
    /// XOR then holds a sign and a payload nobody promises, and is not compared.
    /// </summary>
    private static bool FloatBitsMaskHoldsANaN(Inputs inputs, int thread)
    {
        Float4 fa = inputs.FloatA[thread], fb = inputs.FloatB[thread];
        Half4 ha = inputs.HalfA[thread], hb = inputs.HalfB[thread];
        return float.IsNaN(ha.X != hb.X ? fa.X : fb.Z) || float.IsNaN(ha.W != hb.W ? fa.Y : fb.W);
    }

    private sealed record Inputs(Float4[] FloatA, Float4[] FloatB, Half4[] HalfA, Half4[] HalfB, Int4[] IntA, Int4[] IntB);

    private sealed record Outputs(uint[] Masks, UInt4[] SelectedBits, Half4[] SelectedHalves);

    private static Inputs CreateInputs()
    {
        Random random = new(21);

        // A small pool, so that equal components are common; float subnormals are left out because the
        // GPU flushes them (a documented difference of float arithmetic, not of the comparisons).
        float NextFloat() => random.Next(4) == 0 ? (float)((random.NextDouble() * 8) - 4) : Floats[random.Next(Floats.Length)];
        Half NextHalf() => BitConverter.UInt16BitsToHalf(random.Next(4) == 0 ? (ushort)random.Next(65536) : Halves[random.Next(Halves.Length)]);
        int NextInt() => random.Next(4) == 0 ? random.Next(int.MinValue, int.MaxValue) : Ints[random.Next(Ints.Length)];

        Float4[] fa = new Float4[Count], fb = new Float4[Count];
        Half4[] ha = new Half4[Count], hb = new Half4[Count];
        Int4[] ia = new Int4[Count], ib = new Int4[Count];

        for (int i = 0; i < Count; i++)
        {
            fa[i] = new Float4(NextFloat(), NextFloat(), NextFloat(), NextFloat());
            fb[i] = new Float4(NextFloat(), NextFloat(), NextFloat(), NextFloat());
            ha[i] = new Half4(NextHalf(), NextHalf(), NextHalf(), NextHalf());
            hb[i] = new Half4(NextHalf(), NextHalf(), NextHalf(), NextHalf());
            ia[i] = new Int4(NextInt(), NextInt(), NextInt(), NextInt());
            ib[i] = new Int4(NextInt(), NextInt(), NextInt(), NextInt());
        }

        // NaNs of either sign, with payloads, to follow through Select: what arrives must be a NaN.
        fa[0] = new Float4(BitConverter.Int32BitsToSingle(unchecked((int)0xFFC12345)), -0f, 1f, BitConverter.Int32BitsToSingle(0x7FC00001));
        fb[0] = new Float4(2f, 0f, BitConverter.Int32BitsToSingle(0x7FCABCDE), 4f);
        return new Inputs(fa, fb, ha, hb, ia, ib);
    }

    private static Outputs Run(GraphicsDevice device, Inputs inputs, bool onGpu)
    {
        using ReadOnlyBuffer<Float4> fa = device.AllocateReadOnlyBuffer<Float4>(inputs.FloatA), fb = device.AllocateReadOnlyBuffer<Float4>(inputs.FloatB);
        using ReadOnlyBuffer<Half4> ha = device.AllocateReadOnlyBuffer<Half4>(inputs.HalfA), hb = device.AllocateReadOnlyBuffer<Half4>(inputs.HalfB);
        using ReadOnlyBuffer<Int4> ia = device.AllocateReadOnlyBuffer<Int4>(inputs.IntA), ib = device.AllocateReadOnlyBuffer<Int4>(inputs.IntB);
        using ReadWriteBuffer<uint> masks = device.AllocateReadWriteBuffer<uint>(Count * VectorMasks.Stride);
        using ReadWriteBuffer<UInt4> selectedBits = device.AllocateReadWriteBuffer<UInt4>(Count * VectorMasks.SelectStride);
        using ReadWriteBuffer<Half4> selectedHalves = device.AllocateReadWriteBuffer<Half4>(Count * 2);
        var shader = new VectorMasks(fa, fb, ha, hb, ia, ib, masks, selectedBits, selectedHalves);

        if (onGpu)
        {
            device.For(Count, shader);
        }
        else
        {
            device.ForCpu(Count, shader);
        }

        return new Outputs(masks.ToArray(), selectedBits.ToArray(), selectedHalves.ToArray());
    }

    [MetalFact]
    public void ComparisonsLogicAndSelect_AreBitExact_GpuVsCpu_AnyNaNForAnyNaN()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Inputs inputs = CreateInputs();
        Outputs gpu = Run(device, inputs, onGpu: true);
        Outputs cpu = Run(device, inputs, onGpu: false);

        string[] masks = Enumerable.Range(0, cpu.Masks.Length)
            .Where(i => cpu.Masks[i] != gpu.Masks[i] && !(i % VectorMasks.Stride == FloatBitsMask && FloatBitsMaskHoldsANaN(inputs, i / VectorMasks.Stride)))
            .Select(i => $"thread {i / VectorMasks.Stride}, mask {i % VectorMasks.Stride}: CPU 0x{cpu.Masks[i]:X}, GPU 0x{gpu.Masks[i]:X}")
            .ToArray();
        Assert.True(masks.Length == 0, $"{masks.Length} masks differ:\n" + string.Join("\n", masks.Take(30)));

        uint[] cpuBits = MemoryMarshal.Cast<UInt4, uint>(cpu.SelectedBits.AsSpan()).ToArray(), gpuBits = MemoryMarshal.Cast<UInt4, uint>(gpu.SelectedBits.AsSpan()).ToArray();
        string[] selected = Enumerable.Range(0, cpuBits.Length)
            .Where(i => FloatSelects.Contains(i / 4 % VectorMasks.SelectStride) ? !SameFloat(cpuBits[i], gpuBits[i]) : cpuBits[i] != gpuBits[i])
            .Select(i => $"thread {i / 4 / VectorMasks.SelectStride}, select {i / 4 % VectorMasks.SelectStride}, component {i % 4}: CPU 0x{cpuBits[i]:X8}, GPU 0x{gpuBits[i]:X8}")
            .ToArray();
        Assert.True(selected.Length == 0, $"{selected.Length} selected components differ:\n" + string.Join("\n", selected.Take(30)));

        ushort[] cpuHalves = MemoryMarshal.Cast<Half4, ushort>(cpu.SelectedHalves.AsSpan()).ToArray(), gpuHalves = MemoryMarshal.Cast<Half4, ushort>(gpu.SelectedHalves.AsSpan()).ToArray();
        string[] halves = Enumerable.Range(0, cpuHalves.Length)
            .Where(i => !SameHalf(cpuHalves[i], gpuHalves[i]))
            .Select(i => $"thread {i / 8}, select {i / 4 % 2}, component {i % 4}: CPU 0x{cpuHalves[i]:X4}, GPU 0x{gpuHalves[i]:X4}")
            .ToArray();
        Assert.True(halves.Length == 0, $"{halves.Length} selected halves differ:\n" + string.Join("\n", halves.Take(30)));

        // The allowance is used: NaNs are selected, floats and halves, and arrive as NaNs on both sides.
        Assert.Contains(Enumerable.Range(0, cpuBits.Length), i => FloatSelects.Contains(i / 4 % VectorMasks.SelectStride) && IsNaN(cpuBits[i]) && IsNaN(gpuBits[i]));
        Assert.Contains(Enumerable.Range(0, cpuHalves.Length), i => IsNaN(cpuHalves[i]) && IsNaN(gpuHalves[i]));
    }

    /// <summary>The CPU side (and with the test above the GPU side) is what the definitions say, restated with scalar C#.</summary>
    [MetalFact]
    public void ComparisonsLogicAndSelect_MatchScalarCSharp()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Inputs inputs = CreateInputs();
        Outputs gpu = Run(device, inputs, onGpu: true);

        static float[] F(Float4 v) => [v.X, v.Y, v.Z, v.W];
        static Half[] H(Half4 v) => [v.X, v.Y, v.Z, v.W];
        static int[] I(Int4 v) => [v.X, v.Y, v.Z, v.W];
        static uint Mask(Func<int, bool> component) => (component(0) ? 1u : 0u) | (component(1) ? 2u : 0u) | (component(2) ? 4u : 0u) | (component(3) ? 8u : 0u);

        var seen = new HashSet<uint>();

        for (int t = 0; t < Count; t++)
        {
            float[] fa = F(inputs.FloatA[t]), fb = F(inputs.FloatB[t]);
            Half[] ha = H(inputs.HalfA[t]), hb = H(inputs.HalfB[t]);
            int[] ia = I(inputs.IntA[t]), ib = I(inputs.IntB[t]);
            ReadOnlySpan<uint> o = gpu.Masks.AsSpan(t * VectorMasks.Stride, VectorMasks.Stride);
            string at = $"thread {t}";

            // Float.
            Assert.True(Mask(c => fa[c] < fb[c]) == o[0], at);
            Assert.True(Mask(c => fa[c] <= fb[c]) == o[1], at);
            Assert.True(Mask(c => fa[c] > fb[c]) == o[2], at);
            Assert.True(Mask(c => fa[c] >= fb[c]) == o[3], at);
            Assert.True(Mask(c => fa[c] == fb[c]) == o[4], at);
            Assert.True(Mask(c => fa[c] != fb[c]) == o[5], at);
            Assert.True(Mask(c => fa[c] < fb[0]) == o[6], at);
            Assert.True(Mask(c => fa[0] >= fb[c]) == o[7], at);
            Assert.True(Mask(c => fa[c] == fb[0]) == o[8], at);
            Assert.True(o[1] == o[9] && o[5] == o[10], at);

            // Half.
            Assert.True(Mask(c => ha[c] < hb[c]) == o[11], at);
            Assert.True(Mask(c => ha[c] <= hb[c]) == o[12], at);
            Assert.True(Mask(c => ha[c] > hb[c]) == o[13], at);
            Assert.True(Mask(c => ha[c] >= hb[c]) == o[14], at);
            Assert.True(Mask(c => ha[c] == hb[c]) == o[15], at);
            Assert.True(Mask(c => ha[c] != hb[c]) == o[16], at);
            Assert.True(Mask(c => ha[c] > hb[0]) == o[17], at);
            Assert.True(Mask(c => ha[0] <= hb[c]) == o[18], at);
            Assert.True(Mask(c => ha[c] != hb[0]) == o[19], at);
            Assert.True(o[14] == o[20] && o[15] == o[21], at);

            // Int, and the same bits as uint.
            Assert.True(Mask(c => ia[c] < ib[c]) == o[22], at);
            Assert.True(Mask(c => ia[c] <= ib[c]) == o[23], at);
            Assert.True(Mask(c => ia[c] > ib[c]) == o[24], at);
            Assert.True(Mask(c => ia[c] >= ib[c]) == o[25], at);
            Assert.True(Mask(c => ia[c] == ib[c]) == o[26], at);
            Assert.True(Mask(c => ia[c] != ib[c]) == o[27], at);
            Assert.True(Mask(c => ia[c] < ib[0]) == o[28], at);
            Assert.True(Mask(c => ia[0] >= ib[c]) == o[29], at);
            Assert.True(o[24] == o[30], at);
            Assert.True(Mask(c => (uint)ia[c] < (uint)ib[c]) == o[31], at);
            Assert.True(Mask(c => (uint)ia[c] <= (uint)ib[c]) == o[32], at);
            Assert.True(Mask(c => (uint)ia[c] > (uint)ib[c]) == o[33], at);
            Assert.True(Mask(c => (uint)ia[c] >= (uint)ib[c]) == o[34], at);
            Assert.True(Mask(c => ia[c] == ib[c]) == o[35], at);
            Assert.True(Mask(c => (uint)ia[c] != (uint)ib[0]) == o[36], at);

            // Classification.
            Assert.True((Mask(c => float.IsNaN(fa[c])) | (Mask(c => float.IsInfinity(fa[c])) << 4) | (Mask(c => float.IsFinite(fa[c])) << 8)) == o[37], at);
            Assert.True((Mask(c => Half.IsNaN(ha[c])) | (Mask(c => Half.IsInfinity(ha[c])) << 4) | (Mask(c => Half.IsFinite(ha[c])) << 8)) == o[38], at);
            Assert.True((Mask(c => c < 2 && float.IsNaN(fa[c])) | (Mask(c => c < 3 && Half.IsInfinity(ha[c + 1])) << 4) | (Mask(c => Half.IsFinite(ha[c])) << 8)) == o[39], at);

            // Logic.
            bool[] m = [ia[0] < ib[0], ia[1] < ib[1], ia[2] < ib[2], ia[3] < ib[3]];
            bool[] n = [ha[0] != hb[0], ha[1] != hb[1], ha[2] != hb[2], ha[3] != hb[3]];
            Assert.True(Mask(c => m[c] & n[c]) == o[40], at);
            Assert.True(Mask(c => m[c] | n[c]) == o[41], at);
            Assert.True(Mask(c => m[c] ^ n[c]) == o[42], at);
            Assert.True(Mask(c => !m[c]) == o[43], at);
            Assert.True((Mask(c => c < 2 && m[c] & !n[c + 2]) | (Mask(c => c < 3 && m[c] ^ n[c + 1]) << 4)) == o[44], at);

            // Any and All.
            uint reductions =
                (m.Any(b => b) ? 1u : 0u) | (m.All(b => b) ? 2u : 0u) |
                (m.Take(3).Any(b => b) ? 4u : 0u) | (m.Take(3).All(b => b) ? 8u : 0u) |
                (m.Skip(2).Any(b => b) ? 16u : 0u) | (m.Skip(2).All(b => b) ? 32u : 0u);
            Assert.True(reductions == o[45], at);
            Assert.True((Enumerable.Range(0, 4).All(c => !float.IsNaN(fa[c]) && !float.IsNaN(fb[c])) ? 1u : 0u) == o[46], at); // a < b | a >= b: false only for NaN
            seen.Add(reductions);

            // Select.
            Assert.True(Mask(c => m[c] ? n[c] : !n[c]) == o[47], at);
            Assert.True((Mask(c => c < 3 && (m[c] ? n[c] : n[c + 1])) | (Mask(c => c < 2 && (m[c + 2] ? n[c] : m[c])) << 4)) == o[48], at);
            Assert.True((uint)((n[0] ? ia[0] : ib[0]) ^ (n[1] ? ia[1] : ib[1]) ^ (n[2] ? ia[2] : ib[2])) == o[49], at);
            Assert.True((uint)((m[1] ? ia[2] : ib[0]) ^ (m[2] ? ia[3] : ib[1])) == o[50], at);
            Assert.True(FloatBitsMaskHoldsANaN(inputs, t) || (BitConverter.SingleToUInt32Bits(n[0] ? fa[0] : fb[2]) ^ BitConverter.SingleToUInt32Bits(n[3] ? fa[1] : fb[3])) == o[FloatBitsMask], at);

            ReadOnlySpan<UInt4> s = gpu.SelectedBits.AsSpan(t * VectorMasks.SelectStride, VectorMasks.SelectStride);
            ReadOnlySpan<Half4> sh = gpu.SelectedHalves.AsSpan(t * 2, 2);

            for (int c = 0; c < 4; c++)
            {
                static uint Component(UInt4 v, int c) => c switch { 0 => v.X, 1 => v.Y, 2 => v.Z, _ => v.W };

                Assert.True(SameFloat(m[c] ? fa[c] : fb[c], Component(s[0], c)), $"{at}, component {c}: Select of floats");
                Assert.True((uint)(n[c] ? ia[c] : ib[c]) == Component(s[1], c), $"{at}, component {c}: Select of ints");
                Assert.True((uint)(m[c] ^ n[c] ? ia[c] : ib[c]) == Component(s[2], c), $"{at}, component {c}: Select of uints");
                Assert.True(SameFloat(fa[c] > fb[c] ? fa[c] : fb[c], Component(s[3], c)), $"{at}, component {c}: Select from buffers");
                Assert.True(SameFloat(float.IsNaN(fa[c]) ? 0f : fa[c], Component(s[4], c)), $"{at}, component {c}: Select on IsNaN");

                Half[] first = H(sh[0]), second = H(sh[1]);
                Assert.True(SameHalf(m[c] ? ha[c] : hb[c], first[c]), $"{at}, component {c}: Select of halves");
                Assert.True(SameHalf(ha[c] < hb[c] ? ha[c] : hb[c], second[c]), $"{at}, component {c}: Select of halves from buffers");
            }
        }

        // The inputs reach every combination of Any and All that can occur for the full mask.
        Assert.Contains(seen, r => (r & 3) == 0);
        Assert.Contains(seen, r => (r & 3) == 1);
        Assert.Contains(seen, r => (r & 3) == 3);

        // A NaN that is selected arrives as a NaN on the GPU too (its sign and payload are not asked
        // for), and the number beside it arrives when the mask says so.
        uint firstSelected = gpu.SelectedBits[0].X;
        Assert.True(inputs.IntA[0].X < inputs.IntB[0].X ? IsNaN(firstSelected) : firstSelected == 0x40000000u, $"0x{firstSelected:X8}");
    }
}
