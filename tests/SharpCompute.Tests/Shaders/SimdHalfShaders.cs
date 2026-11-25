using System;
using SharpCompute;

namespace SharpCompute.Tests.Shaders;

// The SIMD-group operations on halves and half vectors, and the half overloads of Radians, Degrees
// and the bit casts. Each shader runs unchanged on the GPU (For) and on the CPU (ForCpu).

/// <summary>Every typed SIMD-group and quad operation that takes a <see cref="Half"/>, once each.</summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdHalfScalars(ReadOnlyBuffer<Half> input, ReadWriteBuffer<Half> output, int delta) : IComputeShader
{
    public const int Stride = 31;

    /// <summary>The outputs that are sums or scans of sums, and those that are products or scans of products: the ones whose rounding depends on the order of the lanes.</summary>
    public static readonly int[] Sums = [0, 4, 6, 19, 23, 25];
    public static readonly int[] Products = [1, 5, 7, 20, 24, 26];

    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * Stride;
        Half v = input[i];

        output[o + 0] = Gpu.WaveActiveSum(v);
        output[o + 1] = Gpu.WaveActiveProduct(v);
        output[o + 2] = Gpu.WaveActiveMin(v);
        output[o + 3] = Gpu.WaveActiveMax(v);
        output[o + 4] = Gpu.WavePrefixSum(v);
        output[o + 5] = Gpu.WavePrefixProduct(v);
        output[o + 6] = Gpu.WaveInclusivePrefixSum(v);
        output[o + 7] = Gpu.WaveInclusivePrefixProduct(v);

        output[o + 8] = Gpu.WaveReadLaneAt(v, 2);
        output[o + 9] = Gpu.WaveReadLaneFirst(v);
        output[o + 10] = Gpu.WaveShuffleUp(v, delta);
        output[o + 11] = Gpu.WaveShuffleDown(v, delta);
        output[o + 12] = Gpu.WaveShuffleXor(v, 1);
        output[o + 13] = Gpu.WaveRotateUp(v, delta);
        output[o + 14] = Gpu.WaveRotateDown(v, delta);
        output[o + 15] = Gpu.WaveShuffleUpAndFill(v, -v, delta);
        output[o + 16] = Gpu.WaveShuffleDownAndFill(v, -v, delta);

        output[o + 17] = Gpu.WaveActiveAllEqual(v) ? (Half)1f : (Half)0f;
        output[o + 18] = Gpu.WaveActiveAllEqual(Gpu.WaveReadLaneFirst(v)) ? (Half)1f : (Half)0f;

        output[o + 19] = Gpu.QuadActiveSum(v);
        output[o + 20] = Gpu.QuadActiveProduct(v);
        output[o + 21] = Gpu.QuadActiveMin(v);
        output[o + 22] = Gpu.QuadActiveMax(v);
        output[o + 23] = Gpu.QuadPrefixSum(v);
        output[o + 24] = Gpu.QuadPrefixProduct(v);
        output[o + 25] = Gpu.QuadInclusivePrefixSum(v);
        output[o + 26] = Gpu.QuadInclusivePrefixProduct(v);
        output[o + 27] = Gpu.QuadReadAcrossX(v);
        output[o + 28] = Gpu.QuadReadAcrossY(v);
        output[o + 29] = Gpu.QuadReadAcrossDiagonal(v);
        output[o + 30] = Gpu.QuadReadLaneAt(v, 3 - (Gpu.WaveGetLaneIndex() & 3));
    }
}

