using System;
using System.Runtime.CompilerServices;

namespace SharpCompute.Tests.LocalArrays;

// Shaders for the local array tests. Each runs unchanged on the GPU (For) and on the CPU (ForCpu),
// so the tests compare the two and check both against plain C#.

/// <summary>
/// Sorts eight values per thread with Batcher's odd-even merge network (19 compare-and-swaps). The
/// network is a <c>ReadOnlySpan&lt;int&gt;</c> constant table, and each step passes two elements of
/// the thread's array to a helper by <c>ref</c>.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SortEight(ReadOnlyBuffer<float> input, ReadWriteBuffer<float> output) : IComputeShader
{
    public const int Width = 8;

    private static void CompareSwap(ref float a, ref float b)
    {
        float low = Gpu.Min(a, b);
        b = Gpu.Max(a, b);
        a = low;
    }

    private static void Sort(Span<float> values)
    {
        ReadOnlySpan<int> network =
        [
            0, 1, 2, 3, 4, 5, 6, 7,
            0, 2, 1, 3, 4, 6, 5, 7,
            1, 2, 5, 6, 0, 4, 3, 7,
            1, 5, 2, 6,
            1, 4, 3, 6,
            2, 4, 3, 5,
            3, 4,
        ];

        for (int k = 0; k < network.Length; k += 2)
        {
            CompareSwap(ref values[network[k]], ref values[network[k + 1]]);
        }
    }

    public void Execute()
    {
        int row = ThreadIds.X * Width;
        Span<float> values = stackalloc float[Width];

        for (int i = 0; i < values.Length; i++)
        {
            values[i] = input[row + i];
        }

        Sort(values);

        for (int i = 0; i < values.Length; i++)
        {
            output[row + i] = values[i];
        }
    }
}

/// <summary>
/// A 3 × 3 median filter: the nine neighbours (clamped at the edges) in a nine-element array, and the
/// median picked by the 19 compare-and-swaps of Paeth's median-of-nine network, written out in full.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Median3x3(ReadOnlyBuffer<float> image, ReadWriteBuffer<float> output, int width, int height) : IComputeShader
{
    private static void Order(ref float a, ref float b)
    {
        float low = Gpu.Min(a, b);
        b = Gpu.Max(a, b);
        a = low;
    }

    /// <summary>Leaves the median of the nine values in <c>p[4]</c>.</summary>
    public static void MedianOfNine(Span<float> p)
    {
        Order(ref p[1], ref p[2]); Order(ref p[4], ref p[5]); Order(ref p[7], ref p[8]);
        Order(ref p[0], ref p[1]); Order(ref p[3], ref p[4]); Order(ref p[6], ref p[7]);
        Order(ref p[1], ref p[2]); Order(ref p[4], ref p[5]); Order(ref p[7], ref p[8]);
        Order(ref p[0], ref p[3]); Order(ref p[5], ref p[8]); Order(ref p[4], ref p[7]);
        Order(ref p[3], ref p[6]); Order(ref p[1], ref p[4]); Order(ref p[2], ref p[5]);
        Order(ref p[4], ref p[7]); Order(ref p[4], ref p[2]); Order(ref p[6], ref p[4]);
        Order(ref p[4], ref p[2]);
    }

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Span<float> window = stackalloc float[9];
        int n = 0;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int sx = Math.Clamp(x + dx, 0, width - 1);
                int sy = Math.Clamp(y + dy, 0, height - 1);
                window[n++] = image[(sy * width) + sx];
            }
        }

        MedianOfNine(window);
        output[(y * width) + x] = window[4];
    }
}

/// <summary>
/// Arrays of matrices and vectors: a chain of three transforms from a buffer in a <c>Span&lt;Float4x4&gt;</c>,
/// two rotations in a <c>Float3x3[]</c> (locals only in MSL: it has no packed form) and their products.
/// Slots per thread: the transformed point (4) and the rotated vector (3).
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct MatrixArrays(ReadOnlyBuffer<Float4x4> transforms, ReadWriteBuffer<float> output) : IComputeShader
{
    public const int Slots = 7;

    public void Execute()
    {
        int i = ThreadIds.X;
        Span<Float4x4> chain = stackalloc Float4x4[3];
        Float3x3[] rotations = new Float3x3[2];

        for (int k = 0; k < chain.Length; k++)
        {
            chain[k] = transforms[(i + k) % transforms.Length];
        }

        rotations[0] = new Float3x3(0, -1, 0, 1, 0, 0, 0, 0, 1);
        rotations[1] = new Float3x3(1, 0, 0, 0, 0, -1, 0, 1, 0);

        Float4 point = new Float4(i % 5, 1, -2, 1);

        foreach (Float4x4 m in chain)
        {
            point = Gpu.Mul(point, m);
        }

        Float3 v = new Float3(1, 2, 3);

        foreach (Float3x3 r in rotations)
        {
            v = Gpu.Mul(v, r);
        }

        int o = i * Slots;
        output[o] = point.X;
        output[o + 1] = point.Y;
        output[o + 2] = point.Z;
        output[o + 3] = point.W;
        output[o + 4] = v.X;
        output[o + 5] = v.Y;
        output[o + 6] = v.Z;
    }
}

