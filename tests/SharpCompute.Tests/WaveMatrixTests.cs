using System;
using System.Linq;
using SharpCompute.Tests.CpuExecution;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// The wave matrices on the GPU: which elements a load and a store name (offset, stride, transpose),
/// the matrices made of a value, the products against a double-precision reference, every mix of half
/// and float, and threadgroup memory. Expectations are literals or come from <see cref="WaveMatrixReference"/>,
/// which is the documented meaning and nothing else.
/// </summary>
public class WaveMatrixTests
{
    private static float[] Iota(int count) => Enumerable.Range(0, count).Select(static i => (float)i).ToArray();

    /// <summary>Loads a matrix from 0, 1, 2, … and stores it; returns the destination, which starts as -1.</summary>
    private static float[] Copy(int sourceLength, int destinationLength, int loadOffset, int loadStride, bool loadTranspose, int storeOffset, int storeStride, bool storeTranspose)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer(Iota(sourceLength));
        using ReadWriteBuffer<float> destination = device.AllocateReadWriteBuffer<float>(destinationLength);
        destination.AsSpan().Fill(-1f);

        device.For(32, new WaveMatrixCopy(source, destination, loadOffset, loadStride, loadTranspose ? 1 : 0, storeOffset, storeStride, storeTranspose ? 1 : 0));
        return destination.ToArray();
    }

    // ---- load and store ---------------------------------------------------------------------------

    [MetalFact]
    public void Load_ReadsRowsOfEightAtTheStride()
    {
        // A 16-wide source: the matrix is its left half, eight rows down.
        float[] actual = Copy(256, 64, loadOffset: 0, loadStride: 16, loadTranspose: false, storeOffset: 0, storeStride: 8, storeTranspose: false);

        Assert.Equal(
            [
                0, 1, 2, 3, 4, 5, 6, 7,
                16, 17, 18, 19, 20, 21, 22, 23,
                32, 33, 34, 35, 36, 37, 38, 39,
                48, 49, 50, 51, 52, 53, 54, 55,
                64, 65, 66, 67, 68, 69, 70, 71,
                80, 81, 82, 83, 84, 85, 86, 87,
                96, 97, 98, 99, 100, 101, 102, 103,
                112, 113, 114, 115, 116, 117, 118, 119,
            ],
            actual);
    }

    [MetalFact]
    public void Load_AtAnOffset_StartsThere()
    {
        // Offset 50 of a 16-wide source is row 3, column 2.
        float[] actual = Copy(256, 64, loadOffset: 50, loadStride: 16, loadTranspose: false, storeOffset: 0, storeStride: 8, storeTranspose: false);

        Assert.Equal([50f, 51, 52, 53, 54, 55, 56, 57], actual[..8]);
        Assert.Equal([66f, 67, 68, 69, 70, 71, 72, 73], actual[8..16]);
        Assert.Equal([162f, 163, 164, 165, 166, 167, 168, 169], actual[56..]);
    }

    [MetalFact]
    public void Load_Transposed_ReadsColumnsAsRows()
    {
        float[] actual = Copy(256, 64, loadOffset: 50, loadStride: 16, loadTranspose: true, storeOffset: 0, storeStride: 8, storeTranspose: false);

        // Row 0 of the matrix is the column that starts at the offset.
        Assert.Equal([50f, 66, 82, 98, 114, 130, 146, 162], actual[..8]);
        Assert.Equal([51f, 67, 83, 99, 115, 131, 147, 163], actual[8..16]);
        Assert.Equal([57f, 73, 89, 105, 121, 137, 153, 169], actual[56..]);
    }

    [MetalFact]
    public void Store_WritesTheSixtyFourElementsAndNothingElse()
    {
        // Into a 16-wide destination at row 3, column 2.
        float[] actual = Copy(64, 192, loadOffset: 0, loadStride: 8, loadTranspose: false, storeOffset: 50, storeStride: 16, storeTranspose: false);
        float[] expected = Enumerable.Repeat(-1f, 192).ToArray();

        for (int row = 0; row < 8; row++)
        {
            for (int column = 0; column < 8; column++)
            {
                expected[50 + (row * 16) + column] = (row * 8) + column;
            }
        }

        Assert.Equal(expected, actual);
        Assert.Equal([-1f, -1, 0, 1, 2, 3, 4, 5, 6, 7, -1, -1, -1, -1, -1, -1], actual[48..64]);
    }

    [MetalFact]
    public void Store_Transposed_WritesRowsAsColumns()
    {
        float[] actual = Copy(64, 192, loadOffset: 0, loadStride: 8, loadTranspose: false, storeOffset: 50, storeStride: 16, storeTranspose: true);

        // The first row written holds the first column of the matrix.
        Assert.Equal([-1f, -1, 0, 8, 16, 24, 32, 40, 48, 56, -1, -1, -1, -1, -1, -1], actual[48..64]);
        Assert.Equal([-1f, -1, 1, 9, 17, 25, 33, 41, 49, 57, -1, -1, -1, -1, -1, -1], actual[64..80]);
    }

    [MetalFact]
    public void LoadAndStore_BothTransposed_AreACopy()
    {
        float[] actual = Copy(64, 64, loadOffset: 0, loadStride: 8, loadTranspose: true, storeOffset: 0, storeStride: 8, storeTranspose: true);

        Assert.Equal(Iota(64), actual);
    }

    [MetalTheory]
    [InlineData(0, 8, false, 0, 8, false)]
    [InlineData(3, 11, false, 5, 9, true)]
    [InlineData(40, 20, true, 17, 30, false)]
    [InlineData(7, 8, true, 1, 64, true)]
    public void LoadAndStore_MatchTheDocumentedElements(int loadOffset, int loadStride, bool loadTranspose, int storeOffset, int storeStride, bool storeTranspose)
    {
        const int sourceLength = 512, destinationLength = 512;
        float[] actual = Copy(sourceLength, destinationLength, loadOffset, loadStride, loadTranspose, storeOffset, storeStride, storeTranspose);

        float[] matrix = WaveMatrixReference.Load(Iota(sourceLength), loadOffset, loadStride, loadTranspose);
        float[] expected = Enumerable.Repeat(-1f, destinationLength).ToArray();
        WaveMatrixReference.Store(matrix, expected, storeOffset, storeStride, storeTranspose);

        Assert.Equal(expected, actual);
    }

    [MetalFact]
    public void Load_FromAReadWriteBuffer_AndStoreBackIntoIt()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> data = device.AllocateReadWriteBuffer(Iota(256));

        // The 8 x 8 block at the left of a 16-wide matrix, copied to its right.
        device.For(32, new WaveMatrixMove(data, from: 0, to: 8, stride: 16));
        float[] actual = data.ToArray();

        for (int row = 0; row < 8; row++)
        {
            Assert.Equal(actual[(row * 16)..((row * 16) + 8)], actual[((row * 16) + 8)..((row * 16) + 16)]);
            Assert.Equal(row * 16f, actual[(row * 16) + 8]);
        }

        Assert.Equal(Iota(256)[128..], actual[128..]);
    }

    [MetalFact]
    public void LoadAndStore_OnViews_AreRelativeToTheView()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer(Iota(256));
        using ReadWriteBuffer<float> destination = device.AllocateReadWriteBuffer<float>(256);
        destination.AsSpan().Fill(-1f);

        using ReadOnlyBuffer<float> sourceView = source.Slice(100);
        using ReadWriteBuffer<float> destinationView = destination.Slice(32, 64);

        device.For(32, new WaveMatrixCopy(sourceView, destinationView, 4, 8, 0, 0, 8, 0));
        float[] actual = destination.ToArray();

        Assert.All(actual[..32], static value => Assert.Equal(-1f, value));
        Assert.Equal(Iota(256)[104..168], actual[32..96]);
        Assert.All(actual[96..], static value => Assert.Equal(-1f, value));
    }

    // ---- values -----------------------------------------------------------------------------------

    [MetalFact]
    public void FilledDiagonalAndDefault_HaveTheirElements()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> destination = device.AllocateReadWriteBuffer<float>(256);
        destination.AsSpan().Fill(-1f);

        device.For(32, new WaveMatrixValues(destination, filled: 2.5f, diagonal: 3f));
        float[] actual = destination.ToArray();

        Assert.All(actual[..64], static value => Assert.Equal(2.5f, value));

        for (int i = 0; i < 64; i++)
        {
            Assert.Equal(i / 8 == i % 8 ? 3f : 0f, actual[64 + i]);
        }

        // default and new(): the zero matrix, on the GPU too (where a default-constructed matrix is uninitialized).
        Assert.All(actual[128..], static value => Assert.Equal(0f, value));
    }

    [MetalFact]
    public void HalfFilledDiagonalAndDefault_HaveTheirElements()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Half> destination = device.AllocateReadWriteBuffer<Half>(192);
        destination.AsSpan().Fill((Half)(-1f));

        device.For(32, new WaveMatrixHalfValues(destination, filled: (Half)0.75f, diagonal: (Half)(-4f)));
        float[] actual = destination.ToArray().Select(static h => (float)h).ToArray();

        Assert.All(actual[..64], static value => Assert.Equal(0.75f, value));

        for (int i = 0; i < 64; i++)
        {
            Assert.Equal(i / 8 == i % 8 ? -4f : 0f, actual[64 + i]);
        }

        Assert.All(actual[128..], static value => Assert.Equal(0f, value));
    }

    [MetalTheory]
    [InlineData(0, false, 0, false)]
    [InlineData(21, true, 9, false)]
    [InlineData(5, false, 30, true)]
    public void HalfLoadAndStore_MatchTheDocumentedElements(int loadOffset, bool loadTranspose, int storeOffset, bool storeTranspose)
    {
        const int length = 256, stride = 12;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<Half> source = device.AllocateReadOnlyBuffer(Iota(length).Select(static f => (Half)f).ToArray());
        using ReadWriteBuffer<Half> destination = device.AllocateReadWriteBuffer<Half>(length);
        destination.AsSpan().Fill((Half)(-1f));

        device.For(32, new WaveMatrixHalfCopy(source, destination, loadOffset, stride, loadTranspose ? 1 : 0, storeOffset, storeTranspose ? 1 : 0));

        float[] matrix = WaveMatrixReference.Load(Iota(length), loadOffset, stride, loadTranspose);
        float[] expected = Enumerable.Repeat(-1f, length).ToArray();
        WaveMatrixReference.Store(matrix, expected, storeOffset, stride, storeTranspose);

        Assert.Equal(expected, destination.ToArray().Select(static h => (float)h).ToArray());
    }

    // ---- products ---------------------------------------------------------------------------------

    [MetalFact]
    public void Multiply_OfSmallIntegers_IsExact()
    {
        // a[r][k] = r + k and b[k][c] = k - c: every product and sum is a small integer.
        float[] a = new float[64], b = new float[64], c = new float[64];

        for (int i = 0; i < 64; i++)
        {
            (a[i], b[i], c[i]) = ((i / 8) + (i % 8), (i / 8) - (i % 8), 100 + i);
        }

        float[] actual = Products(a, b, c, groups: 1);

        // Row 0 of a is 0..7 and column 0 of b is 0..7: the sum of squares, 140.
        Assert.Equal(140f, actual[0]);
        Assert.Equal(240f, actual[64]);
        Assert.Equal(WaveMatrixReference.Multiply(a, b, null).Select(static d => (float)d).ToArray(), actual[..64]);
        Assert.Equal(WaveMatrixReference.Multiply(a, b, c).Select(static d => (float)d).ToArray(), actual[64..]);
    }

    [MetalFact]
    public void Multiply_ByTheIdentity_IsTheMatrix()
    {
        float[] identity = new float[64], zero = new float[64];
        float[] a = WaveMatrixReference.Random(64, seed: 5);

        for (int i = 0; i < 8; i++)
        {
            identity[i * 9] = 1f;
        }

        Assert.Equal(a, Products(a, identity, zero, groups: 1)[..64]);
        Assert.Equal(a, Products(identity, a, zero, groups: 1)[..64]);
    }

    [MetalFact]
    public void MultiplyAccumulate_OnRandomData_MatchesADoubleReference()
    {
        // 40 products, one per threadgroup, each of 8 x 8 values in -1..1.
        const int groups = 40;
        float[] a = WaveMatrixReference.Random(groups * 64, seed: 1);
        float[] b = WaveMatrixReference.Random(groups * 64, seed: 2);
        float[] c = WaveMatrixReference.Random(groups * 64, seed: 3);

        float[] actual = Products(a, b, c, groups);

        for (int group = 0; group < groups; group++)
        {
            Range matrix = (group * 64)..((group + 1) * 64);
            double[] product = WaveMatrixReference.Multiply(a[matrix], b[matrix], null);
            double[] sum = WaveMatrixReference.Multiply(a[matrix], b[matrix], c[matrix]);

            // Eight products of values below 1 and a sum: a few ulp of 8, whatever the order.
            WaveMatrixReference.AssertClose(product, actual[(group * 128)..((group * 128) + 64)], 1e-5, $"product {group}");
            WaveMatrixReference.AssertClose(sum, actual[((group * 128) + 64)..((group + 1) * 128)], 1e-5, $"sum {group}");
        }
    }

    private static float[] Products(float[] a, float[] b, float[] c, int groups)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> left = device.AllocateReadOnlyBuffer(a);
        using ReadOnlyBuffer<float> right = device.AllocateReadOnlyBuffer(b);
        using ReadOnlyBuffer<float> addend = device.AllocateReadOnlyBuffer(c);
        using ReadWriteBuffer<float> result = device.AllocateReadWriteBuffer<float>(groups * 128);

        device.For(groups * 32, new WaveMatrixProducts(left, right, addend, result));
        return result.ToArray();
    }

    // ---- mixed precision --------------------------------------------------------------------------

    [MetalFact]
    public void EveryMixOfHalfAndFloat_GivesTheSameProduct()
    {
        // Small integers, so that every operand is exact as a half and every result is too.
        float[] a = new float[64], b = new float[64], c = new float[64];
        Random random = new(11);

        for (int i = 0; i < 64; i++)
        {
            (a[i], b[i], c[i]) = (random.Next(-4, 5), random.Next(-4, 5), random.Next(-20, 21));
        }

        (float[] floats, float[] halves) = WaveMatrixRuns.Mixed(a, b, c, cpu: false);
        float[] product = WaveMatrixReference.Multiply(a, b, null).Select(static d => (float)d).ToArray();
        float[] sum = WaveMatrixReference.Multiply(a, b, c).Select(static d => (float)d).ToArray();

        for (int combination = 0; combination < 12; combination++)
        {
            float[] expected = combination < 4 ? product : sum;
            Range matrix = (combination * 64)..((combination + 1) * 64);

            Assert.True(expected.SequenceEqual(floats[matrix]), $"float result, combination {combination}");
            Assert.True(expected.SequenceEqual(halves[matrix]), $"half result, combination {combination}");
        }
    }

    [MetalFact]
    public void HalfOperandsWithAFloatAccumulator_KeepFloatPrecision()
    {
        // Operands that are exact halves, an accumulator that is not: 1/3 survives only in a float result.
        float[] a = new float[64], b = new float[64], c = Enumerable.Repeat(1f / 3f, 64).ToArray();

        for (int i = 0; i < 64; i++)
        {
            (a[i], b[i]) = (((i * 7) % 5) * 0.25f, ((i * 3) % 7) * 0.5f);
        }

        (float[] floats, float[] halves) = WaveMatrixRuns.Mixed(a, b, c, cpu: false);
        double[] sum = WaveMatrixReference.Multiply(a, b, c);

        // Combination 10: half a, half b, float c.
        WaveMatrixReference.AssertClose(sum, floats[(10 * 64)..(11 * 64)], 1e-6, "float accumulator");

        // The half result of the same operands is the nearest half, or its neighbour: 2^-10 relative.
        WaveMatrixReference.AssertClose(sum, halves[(10 * 64)..(11 * 64)], 1.0 / 1024, "half result");
        Assert.Contains(Enumerable.Range(0, 64), i => floats[(10 * 64) + i] != halves[(10 * 64) + i]);
    }

    // ---- threadgroup memory -----------------------------------------------------------------------

    [MetalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupShared_LoadsAndStores_PerSimdGroup(bool transpose)
    {
        const int threadgroups = 3;
        float[] source = WaveMatrixReference.SmallIntegers(threadgroups * 256, seed: 21);

        (float[] products, float[] operands) = WaveMatrixRuns.Shared(source, threadgroups, transpose, cpu: false);
        (float[] expectedProducts, float[] expectedOperands) = WaveMatrixReference.Shared(source, threadgroups, transpose);

        Assert.Equal(expectedProducts, products);
        Assert.Equal(expectedOperands, operands);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    [MetalFact]
    public void Helpers_TakeReturnAndAccumulateMatrices()
    {
        float[] a = WaveMatrixReference.SmallIntegers(64, seed: 31), b = WaveMatrixReference.SmallIntegers(64, seed: 32);

        Assert.Equal(WaveMatrixReference.Helpers(a, b), WaveMatrixRuns.Helpers(a, b, cpu: false));
    }

    // ---- what Metal leaves undefined, pinned as measured ------------------------------------------

    [MetalTheory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(31)]
    public void APartialSimdGroup_StoresOnlyTheElementsItsLanesHold(int lanes)
    {
        // Measured on Apple GPUs, not promised by Metal: lane L holds two elements, at row
        // 4 * (L / 16) + (L % 8) / 2 and columns 4 * ((L / 8) % 2) + 2 * (L % 2) and the next. This is
        // why the CPU refuses a group that is not full rather than store a matrix that is.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer(Iota(64));
        using ReadWriteBuffer<float> destination = device.AllocateReadWriteBuffer<float>(64);
        destination.AsSpan().Fill(-1f);

        device.For(32, new WaveMatrixSomeLanes(source, destination, lanes));

        float[] expected = Enumerable.Repeat(-1f, 64).ToArray();

        for (int lane = 0; lane < lanes; lane++)
        {
            int row = (4 * (lane / 16)) + ((lane % 8) / 2);
            int column = (4 * ((lane / 8) % 2)) + (2 * (lane % 2));

            expected[(row * 8) + column] = (row * 8) + column;
            expected[(row * 8) + column + 1] = (row * 8) + column + 1;
        }

        Assert.Equal(expected, destination.ToArray());
    }
}

/// <summary>The dispatches the GPU tests and the CPU tests share: the same shader, on <c>For</c> or <c>ForCpu</c>.</summary>
internal static class WaveMatrixRuns
{
    public static (float[] Floats, float[] Halves) Mixed(float[] a, float[] b, float[] c, bool cpu)
    {
        static Half[] AsHalf(float[] values) => values.Select(static f => (Half)f).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> fa = device.AllocateReadOnlyBuffer(a), fb = device.AllocateReadOnlyBuffer(b), fc = device.AllocateReadOnlyBuffer(c);
        using ReadOnlyBuffer<Half> ha = device.AllocateReadOnlyBuffer(AsHalf(a)), hb = device.AllocateReadOnlyBuffer(AsHalf(b)), hc = device.AllocateReadOnlyBuffer(AsHalf(c));
        using ReadWriteBuffer<float> floats = device.AllocateReadWriteBuffer<float>(12 * 64);
        using ReadWriteBuffer<Half> halves = device.AllocateReadWriteBuffer<Half>(12 * 64);
        WaveMatrixMixed shader = new(fa, fb, fc, ha, hb, hc, floats, halves);

        if (cpu)
        {
            device.ForCpu(32, shader);
        }
        else
        {
            device.For(32, shader);
        }

        return (floats.ToArray(), halves.ToArray().Select(static h => (float)h).ToArray());
    }

    public static (float[] Products, float[] Operands) Shared(float[] source, int threadgroups, bool transpose, bool cpu)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> input = device.AllocateReadOnlyBuffer(source);
        using ReadWriteBuffer<float> products = device.AllocateReadWriteBuffer<float>(threadgroups * 128);
        using ReadWriteBuffer<Half> operands = device.AllocateReadWriteBuffer<Half>(threadgroups * 128);
        WaveMatrixShared shader = new(input, products, operands, transpose ? 1 : 0);

        if (cpu)
        {
            device.ForCpu(threadgroups * 64, shader);
        }
        else
        {
            device.For(threadgroups * 64, shader);
        }

        return (products.ToArray(), operands.ToArray().Select(static h => (float)h).ToArray());
    }

    public static float[] Helpers(float[] a, float[] b, bool cpu)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> left = device.AllocateReadOnlyBuffer(a), right = device.AllocateReadOnlyBuffer(b);
        using ReadWriteBuffer<float> result = device.AllocateReadWriteBuffer<float>(64);
        WaveMatrixHelpers shader = new(left, right, result);

        if (cpu)
        {
            device.ForCpu(32, shader);
        }
        else
        {
            device.For(32, shader);
        }

        return result.ToArray();
    }
}

