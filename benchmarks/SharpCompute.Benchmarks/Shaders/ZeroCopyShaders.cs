namespace SharpCompute.Benchmarks.Shaders;

// Shaders for the zero-copy workloads (Workloads/ImagePipeline.cs and Workloads/FileToGpu.cs).
// The RGBA8 blur weights are pre-scaled by 1/255, so unpacking a byte is one int-to-float conversion.

/// <summary>Horizontal blur pass that reads packed RGBA8 pixels (bytes R, G, B, A; one <c>uint</c> each) straight from the decoder's buffer.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BlurRgba8Horizontal(
    ReadOnlyBuffer<uint> source, ReadWriteBuffer<Float4> destination, ReadOnlyBuffer<float> scaledWeights, int width, int radius) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int row = ThreadIds.Y * width;
        Float4 sum = new(0f, 0f, 0f, 0f);

        for (int k = -radius; k <= radius; k++)
        {
            uint p = source[row + Gpu.Clamp(x + k, 0, width - 1)];
            Float4 pixel = new((float)(p & 0xFFu), (float)((p >> 8) & 0xFFu), (float)((p >> 16) & 0xFFu), (float)(p >> 24));
            sum += pixel * scaledWeights[k + radius];
        }

        destination[row + x] = sum;
    }
}

/// <summary>Vertical blur pass that writes packed RGBA8 pixels, rounding to nearest, ready for the encoder.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BlurRgba8Vertical(
    ReadWriteBuffer<Float4> source, ReadWriteBuffer<uint> destination, ReadOnlyBuffer<float> weights, int width, int height, int radius) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float4 sum = new(0f, 0f, 0f, 0f);

        for (int k = -radius; k <= radius; k++)
        {
            sum += source[(Gpu.Clamp(y + k, 0, height - 1) * width) + x] * weights[k + radius];
        }

        Float4 q = (Gpu.Saturate(sum) * 255f) + 0.5f;
        destination[(y * width) + x] = (uint)q.X | ((uint)q.Y << 8) | ((uint)q.Z << 16) | ((uint)q.W << 24);
    }
}

/// <summary>Horizontal blur pass over <see cref="Float4"/> pixels (the copy path: the CPU converted them already).</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BlurFloat4Horizontal(
    ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination, ReadOnlyBuffer<float> weights, int width, int radius) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int row = ThreadIds.Y * width;
        Float4 sum = new(0f, 0f, 0f, 0f);

        for (int k = -radius; k <= radius; k++)
        {
            sum += source[row + Gpu.Clamp(x + k, 0, width - 1)] * weights[k + radius];
        }

        destination[row + x] = sum;
    }
}

/// <summary>Vertical blur pass over <see cref="Float4"/> pixels (the copy path: the CPU converts them back).</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BlurFloat4Vertical(
    ReadWriteBuffer<Float4> source, ReadWriteBuffer<Float4> destination, ReadOnlyBuffer<float> weights, int width, int height, int radius) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float4 sum = new(0f, 0f, 0f, 0f);

        for (int k = -radius; k <= radius; k++)
        {
            sum += source[(Gpu.Clamp(y + k, 0, height - 1) * width) + x] * weights[k + radius];
        }

        destination[(y * width) + x] = sum;
    }
}

/// <summary>
/// Partial sums for a reduction: thread <c>i</c> of <c>T</c> adds <c>data[i + j·T]</c> for
/// <c>j &lt; count</c>, so neighbouring threads read neighbouring floats.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct StridedPartialSums(ReadOnlyBuffer<float> data, ReadWriteBuffer<float> partials, int count) : IComputeShader
{
    public void Execute()
    {
        int threads = DispatchSize.X;
        float sum = 0f;

        for (int j = 0; j < count; j++)
        {
            sum += data[ThreadIds.X + (j * threads)];
        }

        partials[ThreadIds.X] = sum;
    }
}

/// <summary><see cref="StridedPartialSums"/> over a read/write buffer (the one-copy variant reads into one).</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct StridedPartialSumsReadWrite(ReadWriteBuffer<float> data, ReadWriteBuffer<float> partials, int count) : IComputeShader
{
    public void Execute()
    {
        int threads = DispatchSize.X;
        float sum = 0f;

        for (int j = 0; j < count; j++)
        {
            sum += data[ThreadIds.X + (j * threads)];
        }

        partials[ThreadIds.X] = sum;
    }
}
