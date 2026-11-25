using System;
using System.Linq;
using System.Numerics;

namespace SharpCompute.Vectors.Tests;

/// <summary>
/// CPU semantics of the integer intrinsics for <c>byte</c>, <c>sbyte</c>, <c>short</c>, <c>ushort</c>,
/// <c>long</c> and <c>ulong</c>, and of the rotates. Each is checked against a definition written out
/// bit by bit, so it does not lean on the .NET method the implementation calls. The same operations
/// are compared against the GPU in the runtime tests.
/// </summary>
public class IntegerIntrinsicTests
{
    private static readonly ulong[] Patterns =
    [
        0, 1, 2, 3, 0x7F, 0x80, 0xFF, 0x100, 0x7FFF, 0x8000, 0xFFFF, 0x10000, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF, 0x100000000,
        0x7FFFFFFFFFFFFFFF, 0x8000000000000000, 0xFFFFFFFFFFFFFFFF, 0x0123456789ABCDEF, 0xFEDCBA9876543210,
        0xAAAAAAAAAAAAAAAA, 0x5555555555555555, 0x00FF00FF00FF00FF, 0x8000000000000001, 0x0000000100000000,
    ];

    private static readonly int[] Counts = [0, 1, 3, 7, 8, 9, 15, 16, 17, 31, 32, 33, 63, 64, 65, 100, -1, -8, -9, -64, -65, int.MaxValue, int.MinValue];

    // ---- reference definitions, on the low `bits` bits of a ulong -----------------------------------

    private static ulong Mask(int bits) => bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;

    private static int PopCount(ulong x, int bits) => Enumerable.Range(0, bits).Count(i => ((x >> i) & 1) != 0);

    private static int LeadingZeros(ulong x, int bits) => Enumerable.Range(0, bits).TakeWhile(i => ((x >> (bits - 1 - i)) & 1) == 0).Count();

    private static int TrailingZeros(ulong x, int bits) => Enumerable.Range(0, bits).TakeWhile(i => ((x >> i) & 1) == 0).Count();

    private static ulong Reverse(ulong x, int bits) => Enumerable.Range(0, bits).Aggregate(0UL, (r, i) => r | (((x >> i) & 1) << (bits - 1 - i)));

    private static ulong RotateLeft(ulong x, int count, int bits)
    {
        int n = ((count % bits) + bits) % bits;
        x &= Mask(bits);
        return n == 0 ? x : ((x << n) | (x >> (bits - n))) & Mask(bits);
    }

    private static ulong RotateRight(ulong x, int count, int bits) => RotateLeft(x, -(count % bits), bits);

    // ---- bit functions ----------------------------------------------------------------------------

    [Fact]
    public void BitCounts_AreThoseOfTheTypesOwnWidth()
    {
        foreach (ulong p in Patterns)
        {
            unchecked
            {
                Assert.Equal(PopCount((byte)p, 8), Gpu.CountBits((byte)p));
                Assert.Equal(PopCount((byte)p, 8), Gpu.CountBits((sbyte)p));
                Assert.Equal(PopCount((ushort)p, 16), Gpu.CountBits((short)p));
                Assert.Equal(PopCount((ushort)p, 16), Gpu.CountBits((ushort)p));
                Assert.Equal(PopCount(p, 64), Gpu.CountBits((long)p));
                Assert.Equal((ulong)PopCount(p, 64), Gpu.CountBits(p));

                Assert.Equal(LeadingZeros((byte)p, 8), Gpu.LeadingZeroCount((byte)p));
                Assert.Equal(LeadingZeros((byte)p, 8), Gpu.LeadingZeroCount((sbyte)p));
                Assert.Equal(LeadingZeros((ushort)p, 16), Gpu.LeadingZeroCount((short)p));
                Assert.Equal(LeadingZeros((ushort)p, 16), Gpu.LeadingZeroCount((ushort)p));
                Assert.Equal(LeadingZeros(p, 64), Gpu.LeadingZeroCount((long)p));
                Assert.Equal((ulong)LeadingZeros(p, 64), Gpu.LeadingZeroCount(p));

                Assert.Equal(TrailingZeros((byte)p, 8), Gpu.TrailingZeroCount((byte)p));
                Assert.Equal(TrailingZeros((byte)p, 8), Gpu.TrailingZeroCount((sbyte)p));
                Assert.Equal(TrailingZeros((ushort)p, 16), Gpu.TrailingZeroCount((short)p));
                Assert.Equal(TrailingZeros((ushort)p, 16), Gpu.TrailingZeroCount((ushort)p));
                Assert.Equal(TrailingZeros(p, 64), Gpu.TrailingZeroCount((long)p));
                Assert.Equal((ulong)TrailingZeros(p, 64), Gpu.TrailingZeroCount(p));
            }
        }

        // The width for zero, as MSL's clz and ctz give, and nothing above the sign bit of a negative value.
        Assert.Equal(8, Gpu.LeadingZeroCount((byte)0));
        Assert.Equal(8, Gpu.TrailingZeroCount((sbyte)0));
        Assert.Equal(16, Gpu.LeadingZeroCount((short)0));
        Assert.Equal(16, Gpu.TrailingZeroCount((ushort)0));
        Assert.Equal(64, Gpu.LeadingZeroCount(0L));
        Assert.Equal(64UL, Gpu.TrailingZeroCount(0UL));
        Assert.Equal(0, Gpu.LeadingZeroCount((sbyte)-1));
        Assert.Equal(0, Gpu.LeadingZeroCount((short)-1));
        Assert.Equal(0, Gpu.LeadingZeroCount(-1L));
        Assert.Equal(7, Gpu.LeadingZeroCount((byte)1));  // not 31: the result is that of a byte
    }

