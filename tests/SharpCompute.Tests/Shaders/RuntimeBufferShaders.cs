using System;
using SharpCompute;

namespace SharpCompute.Tests.Shaders;

// Shaders for the buffer view, GPU copy/fill, indirect dispatch, shader options and warm-up tests.

/// <summary><c>data[i] = data.Length * 1000 + i</c>: shows where a view starts and what length the shader sees.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct WriteIndexAndLength(ReadWriteBuffer<int> data) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] = (data.Length * 1000) + ThreadIds.X;
}

/// <summary><c>sum[i] = a[i] + b[i] + a.Length</c>, over three buffers that may all be views.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct AddBuffers(ReadOnlyBuffer<int> a, ReadOnlyBuffer<int> b, ReadWriteBuffer<int> sum) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        sum[i] = a[i] + b[i] + a.Length;
    }
}

/// <summary><c>destination[i] = i &lt; source.Length ? source[i] : -1</c>: reads nothing from an empty source.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct CopyUpToLength(ReadOnlyBuffer<int> source, ReadWriteBuffer<int> destination) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;

        if (i < source.Length)
        {
            destination[i] = source[i];
        }
        else
        {
            destination[i] = -1;
        }
    }
}

/// <summary>Scales <see cref="Float4"/> elements, whose MSL type is aligned to 16 bytes.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct ScaleFloat4(ReadWriteBuffer<Float4> data, float factor) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] = data[ThreadIds.X] * factor;
}

/// <summary><c>data[i] += 1.0h</c> on 2-byte elements: a view at an odd index is bound at an offset that is not a multiple of 4.</summary>
internal readonly struct IncrementHalves(ReadWriteBuffer<Half> data)
    : IComputeShader, IComputeShaderDescriptor<IncrementHalves>
{
    private readonly ReadWriteBuffer<Half> _data = data;

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "IncrementHalves_Execute";

    public static string MslSource => Msl.Header + """
        kernel void IncrementHalves_Execute(
            device half* data [[buffer(0)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            data[int(__tid.x)] += 1.0h;
        }
        """;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in IncrementHalves shader, ref ShaderArgumentWriter writer) =>
        writer.SetBuffer(0, shader._data);
}

/// <summary><c>destination[i] = source[i] * factor</c>.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct MultiplyInto(ReadOnlyBuffer<int> source, ReadWriteBuffer<int> destination, int factor) : IComputeShader
{
    public void Execute() => destination[ThreadIds.X] = source[ThreadIds.X] * factor;
}

/// <summary>Adds <paramref name="amount"/> to the red channel of every pixel of a single-channel float texture.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct AddToTexture(ReadWriteTexture2D<float> texture, float amount) : IComputeShader
{
    public void Execute()
    {
        Float4 pixel = texture[ThreadIds.X, ThreadIds.Y];
        texture[ThreadIds.X, ThreadIds.Y] = new Float4(pixel.X + amount, 0f, 0f, 1f);
    }
}

/// <summary>Adds 1 to <c>counter[0]</c> for every element at or above <paramref name="cutoff"/>.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct CountAbove(ReadOnlyBuffer<int> values, ReadWriteBuffer<int> counter, int cutoff) : IComputeShader
{
    public void Execute()
    {
        if (values[ThreadIds.X] >= cutoff)
        {
            Gpu.AtomicAdd(ref counter[0], 1);
        }
    }
}

/// <summary>
/// An indirectly dispatched pass with an explicit threadgroup size. Every thread counts itself in
/// <c>stats[0]</c>; thread 0 records <see cref="DispatchSize"/> in <c>stats[1]</c>; threads below the
/// real count add 1 to their element, which is the bounds check an indirect shader needs.
/// </summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct IndirectProbe64(ReadWriteBuffer<int> count, ReadWriteBuffer<int> touched, ReadWriteBuffer<int> stats) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        Gpu.AtomicAdd(ref stats[0], 1);

        if (i == 0)
        {
            stats[1] = DispatchSize.X;
        }

        if (i < count[0])
        {
            touched[i] += 1;
        }
    }
}

/// <summary><see cref="IndirectProbe64"/> without a threadgroup size: indirect dispatch then uses the one-dimensional default.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct IndirectProbeDefault(ReadWriteBuffer<int> count, ReadWriteBuffer<int> touched, ReadWriteBuffer<int> stats) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        Gpu.AtomicAdd(ref stats[0], 1);

        if (i == 0)
        {
            stats[1] = DispatchSize.X;
        }

        if (i < count[0])
        {
            touched[i] += 1;
        }
    }
}

