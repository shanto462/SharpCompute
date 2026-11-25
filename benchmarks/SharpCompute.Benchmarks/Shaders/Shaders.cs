using SharpCompute;

namespace SharpCompute.Benchmarks.Shaders;

// The generated shaders the benchmarks dispatch. Each has a CPU twin in Workloads/ that computes
// the same thing in the same order, which --verify compares against.

/// <summary><c>y = a * x + y</c>, one thread per element.</summary>
[ThreadGroupSize(256)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SaxpyGenerated(ReadOnlyBuffer<float> x, ReadWriteBuffer<float> y, float a) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        y[i] = (a * x[i]) + y[i];
    }
}

/// <summary>
/// Escape-time Mandelbrot. The iteration count goes in the low 16 bits (R = low byte, G = high
/// byte), B is an integer shade and A is 255, so the count can be decoded from the RGBA pixel.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct MandelbrotIterations(
    ReadWriteBuffer<uint> pixels, int width, int height, int maxIterations, Float2 center, float step) : IComputeShader
{
    public void Execute()
    {
        int px = ThreadIds.X;
        int py = ThreadIds.Y;

        float cx = center.X + ((px - (width * 0.5f)) * step);
        float cy = center.Y + ((py - (height * 0.5f)) * step);

        float x = 0f;
        float y = 0f;
        float x2 = 0f;
        float y2 = 0f;
        int n = 0;

        while (n < maxIterations && x2 + y2 <= 4f)
        {
            y = (2f * x * y) + cy;
            x = x2 - y2 + cx;
            x2 = x * x;
            y2 = y * y;
            n++;
        }

        uint count = (uint)n;
        uint shade = n < maxIterations ? 255u - ((count * 8u) & 255u) : 0u;
        pixels[(py * width) + px] = (count & 0xFFFFu) | (shade << 16) | (255u << 24);
    }
}

/// <summary>Horizontal pass of a separable blur: <c>dst[y, x] = Σ w[k] · src[y, clamp(x + k − r)]</c>.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BlurHorizontal(
    ReadOnlyBuffer<float> source, ReadWriteBuffer<float> destination, ReadOnlyBuffer<float> weights, int width, int radius) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int row = ThreadIds.Y * width;
        float sum = 0f;

        for (int k = -radius; k <= radius; k++)
        {
            int sx = Gpu.Clamp(x + k, 0, width - 1);
            sum += weights[k + radius] * source[row + sx];
        }

        destination[row + x] = sum;
    }
}

/// <summary>Vertical pass of a separable blur: <c>dst[y, x] = Σ w[k] · src[clamp(y + k − r), x]</c>.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BlurVertical(
    ReadWriteBuffer<float> source, ReadWriteBuffer<float> destination, ReadOnlyBuffer<float> weights, int width, int height, int radius) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        float sum = 0f;

        for (int k = -radius; k <= radius; k++)
        {
            int sy = Gpu.Clamp(y + k, 0, height - 1);
            sum += weights[k + radius] * source[(sy * width) + x];
        }

        destination[(y * width) + x] = sum;
    }
}

/// <summary>Naive row-major <c>C = A · B</c> for n × n matrices, one thread per output element.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct MatMulNaive(ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadWriteBuffer<float> c, int n) : IComputeShader
{
    public void Execute()
    {
        int col = ThreadIds.X;
        int row = ThreadIds.Y;
        float sum = 0f;

        for (int k = 0; k < n; k++)
        {
            sum += a[(row * n) + k] * b[(k * n) + col];
        }

        c[(row * n) + col] = sum;
    }
}

/// <summary>
/// One O(n²) N-body step with Plummer softening. Positions are xyz + mass, velocities xyz + unused.
/// Reads the current state and writes the next one to separate buffers, so repeated runs are identical.
/// </summary>
[ThreadGroupSize(256)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct NBodyStep(
    ReadOnlyBuffer<Float4> positions,
    ReadOnlyBuffer<Float4> velocities,
    ReadWriteBuffer<Float4> nextPositions,
    ReadWriteBuffer<Float4> nextVelocities,
    int count,
    float dt,
    float softeningSquared) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        Float4 self = positions[i];
        Float3 p = self.XYZ;
        Float3 acc = new(0f);

        for (int j = 0; j < count; j++)
        {
            Float4 other = positions[j];
            Float3 d = other.XYZ - p;
            float distSquared = Gpu.Dot(d, d) + softeningSquared;
            float inv = 1f / Gpu.Sqrt(distSquared);
            float s = other.W * inv * inv * inv;
            acc += d * s;
        }

        Float4 v = velocities[i];
        Float3 nv = v.XYZ + (acc * dt);
        Float3 np = p + (nv * dt);
        nextVelocities[i] = new Float4(nv, v.W);
        nextPositions[i] = new Float4(np, self.W);
    }
}

// The shaders of the --runtime-features measurements.

/// <summary><see cref="SaxpyGenerated"/> compiled with FP contraction off: a multiply, then an add, as on the CPU.</summary>
[ThreadGroupSize(256)]
[ShaderOptions(FpContract = false)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SaxpyNoContract(ReadOnlyBuffer<float> x, ReadWriteBuffer<float> y, float a) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        y[i] = (a * x[i]) + y[i];
    }
}

/// <summary><see cref="MandelbrotIterations"/> compiled with FP contraction off.</summary>
[ShaderOptions(FpContract = false)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MandelbrotIterationsNoContract(
    ReadWriteBuffer<uint> pixels, int width, int height, int maxIterations, Float2 center, float step) : IComputeShader
{
    public void Execute()
    {
        int px = ThreadIds.X;
        int py = ThreadIds.Y;

        float cx = center.X + ((px - (width * 0.5f)) * step);
        float cy = center.Y + ((py - (height * 0.5f)) * step);

        float x = 0f;
        float y = 0f;
        float x2 = 0f;
        float y2 = 0f;
        int n = 0;

        while (n < maxIterations && x2 + y2 <= 4f)
        {
            y = (2f * x * y) + cy;
            x = x2 - y2 + cx;
            x2 = x * x;
            y2 = y * y;
            n++;
        }

        uint count = (uint)n;
        uint shade = n < maxIterations ? 255u - ((count * 8u) & 255u) : 0u;
        pixels[(py * width) + px] = (count & 0xFFFFu) | (shade << 16) | (255u << 24);
    }
}

/// <summary><c>destination[i] = source[i]</c>: a buffer copy as a compute kernel, to compare with a blit.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct CopyKernel(ReadOnlyBuffer<uint> source, ReadWriteBuffer<uint> destination) : IComputeShader
{
    public void Execute() => destination[ThreadIds.X] = source[ThreadIds.X];
}

/// <summary>A tiny dispatch to put on either side of a copy.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct TouchInts(ReadWriteBuffer<int> data) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] += 1;
}
