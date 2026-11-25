using System;

namespace SharpCompute.Tests.Shaders;

// Generated shaders for the wave matrix tests. A matrix is held by the 32 lanes of a SIMD group, so
// every shader has a threadgroup size that is a multiple of 32 and computes its arguments from what a
// whole SIMD group shares. Every matrix operation is on a line of its own (the CPU identifies a call
// site by its line). The transpose flags are ints: a bool cannot be captured.

/// <summary>Loads one matrix from <c>source</c> and stores it to <c>destination</c>, each with its own offset, stride and transpose flag.</summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixCopy(
    ReadOnlyBuffer<float> source, ReadWriteBuffer<float> destination, int loadOffset, int loadStride, int loadTranspose, int storeOffset, int storeStride, int storeTranspose)
    : IComputeShader
{
    public void Execute()
    {
        WaveMatrix8x8 matrix = WaveMatrix8x8.Load(source, loadOffset, loadStride, loadTranspose != 0);
        matrix.Store(destination, storeOffset, storeStride, storeTranspose != 0);
    }
}

/// <summary>The same, read from and written to one read-write buffer: a matrix moved within it.</summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixMove(ReadWriteBuffer<float> data, int from, int to, int stride) : IComputeShader
{
    public void Execute()
    {
        WaveMatrix8x8 matrix = WaveMatrix8x8.Load(data, from, stride);
        matrix.Store(data, to, stride);
    }
}

/// <summary>The matrices that are made of a value: filled, diagonal, <c>default</c> and <c>new()</c>, stored one after another.</summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixValues(ReadWriteBuffer<float> destination, float filled, float diagonal) : IComputeShader
{
    public void Execute()
    {
        WaveMatrix8x8 a = WaveMatrix8x8.Filled(filled);
        WaveMatrix8x8 b = WaveMatrix8x8.Diagonal(diagonal);
        WaveMatrix8x8 c = default;
        WaveMatrix8x8 d = new();

        a.Store(destination, 0, 8);
        b.Store(destination, 64, 8);
        c.Store(destination, 128, 8);
        d.Store(destination, 192, 8);
    }
}

/// <summary>The same for half matrices.</summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixHalfValues(ReadWriteBuffer<Half> destination, Half filled, Half diagonal) : IComputeShader
{
    public void Execute()
    {
        WaveMatrixHalf8x8 a = WaveMatrixHalf8x8.Filled(filled);
        WaveMatrixHalf8x8 b = WaveMatrixHalf8x8.Diagonal(diagonal);
        WaveMatrixHalf8x8 c = default;

        a.Store(destination, 0, 8);
        b.Store(destination, 64, 8);
        c.Store(destination, 128, 8);
    }
}

/// <summary>Loads a half matrix and stores it, with offsets, a stride and transpose flags.</summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixHalfCopy(
    ReadOnlyBuffer<Half> source, ReadWriteBuffer<Half> destination, int loadOffset, int stride, int loadTranspose, int storeOffset, int storeTranspose) : IComputeShader
{
    public void Execute()
    {
        WaveMatrixHalf8x8 matrix = WaveMatrixHalf8x8.Load(source, loadOffset, stride, loadTranspose != 0);
        matrix.Store(destination, storeOffset, stride, storeTranspose != 0);
    }
}

/// <summary>
/// <c>a × b</c> at 0 and <c>a × b + c</c> at 64 of <c>result</c>, for 8 × 8 matrices stored back to
/// back. One SIMD group per threadgroup, and one product per threadgroup: group <c>g</c> of the
/// dispatch multiplies the <c>g</c>-th matrices of the buffers.
/// </summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixProducts(ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadOnlyBuffer<float> c, ReadWriteBuffer<float> result) : IComputeShader
{
    public void Execute()
    {
        int group = ThreadGroupIds.X;
        WaveMatrix8x8 left = WaveMatrix8x8.Load(a, group * 64, 8);
        WaveMatrix8x8 right = WaveMatrix8x8.Load(b, group * 64, 8);
        WaveMatrix8x8 addend = WaveMatrix8x8.Load(c, group * 64, 8);
        WaveMatrix8x8 product = WaveMatrix8x8.Multiply(left, right);
        WaveMatrix8x8 sum = WaveMatrix8x8.MultiplyAccumulate(left, right, addend);

        product.Store(result, group * 128, 8);
        sum.Store(result, (group * 128) + 64, 8);
    }
}

