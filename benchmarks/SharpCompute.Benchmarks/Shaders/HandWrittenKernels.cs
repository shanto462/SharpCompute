using System;
using System.Runtime.InteropServices;
using SharpCompute;

namespace SharpCompute.Benchmarks.Shaders;

// The kernels of the native baselines, written directly in MSL the way a Metal programmer would
// write them (unsigned indices, aligned float4, no grid-window prelude), each implementing the
// shader descriptor by hand like SaxpyHandWritten. They go through the same runtime as a generated
// shader: compiled at run time with the runtime's own MTLCompileOptions (MSL 3.2, safe math mode,
// precise math functions) and dispatched by the same For. A gap between a generated kernel and its
// twin here is therefore the generated MSL's doing, and a gap to a kernel with no generated twin
// (the simdgroup_matrix SGEMM) is a feature a C# shader cannot use yet.

/// <summary>
/// <see cref="MatMulNaive"/> by hand: one thread per element of C, the same sum in the same order
/// (so the two agree bit for bit), with <c>uint</c> indices instead of the generated <c>int</c>.
/// </summary>
public readonly struct MatMulNaiveHandWritten(ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadWriteBuffer<float> c, int n)
    : IComputeShader, IComputeShaderDescriptor<MatMulNaiveHandWritten>
{
    private readonly ReadOnlyBuffer<float> _a = a;
    private readonly ReadOnlyBuffer<float> _b = b;
    private readonly ReadWriteBuffer<float> _c = c;
    private readonly uint _n = (uint)n;

    public static string EntryPoint => "matmul_naive_hand_written";

    public static string MslSource => """
        #include <metal_stdlib>
        using namespace metal;

        kernel void matmul_naive_hand_written(
            const device float* A [[buffer(0)]],
            const device float* B [[buffer(1)]],
            device float* C [[buffer(2)]],
            constant uint& N [[buffer(3)]],
            uint2 g [[thread_position_in_grid]])
        {
            float sum = 0.0f;

            for (uint k = 0; k < N; k++)
            {
                sum += A[g.y * N + k] * B[k * N + g.x];
            }

            C[g.y * N + g.x] = sum;
        }
        """;

    // 0: the runtime's choice for a 2D grid (16 × 16), as for MatMulNaive, which sets none either.
    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public void Execute() => throw new NotSupportedException("Hand-written MSL; dispatch it on the GPU.");

    public static void SetArguments(in MatMulNaiveHandWritten shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._a);
        writer.SetBuffer(1, shader._b);
        writer.SetBuffer(2, shader._c);
        writer.SetBytes(3, shader._n);
    }
}

