using System;

namespace SharpCompute.Tests.Shaders;

// Generated shaders for the SIMD-group tests. Threadgroup sizes are explicit wherever a test spells
// out lanes: a SIMD group is 32 consecutive threads of a threadgroup, so the expectations depend on it.
// Every SIMD-group operation is on a line of its own (the CPU identifies a call site by its line).

/// <summary>Per thread: its lane, the lane count, its SIMD group and the SIMD groups of its threadgroup.</summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdLanes1D(ReadWriteBuffer<int> output) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;

        output[(i * 4) + 0] = Gpu.WaveGetLaneIndex();
        output[(i * 4) + 1] = Gpu.WaveGetLaneCount();
        output[(i * 4) + 2] = SimdGroupIds.Index;
        output[(i * 4) + 3] = SimdGroupIds.Count;
    }
}

/// <summary>The same for a 12 × 12 threadgroup (144 threads: four full SIMD groups and one of 16 lanes), plus the thread's position in it.</summary>
[ThreadGroupSize(12, 12)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdLanes2D(ReadWriteBuffer<int> output) : IComputeShader
{
    public void Execute()
    {
        int i = (ThreadIds.Y * DispatchSize.X) + ThreadIds.X;

        output[(i * 5) + 0] = Gpu.WaveGetLaneIndex();
        output[(i * 5) + 1] = SimdGroupIds.Index;
        output[(i * 5) + 2] = SimdGroupIds.Count;
        output[(i * 5) + 3] = GroupIds.X;
        output[(i * 5) + 4] = GroupIds.Y;
    }
}

/// <summary>The same with the runtime's default threadgroup size for the dispatch rank (256, 16 × 16 or 8 × 8 × 4).</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdLanesDefault(ReadWriteBuffer<int> output) : IComputeShader
{
    public void Execute()
    {
        int i = (((ThreadIds.Z * DispatchSize.Y) + ThreadIds.Y) * DispatchSize.X) + ThreadIds.X;

        output[(i * 6) + 0] = Gpu.WaveGetLaneIndex();
        output[(i * 6) + 1] = SimdGroupIds.Index;
        output[(i * 6) + 2] = SimdGroupIds.Count;
        output[(i * 6) + 3] = GroupIds.X;
        output[(i * 6) + 4] = GroupIds.Y;
        output[(i * 6) + 5] = GroupIds.Z;
    }
}

/// <summary>The votes over <c>input</c>; threads at or past <c>count</c> return before any of them.</summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdVotes(ReadOnlyBuffer<int> input, ReadWriteBuffer<uint> output, int count) : IComputeShader
{
    public const int Stride = 7;

    public void Execute()
    {
        int i = ThreadIds.X;

        if (i >= count)
        {
            return;
        }

        int v = input[i];
        bool odd = (v & 1) != 0;

        output[(i * Stride) + 0] = Gpu.WaveActiveBallot(odd);
        output[(i * Stride) + 1] = Gpu.WaveActiveBallot(true);
        output[(i * Stride) + 2] = Gpu.WaveActiveAnyTrue(v >= 100) ? 1u : 0u;
        output[(i * Stride) + 3] = Gpu.WaveActiveAllTrue(v < 100) ? 1u : 0u;
        output[(i * Stride) + 4] = (uint)Gpu.WaveActiveCountBits(odd);
        output[(i * Stride) + 5] = (uint)Gpu.WavePrefixCountBits(odd);
        output[(i * Stride) + 6] = Gpu.WaveIsFirstLane() ? 1u : 0u;
    }
}

/// <summary>Every reduction and scan of an <see cref="int"/>.</summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdReduceInt(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> output, int count) : IComputeShader
{
    public const int Stride = 11;

    public void Execute()
    {
        int i = ThreadIds.X;

        if (i >= count)
        {
            return;
        }

        int v = input[i];

        output[(i * Stride) + 0] = Gpu.WaveActiveSum(v);
        output[(i * Stride) + 1] = Gpu.WaveActiveProduct(v);
        output[(i * Stride) + 2] = Gpu.WaveActiveMin(v);
        output[(i * Stride) + 3] = Gpu.WaveActiveMax(v);
        output[(i * Stride) + 4] = Gpu.WaveActiveBitAnd(v);
        output[(i * Stride) + 5] = Gpu.WaveActiveBitOr(v);
        output[(i * Stride) + 6] = Gpu.WaveActiveBitXor(v);
        output[(i * Stride) + 7] = Gpu.WavePrefixSum(v);
        output[(i * Stride) + 8] = Gpu.WavePrefixProduct(v);
        output[(i * Stride) + 9] = Gpu.WaveInclusivePrefixSum(v);
        output[(i * Stride) + 10] = Gpu.WaveInclusivePrefixProduct(v);
    }
}

/// <summary>Every reduction and scan of a <see cref="uint"/>.</summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdReduceUInt(ReadOnlyBuffer<uint> input, ReadWriteBuffer<uint> output, int count) : IComputeShader
{
    public const int Stride = 11;

    public void Execute()
    {
        int i = ThreadIds.X;

        if (i >= count)
        {
            return;
        }

        uint v = input[i];

        output[(i * Stride) + 0] = Gpu.WaveActiveSum(v);
        output[(i * Stride) + 1] = Gpu.WaveActiveProduct(v);
        output[(i * Stride) + 2] = Gpu.WaveActiveMin(v);
        output[(i * Stride) + 3] = Gpu.WaveActiveMax(v);
        output[(i * Stride) + 4] = Gpu.WaveActiveBitAnd(v);
        output[(i * Stride) + 5] = Gpu.WaveActiveBitOr(v);
        output[(i * Stride) + 6] = Gpu.WaveActiveBitXor(v);
        output[(i * Stride) + 7] = Gpu.WavePrefixSum(v);
        output[(i * Stride) + 8] = Gpu.WavePrefixProduct(v);
        output[(i * Stride) + 9] = Gpu.WaveInclusivePrefixSum(v);
        output[(i * Stride) + 10] = Gpu.WaveInclusivePrefixProduct(v);
    }
}

/// <summary>Every reduction and scan of a <see cref="float"/>.</summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdReduceFloat(ReadOnlyBuffer<float> input, ReadWriteBuffer<float> output, int count) : IComputeShader
{
    public const int Stride = 8;

    public void Execute()
    {
        int i = ThreadIds.X;

        if (i >= count)
        {
            return;
        }

        float v = input[i];

        output[(i * Stride) + 0] = Gpu.WaveActiveSum(v);
        output[(i * Stride) + 1] = Gpu.WaveActiveProduct(v);
        output[(i * Stride) + 2] = Gpu.WaveActiveMin(v);
        output[(i * Stride) + 3] = Gpu.WaveActiveMax(v);
        output[(i * Stride) + 4] = Gpu.WavePrefixSum(v);
        output[(i * Stride) + 5] = Gpu.WavePrefixProduct(v);
        output[(i * Stride) + 6] = Gpu.WaveInclusivePrefixSum(v);
        output[(i * Stride) + 7] = Gpu.WaveInclusivePrefixProduct(v);
    }
}

/// <summary>Reductions of vectors straight from buffers (packed in MSL) and of a vector built in the shader.</summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdReduceVectors(
    ReadOnlyBuffer<Float3> floats, ReadOnlyBuffer<Int2> ints, ReadWriteBuffer<Float3> floatSums, ReadWriteBuffer<Float3> floatMax,
    ReadWriteBuffer<Int2> intSums, ReadWriteBuffer<Int2> intOr, ReadWriteBuffer<UInt4> prefix) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;

        floatSums[i] = Gpu.WaveActiveSum(floats[i]);
        floatMax[i] = Gpu.WaveActiveMax(floats[i]);
        intSums[i] = Gpu.WaveActiveSum(ints[i]);
        intOr[i] = Gpu.WaveActiveBitOr(ints[i]);
        prefix[i] = Gpu.WavePrefixSum(new UInt4(1u, 2u, (uint)i, 0u));
    }
}

