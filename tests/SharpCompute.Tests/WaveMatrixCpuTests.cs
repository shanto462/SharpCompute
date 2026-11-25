using System;
using System.Linq;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// The wave matrices on the CPU: every shader of the GPU tests gives the GPU's result under
/// <c>ForCpu</c> and <c>ForSplit</c> (exactly where the arithmetic is exact, within rounding for
/// products of arbitrary floats), and what the GPU leaves undefined (lanes that disagree, a SIMD group
/// that is not full) throws instead of producing a matrix nobody asked for.
/// </summary>
public class WaveMatrixCpuTests
{
    private const string Divergence = "SIMD-group operation in divergent control flow is not supported on the CPU; it is supported on the GPU";

    private static float[] Iota(int count) => Enumerable.Range(0, count).Select(static i => (float)i).ToArray();

    // ---- the same results as the GPU ----------------------------------------------------------------

    [MetalTheory]
    [InlineData(0, 8, false, 0, 8, false)]
    [InlineData(50, 16, false, 0, 8, false)]
    [InlineData(50, 16, true, 3, 9, false)]
    [InlineData(3, 11, false, 5, 9, true)]
    [InlineData(7, 8, true, 1, 64, true)]
    public void LoadAndStore_MatchTheGpu(int loadOffset, int loadStride, bool loadTranspose, int storeOffset, int storeStride, bool storeTranspose)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer(Iota(512));
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(512);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(512);
        gpu.AsSpan().Fill(-1f);
        cpu.AsSpan().Fill(-1f);

        device.For(32, new WaveMatrixCopy(source, gpu, loadOffset, loadStride, loadTranspose ? 1 : 0, storeOffset, storeStride, storeTranspose ? 1 : 0));
        device.ForCpu(32, new WaveMatrixCopy(source, cpu, loadOffset, loadStride, loadTranspose ? 1 : 0, storeOffset, storeStride, storeTranspose ? 1 : 0));

