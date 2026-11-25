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

/// <summary>The operations HLSL does not have: all-equal, the rotations and the shuffles that fill.</summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdShiftsAndEquality(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> output, int delta) : IComputeShader
{
    public const int Stride = 8;

    public void Execute()
    {
        int i = ThreadIds.X;
        int v = input[i];
        Int2 pair = new Int2(v / 100, 7);

        output[(i * Stride) + 0] = Gpu.WaveRotateUp(v, delta);
        output[(i * Stride) + 1] = Gpu.WaveRotateDown(v, delta);
        output[(i * Stride) + 2] = Gpu.WaveShuffleUpAndFill(v, -v, delta);
        output[(i * Stride) + 3] = Gpu.WaveShuffleDownAndFill(v, -v, delta);
        output[(i * Stride) + 4] = Gpu.WaveActiveAllEqual(v) ? 1 : 0;
        output[(i * Stride) + 5] = Gpu.WaveActiveAllEqual(v / 100) ? 1 : 0;
        output[(i * Stride) + 6] = Gpu.WaveActiveAllEqual(pair) ? 1 : 0;
        output[(i * Stride) + 7] = Gpu.WaveActiveAllEqual(new Float3(1f, 2f, i < 40 ? 3f : 4f)) ? 1 : 0;
    }
}

/// <summary>The quad reductions, scans and votes; the lanes for which <c>lane % 4 == skip</c> return first (none for a skip of 4).</summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdQuadReductions(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> output, int skip) : IComputeShader
{
    public const int Stride = 14;

    public void Execute()
    {
        int i = ThreadIds.X;
        int v = input[i];

        if ((Gpu.WaveGetLaneIndex() & 3) == skip)
        {
            return;
        }

        output[(i * Stride) + 0] = Gpu.QuadActiveSum(v);
        output[(i * Stride) + 1] = Gpu.QuadActiveProduct(v);
        output[(i * Stride) + 2] = Gpu.QuadActiveMin(v);
        output[(i * Stride) + 3] = Gpu.QuadActiveMax(v);
        output[(i * Stride) + 4] = Gpu.QuadActiveBitAnd(v);
        output[(i * Stride) + 5] = Gpu.QuadActiveBitOr(v);
        output[(i * Stride) + 6] = Gpu.QuadActiveBitXor(v);
        output[(i * Stride) + 7] = Gpu.QuadPrefixSum(v);
        output[(i * Stride) + 8] = Gpu.QuadPrefixProduct(v);
        output[(i * Stride) + 9] = Gpu.QuadInclusivePrefixSum(v);
        output[(i * Stride) + 10] = Gpu.QuadInclusivePrefixProduct(v);
        output[(i * Stride) + 11] = (int)Gpu.QuadActiveBallot((v & 1) == 0);
        output[(i * Stride) + 12] = Gpu.QuadActiveAnyTrue(v > 9) ? 1 : 0;
        output[(i * Stride) + 13] = Gpu.QuadActiveAllTrue(v < 9) ? 1 : 0;
    }
}

/// <summary>Operations after a third of the lanes returned, with no call inside a branch: what the CPU reproduces exactly.</summary>
[ThreadGroupSize(64)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdAfterReturns(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Stride = 8;

    public void Execute()
    {
        int i = ThreadIds.X;
        int lane = Gpu.WaveGetLaneIndex();
        int v = input[i];

        output[(i * Stride) + 0] = Gpu.WaveActiveSum(v);

        if (lane % 3 == 0)
        {
            return;
        }

        output[(i * Stride) + 1] = Gpu.WaveActiveSum(v);
        output[(i * Stride) + 2] = Gpu.WavePrefixSum(1);
        output[(i * Stride) + 3] = Gpu.WaveReadLaneFirst(v);
        output[(i * Stride) + 4] = Gpu.WaveIsFirstLane() ? 1 : 0;
        output[(i * Stride) + 5] = (int)Gpu.WaveActiveBallot(true);
        output[(i * Stride) + 6] = Gpu.WaveActiveCountBits(true);
        output[(i * Stride) + 7] = Gpu.WaveShuffleUp(v, 1);
    }
}

/// <summary>Operations inside a loop every lane runs alike, and inside a branch every lane takes: uniform control flow.</summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdInUniformLoops(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> output, int passes) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        int v = input[i];

        for (int pass = 0; pass < passes; pass++)
        {
            if (pass % 2 == 0)
            {
                v += Gpu.WaveShuffleUp(v, 1);
            }
            else
            {
                v ^= Gpu.WaveActiveMax(v);
            }
        }

        output[i] = v;
    }
}

