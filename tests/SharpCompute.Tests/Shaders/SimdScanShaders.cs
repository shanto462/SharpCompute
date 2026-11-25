using System;
using SharpCompute;

namespace SharpCompute.Tests.Shaders;

// What a scan gives the first active lane, and the sign of a zero through the scans and the sums.
// The shader runs unchanged on the GPU (For) and on the CPU (ForCpu).

/// <summary>
/// The scans and the sums of a <see cref="float"/> and of a <see cref="Half"/>, as their bits, with
/// the place of each thread among the active lanes of its SIMD group and of its quad. Every
/// <paramref name="skip"/>-th lane returns before them (none when it is 0), so that the first active
/// lane is not always lane 0.
/// </summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdScanZeros(ReadOnlyBuffer<float> floats, ReadOnlyBuffer<Half> halves, ReadWriteBuffer<uint> output, int skip) : IComputeShader
{
    public const int Stride = 22;

    /// <summary>The exclusive scans of sums over the SIMD group: float, a component of a float vector, half, a component of a half vector.</summary>
    public static readonly int[] ExclusiveSums = [3, 9, 10, 16];

    /// <summary>The exclusive scans of sums over the quad: float, half.</summary>
    public static readonly int[] QuadExclusiveSums = [6, 13];

    /// <summary>The exclusive scans of products over the SIMD group and over the quad, float and half.</summary>
    public static readonly int[] ExclusiveProducts = [5, 12], QuadExclusiveProducts = [8, 15];

    /// <summary>The inclusive scans of sums and the sums, over the SIMD group and over the quad.</summary>
    public static readonly int[] InclusiveSumsAndSums = [4, 7, 11, 14, 17, 18, 19, 20];

    /// <summary>The outputs that hold the 16 bits of a half; the others from 3 on hold the 32 bits of a float.</summary>
    public static readonly int[] Halves = [10, 11, 12, 13, 14, 15, 16, 18, 20];

    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * Stride;

        if (skip > 0 && Gpu.WaveGetLaneIndex() % skip == 0)
        {
            return;
        }

        float f = floats[i];
        Half h = halves[i];

        output[o + 0] = 1u;
        output[o + 1] = (uint)Gpu.WavePrefixCountBits(true);
        output[o + 2] = (uint)Gpu.QuadPrefixSum(1);

        output[o + 3] = Gpu.AsUInt(Gpu.WavePrefixSum(f));
        output[o + 4] = Gpu.AsUInt(Gpu.WaveInclusivePrefixSum(f));
        output[o + 5] = Gpu.AsUInt(Gpu.WavePrefixProduct(f));
        output[o + 6] = Gpu.AsUInt(Gpu.QuadPrefixSum(f));
        output[o + 7] = Gpu.AsUInt(Gpu.QuadInclusivePrefixSum(f));
        output[o + 8] = Gpu.AsUInt(Gpu.QuadPrefixProduct(f));
        output[o + 9] = Gpu.AsUInt(Gpu.WavePrefixSum(new Float3(1f, f, 2f)).Y);

        output[o + 10] = Gpu.AsUShort(Gpu.WavePrefixSum(h));
        output[o + 11] = Gpu.AsUShort(Gpu.WaveInclusivePrefixSum(h));
        output[o + 12] = Gpu.AsUShort(Gpu.WavePrefixProduct(h));
        output[o + 13] = Gpu.AsUShort(Gpu.QuadPrefixSum(h));
        output[o + 14] = Gpu.AsUShort(Gpu.QuadInclusivePrefixSum(h));
        output[o + 15] = Gpu.AsUShort(Gpu.QuadPrefixProduct(h));
        output[o + 16] = Gpu.AsUShort(Gpu.WavePrefixSum(new Half2(h, h)).Y);

        output[o + 17] = Gpu.AsUInt(Gpu.WaveActiveSum(f));
        output[o + 18] = Gpu.AsUShort(Gpu.WaveActiveSum(h));
        output[o + 19] = Gpu.AsUInt(Gpu.QuadActiveSum(f));
        output[o + 20] = Gpu.AsUShort(Gpu.QuadActiveSum(h));
        output[o + 21] = (uint)Gpu.WaveGetLaneIndex();
    }
}