/// <summary>
/// The same operations on <see cref="Half2"/>, <see cref="Half3"/> and <see cref="Half4"/>: values
/// straight from a buffer (packed in MSL), swizzles of them and vectors built in the shader.
/// </summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdHalfVectors(ReadOnlyBuffer<Half4> input, ReadWriteBuffer<Half4> output, int delta) : IComputeShader
{
    public const int Stride = 20;

    /// <summary>The outputs with a sum, a product or a scan of one in them.</summary>
    public static readonly int[] OrderDependent = [0, 1, 3, 4, 5, 14, 15];

    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * Stride;
        Half4 x = input[i];
        Half zero = (Half)0f;

        output[o + 0] = Gpu.WaveActiveSum(input[i]);
        output[o + 1] = new Half4(Gpu.WaveActiveProduct(x.XY), Gpu.WaveActiveSum(x.ZW));
        output[o + 2] = new Half4(Gpu.WaveActiveMin(x.XYZ), Gpu.WaveActiveMax(x.XYZ).X);
        output[o + 3] = Gpu.WavePrefixSum(x);
        output[o + 4] = new Half4(Gpu.WavePrefixProduct(x.XYZ), zero);
        output[o + 5] = new Half4(Gpu.WaveInclusivePrefixSum(x.XY), Gpu.WaveInclusivePrefixProduct(x.ZW));
        output[o + 6] = new Half4(Gpu.WaveActiveMax(x.ZW), Gpu.WaveActiveMin(x).XW);

        output[o + 7] = Gpu.WaveReadLaneAt(x, 2);
        output[o + 8] = new Half4(Gpu.WaveReadLaneFirst(x.XYZ), zero);
        output[o + 9] = Gpu.WaveShuffleUp(input[i], delta);
        output[o + 10] = new Half4(Gpu.WaveShuffleDown(x.XY, delta), Gpu.WaveShuffleXor(x.ZW, 1));
        output[o + 11] = Gpu.WaveRotateUp(x, delta);
        output[o + 12] = new Half4(Gpu.WaveRotateDown(x.XYZ, delta), zero);
        output[o + 13] = new Half4(Gpu.WaveShuffleUpAndFill(x.XY, -x.XY, delta), Gpu.WaveShuffleDownAndFill(x.ZW, -x.ZW, delta));

        output[o + 14] = Gpu.QuadActiveSum(x);
        output[o + 15] = new Half4(Gpu.QuadPrefixSum(x.XYZ), Gpu.QuadInclusivePrefixProduct(x.XY).X);
        output[o + 16] = new Half4(Gpu.QuadActiveMax(x.XYZ), Gpu.QuadActiveMin(x.ZW).Y);
        output[o + 17] = Gpu.QuadReadAcrossX(x);
        output[o + 18] = new Half4(Gpu.QuadReadAcrossY(x.XY), Gpu.QuadReadLaneAt(x.ZW, 3 - (Gpu.WaveGetLaneIndex() & 3)));
        output[o + 19] = new Half4(
            Gpu.WaveActiveAllEqual(x) ? (Half)1f : zero,
            Gpu.WaveActiveAllEqual(Gpu.WaveReadLaneFirst(x.XYZ)) ? (Half)1f : zero,
            Gpu.WaveActiveAllEqual(new Half2((Half)2f, x.W)) ? (Half)1f : zero,
            Gpu.QuadReadAcrossDiagonal(x.XYZ).Z);
    }
}

/// <summary>Radians and degrees of a half and of half vectors, and the bits of a half through the 16-bit integers.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct HalfScalesAndBits(
    ReadOnlyBuffer<Half4> input, ReadWriteBuffer<Half4> scaled, ReadWriteBuffer<ushort> bits, ReadWriteBuffer<Half> fromBits) : IComputeShader
{
    public const int ScaledStride = 4;
    public const int BitsStride = 6;

    public void Execute()
    {
        int i = ThreadIds.X;
        Half4 x = input[i];

        scaled[i * ScaledStride] = Gpu.Radians(x);
        scaled[(i * ScaledStride) + 1] = Gpu.Degrees(x);
        scaled[(i * ScaledStride) + 2] = new Half4(Gpu.Radians(x.X), Gpu.Degrees(x.X), Gpu.Radians(x.YZ).Y, Gpu.Degrees(x.YZW).Z);
        scaled[(i * ScaledStride) + 3] = new Half4(Gpu.Degrees(Gpu.Radians(x.XY)), Gpu.Radians(Gpu.Degrees(x.ZW)));

        // The bits out, three ways, and changed on the way back in: the sign bit flipped, the lowest bit set.
        ushort u = Gpu.AsUShort(x.X);
        short s = Gpu.AsShort(x.X);

        bits[i * BitsStride] = u;
        bits[(i * BitsStride) + 1] = (ushort)s;
        bits[(i * BitsStride) + 2] = BitConverter.HalfToUInt16Bits(x.Y);
        bits[(i * BitsStride) + 3] = (ushort)BitConverter.HalfToInt16Bits(x.Y);
        bits[(i * BitsStride) + 4] = Gpu.AsUShort(Gpu.AsHalf((ushort)(u ^ 0x8000)));
        bits[(i * BitsStride) + 5] = (ushort)(s < 0 ? 1 : 0);

        fromBits[i * 4] = Gpu.AsHalf(u);
        fromBits[(i * 4) + 1] = Gpu.AsHalf(s);
        fromBits[(i * 4) + 2] = BitConverter.UInt16BitsToHalf((ushort)(u | 1));
        fromBits[(i * 4) + 3] = BitConverter.Int16BitsToHalf((short)(s & 0x7FFF));
    }
}