/// <summary>
/// Every mix of half and float that a product takes: the four <c>Multiply</c> and the eight
/// <c>MultiplyAccumulate</c> overloads of each result type, on the same three matrices held as floats
/// and as halves. Matrix <c>i</c> of <c>floats</c> and of <c>halves</c> is the <c>i</c>-th combination,
/// the operands counting through float (0) and half (1) with the last one fastest.
/// </summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixMixed(
    ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadOnlyBuffer<float> c,
    ReadOnlyBuffer<Half> halfA, ReadOnlyBuffer<Half> halfB, ReadOnlyBuffer<Half> halfC,
    ReadWriteBuffer<float> floats, ReadWriteBuffer<Half> halves) : IComputeShader
{
    public void Execute()
    {
        WaveMatrix8x8 fa = WaveMatrix8x8.Load(a, 0, 8);
        WaveMatrix8x8 fb = WaveMatrix8x8.Load(b, 0, 8);
        WaveMatrix8x8 fc = WaveMatrix8x8.Load(c, 0, 8);
        WaveMatrixHalf8x8 ha = WaveMatrixHalf8x8.Load(halfA, 0, 8);
        WaveMatrixHalf8x8 hb = WaveMatrixHalf8x8.Load(halfB, 0, 8);
        WaveMatrixHalf8x8 hc = WaveMatrixHalf8x8.Load(halfC, 0, 8);

        // Multiply, float results.
        WaveMatrix8x8 f0 = WaveMatrix8x8.Multiply(fa, fb);
        WaveMatrix8x8 f1 = WaveMatrix8x8.Multiply(fa, hb);
        WaveMatrix8x8 f2 = WaveMatrix8x8.Multiply(ha, fb);
        WaveMatrix8x8 f3 = WaveMatrix8x8.Multiply(ha, hb);

        // MultiplyAccumulate, float results.
        WaveMatrix8x8 f4 = WaveMatrix8x8.MultiplyAccumulate(fa, fb, fc);
        WaveMatrix8x8 f5 = WaveMatrix8x8.MultiplyAccumulate(fa, fb, hc);
        WaveMatrix8x8 f6 = WaveMatrix8x8.MultiplyAccumulate(fa, hb, fc);
        WaveMatrix8x8 f7 = WaveMatrix8x8.MultiplyAccumulate(fa, hb, hc);
        WaveMatrix8x8 f8 = WaveMatrix8x8.MultiplyAccumulate(ha, fb, fc);
        WaveMatrix8x8 f9 = WaveMatrix8x8.MultiplyAccumulate(ha, fb, hc);
        WaveMatrix8x8 f10 = WaveMatrix8x8.MultiplyAccumulate(ha, hb, fc);
        WaveMatrix8x8 f11 = WaveMatrix8x8.MultiplyAccumulate(ha, hb, hc);

        f0.Store(floats, 0 * 64, 8);
        f1.Store(floats, 1 * 64, 8);
        f2.Store(floats, 2 * 64, 8);
        f3.Store(floats, 3 * 64, 8);
        f4.Store(floats, 4 * 64, 8);
        f5.Store(floats, 5 * 64, 8);
        f6.Store(floats, 6 * 64, 8);
        f7.Store(floats, 7 * 64, 8);
        f8.Store(floats, 8 * 64, 8);
        f9.Store(floats, 9 * 64, 8);
        f10.Store(floats, 10 * 64, 8);
        f11.Store(floats, 11 * 64, 8);

        // Multiply, half results.
        WaveMatrixHalf8x8 h0 = WaveMatrixHalf8x8.Multiply(fa, fb);
        WaveMatrixHalf8x8 h1 = WaveMatrixHalf8x8.Multiply(fa, hb);
        WaveMatrixHalf8x8 h2 = WaveMatrixHalf8x8.Multiply(ha, fb);
        WaveMatrixHalf8x8 h3 = WaveMatrixHalf8x8.Multiply(ha, hb);

        // MultiplyAccumulate, half results.
        WaveMatrixHalf8x8 h4 = WaveMatrixHalf8x8.MultiplyAccumulate(fa, fb, fc);
        WaveMatrixHalf8x8 h5 = WaveMatrixHalf8x8.MultiplyAccumulate(fa, fb, hc);
        WaveMatrixHalf8x8 h6 = WaveMatrixHalf8x8.MultiplyAccumulate(fa, hb, fc);
        WaveMatrixHalf8x8 h7 = WaveMatrixHalf8x8.MultiplyAccumulate(fa, hb, hc);
        WaveMatrixHalf8x8 h8 = WaveMatrixHalf8x8.MultiplyAccumulate(ha, fb, fc);
        WaveMatrixHalf8x8 h9 = WaveMatrixHalf8x8.MultiplyAccumulate(ha, fb, hc);
        WaveMatrixHalf8x8 h10 = WaveMatrixHalf8x8.MultiplyAccumulate(ha, hb, fc);
        WaveMatrixHalf8x8 h11 = WaveMatrixHalf8x8.MultiplyAccumulate(ha, hb, hc);

        h0.Store(halves, 0 * 64, 8);
        h1.Store(halves, 1 * 64, 8);
        h2.Store(halves, 2 * 64, 8);
        h3.Store(halves, 3 * 64, 8);
        h4.Store(halves, 4 * 64, 8);
        h5.Store(halves, 5 * 64, 8);
        h6.Store(halves, 6 * 64, 8);
        h7.Store(halves, 7 * 64, 8);
        h8.Store(halves, 8 * 64, 8);
        h9.Store(halves, 9 * 64, 8);
        h10.Store(halves, 10 * 64, 8);
        h11.Store(halves, 11 * 64, 8);
    }
}