    [Fact]
    public void ReverseBits_ReversesWithinTheTypesOwnWidth()
    {
        foreach (ulong p in Patterns)
        {
            unchecked
            {
                Assert.Equal((byte)Reverse((byte)p, 8), Gpu.ReverseBits((byte)p));
                Assert.Equal((sbyte)Reverse((byte)p, 8), Gpu.ReverseBits((sbyte)p));
                Assert.Equal((short)Reverse((ushort)p, 16), Gpu.ReverseBits((short)p));
                Assert.Equal((ushort)Reverse((ushort)p, 16), Gpu.ReverseBits((ushort)p));
                Assert.Equal((long)Reverse(p, 64), Gpu.ReverseBits((long)p));
                Assert.Equal(Reverse(p, 64), Gpu.ReverseBits(p));

                // An involution.
                Assert.Equal((byte)p, Gpu.ReverseBits(Gpu.ReverseBits((byte)p)));
                Assert.Equal((long)p, Gpu.ReverseBits(Gpu.ReverseBits((long)p)));
            }
        }

        Assert.Equal(0x80, Gpu.ReverseBits((byte)1));
        Assert.Equal(sbyte.MinValue, Gpu.ReverseBits((sbyte)1));
        Assert.Equal(short.MinValue, Gpu.ReverseBits((short)1));
        Assert.Equal(0x8000, Gpu.ReverseBits((ushort)1));
        Assert.Equal(long.MinValue, Gpu.ReverseBits(1L));
        Assert.Equal(0x8000000000000000UL, Gpu.ReverseBits(1UL));
        Assert.Equal(0xF7B3D591E6A2C480UL, Gpu.ReverseBits(0x0123456789ABCDEFUL));
    }

    [Fact]
    public void Rotates_TakeTheCountModuloTheWidth_ForEveryIntegerType()
    {
        foreach (ulong p in Patterns)
        {
            foreach (int n in Counts)
            {
                unchecked
                {
                    Assert.Equal((byte)RotateLeft(p, n, 8), Gpu.RotateLeft((byte)p, n));
                    Assert.Equal((sbyte)RotateLeft(p, n, 8), Gpu.RotateLeft((sbyte)p, n));
                    Assert.Equal((short)RotateLeft(p, n, 16), Gpu.RotateLeft((short)p, n));
                    Assert.Equal((ushort)RotateLeft(p, n, 16), Gpu.RotateLeft((ushort)p, n));
                    Assert.Equal((int)RotateLeft(p, n, 32), Gpu.RotateLeft((int)p, n));
                    Assert.Equal((uint)RotateLeft(p, n, 32), Gpu.RotateLeft((uint)p, n));
                    Assert.Equal((long)RotateLeft(p, n, 64), Gpu.RotateLeft((long)p, n));
                    Assert.Equal(RotateLeft(p, n, 64), Gpu.RotateLeft(p, n));

                    Assert.Equal((byte)RotateRight(p, n, 8), Gpu.RotateRight((byte)p, n));
                    Assert.Equal((sbyte)RotateRight(p, n, 8), Gpu.RotateRight((sbyte)p, n));
                    Assert.Equal((short)RotateRight(p, n, 16), Gpu.RotateRight((short)p, n));
                    Assert.Equal((ushort)RotateRight(p, n, 16), Gpu.RotateRight((ushort)p, n));
                    Assert.Equal((int)RotateRight(p, n, 32), Gpu.RotateRight((int)p, n));
                    Assert.Equal((uint)RotateRight(p, n, 32), Gpu.RotateRight((uint)p, n));
                    Assert.Equal((long)RotateRight(p, n, 64), Gpu.RotateRight((long)p, n));
                    Assert.Equal(RotateRight(p, n, 64), Gpu.RotateRight(p, n));

                    // The 32- and 64-bit ones are BitOperations' rotates.
                    Assert.Equal(BitOperations.RotateLeft((uint)p, n), Gpu.RotateLeft((uint)p, n));
                    Assert.Equal(BitOperations.RotateRight(p, n), Gpu.RotateRight(p, n));
                }
            }
        }

        Assert.Equal(0x03, Gpu.RotateLeft((byte)0x81, 1));
        Assert.Equal(0x03, Gpu.RotateLeft((byte)0x81, 9));   // 9 mod 8
        Assert.Equal(0xC0, Gpu.RotateRight((byte)0x81, 1));
        Assert.Equal(0xC0, Gpu.RotateLeft((byte)0x81, -1));  // a negative count rotates the other way
        Assert.Equal(-2, Gpu.RotateLeft((short)0x7FFF, 1));
        Assert.Equal(1L, Gpu.RotateLeft(long.MinValue, 1));
        Assert.Equal(0x123456789ABCDEF0UL, Gpu.RotateLeft(0x0123456789ABCDEFUL, 68));
    }