/// <summary>A user struct as the element of a local array.</summary>
public struct Weighted
{
    public Float3 Position;
    public int Weight;
}

/// <summary>
/// Arrays of user structs: four particles per thread, sorted by weight with a struct swap through
/// <c>ref</c> elements, then their positions summed in weight order (an order-dependent result).
/// Slots per thread: the weights in order (4), then the weighted position sum (3).
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct StructArrays(ReadOnlyBuffer<int> seeds, ReadWriteBuffer<float> output) : IComputeShader
{
    public const int Slots = 7;

    private static void Swap(ref Weighted a, ref Weighted b)
    {
        Weighted t = a;
        a = b;
        b = t;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        Span<Weighted> particles = stackalloc Weighted[4];

        for (int k = 0; k < particles.Length; k++)
        {
            int seed = seeds[(i * 4) + k];
            particles[k].Position = new Float3(seed % 7, seed % 5, k);
            particles[k].Weight = seed % 11;
        }

        for (int a = 0; a < particles.Length; a++)
        {
            for (int b = a + 1; b < particles.Length; b++)
            {
                if (particles[b].Weight < particles[a].Weight)
                {
                    Swap(ref particles[a], ref particles[b]);
                }
            }
        }

        Float3 sum = new Float3(0, 0, 0);
        int o = i * Slots;

        for (int k = 0; k < particles.Length; k++)
        {
            sum = (sum * 2f) + particles[k].Position;
            output[o + k] = particles[k].Weight;
        }

        output[o + 4] = sum.X;
        output[o + 5] = sum.Y;
        output[o + 6] = sum.Z;
    }
}

/// <summary>
/// A helper that writes into its caller's array, through a helper of its own: each thread fills a
/// <c>float[]</c> and a <c>Span&lt;int&gt;</c>, then a read-only helper reduces them. Slots per
/// thread: the six floats, the four ints, and the reduction.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct HelperWritesCallersArray(ReadWriteBuffer<float> output) : IComputeShader
{
    public const int Slots = 11;

    private static void Ramp(Span<float> values, float start)
    {
        for (int k = 0; k < values.Length; k++)
        {
            values[k] = start + k;
        }

        Double(values);
    }

    private static void Double(Span<float> values)
    {
        foreach (ref float value in values)
        {
            value *= 2;
        }
    }

    private static void Squares(Span<int> values, int offset)
    {
        for (int k = 0; k < values.Length; k++)
        {
            values[k] = (k + offset) * (k + offset);
        }
    }

    private static float Total(ReadOnlySpan<float> floats, ReadOnlySpan<int> ints)
    {
        float total = 0;

        foreach (float f in floats)
        {
            total += f;
        }

        foreach (int n in ints)
        {
            total -= n;
        }

        return total;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        float[] floats = new float[6];
        Span<int> ints = stackalloc int[4];

        Ramp(floats, i);
        Squares(ints, i % 3);

        int o = i * Slots;

        for (int k = 0; k < floats.Length; k++)
        {
            output[o + k] = floats[k];
        }

        for (int k = 0; k < ints.Length; k++)
        {
            output[o + 6 + k] = ints[k];
        }

        output[o + 10] = Total(floats, ints);
    }
}