/// <summary>
/// Matrices in threadgroup memory. A threadgroup of 64 threads (two SIMD groups) copies two 8 × 16
/// blocks of <c>source</c> into a group-shared array, doubling them, and each SIMD group then loads an
/// 8 × 8 matrix out of its half of every block (transposed when <c>transpose</c> is set), multiplies
/// the two, and stores the product to a second group-shared array (float) and its first operand to a
/// third (half), from where the threads copy both out.
/// </summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixShared(ReadOnlyBuffer<float> source, ReadWriteBuffer<float> products, ReadWriteBuffer<Half> operands, int transpose) : IComputeShader
{
    public const int BlockStride = 16;

    [GroupShared]
    private static readonly float[] tile = new float[2 * 8 * BlockStride];

    [GroupShared]
    private static readonly float[] staged = new float[2 * 64];

    [GroupShared]
    private static readonly Half[] halves = new Half[2 * 8 * BlockStride];

    [GroupShared]
    private static readonly Half[] stagedHalves = new Half[2 * 64];

    public void Execute()
    {
        int thread = GroupIds.Index;
        int group = SimdGroupIds.Index;
        int groupOrigin = ThreadGroupIds.X * 256;

        // 256 elements, four per thread.
        for (int i = thread; i < 256; i += 64)
        {
            tile[i] = source[groupOrigin + i] * 2f;
            halves[i] = (Half)source[groupOrigin + i];
        }

        Gpu.GroupMemoryBarrierWithGroupSync();

        // SIMD group g works on columns 8g..8g+7 of both blocks.
        WaveMatrix8x8 a = WaveMatrix8x8.Load(tile, group * 8, BlockStride, transpose != 0);
        WaveMatrix8x8 b = WaveMatrix8x8.Load(tile, (8 * BlockStride) + (group * 8), BlockStride);
        WaveMatrix8x8 product = WaveMatrix8x8.Multiply(a, b);
        WaveMatrixHalf8x8 half = WaveMatrixHalf8x8.Load(halves, group * 8, BlockStride, transpose != 0);

        product.Store(staged, group * 64, 8);
        half.Store(stagedHalves, group * 64, 8, true);
        Gpu.GroupMemoryBarrierWithGroupSync();

        for (int i = thread; i < 128; i += 64)
        {
            products[(ThreadGroupIds.X * 128) + i] = staged[i];
            operands[(ThreadGroupIds.X * 128) + i] = stagedHalves[i];
        }
    }
}

/// <summary>
/// Matrices through helpers: one that loads from a captured buffer, one that takes and returns
/// matrices by value, and one that accumulates into a matrix passed by reference.
/// <c>result = (a × b + identity) × a</c> for the 8 × 8 matrices at the start of the buffers.
/// </summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixHelpers(ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadWriteBuffer<float> result) : IComputeShader
{
    private WaveMatrix8x8 LoadA(int offset) => WaveMatrix8x8.Load(a, offset, 8);

    private static WaveMatrix8x8 Times(WaveMatrix8x8 left, WaveMatrix8x8 right) => WaveMatrix8x8.Multiply(left, right);

    private static void Accumulate(ref WaveMatrix8x8 sum, WaveMatrix8x8 left, WaveMatrix8x8 right)
    {
        sum = WaveMatrix8x8.MultiplyAccumulate(left, right, sum);
    }

    public void Execute()
    {
        WaveMatrix8x8 left = LoadA(0);
        WaveMatrix8x8 right = WaveMatrix8x8.Load(b, 0, 8);
        WaveMatrix8x8 sum = WaveMatrix8x8.Diagonal(1f);

        Accumulate(ref sum, left, right);

        WaveMatrix8x8 product = Times(sum, left);
        product.Store(result, 0, 8);
    }
}

