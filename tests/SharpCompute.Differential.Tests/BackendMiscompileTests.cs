using System;
using System.Linq;
using System.Numerics;
using SharpCompute.Tests;

namespace SharpCompute.Differential.Tests;

/// <summary>
/// Regression tests for the three integer miscompiles of Apple's GPU compiler that the generator
/// works around (see MslTranslator.IntegerDivision and MslTranslator.BitCounts): every shape the
/// workarounds were measured on, GPU against <c>ForCpu</c>, over several threads of inputs read from
/// a buffer, so nothing is folded at build time. The minimal repros are in
/// <see cref="KnownDifferencesTests"/>.
/// </summary>
[Trait("Category", "Differential")]
public class BackendMiscompileTests
{
    // Divisors: small ones that divide 2^32 - 1 (3, 5, 17, 257, 65537) and ones that do not, both
    // sides of 2^31, and divisors at and above the constant dividends.
    private static readonly uint[] Divisors =
        [1, 2, 3, 5, 7, 15, 17, 255, 257, 1000, 65535, 65536, 65537, 123457, 0x7FFFFFFF, 0x80000000, 0xFFFFFF7F, 0xFFFFFF80, 0xFFFFFF81, 0xFFFFFFFE, 0xFFFFFFFF];

    [MetalFact]
    public void ConstantDividendsNear2To32_DivideAsWithForCpu()
    {
        ulong[] input = Divisors.Select(static d => (ulong)d).ToArray();
        AssertSame(input, input.Length, NearMaxDividends.Slots, static (d, cpu, n, i, o) => Dispatch(d, cpu, n, new NearMaxDividends(i, o)));
    }

    [MetalFact]
    public void GuardedDivisions_KeepTheirGuardAsWithForCpu()
    {
        // Rows of (x, l, d): l hits each guard (5, long.MinValue, 0) and misses it; d is zero or not.
        ulong[] input =
        [
            2, 5, 0,
            unchecked((ulong)-9223372036854775807L), 5, 7,
            2, unchecked((ulong)long.MinValue), 0,
            unchecked((ulong)-9223372036854775807L), unchecked((ulong)long.MinValue), 3,
            100, 0, 0,
            100, unchecked((ulong)-6L), unchecked((ulong)-4L),
            12345678901, 7, 1,
            unchecked((ulong)-3L), 1, 100,
        ];

        AssertSame(input, input.Length / 3, GuardedDivisions.Slots, static (d, cpu, n, i, o) => Dispatch(d, cpu, n, new GuardedDivisions(i, o)));
    }

    [MetalFact]
    public void BitCountsOfNarrowedValues_CountAsWithForCpu()
    {
        // Rows of (a, b, c): 16- and 8-bit values of both signs in the low bits, junk above them.
        ulong[] input =
        [
            0xFFFE, 0x00FF, 1,
            0x1234FFFE, 0xFFFF80FF, 0,
            0x8001, 0x7F, 1,
            0x80, 0xFE, 0,
            0xFFFFFFFFFFFFFF9C, 0x35, 1,
            3, 0xFFF0, 0,
            0x7FFF, 0x8000, 1,
            0xD0, 0x35, 0,
            0xFFFFFFFFFFFFFFFF, 0xFFFFFFFFFFFFFFFE, 1,
            0, 0, 0,
        ];

        AssertSame(input, input.Length / 3, NarrowedBitCounts.Slots, static (d, cpu, n, i, o) => Dispatch(d, cpu, n, new NarrowedBitCounts(i, o)));
    }

    private static void Dispatch<TShader>(GraphicsDevice device, bool cpu, int threads, in TShader shader)
        where TShader : struct, IComputeShader, IComputeShaderDescriptor<TShader> => DifferentialHarness.Dispatch(device, cpu, threads, shader);