/// <summary>
/// SGEMM on Metal's matrix units: <c>simdgroup_float8x8</c> and
/// <c>simdgroup_multiply_accumulate</c>, which multiply 8 × 8 blocks with a whole SIMD group (32
/// threads) at once. Each SIMD group owns a 16 × 32 block of C as a 2 × 4 grid of 8 × 8
/// accumulators and walks k in steps of 8, loading its blocks of A and B straight from the buffers;
/// a threadgroup is four SIMD groups (2 × 2), so 32 rows × 64 columns of C. The fastest on an Apple
/// M4 Pro of the variants tried: accumulator grids of 2 × 2, 2 × 4 and 4 × 4, each with and without
/// staging the blocks of A and B in threadgroup memory.
/// </summary>
/// <remarks>
/// n must be a multiple of 64. Dispatch with <see cref="GridWidth"/> × <see cref="GridHeight"/>
/// threads: threadgroups of 128 × 1, one per 32 × 64 block of C. A matrix sum is ordered by the
/// matrix unit, not by k, so the result agrees with the naive kernel to rounding, not bit for bit.
/// </remarks>
public readonly struct SgemmSimdgroupHandWritten(ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadWriteBuffer<float> c, int n)
    : IComputeShader, IComputeShaderDescriptor<SgemmSimdgroupHandWritten>
{
    private const int ThreadsPerGroup = 128;
    private const int BlockRows = 32;
    private const int BlockColumns = 64;

    private readonly ReadOnlyBuffer<float> _a = a;
    private readonly ReadOnlyBuffer<float> _b = b;
    private readonly ReadWriteBuffer<float> _c = c;
    private readonly uint _n = (uint)n;

    /// <summary>The grid width, in threads, that covers the columns of an n × n product.</summary>
    public static int GridWidth(int n) => n / BlockColumns * ThreadsPerGroup;

    /// <summary>The grid height, in threads (one row of threadgroups per 32 rows of C).</summary>
    public static int GridHeight(int n) => n / BlockRows;

    public static string EntryPoint => "sgemm_simdgroup_hand_written";

    public static string MslSource => """
        #include <metal_stdlib>
        using namespace metal;

        // Accumulators per SIMD group (TM x TN blocks of 8 x 8), SIMD groups per threadgroup
        // (SGR x SGC), and how many steps of 8 along k one loop iteration takes.
        #define TM 2
        #define TN 4
        #define SGR 2
        #define SGC 2
        #define UNROLL 2

        kernel void sgemm_simdgroup_hand_written(
            const device float* A [[buffer(0)]],
            const device float* B [[buffer(1)]],
            device float* C [[buffer(2)]],
            constant uint& N [[buffer(3)]],
            uint2 tg [[threadgroup_position_in_grid]],
            ushort sg [[simdgroup_index_in_threadgroup]])
        {
            const uint row0 = tg.y * (8 * TM * SGR) + (sg / SGC) * (8 * TM);
            const uint col0 = tg.x * (8 * TN * SGC) + (sg % SGC) * (8 * TN);

            simdgroup_float8x8 acc[TM][TN];

            for (int i = 0; i < TM; i++)
            {
                for (int j = 0; j < TN; j++)
                {
                    acc[i][j] = make_filled_simdgroup_matrix<float, 8, 8>(0.0f);
                }
            }

            const device float* pa = A + row0 * N;
            const device float* pb = B + col0;

            for (uint k = 0; k < N; k += 8 * UNROLL)
            {
                #pragma unroll
                for (int u = 0; u < UNROLL; u++)
                {
                    simdgroup_float8x8 a[TM], b[TN];

                    for (int i = 0; i < TM; i++)
                    {
                        simdgroup_load(a[i], pa + (i * 8) * N + k + u * 8, N);
                    }

                    for (int j = 0; j < TN; j++)
                    {
                        simdgroup_load(b[j], pb + (k + u * 8) * N + j * 8, N);
                    }

                    for (int i = 0; i < TM; i++)
                    {
                        for (int j = 0; j < TN; j++)
                        {
                            simdgroup_multiply_accumulate(acc[i][j], a[i], b[j], acc[i][j]);
                        }
                    }
                }
            }

            for (int i = 0; i < TM; i++)
            {
                for (int j = 0; j < TN; j++)
                {
                    simdgroup_store(acc[i][j], C + (row0 + i * 8) * N + col0 + j * 8, N);
                }
            }
        }
        """;

    public static int ThreadGroupSizeX => ThreadsPerGroup;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    // The kernel takes four SIMD groups of 32 threads per threadgroup for granted; this makes the
    // runtime refuse a device whose SIMD groups are wider instead of computing the wrong blocks.
    public static bool UsesSimdGroups => true;

    public void Execute() => throw new NotSupportedException("Hand-written MSL; dispatch it on the GPU.");

    public static void SetArguments(in SgemmSimdgroupHandWritten shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._a);
        writer.SetBuffer(1, shader._b);
        writer.SetBuffer(2, shader._c);
        writer.SetBytes(3, shader._n);
    }
}