/// <summary>
/// What the wave matrix operations mean, written out in plain loops and double precision: the
/// reference the GPU and the CPU are both held to.
/// </summary>
internal static class WaveMatrixReference
{
    /// <summary>Values in -1..1.</summary>
    public static float[] Random(int count, int seed)
    {
        Random random = new(seed);
        return Enumerable.Range(0, count).Select(_ => (random.NextSingle() * 2f) - 1f).ToArray();
    }

    /// <summary>Integers in -3..3: exact as halves, and so are the 8 × 8 products of two such matrices.</summary>
    public static float[] SmallIntegers(int count, int seed)
    {
        Random random = new(seed);
        return Enumerable.Range(0, count).Select(_ => (float)random.Next(-3, 4)).ToArray();
    }

    /// <summary>Element (row, column) is <c>source[offset + row * stride + column]</c>, or with the two swapped when transposed.</summary>
    public static float[] Load(float[] source, int offset, int stride, bool transpose)
    {
        float[] matrix = new float[64];

        for (int row = 0; row < 8; row++)
        {
            for (int column = 0; column < 8; column++)
            {
                matrix[(row * 8) + column] = source[transpose ? offset + (column * stride) + row : offset + (row * stride) + column];
            }
        }

        return matrix;
    }

    public static void Store(float[] matrix, float[] destination, int offset, int stride, bool transpose)
    {
        for (int row = 0; row < 8; row++)
        {
            for (int column = 0; column < 8; column++)
            {
                destination[transpose ? offset + (column * stride) + row : offset + (row * stride) + column] = matrix[(row * 8) + column];
            }
        }
    }

