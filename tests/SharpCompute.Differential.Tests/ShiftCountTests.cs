using System;
using System.Linq;
using SharpCompute.Tests;

namespace SharpCompute.Differential.Tests;

/// <summary>
/// Shifts by counts beyond the width, GPU against <c>ForCpu</c>: C# masks the count with 31 (63 for
/// the 64-bit types) after promoting an 8- or 16-bit operand to <c>int</c>, in compound assignments
/// as in operators. The generator masks a constant count itself and leaves a variable one to MSL,
/// which takes it modulo the width of the promoted operand.
/// </summary>
[Trait("Category", "Differential")]
public class ShiftCountTests
{
    [MetalFact]
    public void CompoundShiftsOfNarrowTargets_MaskTheCountAsCSharpDoes()
    {
        long[] values = [3, -2, short.MinValue + 1, 0x7FFF, 0x1234, -0x5678, 0xFF, 0x80];
        int[] counts = [0, 1, 15, 16, 17, 31, 32, 33, 1431655765, -1, -31, int.MinValue];
        ulong[] input = values.SelectMany(v => counts.Select(c => new[] { unchecked((ulong)v), unchecked((ulong)(uint)c) })).SelectMany(static r => r).ToArray();
        int threads = input.Length / 2;

        long[] gpu = Run(input, threads, false);
        long[] cpu = Run(input, threads, true);

        for (int k = 0; k < gpu.Length; k++)
        {
            Assert.True(cpu[k] == gpu[k], $"thread {k / NarrowShifts.Slots}, slot {k % NarrowShifts.Slots}: GPU {gpu[k]}, ForCpu {cpu[k]}");
        }
    }

    private static long[] Run(ulong[] input, int threads, bool cpu)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<ulong> inputs = device.AllocateReadOnlyBuffer(input);
        using ReadWriteBuffer<long> output = device.AllocateReadWriteBuffer<long>(threads * NarrowShifts.Slots);
        using ReadWriteBuffer<short> shorts = device.AllocateReadWriteBuffer<short>(threads);
        DifferentialHarness.Dispatch(device, cpu, threads, new NarrowShifts(inputs, output, shorts));
        return output.ToArray();
    }
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct NarrowShifts(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output, ReadWriteBuffer<short> shorts) : IComputeShader
{
    public const int Slots = 22;

    public void Execute()
    {
        int t = ThreadIds.X;
        ulong v = input[2 * t];
        int n = (int)input[2 * t + 1];
        int o = t * Slots;

        short s = (short)v;
        ushort us = (ushort)v;
        sbyte sb = (sbyte)v;
        byte by = (byte)v;
        int i = (int)v;
        long l = (long)v;

        short s0 = s; s0 >>= 1431655765; output[o + 0] = s0;
        short s1 = s; s1 <<= 33; output[o + 1] = s1;
        short s2 = s; s2 >>>= 17; output[o + 2] = s2;
        short s3 = s; s3 >>= n; output[o + 3] = s3;
        short s4 = s; s4 <<= n; output[o + 4] = s4;
        short s5 = s; s5 >>>= n; output[o + 5] = s5;
        ushort u0 = us; u0 >>= 17; output[o + 6] = u0;
        ushort u1 = us; u1 <<= -1; output[o + 7] = u1;
        ushort u2 = us; u2 >>= n; output[o + 8] = u2;
        sbyte b0 = sb; b0 >>= 33; output[o + 9] = b0;
        sbyte b1 = sb; b1 >>>= n; output[o + 10] = b1;
        byte b2 = by; b2 <<= 1431655765; output[o + 11] = b2;
        byte b3 = by; b3 >>= n; output[o + 12] = b3;
        int i0 = i; i0 <<= 33; output[o + 13] = i0;
        int i1 = i; i1 >>>= -31; output[o + 14] = i1;
        long l0 = l; l0 <<= 65; output[o + 15] = l0;
        long l1 = l; l1 >>= n; output[o + 16] = l1;

        // A buffer element, a vector lane and the operators.
        shorts[t] = s;
        shorts[t] >>= 1431655765;
        output[o + 17] = shorts[t];
        Int3 w = new(i, i + 1, i + 2);
        w.Y <<= 40;
        output[o + 18] = w.Y;
        output[o + 19] = s << 33;
        output[o + 20] = (short)(s >> 1431655765);
        output[o + 21] = l >>> 70;
    }
}
