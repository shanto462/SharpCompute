using SharpCompute;

namespace SharpCompute.Tests.CpuExecution;

// Shaders for the CPU-execution and CPU+GPU split tests. Each one runs unchanged on the GPU (For /
// ForEach), on the CPU (ForCpu / ForEachCpu) and split between both (ForSplit / ForEachSplit).

/// <summary>Records every grid built-in of every thread of a 3D grid: 12 ints per thread, plus the normalized position.</summary>
[ThreadGroupSize(4, 2, 2)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct RecordBuiltins3D(ReadWriteBuffer<int> ids, ReadWriteBuffer<Float3> normalized) : IComputeShader
{
    public void Execute()
    {
        Int3 tid = ThreadIds.XYZ;
        Int3 grid = DispatchSize.XYZ;
        Int3 local = GroupIds.XYZ;
        Int3 group = ThreadGroupIds.XYZ;
        int i = (((tid.Z * grid.Y) + tid.Y) * grid.X) + tid.X;

        ids[(i * 12) + 0] = tid.X;
        ids[(i * 12) + 1] = tid.Y;
        ids[(i * 12) + 2] = tid.Z;
        ids[(i * 12) + 3] = grid.X;
        ids[(i * 12) + 4] = grid.Y;
        ids[(i * 12) + 5] = grid.Z;
        ids[(i * 12) + 6] = local.X;
        ids[(i * 12) + 7] = local.Y;
        ids[(i * 12) + 8] = local.Z;
        ids[(i * 12) + 9] = group.X;
        ids[(i * 12) + 10] = group.Y;
        ids[(i * 12) + 11] = group.Z;
        normalized[i] = ThreadIds.Normalized.XYZ;
    }
}

/// <summary>The 2D form, with the runtime's default threadgroup size and the scalar and XY accessors.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct RecordBuiltins2D(ReadWriteBuffer<int> ids, ReadWriteBuffer<Float2> normalized) : IComputeShader
{
    public void Execute()
    {
        Int2 tid = ThreadIds.XY;
        int i = (ThreadIds.Y * DispatchSize.X) + ThreadIds.X;

        ids[(i * 8) + 0] = tid.X;
        ids[(i * 8) + 1] = tid.Y;
        ids[(i * 8) + 2] = DispatchSize.XY.X;
        ids[(i * 8) + 3] = DispatchSize.Y;
        ids[(i * 8) + 4] = GroupIds.X;
        ids[(i * 8) + 5] = GroupIds.XY.Y;
        ids[(i * 8) + 6] = ThreadGroupIds.X;
        ids[(i * 8) + 7] = ThreadGroupIds.XY.Y;
        normalized[i] = new Float2(ThreadIds.Normalized.X, ThreadIds.Normalized.XY.Y);
    }
}

/// <summary>The 1D form, with the runtime's default threadgroup size (256).</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct RecordBuiltins1D(ReadWriteBuffer<int> ids, ReadWriteBuffer<float> normalized) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;

        ids[(i * 4) + 0] = i;
        ids[(i * 4) + 1] = DispatchSize.X;
        ids[(i * 4) + 2] = GroupIds.X;
        ids[(i * 4) + 3] = ThreadGroupIds.X;
        normalized[i] = ThreadIds.Normalized.X;
    }
}

/// <summary>A pixel shader that returns the color stored for its pixel, to compare the CPU's float → unorm8 conversion with the GPU's.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct ColorFromBuffer(ReadOnlyBuffer<Float4> colors) : IComputeShader<Float4>
{
    public Float4 Execute() => colors[(ThreadIds.Y * DispatchSize.X) + ThreadIds.X];
}

/// <summary>A pixel shader that also records its built-ins, to check both parts of a split pixel dispatch.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct PixelWithIds(ReadWriteBuffer<int> ids) : IComputeShader<Float4>
{
    public Float4 Execute()
    {
        int i = (ThreadIds.Y * DispatchSize.X) + ThreadIds.X;
        ids[(i * 4) + 0] = ThreadIds.X;
        ids[(i * 4) + 1] = ThreadIds.Y;
        ids[(i * 4) + 2] = DispatchSize.X;
        ids[(i * 4) + 3] = DispatchSize.Y;

        Float2 uv = ThreadIds.Normalized.XY;
        return new Float4(uv.X, uv.Y, 0.5f, 1f);
    }
}

/// <summary>Thread 0 indexes the buffer at -1: an exception on the CPU (bounds-checked), never run on the GPU by the tests.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct FirstThreadOutOfRange(ReadWriteBuffer<int> data) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        data[i == 0 ? -1 : i] = i;
    }
}

/// <summary>Burns <c>spins</c> iterations per thread, then writes its index; a slow dispatch to race against.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SlowWrite(ReadWriteBuffer<float> data, int spins) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        float v = 0f;

        for (int k = 0; k < spins; k++)
        {
            v = (v * 0.5f) + 1f;
        }

        data[i] = i + (v * 0f);
    }
}

/// <summary>Uses no grid built-in at all: every thread writes the same value to element 0.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct NoBuiltins(ReadWriteBuffer<float> data, float value) : IComputeShader
{
    public void Execute() => data[0] = value;
}

/// <summary>A separable-blur pass and an N-body step, as in the benchmarks: realistic float workloads to split.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BlurRows(ReadOnlyBuffer<float> source, ReadWriteBuffer<float> destination, ReadOnlyBuffer<float> weights, int width, int radius) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int row = ThreadIds.Y * width;
        float sum = 0f;

        for (int k = -radius; k <= radius; k++)
        {
            sum += weights[k + radius] * source[row + Gpu.Clamp(x + k, 0, width - 1)];
        }

        destination[row + x] = sum;
    }
}

[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct Gravity(ReadOnlyBuffer<Float4> positions, ReadWriteBuffer<Float4> accelerations, int count, float softeningSquared) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        Float3 p = positions[i].XYZ;
        Float3 acc = new(0f);

        for (int j = 0; j < count; j++)
        {
            Float4 other = positions[j];
            Float3 d = other.XYZ - p;
            float inv = 1f / Gpu.Sqrt(Gpu.Dot(d, d) + softeningSquared);
            acc += d * (other.W * inv * inv * inv);
        }

        accelerations[i] = new Float4(acc, 0f);
    }
}
