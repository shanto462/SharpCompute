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

    [MetalTheory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(4096)]
    public void GuardedDivisions_KeepTheirGuardAsWithForCpu(int threads)
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

        // One thread a row alone, or the rows repeated over 64 and 4096 threads (SIMD groups whose
        // lanes take different sides of the guards).
        foreach (ulong[] rows in Spread(input, 3, threads))
        {
            AssertSame(rows, rows.Length / 3, GuardedDivisions.Slots, static (d, cpu, n, i, o) => Dispatch(d, cpu, n, new GuardedDivisions(i, o)));
        }
    }

    /// <summary>
    /// Each row of <paramref name="rows"/> (of <paramref name="width"/> values) alone for one thread,
    /// or all of them repeated over <paramref name="threads"/> threads.
    /// </summary>
    private static ulong[][] Spread(ulong[] rows, int width, int threads)
    {
        int count = rows.Length / width;

        if (threads == 1)
        {
            return Enumerable.Range(0, count).Select(r => rows.Skip(r * width).Take(width).ToArray()).ToArray();
        }

        return [Enumerable.Range(0, threads).SelectMany(t => rows.Skip(t % count * width).Take(width)).ToArray()];
    }

    [MetalTheory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(4096)]
    public void GuardChains_InLanesThatDivideByDifferentValues_KeepTheirGuards(int threads)
    {
        // So that the lanes of a SIMD group take both sides of each guard: rows of (a, b, c) with b
        // zero in every third row. Found by the random differential test, where the guards held for
        // one thread alone and were dropped for a SIMD group of different divisors.
        ulong[] input = new ulong[64 * 3];
        for (int t = 0; t < 64; t++)
        {
            input[3 * t] = (ulong)(8 + t * 37);
            input[3 * t + 1] = t % 3 == 0 ? 0 : (ulong)(t % 7 + 1);
            input[3 * t + 2] = unchecked((ulong)(t % 2 == 0 ? -8L : t * 1000003L));
        }

        foreach (ulong[] rows in Spread(input, 3, threads == 1 ? 1 : threads))
        {
            AssertSame(rows, rows.Length / 3, GuardChains.Slots, static (d, cpu, n, i, o) => Dispatch(d, cpu, n, new GuardChains(i, o)));
        }
    }

    [MetalTheory]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(4096)]
    public void ConstantDividends_ByDivisorsTheFrontEndOnlyFoldsLate_DivideAsWithForCpu(int threads)
    {
        // A constant divided by a divisor that is a constant only once the front end has unrolled a
        // loop, or that only the GPU compiler folds (Clamp, Max, TrailingZeroCount of constants):
        // the GPU compiler folds such a division of constants wrongly for most even divisors when
        // the divisor's barrier hides the constant behind reverse_bits (it is plain arithmetic now).
        ulong[] input = Enumerable.Range(0, threads).Select(static t => (ulong)(t * 2654435761u)).ToArray();
        AssertSame(input, threads, LateConstantDivisors.Slots, static (d, cpu, n, i, o) => Dispatch(d, cpu, n, new LateConstantDivisors(i, o)));
    }

    [MetalFact]
    public void ConstantsDividedByConstantsTheFrontEndFolds_DivideAsWithForCpu()
    {
        // The front end folds these divisions (the divisors are constants once it has optimized):
        // a nonzero barrier on the divisor would leave them to the GPU compiler, which folds them
        // wrongly for most even divisors. Found by the random differential test.
        ulong[] input = [0, 1, 7, 12345];
        AssertSame(input, input.Length, FoldedDivisions.Slots, static (d, cpu, n, i, o) => Dispatch(d, cpu, n, new FoldedDivisions(i, o)));
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
    public const int Slots = 27;

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

        // Constant locals that are never written.
        uint m = uint.MaxValue;
        long n = -4294967200L;
        UInt3 w = new(0xFFFFFF80u, 3u, uint.MaxValue);
        output[o + 24] = m / x;
        output[o + 25] = n % y;
        output[o + 26] = (w / v).Z;
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

/// <summary>Chains of guarded divisions, shrunk from the random differential test.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct GuardChains(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 5;

    public void Execute()
    {
        int t = ThreadIds.X;
        ulong a = input[3 * t];
        ushort b = (ushort)input[3 * t + 1];
        long c = (long)input[3 * t + 2];
        int o = t * Slots;

        ulong r0 = a;
        ulong d0 = b;
        ulong q0 = d0 == 0 ? 0 : a % d0;
        if (q0 != 0)
        {
            r0 %= q0;
        }

        output[o + 0] = (long)r0;

        uint d1 = b;
        uint q1 = d1 == 0 ? 0u : 65536u / d1;
        output[o + 1] = q1 == 0 ? 0u : (uint)a % q1;

        ulong q2 = b == 0 ? 0 : (ulong)(ushort)a / b;
        output[o + 2] = (long)(q2 == 0 ? 0 : (ulong)c % q2);

        long d3 = (int)b;
        long q3 = c == 0 || (c == long.MinValue && c == -1) ? 0 : c / c;
        long q4 = d3 == 0 ? 0 : (long)a / d3;
        output[o + 3] = q4 == 0 || (q3 == long.MinValue && q4 == -1) ? 0 : q3 / q4;

        ushort u = (ushort)a;
        if (b != 0)
        {
            u /= b;
        }

        output[o + 4] = u;
    }
}