/// <summary>Reductions before and after a third of the lanes return, and in the two branches of an <c>if</c>.</summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdActiveLanes(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Stride = 8;

    public void Execute()
    {
        int i = ThreadIds.X;
        int lane = Gpu.WaveGetLaneIndex();
        int v = input[i];
        int branch;

        // Divergent control flow is fine on the GPU: each branch sees the lanes that took it.
        if ((lane & 1) == 0)
        {
            branch = Gpu.WaveActiveSum(v);
        }
        else
        {
            branch = -Gpu.WaveActiveSum(v);
        }

        output[(i * Stride) + 0] = Gpu.WaveActiveSum(v);
        output[(i * Stride) + 1] = branch;

        if (lane % 3 == 0)
        {
            return;
        }

        output[(i * Stride) + 2] = Gpu.WaveActiveSum(v);
        output[(i * Stride) + 3] = Gpu.WavePrefixSum(1);
        output[(i * Stride) + 4] = Gpu.WaveReadLaneFirst(v);
        output[(i * Stride) + 5] = Gpu.WaveIsFirstLane() ? 1 : 0;
        output[(i * Stride) + 6] = (int)Gpu.WaveActiveBallot(true);
        output[(i * Stride) + 7] = Gpu.WaveActiveCountBits(true);
    }
}