/// <summary>
/// <see cref="NBodyStep"/> by hand: the same arithmetic in the same order (so the two agree bit for
/// bit), on aligned <c>float4</c> buffers with <c>uint</c> indices where the generator emits
/// <c>packed_float4</c> and <c>int</c>.
/// </summary>
public readonly struct NBodyStepHandWritten(
    ReadOnlyBuffer<Float4> positions,
    ReadOnlyBuffer<Float4> velocities,
    ReadWriteBuffer<Float4> nextPositions,
    ReadWriteBuffer<Float4> nextVelocities,
    int count,
    float dt,
    float softeningSquared) : IComputeShader, IComputeShaderDescriptor<NBodyStepHandWritten>
{
    private readonly ReadOnlyBuffer<Float4> _positions = positions;
    private readonly ReadOnlyBuffer<Float4> _velocities = velocities;
    private readonly ReadWriteBuffer<Float4> _nextPositions = nextPositions;
    private readonly ReadWriteBuffer<Float4> _nextVelocities = nextVelocities;
    private readonly Constants _constants = new() { Count = (uint)count, Dt = dt, SofteningSquared = softeningSquared };

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Constants
    {
        public uint Count;
        public float Dt;
        public float SofteningSquared;
    }

    public static string EntryPoint => "nbody_step_hand_written";

    public static string MslSource => """
        #include <metal_stdlib>
        using namespace metal;

        struct Constants
        {
            uint count;
            float dt;
            float softeningSquared;
        };

        kernel void nbody_step_hand_written(
            const device float4* positions [[buffer(0)]],
            const device float4* velocities [[buffer(1)]],
            device float4* nextPositions [[buffer(2)]],
            device float4* nextVelocities [[buffer(3)]],
            constant Constants& c [[buffer(4)]],
            uint i [[thread_position_in_grid]])
        {
            const float4 self = positions[i];
            const float3 p = self.xyz;
            float3 acc = float3(0.0f);

            for (uint j = 0; j < c.count; j++)
            {
                const float4 other = positions[j];
                const float3 d = other.xyz - p;
                const float inv = 1.0f / sqrt(dot(d, d) + c.softeningSquared);
                acc += d * (other.w * inv * inv * inv);
            }

            const float4 v = velocities[i];
            const float3 nv = v.xyz + acc * c.dt;
            nextVelocities[i] = float4(nv, v.w);
            nextPositions[i] = float4(p + nv * c.dt, self.w);
        }
        """;

    public static int ThreadGroupSizeX => 256;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public void Execute() => throw new NotSupportedException("Hand-written MSL; dispatch it on the GPU.");

    public static void SetArguments(in NBodyStepHandWritten shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._positions);
        writer.SetBuffer(1, shader._velocities);
        writer.SetBuffer(2, shader._nextPositions);
        writer.SetBuffer(3, shader._nextVelocities);
        writer.SetBytes(4, shader._constants);
    }
}

/// <summary><see cref="HashRounds"/> by hand: the same rounds on <c>uint</c>, with an unsigned loop counter.</summary>
public readonly struct HashRoundsHandWritten(ReadOnlyBuffer<uint> input, ReadWriteBuffer<uint> output, int rounds)
    : IComputeShader, IComputeShaderDescriptor<HashRoundsHandWritten>
{
    private readonly ReadOnlyBuffer<uint> _input = input;
    private readonly ReadWriteBuffer<uint> _output = output;
    private readonly uint _rounds = (uint)rounds;

    public static string EntryPoint => "hash_rounds_hand_written";

    public static string MslSource => """
        #include <metal_stdlib>
        using namespace metal;

        kernel void hash_rounds_hand_written(
            const device uint* input [[buffer(0)]],
            device uint* output [[buffer(1)]],
            constant uint& rounds [[buffer(2)]],
            uint i [[thread_position_in_grid]])
        {
            uint h = input[i];

            for (uint r = 0; r < rounds; r++)
            {
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                h = (h << 5) | (h >> 27);
                h += r * 0x9E3779B9u;
            }

            output[i] = h;
        }
        """;

    public static int ThreadGroupSizeX => 256;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public void Execute() => throw new NotSupportedException("Hand-written MSL; dispatch it on the GPU.");

    public static void SetArguments(in HashRoundsHandWritten shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._input);
        writer.SetBuffer(1, shader._output);
        writer.SetBytes(2, shader._rounds);
    }
}