    private static void AssertSame(ulong[] input, int threads, int slots, Action<GraphicsDevice, bool, int, ReadOnlyBuffer<ulong>, ReadWriteBuffer<long>> dispatch)
    {
        long[] gpu = Run(input, threads * slots, (d, i, o) => dispatch(d, false, threads, i, o));
        long[] cpu = Run(input, threads * slots, (d, i, o) => dispatch(d, true, threads, i, o));

        for (int k = 0; k < gpu.Length; k++)
        {
            Assert.True(cpu[k] == gpu[k], $"thread {k / slots}, slot {k % slots}: GPU {gpu[k]}, ForCpu {cpu[k]}");
        }
    }

    private static long[] Run(ulong[] input, int length, Action<GraphicsDevice, ReadOnlyBuffer<ulong>, ReadWriteBuffer<long>> dispatch)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<ulong> inputs = device.AllocateReadOnlyBuffer(input);
        using ReadWriteBuffer<long> output = device.AllocateReadWriteBuffer<long>(length);
        dispatch(device, inputs, output);
        return output.ToArray();
    }
}

/// <summary>Constant dividends of 2^32 - 128 and more, divided by each thread's divisor in every form the generator recognizes.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct NearMaxDividends(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 24;

    private const uint Constant = 0xFFFFFFF0u;
    private static readonly uint ReadOnlyMax = uint.MaxValue;

    public void Execute()
    {
        int t = ThreadIds.X;
        uint x = (uint)input[t];
        long y = x;
        UInt3 v = new(x, x | 1u, x >> 1 | 1u);
        int o = t * Slots;

        output[o + 0] = 4294967295u / x;
        output[o + 1] = 4294967295u % x;
        output[o + 2] = 0xFFFFFF80u / x;
        output[o + 3] = 0xFFFFFF80u % x;
        output[o + 4] = 0xFFFFFFFEu / Gpu.Max(x, 1u);
        output[o + 5] = (uint.MaxValue - 5) % Gpu.Max(x, 1u);
        output[o + 6] = Constant / x;
        output[o + 7] = ReadOnlyMax % x;
        output[o + 8] = 4294967295L % y;
        output[o + 9] = 4294967295L / y;
        output[o + 10] = -4294967295L / y;
        output[o + 11] = -4294967200L % y;
        output[o + 12] = 4294967295L / -y;
        output[o + 13] = 4294967295L % -y;
        output[o + 14] = (long)(4294967295UL / (ulong)y);
        output[o + 15] = (long)(0xFFFFFFA0UL % (ulong)y);

        UInt3 q = new UInt3(uint.MaxValue, 0xFFFFFFF0u, 7u) / v;
        UInt3 r = new UInt3(0xFFFFFFFEu) % v;
        output[o + 16] = q.X;
        output[o + 17] = q.Y;
        output[o + 18] = q.Z;
        output[o + 19] = r.X;
        output[o + 20] = r.Y;
        output[o + 21] = r.Z;

        // Not near 2^32: the native operator, through a nonzero divisor.
        output[o + 22] = 0x7FFFFFFFu / x;
        output[o + 23] = 4294967296L / y;
    }
}