    /// <summary><c>a × b + c</c> (or <c>a × b</c> without <paramref name="c"/>) for row-major 8 × 8 matrices.</summary>
    public static double[] Multiply(float[] a, float[] b, float[]? c)
    {
        double[] result = new double[64];

        for (int row = 0; row < 8; row++)
        {
            for (int column = 0; column < 8; column++)
            {
                double sum = c is null ? 0 : c[(row * 8) + column];

                for (int k = 0; k < 8; k++)
                {
                    sum += (double)a[(row * 8) + k] * b[(k * 8) + column];
                }

                result[(row * 8) + column] = sum;
            }
        }

        return result;
    }

    /// <summary>Every element within <paramref name="tolerance"/> of the reference, relative to 1 or to the reference if that is larger.</summary>
    public static void AssertClose(double[] expected, ReadOnlySpan<float> actual, double tolerance, string what)
    {
        Assert.Equal(expected.Length, actual.Length);

        for (int i = 0; i < expected.Length; i++)
        {
            double allowed = tolerance * Math.Max(1.0, Math.Abs(expected[i]));
            Assert.True(Math.Abs(expected[i] - actual[i]) <= allowed, $"{what}, element {i}: expected {expected[i]:R}, actual {actual[i]:R} (tolerance {allowed:R}).");
        }
    }