/// <summary>Divisions of constants by constants that only the front end can see as such.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct FoldedDivisions(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 8;

    public void Execute()
    {
        int t = ThreadIds.X;
        ushort u = (ushort)input[t];
        int o = t * Slots;

        int a = 1431655765;
        int two = 2;
        if (two != 0)
        {
            a %= two;
        }

        output[o + 0] = a;

        uint b = 2863311530u;
        uint d = false ? u : (ushort)2;
        output[o + 1] = d == 0 ? 0 : b % d;
        output[o + 2] = d == 0 ? 0 : b / d;
        uint e = false ? u : 12u;
        output[o + 3] = b / e;
        output[o + 4] = b % e;
        long f = 1431655765L;
        long g = false ? u : 6L;
        output[o + 5] = f / g;
        UInt3 h = new(2863311530u, 1431655765u, 100u);
        UInt3 k = new(false ? u : 2u, 4u, 6u);
        UInt3 q = h % k;
        output[o + 6] = q.X;
        output[o + 7] = q.Y + q.Z;
    }
}

/// <summary>Constant dividends by divisors that become constants late (see the test).</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct LateConstantDivisors(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 26;

    public void Execute()
    {
        int t = ThreadIds.X;
        uint x = (uint)input[t];
        int o = t * Slots;

        // The loop of the review: an unrolled 1..8, even and odd divisors.
        uint acc = 0;
        for (uint k = 1; k <= 8; k++)
        {
            acc = (acc * 31u) + (1000000007u % k);
        }

        output[o + 0] = acc;

        // Every dividend of the review, written in the division, by the even and odd divisors of the loop.
        uint f1 = 0;
        for (uint k = 1; k <= 8; k++)
        {
            f1 = (f1 * 31u) + (123456789u % k) + (123456789u / k);
        }

        output[o + 1] = f1;
        uint f2 = 0;
        for (uint k = 1; k <= 8; k++)
        {
            f2 = (f2 * 31u) + (1000000007u % k) + (1000000007u / k);
        }

        output[o + 2] = f2;
        uint f3 = 0;
        for (uint k = 1; k <= 8; k++)
        {
            f3 = (f3 * 31u) + (0x9E3779B9u % k) + (0x9E3779B9u / k);
        }

        output[o + 3] = f3;
        uint f4 = 0;
        for (uint k = 1; k <= 8; k++)
        {
            f4 = (f4 * 31u) + (0xAAAAAAAAu % k) + (0xAAAAAAAAu / k);
        }

        output[o + 4] = f4;
        uint f5 = 0;
        for (uint k = 1; k <= 8; k++)
        {
            f5 = (f5 * 31u) + (3000000000u % k) + (3000000000u / k);
        }

        output[o + 5] = f5;
        uint f6 = 0;
        for (uint k = 1; k <= 8; k++)
        {
            f6 = (f6 * 31u) + (4000000000u % k) + (4000000000u / k);
        }

        output[o + 6] = f6;
        uint f7 = 0;
        for (uint k = 1; k <= 8; k++)
        {
            f7 = (f7 * 31u) + (0xFFFF0000u % k) + (0xFFFF0000u / k);
        }

        output[o + 7] = f7;
        uint f8 = 0;
        for (uint k = 1; k <= 8; k++)
        {
            f8 = (f8 * 31u) + (16777216u % k) + (16777216u / k);
        }

        output[o + 8] = f8;
        uint f9 = 0;
        for (uint k = 1; k <= 8; k++)
        {
            f9 = (f9 * 31u) + (16777217u % k) + (16777217u / k);
        }

        output[o + 9] = f9;
        uint f10 = 0;
        for (uint k = 1; k <= 8; k++)
        {
            f10 = (f10 * 31u) + (0x7FFFFFFFu % k) + (0x7FFFFFFFu / k);
        }

        output[o + 10] = f10;
        uint f11 = 0;
        for (uint k = 1; k <= 8; k++)
        {
            f11 = (f11 * 31u) + (0x80000000u % k) + (0x80000000u / k);
        }

        output[o + 11] = f11;
        uint f12 = 0;
        for (uint k = 1; k <= 8; k++)
        {
            f12 = (f12 * 31u) + (7u % k) + (7u / k);
        }

        output[o + 12] = f12;

        int sacc = 0;
        for (int k = 1; k <= 8; k++)
        {
            sacc = (sacc * 31) + (-1431655766 % k) + (-1431655766 / k) + (100000007 % k) + (-100000007 / k);
        }

        output[o + 13] = sacc;

        // Divisors that only the GPU compiler folds.
        output[o + 14] = 1000000007u % Gpu.Clamp(x, 2u, 2u);
        output[o + 15] = 0xAAAAAAAAu / Gpu.Max(x & 0u, 4u);
        output[o + 16] = 0xAAAAAAAAu % Gpu.TrailingZeroCount(4u);
        output[o + 17] = 123456789u / Gpu.Clamp(x, 6u, 6u);
        output[o + 18] = 3000000000u % Gpu.Max(x & 0u, 3u);
        output[o + 19] = -1431655766 % Gpu.Clamp((int)x, 2, 2);

        // 64-bit constants, which were right, and a variable dividend.
        long lacc = 0;
        for (long k = 1; k <= 8; k++)
        {
            lacc = (lacc * 31L) + (-1000000000000007L % k) + (long)(4000000000UL / (ulong)k);
        }

        output[o + 20] = lacc;
        uint vacc = 0;
        for (uint k = 1; k <= 8; k++)
        {
            vacc = (vacc * 31u) + ((x | 0x80000001u) % k);
        }

        output[o + 21] = vacc;

        // A divisor computed in a loop of constants, and one from a constant local.
        uint d = 1;
        for (int k = 0; k < 3; k++)
        {
            d *= 2;
        }

        output[o + 22] = 0x9E3779B9u % d;
        uint e = 6;
        output[o + 23] = 4000000000u / e;
        output[o + 24] = 0xFFFF0000u % (e + 4u);
        output[o + 25] = (long)(0xAAAAAAAAu / (uint)(x % 1u + 2u));
    }
}
