using System;
using System.Linq;

namespace SharpCompute.Tests.Conformance;

/// <summary>
/// The operands of the conformance families: for each type 0, ±1, the minimum and maximum and their
/// neighbours, the points where a product or a promotion overflows, alternating bit patterns, and
/// for floats and halves also ±0, subnormals, the smallest and largest normals, values that
/// overflow the integer types, ±∞ and NaNs of both signs.
/// </summary>
internal static class ConformanceValues
{
    /// <summary>The operands of one type, and for floats how the GPU flushes a subnormal one.</summary>
    internal sealed record Set<T>(T[] Values, Func<T, T>? Flush)
        where T : unmanaged;

    public static readonly byte[] Bytes = [0, 1, 2, 3, 7, 8, 15, 16, 31, 32, 63, 64, 100, 127, 128, 129, 200, 254, 255, 0x55, 0xAA, 0x0F, 0xF0, 16 * 16 - 1];

    public static readonly sbyte[] SBytes = [0, 1, -1, 2, -2, 3, 7, 8, -8, 11, -12, 15, 16, -16, 63, 64, -64, 100, -100, 126, 127, -127, -128, 0x55, -0x56];

    public static readonly short[] Shorts = [0, 1, -1, 2, -2, 3, 15, 16, 181, -182, 255, 256, -256, 1000, -1000, 16384, 32766, 32767, -32767, -32768, 0x5555, -0x5556, 0x00FF, 0x7F00, -0x0100];

    public static readonly ushort[] UShorts = [0, 1, 2, 3, 15, 16, 255, 256, 257, 1000, 4096, 16384, 32767, 32768, 32769, 46341, 65000, 65534, 65535, 0x5555, 0xAAAA, 0x00FF, 0xFF00, 0x8001];

    public static readonly int[] Ints =
    [
        0, 1, -1, 2, -2, 3, 31, 32, 33, 127, 128, 255, 256, 32767, 32768, 65535, 65536, 46341, -46341,
        1 << 30, int.MaxValue, int.MinValue, int.MaxValue - 1, int.MinValue + 1, 0x55555555, unchecked((int)0xAAAAAAAA),
        123456789, -987654321, 0x0000FFFF, unchecked((int)0xFFFF0000),
    ];

    public static readonly uint[] UInts =
    [
        0u, 1u, 2u, 3u, 31u, 32u, 33u, 255u, 256u, 65535u, 65536u, 65537u, 46341u, 1u << 30, 0x7FFFFFFFu, 0x80000000u, 0x80000001u,
        0xFFFFFFFEu, 0xFFFFFFFFu, 0x55555555u, 0xAAAAAAAAu, 123456789u, 3000000000u, 0x0000FFFFu, 0xFFFF0000u,
    ];

    public static readonly long[] Longs =
    [
        0L, 1L, -1L, 2L, -2L, 3L, 31L, 32L, 63L, 64L, int.MaxValue, int.MinValue, uint.MaxValue, 1L << 32, -(1L << 32),
        3037000499L, 3037000500L, -3037000500L, 1L << 62, long.MaxValue, long.MinValue, long.MaxValue - 1, long.MinValue + 1,
        0x5555555555555555L, unchecked((long)0xAAAAAAAAAAAAAAAAUL), 1234567890123456789L, -987654321987654321L, 0x00000000FFFFFFFFL,
    ];

    public static readonly ulong[] ULongs =
    [
        0UL, 1UL, 2UL, 3UL, 31UL, 32UL, 63UL, 64UL, uint.MaxValue, 1UL << 32, (1UL << 32) + 1, 4294967296UL * 3, 1UL << 62, 1UL << 63,
        (1UL << 63) + 1, long.MaxValue, ulong.MaxValue, ulong.MaxValue - 1, 0x5555555555555555UL, 0xAAAAAAAAAAAAAAAAUL,
        12345678901234567890UL, 0x00000000FFFFFFFFUL, 0xFFFFFFFF00000000UL, 6074001000UL,
    ];