/// <summary>
/// SAXPY four elements per thread: <c>y = a · x + y</c> on <c>float4</c>, dispatched over a quarter
/// of the threads <see cref="SaxpyHandWritten"/> needs. What a Metal programmer would write for a
/// kernel that only moves memory; a C# shader can do the same with <c>Float4</c> buffers.
/// </summary>
public readonly struct SaxpyFloat4HandWritten(ReadOnlyBuffer<float> x, ReadWriteBuffer<float> y, float a)
    : IComputeShader, IComputeShaderDescriptor<SaxpyFloat4HandWritten>
{
    private readonly ReadOnlyBuffer<float> _x = x;
    private readonly ReadWriteBuffer<float> _y = y;
    private readonly float _a = a;

    public static string EntryPoint => "saxpy_float4_hand_written";

    public static string MslSource => """
        #include <metal_stdlib>
        using namespace metal;

        kernel void saxpy_float4_hand_written(
            const device float4* x [[buffer(0)]],
            device float4* y [[buffer(1)]],
            constant float& a [[buffer(2)]],
            uint i [[thread_position_in_grid]])
        {
            y[i] = a * x[i] + y[i];
        }
        """;

    public static int ThreadGroupSizeX => 256;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public void Execute() => throw new NotSupportedException("Hand-written MSL; dispatch it on the GPU.");

    public static void SetArguments(in SaxpyFloat4HandWritten shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._x);
        writer.SetBuffer(1, shader._y);
        writer.SetBytes(2, shader._a);
    }
}

/// <summary>
/// A bandwidth probe: <c>y = x</c> on <c>float4</c>, 16 bytes read and 16 written per thread and no
/// arithmetic. With <see cref="FillFloat4HandWritten"/>, it measures the rate memory can be moved at,
/// the ceiling the memory-bound rows are held against.
/// </summary>
public readonly struct CopyFloat4HandWritten(ReadOnlyBuffer<float> x, ReadWriteBuffer<float> y)
    : IComputeShader, IComputeShaderDescriptor<CopyFloat4HandWritten>
{
    private readonly ReadOnlyBuffer<float> _x = x;
    private readonly ReadWriteBuffer<float> _y = y;

    public static string EntryPoint => "copy_float4_hand_written";

    public static string MslSource => """
        #include <metal_stdlib>
        using namespace metal;

        kernel void copy_float4_hand_written(
            const device float4* x [[buffer(0)]],
            device float4* y [[buffer(1)]],
            uint i [[thread_position_in_grid]])
        {
            y[i] = x[i];
        }
        """;

    public static int ThreadGroupSizeX => 256;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public void Execute() => throw new NotSupportedException("Hand-written MSL; dispatch it on the GPU.");

    public static void SetArguments(in CopyFloat4HandWritten shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._x);
        writer.SetBuffer(1, shader._y);
    }
}

/// <summary>
/// A bandwidth probe: <c>y[i] = i</c> on <c>float4</c>, 16 bytes written per thread and nothing read,
/// so it shows the write rate on its own.
/// </summary>
public readonly struct FillFloat4HandWritten(ReadWriteBuffer<float> y)
    : IComputeShader, IComputeShaderDescriptor<FillFloat4HandWritten>
{
    private readonly ReadWriteBuffer<float> _y = y;

    public static string EntryPoint => "fill_float4_hand_written";

    public static string MslSource => """
        #include <metal_stdlib>
        using namespace metal;

        kernel void fill_float4_hand_written(
            device float4* y [[buffer(0)]],
            uint i [[thread_position_in_grid]])
        {
            y[i] = float4(float(i));
        }
        """;

    public static int ThreadGroupSizeX => 256;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public void Execute() => throw new NotSupportedException("Hand-written MSL; dispatch it on the GPU.");

    public static void SetArguments(in FillFloat4HandWritten shader, ref ShaderArgumentWriter writer) =>
        writer.SetBuffer(0, shader._y);
}