/// <summary>A 2D threadgroup whose rows are not a multiple of 32, with reductions, a scan and a shuffle across the rows.</summary>
[ThreadGroupSize(12, 12)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdOps2D(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Stride = 6;

    public void Execute()
    {
        int i = (ThreadIds.Y * DispatchSize.X) + ThreadIds.X;
        int v = input[i];

        output[(i * Stride) + 0] = Gpu.WaveActiveSum(v);
        output[(i * Stride) + 1] = Gpu.WavePrefixSum(v);
        output[(i * Stride) + 2] = Gpu.WaveShuffleUp(v, 5);
        output[(i * Stride) + 3] = Gpu.QuadActiveSum(v);
        output[(i * Stride) + 4] = (SimdGroupIds.Index * 1000) + (SimdGroupIds.Count * 100) + Gpu.WaveGetLaneIndex();
        output[(i * Stride) + 5] = Gpu.WaveActiveCountBits(true);
    }
}

/// <summary>
/// SIMD-group operations together with threadgroup memory and barriers: every SIMD group sums its lanes
/// into shared memory, and after a barrier each thread reads the sums of all the groups of its threadgroup.
/// </summary>
[ThreadGroupSize(96)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdWithSharedMemory(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> output) : IComputeShader
{
    public const int Stride = 3;

    [GroupShared]
    private static readonly int[] sums = new int[4];

    public void Execute()
    {
        int i = ThreadIds.X;
        int sum = Gpu.WaveActiveSum(input[i]);

        if (Gpu.WaveIsFirstLane())
        {
            sums[SimdGroupIds.Index] = sum;
        }

        Gpu.GroupMemoryBarrierWithGroupSync();

        int total = 0;

        for (int group = 0; group < SimdGroupIds.Count; group++)
        {
            total += sums[group];
        }

        // And once more after the barrier: the lanes meet again.
        output[(i * Stride) + 0] = total;
        output[(i * Stride) + 1] = Gpu.WavePrefixSum(total - sum);
        output[(i * Stride) + 2] = GroupSize.Count;
    }
}

/// <summary>
/// Divergent control flow: the even lanes call one operation, the odd lanes another. Fine on the GPU,
/// where each branch sees its own lanes; on the CPU the lanes of the group are not at the same call.
/// </summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdDivergent(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> output) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        int v = input[i];

        if ((Gpu.WaveGetLaneIndex() & 1) == 0)
        {
            output[i] = Gpu.WaveActiveSum(v);
        }
        else
        {
            output[i] = Gpu.WaveActiveMax(v);
        }
    }
}

/// <summary>
/// Divergence between a SIMD-group operation and a barrier: one half of the threadgroup waits at the
/// barrier for everybody, the other half at an operation for its whole SIMD group. Undefined on the
/// GPU (a barrier in divergent control flow), so it only ever runs on the CPU, which must not hang.
/// </summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct SimdAgainstBarrier(ReadWriteBuffer<int> output, int barrierFirstBelow) : IComputeShader
{
    public void Execute()
    {
        int sum;

        // Both halves do both, in opposite orders: whichever comes first, the two halves wait for each other.
        if (GroupIds.X < barrierFirstBelow)
        {
            Gpu.GroupMemoryBarrierWithGroupSync();
            sum = Gpu.WaveActiveSum(1);
        }
        else
        {
            sum = Gpu.WaveActiveMax(1);
            Gpu.GroupMemoryBarrierWithGroupSync();
        }

        output[ThreadIds.X] = sum;
    }
}

/// <summary>
/// A hand-written descriptor that does not report its SIMD-group operation: its threads are not run as
/// lanes on the CPU, so the operation itself has to refuse.
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

/// <summary>A hand-written descriptor that does report them: it runs on the CPU like a generated one.</summary>
internal readonly struct ReportedSimdGroups(ReadWriteBuffer<float> buffer) : IComputeShader, IComputeShaderDescriptor<ReportedSimdGroups>
{
    private readonly ReadWriteBuffer<float> _buffer = buffer;

    public void Execute() => _buffer[ThreadIds.X] = Gpu.WaveActiveSum(1f) + Gpu.WaveGetLaneIndex();

    public static string EntryPoint => "ReportedSimdGroups";

    public static string MslSource => Msl.Header + """
        kernel void ReportedSimdGroups(device float* buffer [[buffer(0)]], uint3 __tid [[thread_position_in_grid]], uint __lane [[thread_index_in_simdgroup]])
        {
            buffer[int(__tid.x)] = metal::simd_sum(1.0f) + float(int(__lane));
        }
        """;

    public static int ThreadGroupSizeX => 32;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static bool UsesSimdGroups => true;

    public static void SetArguments(in ReportedSimdGroups shader, ref ShaderArgumentWriter writer) => writer.SetBuffer(0, shader._buffer);
}