    // ---- abs, min, max, clamp ---------------------------------------------------------------------

    [Fact]
    public void Abs_WrapsAtTheMinimum_AndIsTheIdentityForUnsignedTypes()
    {
        Assert.Equal(5, Gpu.Abs((sbyte)-5));
        Assert.Equal(127, Gpu.Abs((sbyte)-127));
        Assert.Equal(sbyte.MinValue, Gpu.Abs(sbyte.MinValue)); // no overflow exception, as in MSL
        Assert.Equal(300, Gpu.Abs((short)-300));
        Assert.Equal(short.MinValue, Gpu.Abs(short.MinValue));
        Assert.Equal(5_000_000_000L, Gpu.Abs(-5_000_000_000L));
        Assert.Equal(long.MaxValue, Gpu.Abs(-long.MaxValue));
        Assert.Equal(long.MinValue, Gpu.Abs(long.MinValue));
        Assert.Equal(0L, Gpu.Abs(0L));

        Assert.Equal(byte.MaxValue, Gpu.Abs(byte.MaxValue));
        Assert.Equal(ushort.MaxValue, Gpu.Abs(ushort.MaxValue));
        Assert.Equal(ulong.MaxValue, Gpu.Abs(ulong.MaxValue));
    }

    [Fact]
    public void MinMaxClamp_CompareInTheTypesOwnSignedness()
    {
        Assert.Equal(200, Gpu.Max((byte)200, (byte)100)); // 200 is not negative as a byte
        Assert.Equal(100, Gpu.Min((byte)200, (byte)100));
        Assert.Equal(-56, Gpu.Min(unchecked((sbyte)200), (sbyte)100));
        Assert.Equal(100, Gpu.Max(unchecked((sbyte)200), (sbyte)100));
        Assert.Equal(short.MinValue, Gpu.Min(short.MinValue, short.MaxValue));
        Assert.Equal(ushort.MaxValue, Gpu.Max(ushort.MaxValue, (ushort)0x8000));
        Assert.Equal(long.MinValue, Gpu.Min(long.MinValue, long.MaxValue));
        Assert.Equal(long.MaxValue, Gpu.Max(long.MinValue, long.MaxValue));
        Assert.Equal(ulong.MaxValue, Gpu.Max(ulong.MaxValue, 0x8000000000000000UL)); // not negative as a ulong
        Assert.Equal(1UL << 40, Gpu.Min(ulong.MaxValue, 1UL << 40));

        Assert.Equal(10, Gpu.Clamp((byte)3, (byte)10, (byte)20));
        Assert.Equal(20, Gpu.Clamp((byte)250, (byte)10, (byte)20));
        Assert.Equal(-5, Gpu.Clamp((sbyte)-100, (sbyte)-5, (sbyte)5));
        Assert.Equal(7, Gpu.Clamp((short)7, short.MinValue, short.MaxValue));
        Assert.Equal(1000, Gpu.Clamp(ushort.MaxValue, (ushort)0, (ushort)1000));
        Assert.Equal(-1000L, Gpu.Clamp(long.MinValue, -1000L, 1000L));
        Assert.Equal(1000L, Gpu.Clamp(long.MaxValue, -1000L, 1000L));
        Assert.Equal(1UL << 50, Gpu.Clamp(ulong.MaxValue, 1UL << 40, 1UL << 50));

        // min(max(x, lo), hi), as MSL defines clamp: reversed bounds give the upper one instead of throwing.
        Assert.Equal(10, Gpu.Clamp((byte)15, (byte)20, (byte)10));
        Assert.Equal(-1L, Gpu.Clamp(5L, 3L, -1L));
    }
}