    /// <summary>The floats, as bits so that the NaNs, the zeros and the subnormals are exact.</summary>
    public static readonly float[] Floats =
    [
        .. new uint[]
        {
            0x00000000, 0x80000000,             // +0, -0
            0x00000001, 0x80000001,             // the smallest subnormals
            0x00400000, 0x007FFFFF, 0x807FFFFF, // subnormals: half the smallest normal, the largest
            0x00800000, 0x80800000, 0x00C00000, // the smallest normals, and 1.5 times it
            0x7F7FFFFF, 0xFF7FFFFF,             // ±float.MaxValue
            0x7F800000, 0xFF800000,             // ±∞
            0x7FC00000, 0xFFC00000, 0x7FA00001, // NaN, -NaN (.NET's float.NaN), a NaN with a payload
            0x3F7FFFFF, 0x3F800001,             // the neighbours of 1
        }.Select(BitConverter.UInt32BitsToSingle),
        1f, -1f, 0.5f, -0.5f, 1.5f, -2.5f, 3f, 7f, 0.1f, -0.1f, 0.75f, 100f, -100f, 255.5f, 300f, -129f, 1e-5f, -1e-30f,
        65504f, 65520f, 6e-8f, 3.1415927f, 1e5f, 16777215f, 16777216f, 2147483520f, 2147483648f, -2147483648f, 4294967296f,
        9223372036854775808f, -9.223373e18f, 18446744073709551616f, 1e10f, -1e10f, 1e20f, 1e30f,
    ];

    /// <summary>The halves, as bits.</summary>
    public static readonly Half[] Halves =
    [
        .. new ushort[]
        {
            0x0000, 0x8000,                 // +0, -0
            0x0001, 0x8001, 0x0200, 0x03FF, // subnormals: the smallest, a middle one, the largest
            0x0400, 0x8400,                 // the smallest normals
            0x7BFF, 0xFBFF,                 // ±65504
            0x7C00, 0xFC00,                 // ±∞
            0x7E00, 0xFE00, 0x7D01,         // NaN, -NaN, a NaN with a payload
            0x3C00, 0xBC00, 0x3800, 0xB800, // ±1, ±0.5
            0x3E00, 0xC100, 0x4200,         // 1.5, -2.5, 3
            0x2E66, 0xB4CD, 0x3BFF, 0x3C01, // 0.1, -0.3, the neighbours of 1
            0x5640, 0xD640, 0x63D0, 0x6800, // ±100, 1000, 2048
            0x4248, 0x5BF8, 0xD800, 0x1400, // 3.14, 255, -128, 2^-10
        }.Select(BitConverter.UInt16BitsToHalf),
    ];

    /// <summary>The lanes of the bool vectors, as integers that the shader compares with zero.</summary>
    public static readonly int[] Bools = [0, 1];

    public static Set<T> For<T>(string name)
        where T : unmanaged
    {
        object set = name switch
        {
            "byte" => new Set<byte>(Bytes, null),
            "sbyte" => new Set<sbyte>(SBytes, null),
            "short" => new Set<short>(Shorts, null),
            "ushort" => new Set<ushort>(UShorts, null),
            "int" => new Set<int>(Ints, null),
            "uint" => new Set<uint>(UInts, null),
            "long" => new Set<long>(Longs, null),
            "ulong" => new Set<ulong>(ULongs, null),
            "float" => new Set<float>(Floats, FlushSubnormal),
            "Half" => new Set<Half>(Halves, null),   // the GPU keeps half subnormals
            "bool" => new Set<int>(Bools, null),
            _ => throw new ArgumentException($"No conformance values named '{name}'.", nameof(name)),
        };

        return (Set<T>)set;
    }

    /// <summary>A subnormal float as an Apple GPU reads it: a zero with its sign.</summary>
    public static float FlushSubnormal(float value)
    {
        uint bits = BitConverter.SingleToUInt32Bits(value);
        return (bits & 0x7F800000u) == 0 ? BitConverter.UInt32BitsToSingle(bits & 0x80000000u) : value;
    }
}