/// <summary>
/// Initializers: constant tables (a <c>ReadOnlySpan&lt;float&gt;</c> and an <c>int[]</c> collection
/// expression), an array built from run-time values, and a zeroed histogram that the thread counts
/// into. Slots per thread: the weighted sum, the prime sum, the built array's sum, and four bins.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Initializers(ReadOnlyBuffer<int> input, ReadWriteBuffer<float> output) : IComputeShader
{
    public const int Slots = 7;

    public void Execute()
    {
        int i = ThreadIds.X;
        ReadOnlySpan<float> weights = [0.5f, 0.25f, 0.125f, 0.0625f];
        int[] primes = [2, 3, 5, 7, 11];
        Span<int> built = stackalloc int[] { i, i * 2, input[i] };
        Span<int> bins = stackalloc int[4];

        float weighted = 0;

        for (int k = 0; k < weights.Length; k++)
        {
            weighted += weights[k] * (i + k);
        }

        int primeSum = 0;

        foreach (int p in primes)
        {
            primeSum += p * (i % p);
        }

        int builtSum = 0;

        foreach (int b in built)
        {
            builtSum += b;
        }

        for (int k = 0; k < 16; k++)
        {
            bins[(input[i] >> k) & 3]++;
        }

        int o = i * Slots;
        output[o] = weighted;
        output[o + 1] = primeSum;
        output[o + 2] = builtSum;

        for (int k = 0; k < bins.Length; k++)
        {
            output[o + 3 + k] = bins[k];
        }
    }
}

/// <summary>Four floats, inline: an MSL struct holding <c>float e[4]</c>.</summary>
[InlineArray(4)]
public struct Quad
{
    private float _element;
}

/// <summary>A struct with an inline array field, in a buffer.</summary>
public struct Tagged
{
    public int Tag;
    public Quad Values;
}

/// <summary>
/// Inline arrays across the CPU/GPU boundary: a captured constant, buffer elements of an inline array
/// type and of a struct holding one, all read and written in place.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct InlineArrays(ReadWriteBuffer<Quad> quads, ReadWriteBuffer<Tagged> tagged, Quad scale) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        Quad q = quads[i];

        for (int k = 0; k < 4; k++)
        {
            q[k] = (q[k] * scale[k]) + tagged[i].Tag;
        }

        quads[i] = q;
        float sum = 0;

        foreach (float value in q)
        {
            sum += value;
        }

        tagged[i].Values[i % 4] = sum;
        tagged[i].Tag += 1;
    }
}

/// <summary>Reads <c>taps[index]</c>: out of range, ForCpu throws where the GPU would read anything.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct IndexLocalArray(ReadWriteBuffer<float> output, int index) : IComputeShader
{
    public void Execute()
    {
        Span<float> taps = stackalloc float[4];
        taps[ThreadIds.X % 4] = 1;
        output[ThreadIds.X] = taps[index];
    }
}

/// <summary>
/// Local arrays next to threadgroup memory and SIMD-group operations: every thread sorts four values in
/// a local array (through a helper) and weighs them by rank, each SIMD group sums the weights with
/// <c>Gpu.WaveActiveSum</c>, and the first lane of each group adds its sum atomically to a
/// <c>[GroupShared]</c> total, which the first thread of the threadgroup writes out.
/// </summary>
[ThreadGroupSize(GroupSize)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SortThenReduceGroups(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> sorted, ReadWriteBuffer<int> totals) : IComputeShader
{
    public const int GroupSize = 128;
    public const int Width = 4;

    [GroupShared]
    private static readonly int[] total = new int[1];

    private static void InsertionSort(Span<int> values)
    {
        for (int i = 1; i < values.Length; i++)
        {
            int value = values[i];
            int j = i - 1;

            while (j >= 0 && values[j] > value)
            {
                values[j + 1] = values[j];
                j--;
            }

            values[j + 1] = value;
        }
    }

    private static int WeighByRank(ReadOnlySpan<int> values)
    {
        int weighted = 0;
        int rank = 1;

        foreach (int value in values)
        {
            weighted += value * rank;
            rank++;
        }

        return weighted;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        Span<int> values = stackalloc int[Width];

        for (int k = 0; k < values.Length; k++)
        {
            values[k] = input[(i * Width) + k];
        }

        InsertionSort(values);

        for (int k = 0; k < values.Length; k++)
        {
            sorted[(i * Width) + k] = values[k];
        }

        if (GroupIds.Index == 0)
        {
            total[0] = 0;
        }

        Gpu.GroupMemoryBarrierWithGroupSync();

        int simdSum = Gpu.WaveActiveSum(WeighByRank(values));

        if (Gpu.WaveIsFirstLane())
        {
            Gpu.AtomicAdd(ref total[0], simdSum);
        }

        Gpu.GroupMemoryBarrierWithGroupSync();

        if (GroupIds.Index == 0)
        {
            totals[ThreadGroupIds.X] = total[0];
        }
    }
}
