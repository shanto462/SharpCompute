namespace SharpCompute.Benchmarks.Shaders;

/// <summary>
/// Row-major <c>C = A · B</c> for n × n matrices on the GPU's matrix units: a threadgroup of 128
/// threads (four SIMD groups, 2 × 2) computes a 32 × 64 tile of C, each SIMD group 16 × 32 of it as
/// 2 × 4 wave matrices that accumulate 8 × 8 products. <c>n</c> must be a multiple of 64; dispatch
/// <c>n / 64 * 128</c> by <c>n / 32</c> threads.
/// </summary>
/// <remarks>
/// The shape is the one that measured fastest on Apple M4 Pro: eight accumulators per SIMD group
/// (sixteen spill out of the registers), operands loaded straight from the buffers (staging them in
/// threadgroup memory is slower), the loop over the shared dimension unrolled twice, and every offset
/// written as a sum, which the generator turns into one pointer addition per term.
/// </remarks>
[ThreadGroupSize(MatMulWaveMatrix.GroupThreads)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MatMulWaveMatrix(ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadWriteBuffer<float> c, int n) : IComputeShader
{
    /// <summary>Threads of a threadgroup: four SIMD groups of 32 lanes.</summary>
    public const int GroupThreads = 128;

    /// <summary>Rows of <c>c</c> a threadgroup computes.</summary>
    public const int TileRows = 32;

    /// <summary>Columns of <c>c</c> a threadgroup computes.</summary>
    public const int TileColumns = 64;

    public void Execute()
    {
        int group = SimdGroupIds.Index;
        int row = (ThreadGroupIds.Y * TileRows) + (group / 2 * 16);
        int column = (ThreadGroupIds.X * TileColumns) + (group % 2 * 32);

        WaveMatrix8x8 c00 = default;
        WaveMatrix8x8 c01 = default;
        WaveMatrix8x8 c02 = default;
        WaveMatrix8x8 c03 = default;
        WaveMatrix8x8 c10 = default;
        WaveMatrix8x8 c11 = default;
        WaveMatrix8x8 c12 = default;
        WaveMatrix8x8 c13 = default;

        for (int k = 0; k < n; k += 16)
        {
            WaveMatrix8x8 a0 = WaveMatrix8x8.Load(a, row * n + k, n);
            WaveMatrix8x8 a1 = WaveMatrix8x8.Load(a, (row + 8) * n + k, n);
            WaveMatrix8x8 b0 = WaveMatrix8x8.Load(b, k * n + column, n);
            WaveMatrix8x8 b1 = WaveMatrix8x8.Load(b, k * n + column + 8, n);
            WaveMatrix8x8 b2 = WaveMatrix8x8.Load(b, k * n + column + 16, n);
            WaveMatrix8x8 b3 = WaveMatrix8x8.Load(b, k * n + column + 24, n);

            c00 = WaveMatrix8x8.MultiplyAccumulate(a0, b0, c00);
            c01 = WaveMatrix8x8.MultiplyAccumulate(a0, b1, c01);
            c02 = WaveMatrix8x8.MultiplyAccumulate(a0, b2, c02);
            c03 = WaveMatrix8x8.MultiplyAccumulate(a0, b3, c03);
            c10 = WaveMatrix8x8.MultiplyAccumulate(a1, b0, c10);
            c11 = WaveMatrix8x8.MultiplyAccumulate(a1, b1, c11);
            c12 = WaveMatrix8x8.MultiplyAccumulate(a1, b2, c12);
            c13 = WaveMatrix8x8.MultiplyAccumulate(a1, b3, c13);

            a0 = WaveMatrix8x8.Load(a, row * n + k + 8, n);
            a1 = WaveMatrix8x8.Load(a, (row + 8) * n + k + 8, n);
            b0 = WaveMatrix8x8.Load(b, (k + 8) * n + column, n);
            b1 = WaveMatrix8x8.Load(b, (k + 8) * n + column + 8, n);
            b2 = WaveMatrix8x8.Load(b, (k + 8) * n + column + 16, n);
            b3 = WaveMatrix8x8.Load(b, (k + 8) * n + column + 24, n);

            c00 = WaveMatrix8x8.MultiplyAccumulate(a0, b0, c00);
            c01 = WaveMatrix8x8.MultiplyAccumulate(a0, b1, c01);
            c02 = WaveMatrix8x8.MultiplyAccumulate(a0, b2, c02);
            c03 = WaveMatrix8x8.MultiplyAccumulate(a0, b3, c03);
            c10 = WaveMatrix8x8.MultiplyAccumulate(a1, b0, c10);
            c11 = WaveMatrix8x8.MultiplyAccumulate(a1, b1, c11);
            c12 = WaveMatrix8x8.MultiplyAccumulate(a1, b2, c12);
            c13 = WaveMatrix8x8.MultiplyAccumulate(a1, b3, c13);
        }

        c00.Store(c, row * n + column, n);
        c01.Store(c, row * n + column + 8, n);
        c02.Store(c, row * n + column + 16, n);
        c03.Store(c, row * n + column + 24, n);
        c10.Store(c, (row + 8) * n + column, n);
        c11.Store(c, (row + 8) * n + column + 8, n);
        c12.Store(c, (row + 8) * n + column + 16, n);
        c13.Store(c, (row + 8) * n + column + 24, n);
    }
}