/// <summary>
/// Loads and stores beside reads and writes of the same buffer, each in a statement of its own (in
/// one expression they are SCG0021): the index is read before the helper stores the matrix over it,
/// and the first load is done before the helper writes an element for the second.
/// </summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixSplitOrder(ReadWriteBuffer<float> data, ReadWriteBuffer<float> result) : IComputeShader
{
    private int Save(WaveMatrix8x8 matrix)
    {
        matrix.Store(data, 0, 8);
        return 1;
    }

    private int Poke()
    {
        data[0] = 5f;
        return 0;
    }

    public void Execute()
    {
        WaveMatrix8x8 sevens = WaveMatrix8x8.Filled(7f);

        // Unsplit, `result[(int)data[0]] = Save(sevens)` would index with the 7 the helper stores.
        int index = (int)data[0];
        int saved = Save(sevens);
        result[index] = saved;

        WaveMatrix8x8 before = WaveMatrix8x8.Load(data, 0, 8);
        int offset = Poke();
        WaveMatrix8x8 after = WaveMatrix8x8.Load(data, offset, 8);
        WaveMatrix8x8 product = WaveMatrix8x8.Multiply(before, after);
        product.Store(result, 64, 8);
    }
}

/// <summary>
/// The example of docs/matrix-units.md, as it is printed there: one SIMD group per threadgroup computes an 8 × 8
/// block of <c>c = a × b</c> for n × n row-major matrices.
/// </summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MatMulBlocks(ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadWriteBuffer<float> c, int n) : IComputeShader
{
    public void Execute()
    {
        int row = ThreadGroupIds.Y * 8;                    // the same in all 32 lanes
        int column = ThreadGroupIds.X * 8;
        WaveMatrix8x8 sum = default;                       // the zero matrix

        for (int k = 0; k < n; k += 8)
        {
            WaveMatrix8x8 left = WaveMatrix8x8.Load(a, row * n + k, n);       // 8 x 8 of a, rows n apart
            WaveMatrix8x8 right = WaveMatrix8x8.Load(b, k * n + column, n);
            sum = WaveMatrix8x8.MultiplyAccumulate(left, right, sum);         // left x right + sum
        }

        sum.Store(c, row * n + column, n);
    }
}

/// <summary>
/// A copy whose load offset is a sum of two captured terms. On the GPU each term is added to the
/// buffer's address on its own, in 64 bits; in C# the sum is an <c>int</c> and wraps.
/// </summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixOffsetSum(ReadOnlyBuffer<float> source, ReadWriteBuffer<float> destination, int first, int second) : IComputeShader
{
    public void Execute()
    {
        WaveMatrix8x8 matrix = WaveMatrix8x8.Load(source, first + second, 8);
        matrix.Store(destination, 0, 8);
    }
}

// ---- misuse: what the CPU refuses -------------------------------------------------------------

/// <summary>The lanes at or past <c>lanes</c> return before the matrix operations: a SIMD group that is not full.</summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixSomeLanes(ReadOnlyBuffer<float> source, ReadWriteBuffer<float> destination, int lanes) : IComputeShader
{
    public void Execute()
    {
        if (Gpu.WaveGetLaneIndex() >= lanes)
        {
            return;
        }

        WaveMatrix8x8 matrix = WaveMatrix8x8.Load(source, 0, 8);
        matrix.Store(destination, 0, 8);
    }
}

/// <summary>Every lane loads from an offset of its own: arguments that differ within the SIMD group.</summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixLaneOffsets(ReadOnlyBuffer<float> source, ReadWriteBuffer<float> destination) : IComputeShader
{
    public void Execute()
    {
        WaveMatrix8x8 matrix = WaveMatrix8x8.Load(source, Gpu.WaveGetLaneIndex() * 64, 8);
        matrix.Store(destination, 0, 8);
    }
}

/// <summary>Every lane fills with a value of its own.</summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixLaneValues(ReadWriteBuffer<float> destination) : IComputeShader
{
    public void Execute()
    {
        WaveMatrix8x8 matrix = WaveMatrix8x8.Filled(Gpu.WaveGetLaneIndex());
        matrix.Store(destination, 0, 8);
    }
}

/// <summary>The two halves of a SIMD group make their matrix at different calls, and then store "the" matrix together.</summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixSplitGroup(ReadWriteBuffer<float> destination) : IComputeShader
{
    public void Execute()
    {
        WaveMatrix8x8 matrix;

        if (Gpu.WaveGetLaneIndex() < 16)
        {
            matrix = WaveMatrix8x8.Filled(1f);
        }
        else
        {
            matrix = WaveMatrix8x8.Diagonal(2f);
        }

        matrix.Store(destination, 0, 8);
    }
}

/// <summary>Half of the lanes take the zero matrix, half a filled one, and all of them store: different matrices at one call.</summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct WaveMatrixTwoMatrices(ReadWriteBuffer<float> destination) : IComputeShader
{
    public void Execute()
    {
        WaveMatrix8x8 ones = WaveMatrix8x8.Filled(1f);
        WaveMatrix8x8 matrix = Gpu.WaveGetLaneIndex() < 16 ? ones : default;

        matrix.Store(destination, 0, 8);
    }
}