    /// <summary>What <see cref="WaveMatrixShared"/> computes, per threadgroup and SIMD group.</summary>
    public static (float[] Products, float[] Operands) Shared(float[] source, int threadgroups, bool transpose)
    {
        float[] products = new float[threadgroups * 128], operands = new float[threadgroups * 128];

        for (int threadgroup = 0; threadgroup < threadgroups; threadgroup++)
        {
            float[] plain = source[(threadgroup * 256)..((threadgroup + 1) * 256)];
            float[] tile = plain.Select(static f => f * 2f).ToArray();

            for (int group = 0; group < 2; group++)
            {
                float[] a = Load(tile, group * 8, WaveMatrixShared.BlockStride, transpose);
                float[] b = Load(tile, (8 * WaveMatrixShared.BlockStride) + (group * 8), WaveMatrixShared.BlockStride, false);
                float[] product = Multiply(a, b, null).Select(static d => (float)d).ToArray();

                Store(product, products, (threadgroup * 128) + (group * 64), 8, false);
                Store(Load(plain, group * 8, WaveMatrixShared.BlockStride, transpose), operands, (threadgroup * 128) + (group * 64), 8, true);
            }
        }

        return (products, operands);
    }

    /// <summary>What <see cref="WaveMatrixHelpers"/> computes: <c>(a × b + identity) × a</c>.</summary>
    public static float[] Helpers(float[] a, float[] b)
    {
        float[] identity = new float[64];

        for (int i = 0; i < 8; i++)
        {
            identity[i * 9] = 1f;
        }

        float[] sum = Multiply(a, b, identity).Select(static d => (float)d).ToArray();
        return Multiply(sum, a, null).Select(static d => (float)d).ToArray();
    }
}