/// <summary>The exchanges of an <see cref="int"/>: reads of a lane, the first lane, and the three shuffles.</summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdExchange(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> output, int delta) : IComputeShader
{
    public const int Stride = 8;

    public void Execute()
    {
        int i = ThreadIds.X;
        int v = input[i];

        output[(i * Stride) + 0] = Gpu.WaveReadLaneAt(v, 2);
        output[(i * Stride) + 1] = Gpu.WaveReadLaneAt(v, Gpu.WaveActiveCountBits(true) - 1 - Gpu.WaveGetLaneIndex());
        output[(i * Stride) + 2] = Gpu.WaveReadLaneFirst(v);
        output[(i * Stride) + 3] = Gpu.WaveShuffleUp(v, delta);
        output[(i * Stride) + 4] = Gpu.WaveShuffleDown(v, delta);
        output[(i * Stride) + 5] = Gpu.WaveShuffleXor(v, 1);
        output[(i * Stride) + 6] = Gpu.WaveShuffleXor(v, 16);
        output[(i * Stride) + 7] = Gpu.WaveShuffleUp(Gpu.WaveShuffleDown(v, 1), 1);
    }
}

/// <summary>The quad reads, in a threadgroup that is 2 threads wide, where a quad is a 2 × 2 block of the grid.</summary>
[ThreadGroupSize(2, 8)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdQuads2D(ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Stride = 5;

    public void Execute()
    {
        int i = (ThreadIds.Y * DispatchSize.X) + ThreadIds.X;
        int v = (ThreadIds.Y * 100) + ThreadIds.X;

        output[(i * Stride) + 0] = Gpu.QuadReadAcrossX(v);
        output[(i * Stride) + 1] = Gpu.QuadReadAcrossY(v);
        output[(i * Stride) + 2] = Gpu.QuadReadAcrossDiagonal(v);
        output[(i * Stride) + 3] = Gpu.QuadReadLaneAt(v, 0);
        output[(i * Stride) + 4] = Gpu.QuadReadLaneAt(v, 3 - (Gpu.WaveGetLaneIndex() & 3));
    }
}

/// <summary>The quad reads in a 1D threadgroup, where a quad is four threads in a row, on floats.</summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdQuads1D(ReadOnlyBuffer<float> input, ReadWriteBuffer<float> output) : IComputeShader
{
    public const int Stride = 4;

    public void Execute()
    {
        int i = ThreadIds.X;
        float v = input[i];

        output[(i * Stride) + 0] = Gpu.QuadReadAcrossX(v);
        output[(i * Stride) + 1] = Gpu.QuadReadAcrossY(v);
        output[(i * Stride) + 2] = Gpu.QuadReadAcrossDiagonal(v);
        output[(i * Stride) + 3] = Gpu.QuadReadLaneAt(v, 1);
    }
}

/// <summary>SIMD-group operations and built-ins inside helper methods, which receive the built-ins as hidden parameters.</summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdInHelpers(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> output) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;

        output[(i * 2) + 0] = GroupTotal(input[i]);
        output[(i * 2) + 1] = Position();
    }

    private static int GroupTotal(int value) => Gpu.WaveActiveSum(value);

    private static int Position() => (SimdGroupIds.Index * 100) + Lane();

    private static int Lane() => Gpu.WaveGetLaneIndex();
}

