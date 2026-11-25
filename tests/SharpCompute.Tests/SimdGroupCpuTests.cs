using System;
using System.Linq;
using System.Threading.Tasks;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// The SIMD-group operations on the CPU: every shader of the GPU tests gives the same result under
/// <c>ForCpu</c> (and <c>ForSplit</c>), where the lanes of a SIMD group run on threads of their own and
/// meet at each operation; divergent control flow throws instead of hanging or guessing.
/// </summary>
public class SimdGroupCpuTests
{
    private const string Divergence = "SIMD-group operation in divergent control flow is not supported on the CPU; it is supported on the GPU";

    /// <summary>Runs <paramref name="dispatch"/> twice, into two buffers prefilled alike, on the GPU and on the CPU.</summary>
    private static (T[] Gpu, T[] Cpu) Both<T>(int length, T fill, Action<GraphicsDevice, ReadWriteBuffer<T>, bool> dispatch)
        where T : unmanaged
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<T> gpu = device.AllocateReadWriteBuffer<T>(length);
        using ReadWriteBuffer<T> cpu = device.AllocateReadWriteBuffer<T>(length);
        gpu.AsSpan().Fill(fill);
        cpu.AsSpan().Fill(fill);

        dispatch(device, gpu, false);
        dispatch(device, cpu, true);
        return (gpu.ToArray(), cpu.ToArray());
    }

    private static void AssertSame<T>((T[] Gpu, T[] Cpu) results, int stride, string what)
        where T : unmanaged, IEquatable<T>
    {
        string[] differences = Enumerable.Range(0, results.Gpu.Length)
            .Where(i => !results.Gpu[i].Equals(results.Cpu[i]))
            .Select(i => $"thread {i / stride}, value {i % stride}: GPU {results.Gpu[i]}, CPU {results.Cpu[i]}")
            .ToArray();

        Assert.True(differences.Length == 0, $"{what}: {differences.Length} values differ between the GPU and the CPU:\n" + string.Join("\n", differences.Take(30)));
    }

    private static int[] Values(int count, int seed)
    {
        Random random = new(seed);
        return Enumerable.Range(0, count).Select(_ => random.Next(-50, 50)).ToArray();
    }

    // ---- lanes ------------------------------------------------------------------------------------

    [MetalTheory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(64)]
    [InlineData(100)]
    public void Lanes1D_MatchTheGpu(int threads)
    {
        AssertSame(
            Both<int>(threads * 4, -1, (device, output, cpu) =>
            {
                if (cpu)
                {
                    device.ForCpu(threads, new SimdLanes1D(output));
                }
                else
                {
                    device.For(threads, new SimdLanes1D(output));
                }
            }),
            4,
            "lanes");
    }

    [MetalTheory]
    [InlineData(12, 12)]
    [InlineData(20, 10)]
    [InlineData(30, 31)]
    public void Lanes2D_MatchTheGpu(int width, int height)
    {
        AssertSame(
            Both<int>(width * height * 5, -1, (device, output, cpu) =>
            {
                if (cpu)
                {
                    device.ForCpu(width, height, new SimdLanes2D(output));
                }
                else
                {
                    device.For(width, height, new SimdLanes2D(output));
                }
            }),
            5,
            "lanes of a 12 x 12 threadgroup");
    }

    [MetalTheory]
    [InlineData(1000, 1, 1)]
    [InlineData(40, 24, 1)]
    [InlineData(20, 12, 9)]
    public void Lanes_WithTheDefaultThreadgroupSize_MatchTheGpu(int x, int y, int z)
    {
        AssertSame(
            Both<int>(x * y * z * 6, -1, (device, output, cpu) =>
            {
                SimdLanesDefault shader = new(output);

                switch (cpu, z > 1, y > 1)
                {
                    case (false, true, _): device.For(x, y, z, shader); break;
                    case (false, false, true): device.For(x, y, shader); break;
                    case (false, false, false): device.For(x, shader); break;
                    case (true, true, _): device.ForCpu(x, y, z, shader); break;
                    case (true, false, true): device.ForCpu(x, y, shader); break;
                    default: device.ForCpu(x, shader); break;
                }
            }),
            6,
            "lanes with the default threadgroup");
    }

    // ---- every shader of the GPU tests --------------------------------------------------------------

    [MetalTheory]
    [InlineData(70, 96)]  // early-returning lanes
    [InlineData(33, 33)]  // a SIMD group of one lane
    [InlineData(64, 64)]
    public void Votes_MatchTheGpu(int count, int threads)
    {
        int[] input = Enumerable.Range(0, count).ToArray();
        input[^1] = 100;
        GraphicsDevice gpu = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = gpu.AllocateReadOnlyBuffer<int>(input);

        AssertSame(
            Both<uint>(threads * SimdVotes.Stride, uint.MaxValue, (device, output, cpu) =>
            {
                if (cpu)
                {
                    device.ForCpu(threads, new SimdVotes(inputBuffer, output, count));
                }
                else
                {
                    device.For(threads, new SimdVotes(inputBuffer, output, count));
                }
            }),
            SimdVotes.Stride,
            "votes");
    }

    [MetalTheory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(37)]
    [InlineData(64)]
    [InlineData(333)]
    public void IntAndUIntReductions_MatchTheGpuExactly(int count)
    {
        int[] ints = Values(count, count);
        uint[] uints = ints.Select(static v => unchecked((uint)(v * 40_000_003))).ToArray();
        GraphicsDevice gpu = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> intBuffer = gpu.AllocateReadOnlyBuffer<int>(ints);
        using ReadOnlyBuffer<uint> uintBuffer = gpu.AllocateReadOnlyBuffer<uint>(uints);

        // The grid overshoots the data, so the last SIMD group also has lanes that return first.
        int threads = count + 7;

        (int[] Gpu, int[] Cpu) results = Both<int>(threads * SimdReduceInt.Stride, int.MinValue, (device, output, cpu) =>
        {
            if (cpu)
            {
                device.ForCpu(threads, new SimdReduceInt(intBuffer, output, count));
            }
            else
            {
                device.For(threads, new SimdReduceInt(intBuffer, output, count));
            }
        });

        AssertSame(results, SimdReduceInt.Stride, "int reductions");

        // Not just equal to the GPU: equal to the model, so a wrong result on both sides cannot pass.
        for (int thread = 0; thread < count; thread++)
        {
            SimdGroupTests.AssertReductions(ints, results.Cpu, SimdReduceInt.Stride, threadgroup: 64, thread);
        }

        AssertSame(
            Both<uint>(threads * SimdReduceUInt.Stride, uint.MaxValue, (device, output, cpu) =>
            {
                if (cpu)
                {
                    device.ForCpu(threads, new SimdReduceUInt(uintBuffer, output, count));
                }
                else
                {
                    device.For(threads, new SimdReduceUInt(uintBuffer, output, count));
                }
            }),
            SimdReduceUInt.Stride,
            "uint reductions");
    }

    [MetalFact]
    public void FloatReductions_MatchTheGpu_ExactlyWhereSumsAreExact_AndWithinRoundingElsewhere()
    {
        // Small integers and powers of two: exact in any order, so the CPU (lane order) equals the GPU.
        float[] exact = Enumerable.Range(0, 100).Select(static i => (i % 7) - 3f + (i % 2 == 0 ? 0.5f : 0f)).Select(static v => v == 0f ? 2f : v).ToArray();
        GraphicsDevice gpu = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> exactBuffer = gpu.AllocateReadOnlyBuffer<float>(exact);

        (float[] Gpu, float[] Cpu) results = Both<float>(exact.Length * SimdReduceFloat.Stride, float.NaN, (device, output, cpu) =>
        {
            if (cpu)
            {
                device.ForCpu(exact.Length, new SimdReduceFloat(exactBuffer, output, exact.Length));
            }
            else
            {
                device.For(exact.Length, new SimdReduceFloat(exactBuffer, output, exact.Length));
            }
        });

        for (int i = 0; i < results.Gpu.Length; i++)
        {
            // Sums, minima, maxima and the scans of sums; the products of 32 such values are not exact.
            if (i % SimdReduceFloat.Stride is 0 or 2 or 3 or 4 or 6)
            {
                Assert.True(results.Gpu[i] == results.Cpu[i], $"value {i}: GPU {results.Gpu[i]:R}, CPU {results.Cpu[i]:R}");
            }
            else
            {
                Assert.InRange(results.Cpu[i], results.Gpu[i] - (Math.Abs(results.Gpu[i]) * 1e-4f), results.Gpu[i] + (Math.Abs(results.Gpu[i]) * 1e-4f));
            }
        }

        Random random = new(11);
        float[] arbitrary = Enumerable.Range(0, 1000).Select(_ => (random.NextSingle() * 200f) - 100f).ToArray();
        using ReadOnlyBuffer<float> arbitraryBuffer = gpu.AllocateReadOnlyBuffer<float>(arbitrary);

        results = Both<float>(arbitrary.Length * SimdReduceFloat.Stride, float.NaN, (device, output, cpu) =>
        {
            if (cpu)
            {
                device.ForCpu(arbitrary.Length, new SimdReduceFloat(arbitraryBuffer, output, arbitrary.Length));
            }
            else
            {
                device.For(arbitrary.Length, new SimdReduceFloat(arbitraryBuffer, output, arbitrary.Length));
            }
        });

        for (int i = 0; i < results.Gpu.Length; i++)
        {
            switch (i % SimdReduceFloat.Stride)
            {
                case 2 or 3:
                    Assert.Equal(results.Gpu[i], results.Cpu[i]);
                    break;
                case 0 or 4 or 6:
                    // Sums of 32 values up to 100: the two orders round differently, by a few ULP of 3200.
                    Assert.InRange(results.Cpu[i], results.Gpu[i] - 0.01f, results.Gpu[i] + 0.01f);
                    break;
            }
        }
    }

    [MetalFact]
    public void VectorReductions_MatchTheGpu()
    {
        const int count = 40;
        GraphicsDevice gpu = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<Float3> floats = gpu.AllocateReadOnlyBuffer<Float3>(Enumerable.Range(0, count).Select(static i => new Float3(i, -i, i % 4)).ToArray());
        using ReadOnlyBuffer<Int2> ints = gpu.AllocateReadOnlyBuffer<Int2>(Enumerable.Range(0, count).Select(static i => new Int2(i, 1 << (i % 8))).ToArray());
        ReadWriteBuffer<Float3>[] sums = [gpu.AllocateReadWriteBuffer<Float3>(count), gpu.AllocateReadWriteBuffer<Float3>(count)];
        ReadWriteBuffer<Float3>[] max = [gpu.AllocateReadWriteBuffer<Float3>(count), gpu.AllocateReadWriteBuffer<Float3>(count)];
        ReadWriteBuffer<Int2>[] intSums = [gpu.AllocateReadWriteBuffer<Int2>(count), gpu.AllocateReadWriteBuffer<Int2>(count)];
        ReadWriteBuffer<Int2>[] intOr = [gpu.AllocateReadWriteBuffer<Int2>(count), gpu.AllocateReadWriteBuffer<Int2>(count)];
        ReadWriteBuffer<UInt4>[] prefix = [gpu.AllocateReadWriteBuffer<UInt4>(count), gpu.AllocateReadWriteBuffer<UInt4>(count)];

        try
        {
            gpu.For(count, new SimdReduceVectors(floats, ints, sums[0], max[0], intSums[0], intOr[0], prefix[0]));
            gpu.ForCpu(count, new SimdReduceVectors(floats, ints, sums[1], max[1], intSums[1], intOr[1], prefix[1]));

            Assert.Equal(sums[0].ToArray(), sums[1].ToArray());
            Assert.Equal(max[0].ToArray(), max[1].ToArray());
            Assert.Equal(intSums[0].ToArray(), intSums[1].ToArray());
            Assert.Equal(intOr[0].ToArray(), intOr[1].ToArray());
            Assert.Equal(prefix[0].ToArray(), prefix[1].ToArray());
            Assert.Equal(new Float3(496f, -496f, 48f), sums[1][0]);
        }
        finally
        {
            foreach (IDisposable buffer in sums.Concat<IDisposable>(max).Concat(intSums).Concat(intOr).Concat(prefix))
            {
                buffer.Dispose();
            }
        }
    }

    [MetalTheory]
    [InlineData(32)]
    [InlineData(40)]
    [InlineData(101)]
    public void LanesThatReturned_TakeNoPart_AsOnTheGpu(int threads)
    {
        int[] input = Enumerable.Range(100, threads).ToArray();
        GraphicsDevice gpu = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = gpu.AllocateReadOnlyBuffer<int>(input);

        (int[] Gpu, int[] Cpu) results = Both<int>(threads * SimdAfterReturns.Stride, int.MinValue, (device, output, cpu) =>
        {
            if (cpu)
            {
                device.ForCpu(threads, new SimdAfterReturns(inputBuffer, output));
            }
            else
            {
                device.For(threads, new SimdAfterReturns(inputBuffer, output));
            }
        });

        // Lane 1 reads lane 0, which has returned by then: undefined, so that one value is not compared.
        for (int thread = 0; thread < threads; thread++)
        {
            if (thread % 64 % 32 % 3 == 1)
            {
                results.Gpu[(thread * SimdAfterReturns.Stride) + 7] = results.Cpu[(thread * SimdAfterReturns.Stride) + 7] = 0;
            }
        }

        AssertSame(results, SimdAfterReturns.Stride, "operations after some lanes returned");

        // sum of all 32, then after lanes 0, 3, 6, ... returned: sum, exclusive count, first lane's value, is first, mask, count
        Assert.Equal([3696, 2431, 0, 101, 1, unchecked((int)0xB6DB6DB6), 21], results.Cpu.AsSpan(SimdAfterReturns.Stride, 7).ToArray());
    }

    [MetalTheory]
    [InlineData(32, 3)]
    [InlineData(8, 3)]
    [InlineData(100, 3)]
    [InlineData(32, 0)]
    [InlineData(32, 31)]
    public void Exchanges_MatchTheGpu_WhereTheSourceLaneExists(int threads, int delta)
    {
        int[] input = Enumerable.Range(1000, threads).ToArray();
        GraphicsDevice gpu = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = gpu.AllocateReadOnlyBuffer<int>(input);

        (int[] Gpu, int[] Cpu) results = Both<int>(threads * SimdExchange.Stride, int.MinValue, (device, output, cpu) =>
        {
            if (cpu)
            {
                device.ForCpu(threads, new SimdExchange(inputBuffer, output, delta));
            }
            else
            {
                device.For(threads, new SimdExchange(inputBuffer, output, delta));
            }
        });

        // Reads of lanes that do not exist are undefined on the GPU; Apple GPUs return 0 and so does
        // the CPU, so in fact everything agrees. That is measured behaviour, pinned here on purpose:
        // if a GPU ever answers differently, this test says where the CPU stopped mirroring it.
        AssertSame(results, SimdExchange.Stride, "exchanges");
    }

    [MetalTheory]
    [InlineData(32, 3)]
    [InlineData(32, 0)]
    [InlineData(32, 31)]
    [InlineData(40, 5)]
    [InlineData(71, 1)]
    public void RotationsFillsAndAllEqual_MatchTheGpu(int threads, int delta)
    {
        int[] input = Enumerable.Range(0, threads).Select(static i => 1000 + i).ToArray();
        GraphicsDevice gpu = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = gpu.AllocateReadOnlyBuffer<int>(input);

        AssertSame(
            Both<int>(threads * SimdShiftsAndEquality.Stride, int.MinValue, (device, output, cpu) =>
            {
                if (cpu)
                {
                    device.ForCpu(threads, new SimdShiftsAndEquality(inputBuffer, output, delta));
                }
                else
                {
                    device.For(threads, new SimdShiftsAndEquality(inputBuffer, output, delta));
                }
            }),
            SimdShiftsAndEquality.Stride,
            "rotations, fills and all-equal");
    }

    [MetalTheory]
    [InlineData(64, 4)]  // every lane takes part
    [InlineData(11, 4)]  // a partial quad
    [InlineData(64, 1)]  // lane 1 of every quad returned
    [InlineData(39, 0)]  // the first lane of every quad returned, and a partial quad
    public void QuadReductionsAndVotes_MatchTheGpu(int threads, int skip)
    {
        int[] input = Enumerable.Range(1, threads).ToArray();
        GraphicsDevice gpu = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = gpu.AllocateReadOnlyBuffer<int>(input);

        AssertSame(
            Both<int>(threads * SimdQuadReductions.Stride, int.MinValue, (device, output, cpu) =>
            {
                if (cpu)
                {
                    device.ForCpu(threads, new SimdQuadReductions(inputBuffer, output, skip));
                }
                else
                {
                    device.For(threads, new SimdQuadReductions(inputBuffer, output, skip));
                }
            }),
            SimdQuadReductions.Stride,
            "quad reductions and votes");
    }

    [MetalFact]
    public void QuadReads_MatchTheGpu_In1DAndIn2D()
    {
        AssertSame(
            Both<int>(2 * 6 * SimdQuads2D.Stride, -1, (device, output, cpu) =>
            {
                if (cpu)
                {
                    device.ForCpu(2, 6, new SimdQuads2D(output));
                }
                else
                {
                    device.For(2, 6, new SimdQuads2D(output));
                }
            }),
            SimdQuads2D.Stride,
            "quad reads in a threadgroup 2 wide");

        float[] input = Enumerable.Range(0, 44).Select(static i => 10f + i).ToArray();
        GraphicsDevice gpu = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> inputBuffer = gpu.AllocateReadOnlyBuffer<float>(input);

        AssertSame(
            Both<float>(input.Length * SimdQuads1D.Stride, -1f, (device, output, cpu) =>
            {
                if (cpu)
                {
                    device.ForCpu(input.Length, new SimdQuads1D(inputBuffer, output));
                }
                else
                {
                    device.For(input.Length, new SimdQuads1D(inputBuffer, output));
                }
            }),
            SimdQuads1D.Stride,
            "quad reads in 1D");
    }

    [MetalTheory]
    [InlineData(12, 12)]
    [InlineData(20, 10)]
    [InlineData(25, 13)]
    public void OperationsIn2DThreadgroups_MatchTheGpu(int width, int height)
    {
        int[] input = Values(width * height, width);
        GraphicsDevice gpu = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = gpu.AllocateReadOnlyBuffer<int>(input);

        AssertSame(
            Both<int>(width * height * SimdOps2D.Stride, int.MinValue, (device, output, cpu) =>
            {
                if (cpu)
                {
                    device.ForCpu(width, height, new SimdOps2D(inputBuffer, output));
                }
                else
                {
                    device.For(width, height, new SimdOps2D(inputBuffer, output));
                }
            }),
            SimdOps2D.Stride,
            "operations in a 12 x 12 threadgroup");
    }

    [MetalFact]
    public void HelpersUniformLoopsAndPixelShaders_MatchTheGpu()
    {
        int[] input = Values(40, 3);
        GraphicsDevice gpu = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = gpu.AllocateReadOnlyBuffer<int>(input);

        AssertSame(
            Both<int>(input.Length * 2, -1, (device, output, cpu) =>
            {
                if (cpu)
                {
                    device.ForCpu(input.Length, new SimdInHelpers(inputBuffer, output));
                }
                else
                {
                    device.For(input.Length, new SimdInHelpers(inputBuffer, output));
                }
            }),
            2,
            "operations in helper methods");

        // A loop and a branch that every lane takes alike: uniform control flow, exact on the CPU.
        AssertSame(
            Both<int>(input.Length, -1, (device, output, cpu) =>
            {
                if (cpu)
                {
                    device.ForCpu(input.Length, new SimdInUniformLoops(inputBuffer, output, passes: 9));
                }
                else
                {
                    device.For(input.Length, new SimdInUniformLoops(inputBuffer, output, passes: 9));
                }
            }),
            1,
            "operations in a uniform loop");

        using ReadWriteTexture2D<Rgba32> onGpu = gpu.AllocateReadWriteTexture2D<Rgba32>(20, 16);
        using ReadWriteTexture2D<Rgba32> onCpu = gpu.AllocateReadWriteTexture2D<Rgba32>(20, 16);
        gpu.ForEach(onGpu, default(SimdPixels));
        gpu.ForEachCpu(onCpu, default(SimdPixels));
        Assert.Equal(onGpu.ToArray(), onCpu.ToArray());
    }

    [MetalTheory]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(5000)]
    public void PartialSums_OnTheCpu_AreTheLaneOrderSums(int count)
    {
        Random random = new(count);
        float[] input = Enumerable.Range(0, count).Select(_ => random.NextSingle()).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> inputBuffer = device.AllocateReadOnlyBuffer<float>(input);
        using ReadWriteBuffer<float> partials = device.AllocateReadWriteBuffer<float>((count + 31) / 32);

        device.ForCpu(((count + 255) / 256) * 256, new SimdPartialSums(inputBuffer, partials));
        float[] actual = partials.ToArray();

        // On the CPU the order is defined: lane 0 first.
        for (int group = 0; group < actual.Length; group++)
        {
            float expected = input.Skip(group * 32).Take(32).Aggregate(0f, static (a, b) => a + b);
            Assert.Equal(expected, actual[group]);
        }
    }

    // ---- with threadgroup memory and barriers -------------------------------------------------------

    [MetalTheory]
    [InlineData(96)]
    [InlineData(192)]
    [InlineData(200)]
    [InlineData(40)]
    public void WithSharedMemoryAndBarriers_MatchesTheGpu(int threads)
    {
        int[] input = Values(threads, threads);
        GraphicsDevice gpu = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = gpu.AllocateReadOnlyBuffer<int>(input);

        (int[] Gpu, int[] Cpu) results = Both<int>(threads * SimdWithSharedMemory.Stride, int.MinValue, (device, output, cpu) =>
        {
            if (cpu)
            {
                device.ForCpu(threads, new SimdWithSharedMemory(inputBuffer, output));
            }
            else
            {
                device.For(threads, new SimdWithSharedMemory(inputBuffer, output));
            }
        });

        AssertSame(results, SimdWithSharedMemory.Stride, "SIMD-group operations with shared memory and barriers");

        // Every thread of a threadgroup of 96 read the sum of its whole threadgroup.
        for (int thread = 0; thread < threads; thread++)
        {
            int start = thread / 96 * 96, end = Math.Min(threads, start + 96);
            Assert.Equal(input[start..end].Sum(), results.Cpu[thread * SimdWithSharedMemory.Stride]);
            Assert.Equal(end - start, results.Cpu[(thread * SimdWithSharedMemory.Stride) + 2]);
        }
    }

    // ---- splits -----------------------------------------------------------------------------------

    [MetalTheory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void ForSplit_GivesTheGpuResult_WhateverTheShare(double cpuFraction)
    {
        const int count = 640; // ten threadgroups of 64
        int[] input = Values(count, 17);
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = device.AllocateReadOnlyBuffer<int>(input);
        using ReadWriteBuffer<int> whole = device.AllocateReadWriteBuffer<int>(count * SimdReduceInt.Stride);
        using ReadWriteBuffer<int> split = device.AllocateReadWriteBuffer<int>(count * SimdReduceInt.Stride);
        using ReadWriteBuffer<int> shared = device.AllocateReadWriteBuffer<int>(576 * SimdWithSharedMemory.Stride);
        using ReadWriteBuffer<int> sharedSplit = device.AllocateReadWriteBuffer<int>(576 * SimdWithSharedMemory.Stride);

        device.For(count, new SimdReduceInt(inputBuffer, whole, count));
        SplitDispatchInfo info = device.ForSplit(count, new SimdReduceInt(inputBuffer, split, count), cpuFraction);

        // The cut is on a threadgroup boundary, so every SIMD group is whole on one device.
        Assert.Equal(0, info.CpuExtent % 64);
        Assert.InRange(info.CpuExtent, (int)(count * cpuFraction) - 32, (int)(count * cpuFraction) + 32);
        Assert.Equal(whole.ToArray(), split.ToArray());

        // The same with barriers and shared memory in the shader (threadgroups of 96, six of them).
        device.For(576, new SimdWithSharedMemory(inputBuffer, shared));
        device.ForSplit(576, new SimdWithSharedMemory(inputBuffer, sharedSplit), cpuFraction);
        Assert.Equal(shared.ToArray(), sharedSplit.ToArray());
    }

    [MetalFact]
    public void ForSplit_Adaptive_And_ForEachSplit_GiveTheGpuResult()
    {
        const int count = 640;
        int[] input = Values(count, 5);
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = device.AllocateReadOnlyBuffer<int>(input);
        using ReadWriteBuffer<int> whole = device.AllocateReadWriteBuffer<int>(count * SimdReduceInt.Stride);
        using ReadWriteBuffer<int> split = device.AllocateReadWriteBuffer<int>(count * SimdReduceInt.Stride);

        device.For(count, new SimdReduceInt(inputBuffer, whole, count));

        for (int i = 0; i < 6; i++)
        {
            split.AsSpan().Clear();
            device.ForSplit(count, new SimdReduceInt(inputBuffer, split, count));
            Assert.Equal(whole.ToArray(), split.ToArray());
        }

        using ReadWriteTexture2D<Rgba32> onGpu = device.AllocateReadWriteTexture2D<Rgba32>(40, 48);
        using ReadWriteTexture2D<Rgba32> halves = device.AllocateReadWriteTexture2D<Rgba32>(40, 48);
        device.ForEach(onGpu, default(SimdPixels));
        device.ForEachSplit(halves, default(SimdPixels), cpuFraction: 0.5);
        Assert.Equal(onGpu.ToArray(), halves.ToArray());
    }

    // ---- what the CPU refuses -----------------------------------------------------------------------

    [MetalFact]
    public void DivergentControlFlow_WorksOnTheGpu_AndThrowsOnTheCpu()
    {
        int[] input = Enumerable.Range(1, 64).ToArray();
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = device.AllocateReadOnlyBuffer<int>(input);
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(64);

        // On the GPU each branch sees its own lanes: the even lanes their sum, the odd lanes their maximum.
        device.For(64, new SimdDivergent(inputBuffer, output));
        Assert.Equal(256, output[0]);  // 1 + 3 + ... + 31
        Assert.Equal(32, output[1]);
        Assert.Equal(768, output[32]); // 33 + 35 + ... + 63
        Assert.Equal(64, output[33]);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.ForCpu(64, new SimdDivergent(inputBuffer, output)));

        Assert.Contains(Divergence, exception.Message);
        Assert.Contains("Gpu.WaveActiveSum", exception.Message);
        Assert.Contains("Gpu.WaveActiveMax", exception.Message);
        Assert.Contains("SimdGroupShaders.cs", exception.Message);

        // The same for the two branches of SimdActiveLanes, which differ only in the line of the call.
        using ReadWriteBuffer<int> wide = device.AllocateReadWriteBuffer<int>(64 * SimdActiveLanes.Stride);
        exception = Assert.Throws<NotSupportedException>(() => device.ForCpu(64, new SimdActiveLanes(inputBuffer, wide)));
        Assert.Contains(Divergence, exception.Message);

        // And in the CPU part of a split.
        exception = Assert.Throws<NotSupportedException>(() => device.ForSplit(64, new SimdDivergent(inputBuffer, output), cpuFraction: 0.5));
        Assert.Contains(Divergence, exception.Message);

        // The dispatcher is fine afterwards.
        device.ForCpu(64, new SimdReduceInt(inputBuffer, wide, 32));
    }

    [MetalTheory]
    [InlineData(16)] // lanes 0..15 are at the barrier while 16..31 are at an operation
    [InlineData(1)]  // one thread at the barrier is enough
    [InlineData(31)] // and so is one lane at the operation
    public async Task AnOperationWhoseLanesAreAtABarrier_ThrowsInsteadOfHanging(int barrierFirstBelow)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(96);

        Task<NotSupportedException> dispatch = Task.Run(() => Assert.Throws<NotSupportedException>(() => device.ForCpu(96, new SimdAgainstBarrier(output, barrierFirstBelow))));
        Task finished = await Task.WhenAny(dispatch, Task.Delay(TimeSpan.FromSeconds(30)));

        Assert.True(ReferenceEquals(finished, dispatch), "The dispatch is still running after 30 seconds: the threads of the group wait for each other.");

        NotSupportedException exception = await dispatch;
        Assert.Contains(Divergence, exception.Message);
        Assert.Contains("barrier", exception.Message);
    }

    [MetalFact]
    public void UniformUseOfBarriersAndOperations_DoesNotTripTheGuard()
    {
        // barrierFirstBelow 0 and 32: every thread takes the same branch, so nothing is divergent.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(70);

        device.ForCpu(70, new SimdAgainstBarrier(output, barrierFirstBelow: 32));
        Assert.Equal([.. Enumerable.Repeat(32, 64), .. Enumerable.Repeat(6, 6)], output.ToArray());

        device.ForCpu(70, new SimdAgainstBarrier(output, barrierFirstBelow: 0));
        Assert.All(output.ToArray(), static value => Assert.Equal(1, value));
    }

    [MetalFact]
    public void AHandWrittenDescriptor_RunsOnTheCpuWhenItReportsItsOperations_AndIsToldWhenItDoesNot()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(40);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(40);

        device.For(40, new ReportedSimdGroups(gpu));
        device.ForCpu(40, new ReportedSimdGroups(cpu));
        Assert.Equal(gpu.ToArray(), cpu.ToArray());
        Assert.Equal(32f, cpu[0]);
        Assert.Equal(8f + 7f, cpu[39]);

        // The descriptor says nothing, so its threads run independently; the operation notices.
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => device.ForCpu(8, new UnreportedSimdGroups(cpu)));
        Assert.Contains("UsesSimdGroups", exception.Message);
        Assert.Contains("Gpu.WaveActiveSum", exception.Message);
    }

    [MetalFact]
    public void AnExceptionInALane_IsRethrown_AndTheLanesWaitingForItAreReleased()
    {
        // input is shorter than the grid: the lanes past it throw from the buffer's bounds check while
        // the others are (about to be) parked at the first operation.
        int[] input = Enumerable.Range(0, 20).ToArray();
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> inputBuffer = device.AllocateReadOnlyBuffer<int>(input);
        using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(32 * SimdReduceInt.Stride);

        Assert.Throws<ArgumentOutOfRangeException>(() => device.ForCpu(32, new SimdReduceInt(inputBuffer, output, count: 32)));

        // And again: nothing is left over from the failed dispatch.
        device.ForCpu(20, new SimdReduceInt(inputBuffer, output, count: 20));
        Assert.Equal(190, output[0]);
    }

    [MetalFact]
    public void ConcurrentCpuDispatches_OfOneShader_DoNotMixTheirLanes()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        Parallel.For(0, 4, new ParallelOptions { MaxDegreeOfParallelism = 4 }, k =>
        {
            int[] input = Enumerable.Range(0, 96).Select(i => i * (k + 1)).ToArray();
            using ReadOnlyBuffer<int> inputBuffer = device.AllocateReadOnlyBuffer<int>(input);
            using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(96 * SimdReduceInt.Stride);

            device.ForCpu(96, new SimdReduceInt(inputBuffer, output, 96));

            Assert.Equal(496 * (k + 1), output[0]);
            Assert.Equal((2016 - 496) * (k + 1), output[32 * SimdReduceInt.Stride]);
        });
    }
}