/// <summary>Divisions behind a guard whose divisor comes from a ternary, which the GPU compiler hoisted past the guard.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct GuardedDivisions(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 16;

    public void Execute()
    {
        int t = ThreadIds.X;
        long x = (long)input[3 * t];
        long l = (long)input[3 * t + 1];
        int d = (int)input[3 * t + 2];
        int o = t * Slots;

        long a = l == long.MinValue ? 0 : Math.Abs(l);
        output[o + 0] = a;
        output[o + 1] = a == 0 ? 0 : x % a;
        output[o + 2] = a == 0 ? 0 : x / a;

        long b = l == 5 ? 0 : Math.Abs(l == long.MinValue ? 1 : l);
        output[o + 3] = b;
        output[o + 4] = b == 0 ? 0 : x % b;
        output[o + 5] = b != 0 ? x / b : 0;

        long r = 0;
        if (b != 0)
        {
            r = x % b;
        }

        output[o + 6] = r;

        long s = -1;
        if (a != 0)
        {
            s = x;
            s %= a;
        }

        output[o + 7] = s;

        int xi = (int)x;
        int li = (int)(l >> 32);
        int c = li == int.MinValue ? 0 : Math.Abs(li);
        output[o + 8] = c;
        output[o + 9] = c == 0 ? 0 : xi % c;

        ulong u = l == 5 ? 0UL : (ulong)Math.Abs(l == long.MinValue ? 1 : l);
        output[o + 10] = (long)u;
        output[o + 11] = (long)(u == 0 ? 0 : (ulong)x % u);

        // A chain of 32-bit divisions, each behind its own guard.
        int e = d == 0 ? 0 : xi / d;
        output[o + 12] = e;
        output[o + 13] = e == 0 ? 0 : 100 % e;
        uint f = d == 0 ? 0u : (uint)xi / (uint)d;
        output[o + 14] = f;
        output[o + 15] = f == 0 ? 0 : 1000u / f;
    }
}

/// <summary>Bit counts of values the GPU compiler holds as extensions of 8- and 16-bit ones.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct NarrowedBitCounts(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 24;

    public void Execute()
    {
        int t = ThreadIds.X;
        ulong a = input[3 * t];
        ulong b = input[3 * t + 1];
        bool c = input[3 * t + 2] != 0;
        int o = t * Slots;

        short sa = (short)a;
        short sb = (short)b;
        int i = (int)a;
        int y = (sbyte)b;
        int z = (sbyte)a;

        // 16-bit counts of a value computed in a wider register.
        output[o + 0] = Gpu.CountBits(c ? sa : sb);
        output[o + 1] = Gpu.CountBits(c ? (ushort)a : (ushort)b);
        output[o + 2] = Gpu.CountBits(c ? (sbyte)a : (sbyte)b);
        output[o + 3] = Gpu.LeadingZeroCount(c ? sa : sb);

        // 32-bit counts of a sign-extended 16-bit value.
        output[o + 4] = Gpu.CountBits((int)sa);
        output[o + 5] = Gpu.LeadingZeroCount((int)sa);
        output[o + 6] = Gpu.CountBits((uint)(short)i);
        output[o + 7] = Gpu.LeadingZeroCount((uint)(short)i);
        output[o + 8] = Gpu.CountBits((i << 16) >> 16);
        output[o + 9] = Gpu.LeadingZeroCount((i << 16) >> 16);
        output[o + 10] = Gpu.CountBits(c ? (int)sa : (int)sb);
        output[o + 11] = Gpu.LeadingZeroCount(c ? (int)sa : (int)sb);
        output[o + 12] = Gpu.CountBits(z % (y == 0 ? 1 : y));
        output[o + 13] = Gpu.LeadingZeroCount(y == 0 ? 0 : z % y);
        output[o + 14] = BitOperations.PopCount((uint)sa);
        output[o + 15] = BitOperations.LeadingZeroCount((uint)sa);

        // 64-bit counts.
        output[o + 16] = Gpu.CountBits((long)sa);
        output[o + 17] = (long)Gpu.CountBits((ulong)(long)sb);
        output[o + 18] = Gpu.LeadingZeroCount((long)sa);
        output[o + 19] = BitOperations.PopCount((ulong)(long)sa);

        // Vectors of sign-extended lanes.
        Int3 v = new(sa, sb, (short)(a ^ b));
        output[o + 20] = Gpu.CountBits(v).X;
        output[o + 21] = Gpu.LeadingZeroCount(v).Y;
        UInt3 w = new((uint)sa, (uint)sb, (uint)(short)(a ^ b));
        output[o + 22] = Gpu.CountBits(w).Z;
        output[o + 23] = Gpu.LeadingZeroCount(w).X;
    }
}