/// <summary>A two-dimensional indirect pass: counts its threads and records the grid of thread (0, 0).</summary>
[ThreadGroupSize(8, 4)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct IndirectProbe2D(ReadWriteBuffer<int> stats) : IComputeShader
{
    public void Execute()
    {
        Gpu.AtomicAdd(ref stats[0], 1);

        if (ThreadIds.X == 0 && ThreadIds.Y == 0)
        {
            stats[1] = DispatchSize.X;
            stats[2] = DispatchSize.Y;
        }
    }
}

/// <summary>Writes the arguments of an indirect dispatch in a shader: <c>ceil(count / 64)</c> threadgroups by 1 by 1.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct WriteArguments64(ReadWriteBuffer<int> count, ReadWriteBuffer<DispatchArguments> arguments, int slot) : IComputeShader
{
    public void Execute()
    {
        DispatchArguments result = default;
        result.ThreadGroupsX = (uint)((count[0] + 63) / 64);
        result.ThreadGroupsY = 1u;
        result.ThreadGroupsZ = 1u;
        arguments[slot] = result;
    }
}

/// <summary><c>result[i] = a[i] * b[i] + c[i]</c> with Metal's default contraction: the compiler may fuse it.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct MulAdd(ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadOnlyBuffer<float> c, ReadWriteBuffer<float> result) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        result[i] = (a[i] * b[i]) + c[i];
    }
}

/// <summary><see cref="MulAdd"/> with contraction off: a rounded multiply, then a rounded add, as in C#.</summary>
[ShaderOptions(FpContract = false)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MulAddNoContract(ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadOnlyBuffer<float> c, ReadWriteBuffer<float> result) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        result[i] = (a[i] * b[i]) + c[i];
    }
}

/// <summary><see cref="MulAdd"/> with both options: fast math, but contraction still off.</summary>
[ShaderOptions(FastMath = true, FpContract = false)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MulAddFastNoContract(ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadOnlyBuffer<float> c, ReadWriteBuffer<float> result) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        result[i] = (a[i] * b[i]) + c[i];
    }
}

/// <summary>Two tests that only a NaN passes, with the default (IEEE-safe) math: bit 0 = <c>isnan(x)</c>, bit 1 = <c>x != x</c>.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct NaNProbe(ReadOnlyBuffer<float> input, ReadWriteBuffer<int> output) : IComputeShader
{
    public void Execute()
    {
        float x = input[ThreadIds.X];
        float same = x;
        int flags = 0;

        if (Gpu.IsNaN(x))
        {
            flags |= 1;
        }

        if (x != same)
        {
            flags |= 2;
        }

        output[ThreadIds.X] = flags;
    }
}

/// <summary><see cref="NaNProbe"/> under fast math, which lets the compiler assume no value is NaN and fold the comparison away.</summary>
[ShaderOptions(FastMath = true)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct NaNProbeFast(ReadOnlyBuffer<float> input, ReadWriteBuffer<int> output) : IComputeShader
{
    public void Execute()
    {
        float x = input[ThreadIds.X];
        float same = x;
        int flags = 0;

        if (Gpu.IsNaN(x))
        {
            flags |= 1;
        }

        if (x != same)
        {
            flags |= 2;
        }

        output[ThreadIds.X] = flags;
    }
}

/// <summary>An attribute that sets nothing: the options stay at their defaults.</summary>
[ShaderOptions(FastMath = false, FpContract = true)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct DefaultOptionsSpelledOut(ReadWriteBuffer<int> data) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] = 1;
}

/// <summary>Shaders nothing else dispatches, for the warm-up tests.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct WarmupA(ReadWriteBuffer<int> data) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] = 11;
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct WarmupB(ReadWriteBuffer<int> data) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] = 22;
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct WarmupC(ReadWriteBuffer<int> data) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] = 33;
}

/// <summary>A shader whose MSL does not compile.</summary>
internal readonly struct WarmupBroken(ReadWriteBuffer<int> data)
    : IComputeShader, IComputeShaderDescriptor<WarmupBroken>
{
    private readonly ReadWriteBuffer<int> _data = data;

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "WarmupBroken_Execute";

    public static string MslSource => Msl.Header + """
        kernel void WarmupBroken_Execute(device int* data [[buffer(0)]])
        {
            this is not Metal Shading Language;
        }
        """;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in WarmupBroken shader, ref ShaderArgumentWriter writer) =>
        writer.SetBuffer(0, shader._data);
}