/// <summary>
/// The FLOP probe: every thread runs <see cref="Chains"/> independent chains of
/// <c>a = fma(a, m, c)</c> for <c>iterations</c> steps, touching memory only to load its seed and
/// store the sum of the chains. Nothing but multiply-adds on registers, so the rate it reaches is
/// the measured FP32 ceiling the compute-bound rows are held against.
/// </summary>
/// <remarks>
/// A chain's next step needs its previous one, so a thread with few chains waits on its own results:
/// measured on an Apple M4 Pro, 4 chains reach 3.2 TFLOP/s, 16 reach 5.1, 32 reach 6.2 and from 48
/// on the rate is flat (6.24, then 6.26 with 64), which is why there are 48. The chains start from
/// different values, so none can be merged with another; safe math mode keeps the compiler from
/// reassociating a chain into a closed form, and the step count is a run-time constant.
/// </remarks>
public readonly struct FmaChainsHandWritten(ReadOnlyBuffer<float> seeds, ReadWriteBuffer<float> sums, int iterations)
    : IComputeShader, IComputeShaderDescriptor<FmaChainsHandWritten>
{
    /// <summary>Independent accumulators per thread.</summary>
    public const int Chains = 48;

    /// <summary>The factor and the addend of every step: a contraction, so a chain stays bounded and never goes denormal.</summary>
    public const float Factor = 0.999f, Addend = 0.001f;

    /// <summary>How far apart neighbouring chains of a thread start.</summary>
    public const float ChainSpacing = 0.25f;

    /// <summary>FP32 operations per thread and step: a multiply and an add per chain.</summary>
    public const int FlopsPerIteration = Chains * 2;

    private readonly ReadOnlyBuffer<float> _seeds = seeds;
    private readonly ReadWriteBuffer<float> _sums = sums;
    private readonly uint _iterations = (uint)iterations;

    public static string EntryPoint => "fma_chains_hand_written";

    public static string MslSource => $$"""
        #include <metal_stdlib>
        using namespace metal;

        #define CHAINS {{Chains}}

        kernel void fma_chains_hand_written(
            const device float* seeds [[buffer(0)]],
            device float* sums [[buffer(1)]],
            constant uint& iterations [[buffer(2)]],
            uint i [[thread_position_in_grid]])
        {
            const float m = 0.999f;
            const float c = 0.001f;
            float a[CHAINS];

            for (int q = 0; q < CHAINS; q++)
            {
                a[q] = seeds[i] + 0.25f * q;
            }

            for (uint k = 0; k < iterations; k++)
            {
                for (int q = 0; q < CHAINS; q++)
                {
                    a[q] = fma(a[q], m, c);
                }
            }

            float sum = 0.0f;

            for (int q = 0; q < CHAINS; q++)
            {
                sum += a[q];
            }

            sums[i] = sum;
        }
        """;

    public static int ThreadGroupSizeX => 256;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public void Execute() => throw new NotSupportedException("Hand-written MSL; dispatch it on the GPU.");

    public static void SetArguments(in FmaChainsHandWritten shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._seeds);
        writer.SetBuffer(1, shader._sums);
        writer.SetBytes(2, shader._iterations);
    }

    /// <summary>What the kernel stores for a thread whose seed is <paramref name="seed"/>, computed with the same operations in the same order.</summary>
    public static float Expected(float seed, int iterations)
    {
        Span<float> a = stackalloc float[Chains];

        for (int q = 0; q < Chains; q++)
        {
            a[q] = seed + (ChainSpacing * q);
        }

        for (int k = 0; k < iterations; k++)
        {
            for (int q = 0; q < Chains; q++)
            {
                a[q] = MathF.FusedMultiplyAdd(a[q], Factor, Addend);
            }
        }

        float sum = 0f;

        for (int q = 0; q < Chains; q++)
        {
            sum += a[q];
        }

        return sum;
    }
}