/// <summary>A pixel shader that uses them: the lane, the SIMD group and the number of active lanes, as a color.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdPixels : IComputeShader<Float4>
{
    public Float4 Execute()
    {
        float lane = Gpu.WaveGetLaneIndex();
        float group = SimdGroupIds.Index;
        float active = Gpu.WaveActiveCountBits(true);

        return new Float4(lane / 255f, group / 255f, active / 255f, 1f);
    }
}

/// <summary>The sum of a float buffer in two passes of SIMD-group reductions: one partial per SIMD group, written by its first lane.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdPartialSums(ReadOnlyBuffer<float> input, ReadWriteBuffer<float> partials) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;

        if (i >= input.Length)
        {
            return;
        }

        float sum = Gpu.WaveActiveSum(input[i]);

        if (Gpu.WaveIsFirstLane())
        {
            partials[i / 32] = sum;
        }
    }
}

/// <summary>
/// A hand-written descriptor that does not report its SIMD-group operation: on the CPU the operation
/// itself has to refuse to run.
/// </summary>
internal readonly struct UnreportedSimdGroups(ReadWriteBuffer<float> buffer) : IComputeShader, IComputeShaderDescriptor<UnreportedSimdGroups>
{
    private readonly ReadWriteBuffer<float> _buffer = buffer;

    public void Execute() => _buffer[ThreadIds.X] = Gpu.WaveActiveSum(1f);

    public static string EntryPoint => "UnreportedSimdGroups";

    public static string MslSource => Msl.Header + """
        kernel void UnreportedSimdGroups(device float* buffer [[buffer(0)]], uint3 __tid [[thread_position_in_grid]])
        {
            buffer[int(__tid.x)] = metal::simd_sum(1.0f);
        }
        """;

    public static int ThreadGroupSizeX => 32;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in UnreportedSimdGroups shader, ref ShaderArgumentWriter writer) => writer.SetBuffer(0, shader._buffer);
}

/// <summary>A hand-written descriptor that does report them, with a real <c>Execute</c> the CPU must not reach.</summary>
internal readonly struct ReportedSimdGroups(ReadWriteBuffer<float> buffer) : IComputeShader, IComputeShaderDescriptor<ReportedSimdGroups>
{
    private readonly ReadWriteBuffer<float> _buffer = buffer;

    public void Execute() => throw new InvalidOperationException("Execute must not run: the dispatch has to refuse the shader first.");

    public static string EntryPoint => "ReportedSimdGroups";

    public static string MslSource => Msl.Header + """
        kernel void ReportedSimdGroups(device float* buffer [[buffer(0)]], uint3 __tid [[thread_position_in_grid]])
        {
            buffer[int(__tid.x)] = metal::simd_sum(1.0f);
        }
        """;

    public static int ThreadGroupSizeX => 32;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static bool UsesSimdGroups => true;

    public static void SetArguments(in ReportedSimdGroups shader, ref ShaderArgumentWriter writer) => writer.SetBuffer(0, shader._buffer);
}