        Assert.Equal(gpu.ToArray(), cpu.ToArray());
        Assert.Contains(cpu.ToArray(), static value => value >= 0f);
    }

    [MetalFact]
    public void LoadAndStore_InOneReadWriteBuffer_AndOnViews_MatchTheGpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer(Iota(256));
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer(Iota(256));

        device.For(32, new WaveMatrixMove(gpu, from: 0, to: 8, stride: 16));
        device.ForCpu(32, new WaveMatrixMove(cpu, from: 0, to: 8, stride: 16));
        Assert.Equal(gpu.ToArray(), cpu.ToArray());

        // A view: offsets count from its start, and the range check is against its length.
        float[] before = cpu.ToArray();
        using ReadWriteBuffer<float> gpuView = gpu.Slice(64, 128);
        using ReadWriteBuffer<float> cpuView = cpu.Slice(64, 128);

        device.For(32, new WaveMatrixMove(gpuView, from: 0, to: 64, stride: 8));
        device.ForCpu(32, new WaveMatrixMove(cpuView, from: 0, to: 64, stride: 8));
        Assert.Equal(gpu.ToArray(), cpu.ToArray());
        Assert.Equal(before[64..128], cpu.ToArray()[128..192]);
        Assert.Equal(before[..128], cpu.ToArray()[..128]);

        // One element further and the matrix no longer fits in the view, though it would in the buffer.
        Assert.Throws<ArgumentOutOfRangeException>(() => device.ForCpu(32, new WaveMatrixMove(cpuView, from: 0, to: 65, stride: 8)));
    }

    [MetalFact]
    public void FilledDiagonalAndDefault_MatchTheGpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(256);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(256);
        using ReadWriteBuffer<Half> gpuHalf = device.AllocateReadWriteBuffer<Half>(192);
        using ReadWriteBuffer<Half> cpuHalf = device.AllocateReadWriteBuffer<Half>(192);
        cpu.AsSpan().Fill(-1f);
        cpuHalf.AsSpan().Fill((Half)(-1f));

        device.For(32, new WaveMatrixValues(gpu, filled: 2.5f, diagonal: 3f));
        device.ForCpu(32, new WaveMatrixValues(cpu, filled: 2.5f, diagonal: 3f));
        device.For(32, new WaveMatrixHalfValues(gpuHalf, filled: (Half)0.75f, diagonal: (Half)(-4f)));
        device.ForCpu(32, new WaveMatrixHalfValues(cpuHalf, filled: (Half)0.75f, diagonal: (Half)(-4f)));

        Assert.Equal(gpu.ToArray(), cpu.ToArray());
        Assert.Equal(gpuHalf.ToArray(), cpuHalf.ToArray());
        Assert.Equal(2.5f, cpu[0]);
        Assert.Equal(3f, cpu[64 + 9]);
        Assert.Equal(0f, cpu[128]);
    }

    [MetalTheory]
    [InlineData(0, false, 0, false)]
    [InlineData(21, true, 9, false)]
    [InlineData(5, false, 30, true)]
    public void HalfLoadAndStore_MatchTheGpu(int loadOffset, bool loadTranspose, int storeOffset, bool storeTranspose)
    {
        const int length = 256, stride = 12;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<Half> source = device.AllocateReadOnlyBuffer(Iota(length).Select(static f => (Half)(f * 0.5f)).ToArray());
        using ReadWriteBuffer<Half> gpu = device.AllocateReadWriteBuffer<Half>(length);
        using ReadWriteBuffer<Half> cpu = device.AllocateReadWriteBuffer<Half>(length);

        device.For(32, new WaveMatrixHalfCopy(source, gpu, loadOffset, stride, loadTranspose ? 1 : 0, storeOffset, storeTranspose ? 1 : 0));
        device.ForCpu(32, new WaveMatrixHalfCopy(source, cpu, loadOffset, stride, loadTranspose ? 1 : 0, storeOffset, storeTranspose ? 1 : 0));

        Assert.Equal(gpu.ToArray(), cpu.ToArray());
        Assert.Contains(cpu.ToArray(), static value => (float)value > 0f);
    }

    [MetalFact]
    public void Products_OnRandomData_MatchTheGpuWithinRounding()
    {
        const int groups = 12;
        float[] a = WaveMatrixReference.Random(groups * 64, seed: 41);
        float[] b = WaveMatrixReference.Random(groups * 64, seed: 42);
        float[] c = WaveMatrixReference.Random(groups * 64, seed: 43);

        float[] gpu = Products(a, b, c, groups, static (device, threads, shader) => device.For(threads, shader));
        float[] cpu = Products(a, b, c, groups, static (device, threads, shader) => device.ForCpu(threads, shader));

        for (int group = 0; group < groups; group++)
        {
            Range matrix = (group * 64)..((group + 1) * 64);
            double[] product = WaveMatrixReference.Multiply(a[matrix], b[matrix], null);
            double[] sum = WaveMatrixReference.Multiply(a[matrix], b[matrix], c[matrix]);

            // Both against the double reference, and so against each other: the order the GPU adds
            // the eight terms in is its own.
            WaveMatrixReference.AssertClose(product, cpu[(group * 128)..((group * 128) + 64)], 1e-5, $"CPU product {group}");
            WaveMatrixReference.AssertClose(sum, cpu[((group * 128) + 64)..((group + 1) * 128)], 1e-5, $"CPU sum {group}");
            WaveMatrixReference.AssertClose(gpu[(group * 128)..((group + 1) * 128)].Select(static f => (double)f).ToArray(), cpu[(group * 128)..((group + 1) * 128)], 2e-5, $"GPU against CPU, group {group}");
        }
    }

    [MetalTheory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void ForSplit_GivesTheGpuResult_WhateverTheShare(double cpuFraction)
    {
        // Exact arithmetic, so that the split can be compared element for element.
        const int groups = 8;
        float[] a = WaveMatrixReference.SmallIntegers(groups * 64, seed: 51);
        float[] b = WaveMatrixReference.SmallIntegers(groups * 64, seed: 52);
        float[] c = WaveMatrixReference.SmallIntegers(groups * 64, seed: 53);

        float[] gpu = Products(a, b, c, groups, static (device, threads, shader) => device.For(threads, shader));
        float[] split = Products(a, b, c, groups, (device, threads, shader) => device.ForSplit(threads, shader, cpuFraction));

        Assert.Equal(gpu, split);
        Assert.Equal((float)WaveMatrixReference.Multiply(a[..64], b[..64], null)[0], split[0]);
    }

    private static float[] Products(float[] a, float[] b, float[] c, int groups, Action<GraphicsDevice, int, WaveMatrixProducts> dispatch)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> left = device.AllocateReadOnlyBuffer(a);
        using ReadOnlyBuffer<float> right = device.AllocateReadOnlyBuffer(b);
        using ReadOnlyBuffer<float> addend = device.AllocateReadOnlyBuffer(c);
        using ReadWriteBuffer<float> result = device.AllocateReadWriteBuffer<float>(groups * 128);

        dispatch(device, groups * 32, new WaveMatrixProducts(left, right, addend, result));
        return result.ToArray();
    }

    [MetalFact]
    public void EveryMixOfHalfAndFloat_MatchesTheGpu()
    {
        float[] a = WaveMatrixReference.SmallIntegers(64, seed: 61), b = WaveMatrixReference.SmallIntegers(64, seed: 62), c = WaveMatrixReference.SmallIntegers(64, seed: 63);

        (float[] gpuFloats, float[] gpuHalves) = WaveMatrixRuns.Mixed(a, b, c, cpu: false);
        (float[] cpuFloats, float[] cpuHalves) = WaveMatrixRuns.Mixed(a, b, c, cpu: true);

        Assert.Equal(gpuFloats, cpuFloats);
        Assert.Equal(gpuHalves, cpuHalves);
        Assert.Equal((float)WaveMatrixReference.Multiply(a, b, c)[5], cpuHalves[(11 * 64) + 5]);
    }

    [MetalFact]
    public void AHalfResult_IsRoundedToHalf()
    {
        // 1/3 is not a half: the float results keep it, the half results hold the nearest half.
        float[] a = new float[64], b = new float[64], c = Enumerable.Repeat(1f / 3f, 64).ToArray();

        (float[] floats, float[] halves) = WaveMatrixRuns.Mixed(a, b, c, cpu: true);

        // Combination 4 accumulates into the float matrix, combination 5 into the half one (itself rounded when loaded).
        Assert.Equal(1f / 3f, floats[4 * 64]);
        Assert.Equal((float)(Half)(1f / 3f), floats[5 * 64]);
        Assert.Equal((float)(Half)(1f / 3f), halves[4 * 64]);
        Assert.NotEqual(1f / 3f, halves[4 * 64]);
    }

    [MetalTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupShared_MatchesTheGpu(bool transpose)
    {
        const int threadgroups = 3;
        float[] source = WaveMatrixReference.SmallIntegers(threadgroups * 256, seed: 71);

        (float[] gpuProducts, float[] gpuOperands) = WaveMatrixRuns.Shared(source, threadgroups, transpose, cpu: false);
        (float[] cpuProducts, float[] cpuOperands) = WaveMatrixRuns.Shared(source, threadgroups, transpose, cpu: true);

        Assert.Equal(gpuProducts, cpuProducts);
        Assert.Equal(gpuOperands, cpuOperands);
        Assert.Equal(WaveMatrixReference.Shared(source, threadgroups, transpose).Products, cpuProducts);
    }

    [MetalFact]
    public void Helpers_MatchTheGpu()
    {
        float[] a = WaveMatrixReference.SmallIntegers(64, seed: 81), b = WaveMatrixReference.SmallIntegers(64, seed: 82);

        Assert.Equal(WaveMatrixRuns.Helpers(a, b, cpu: false), WaveMatrixRuns.Helpers(a, b, cpu: true));
        Assert.Equal(WaveMatrixReference.Helpers(a, b), WaveMatrixRuns.Helpers(a, b, cpu: true));
    }

    [MetalFact]
    public void LoadsAndStoresSplitFromTheAccessesBesideThem_MatchTheGpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        float[] start = new float[64];
        start[0] = 3f;
        using ReadWriteBuffer<float> gpuData = device.AllocateReadWriteBuffer(start), cpuData = device.AllocateReadWriteBuffer(start);
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(128), cpu = device.AllocateReadWriteBuffer<float>(128);

        device.For(32, new WaveMatrixSplitOrder(gpuData, gpu));
        device.ForCpu(32, new WaveMatrixSplitOrder(cpuData, cpu));

        // The index was read (3) before the matrix of sevens was stored over it; the first operand
        // was loaded (all 7) before element 0 became 5 for the second.
        float[] expected = new float[128];
        expected[3] = 1f;

        for (int i = 0; i < 64; i++)
        {
            expected[64 + i] = i % 8 == 0 ? 7f * (5f + (7f * 7f)) : 7f * (8f * 7f);
        }

        Assert.Equal(expected, gpu.ToArray());
        Assert.Equal(expected, cpu.ToArray());
        Assert.Equal(gpuData.ToArray(), cpuData.ToArray());
        Assert.Equal(5f, cpuData.ToArray()[0]);
    }

    [MetalTheory]
    [InlineData(8)]
    [InlineData(24)]
    public void TheReadmeExample_IsAMatrixProduct_OnTheGpuAndOnTheCpu(int n)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        float[] a = WaveMatrixReference.Random(n * n, seed: 91), b = WaveMatrixReference.Random(n * n, seed: 92);
        using ReadOnlyBuffer<float> left = device.AllocateReadOnlyBuffer(a), right = device.AllocateReadOnlyBuffer(b);
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(n * n), cpu = device.AllocateReadWriteBuffer<float>(n * n);

        device.For(n / 8 * 32, n / 8, new MatMulBlocks(left, right, gpu, n));
        device.ForCpu(n / 8 * 32, n / 8, new MatMulBlocks(left, right, cpu, n));

        // The whole product in double precision.
        double[] expected = new double[n * n];

        for (int row = 0; row < n; row++)
        {
            for (int column = 0; column < n; column++)
            {
                for (int k = 0; k < n; k++)
                {
                    expected[(row * n) + column] += (double)a[(row * n) + k] * b[(k * n) + column];
                }
            }
        }

        WaveMatrixReference.AssertClose(expected, gpu.ToArray(), 1e-5, "GPU");
        WaveMatrixReference.AssertClose(expected, cpu.ToArray(), 1e-5, "CPU");
    }

    // ---- what the CPU refuses ---------------------------------------------------------------------

    [MetalTheory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(1)]
    public void LanesThatReturnedEarly_LeaveAGroupThatIsNotFull_WhichThrows(int lanes)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer(Iota(64));
        using ReadWriteBuffer<float> destination = device.AllocateReadWriteBuffer<float>(64);
        destination.AsSpan().Fill(-1f);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.ForCpu(32, new WaveMatrixSomeLanes(source, destination, lanes)));

        Assert.Contains("WaveMatrix8x8.Load", exception.Message);
        Assert.Contains("WaveMatrixShaders.cs", exception.Message);
        Assert.Contains("needs all 32 lanes of a SIMD group", exception.Message);
        Assert.Contains($"but {lanes} took part", exception.Message);

        // Nothing was stored: the load never made a matrix.
        Assert.All(destination.ToArray(), static value => Assert.Equal(-1f, value));

        // With every lane it is a copy, and a group that returns as a whole is fine too.
        device.ForCpu(32, new WaveMatrixSomeLanes(source, destination, lanes: 32));
        Assert.Equal(Iota(64), destination.ToArray());
        device.ForCpu(32, new WaveMatrixSomeLanes(source, destination, lanes: 0));
    }

    [MetalTheory]
    [InlineData(20)] // one threadgroup of 20 threads
    [InlineData(40)] // a full one, then one of 8
    public void AGridThatIsNotAMultipleOf32_EndsInAGroupThatIsNotFull_WhichThrows(int threads)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer(Iota(64));
        using ReadWriteBuffer<float> destination = device.AllocateReadWriteBuffer<float>(64);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.ForCpu(threads, new WaveMatrixCopy(source, destination, 0, 8, 0, 0, 8, 0)));

        Assert.Contains("needs all 32 lanes of a SIMD group", exception.Message);
        Assert.Contains($"but {threads % 32} took part", exception.Message);
    }

    [MetalFact]
    public void LanesWithDifferentOffsets_Throw()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer(Iota(32 * 64));
        using ReadWriteBuffer<float> destination = device.AllocateReadWriteBuffer<float>(64);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.ForCpu(32, new WaveMatrixLaneOffsets(source, destination)));

        Assert.Contains("WaveMatrix8x8.Load", exception.Message);
        Assert.Contains("brings other arguments than the lanes of its SIMD group", exception.Message);

        // The GPU runs it, each lane loading its own two elements from its own offset (measured; not
        // promised by Metal): lane 0 holds elements 0 and 1, lane 1 elements 2 and 3 of its matrix.
        device.For(32, new WaveMatrixLaneOffsets(source, destination));
        Assert.Equal([0f, 1f, 64f + 2f, 64f + 3f], destination.ToArray()[..4]);
    }

    [MetalFact]
    public void LanesWithDifferentFillValues_Throw()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> destination = device.AllocateReadWriteBuffer<float>(64);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.ForCpu(32, new WaveMatrixLaneValues(destination)));

        Assert.Contains("WaveMatrix8x8.Filled", exception.Message);
        Assert.Contains("brings other arguments", exception.Message);
    }

    [MetalFact]
    public void LanesWithDifferentMatrices_Throw()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> destination = device.AllocateReadWriteBuffer<float>(64);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.ForCpu(32, new WaveMatrixTwoMatrices(destination)));

        Assert.Contains("WaveMatrix8x8.Store", exception.Message);
        Assert.Contains("brings other arguments", exception.Message);
    }

    [MetalFact]
    public void LanesAtDifferentCalls_ThrowTheDivergenceException()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> destination = device.AllocateReadWriteBuffer<float>(64);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.ForCpu(32, new WaveMatrixSplitGroup(destination)));

        Assert.Contains(Divergence, exception.Message);
        Assert.Contains("WaveMatrix8x8.Filled", exception.Message);
        Assert.Contains("WaveMatrix8x8.Diagonal", exception.Message);

        // And in the CPU part of a split.
        exception = Assert.Throws<NotSupportedException>(() => device.ForSplit(64, new WaveMatrixSplitGroup(destination), cpuFraction: 0.5));
        Assert.Contains(Divergence, exception.Message);
    }

    [MetalTheory]
    [InlineData(1, 8, false, "offset")] // the last element is one past the end
    [InlineData(-1, 8, false, "offset")]
    [InlineData(0, 9, true, "offset")] // 7 * 9 + 7 = 70, transposed or not
    [InlineData(0, -8, false, "stride")]
    public void ALoadOutsideTheBuffer_Throws(int offset, int stride, bool transpose, string parameter)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer(Iota(64));
        using ReadWriteBuffer<float> destination = device.AllocateReadWriteBuffer<float>(64);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => device.ForCpu(32, new WaveMatrixCopy(source, destination, offset, stride, transpose ? 1 : 0, 0, 8, 0)));

        Assert.Equal(parameter, exception.ParamName);
        Assert.Contains("WaveMatrix8x8.Load", exception.Message);

        // The dispatcher is fine afterwards, and so is the shader with a matrix that fits.
        device.ForCpu(32, new WaveMatrixCopy(source, destination, 0, 8, transpose ? 1 : 0, 0, 8, transpose ? 1 : 0));
        Assert.Equal(Iota(64), destination.ToArray());
    }

    [MetalFact]
    public void AStoreOutsideTheBuffer_Throws_AndWritesNothing()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer(Iota(64));
        using ReadWriteBuffer<float> destination = device.AllocateReadWriteBuffer<float>(100);
        destination.AsSpan().Fill(-1f);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => device.ForCpu(32, new WaveMatrixCopy(source, destination, 0, 8, 0, 37, 8, 0)));

        Assert.Contains("WaveMatrix8x8.Store", exception.Message);
        Assert.Contains("37 to 100", exception.Message);
        Assert.All(destination.ToArray(), static value => Assert.Equal(-1f, value));

        // One element earlier it fits exactly.
        device.ForCpu(32, new WaveMatrixCopy(source, destination, 0, 8, 0, 36, 8, 0));
        Assert.Equal(Iota(64), destination.ToArray()[36..]);
    }

    [MetalTheory]
    [InlineData(0, 0)]
    [InlineData(3, 5)]
    [InlineData(100, -36)]
    [InlineData(-8, 72)]
    public void AnOffsetThatIsASum_NamesTheSameElements_OnTheGpuAndOnTheCpu(int first, int second)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer(Iota(128));
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(64);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(64);

        // The GPU adds the two terms to the buffer's address one after the other; C# adds them first.
        device.For(32, new WaveMatrixOffsetSum(source, gpu, first, second));
        device.ForCpu(32, new WaveMatrixOffsetSum(source, cpu, first, second));

        Assert.Equal(Iota(128)[(first + second)..(first + second + 64)], gpu.ToArray());
        Assert.Equal(gpu.ToArray(), cpu.ToArray());
    }

    [MetalTheory]
    [InlineData(int.MaxValue, 1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(1_500_000_000, 1_000_000_000)]
    public void AnOffsetWhoseTermsAddUpToMoreThanAnInt_IsOutOfRangeOnTheCpu(int first, int second)
    {
        // The one place where the GPU's signed arithmetic is not C#'s: the GPU forms this sum in 64
        // bits, C# wraps it. The wrapped sum is negative, so the CPU never loads a matrix from it:
        // it reports the offset, as it does for any other that is outside the buffer.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer(Iota(128));
        using ReadWriteBuffer<float> destination = device.AllocateReadWriteBuffer<float>(64);
        destination.AsSpan().Fill(-1f);

        Assert.True(unchecked(first + second) < 0);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => device.ForCpu(32, new WaveMatrixOffsetSum(source, destination, first, second)));

        Assert.Equal("offset", exception.ParamName);
        Assert.Equal(unchecked(first + second), exception.ActualValue);
        Assert.All(destination.ToArray(), static value => Assert.Equal(-1f, value));
    }

    // ---- outside a shader -------------------------------------------------------------------------

    [Fact]
    public void OutsideADispatch_EveryMemberThrows()
    {
        const string shaderOnly = "can only be used inside a compute shader's Execute method";
        float[] floats = new float[64];
        Half[] halves = new Half[64];
        WaveMatrix8x8 matrix = default;
        WaveMatrixHalf8x8 half = default;

        Action[] members =
        [
            () => WaveMatrix8x8.Filled(1f),
            () => WaveMatrix8x8.Diagonal(1f),
            () => WaveMatrix8x8.Load(floats, 0, 8),
            () => matrix.Store(floats, 0, 8),
            () => WaveMatrix8x8.Multiply(matrix, matrix),
            () => WaveMatrix8x8.Multiply(half, matrix),
            () => WaveMatrix8x8.MultiplyAccumulate(matrix, matrix, matrix),
            () => WaveMatrix8x8.MultiplyAccumulate(half, half, matrix),
            () => WaveMatrixHalf8x8.Filled((Half)1f),
            () => WaveMatrixHalf8x8.Diagonal((Half)1f),
            () => WaveMatrixHalf8x8.Load(halves, 0, 8),
            () => half.Store(halves, 0, 8),
            () => WaveMatrixHalf8x8.Multiply(half, half),
            () => WaveMatrixHalf8x8.Multiply(matrix, half),
            () => WaveMatrixHalf8x8.MultiplyAccumulate(half, half, half),
            () => WaveMatrixHalf8x8.MultiplyAccumulate(matrix, matrix, half),
        ];

        foreach (Action member in members)
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(member);
            Assert.Contains(shaderOnly, exception.Message);
        }
    }

    [Fact]
    public void TheSizeIsEightByEight()
    {
        Assert.Equal(8, WaveMatrix8x8.Rows);
        Assert.Equal(8, WaveMatrix8x8.Columns);
        Assert.Equal(8, WaveMatrixHalf8x8.Rows);
        Assert.Equal(8, WaveMatrixHalf8x8.Columns);
    }
}
