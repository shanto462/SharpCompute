using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpCompute.Tests.LocalArrays;

/// <summary>
/// Local arrays (stackalloc, array locals, collection expressions and inline arrays) on the GPU against
/// the same shader on the CPU (<c>ForCpu</c>), and both against plain C#.
/// </summary>
public class LocalArrayTests
{
    private static float[] RandomFloats(int count, int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, count).Select(_ => (random.Next(-1000, 1001) / 8f)).ToArray();
    }

    [MetalFact]
    public void SortingNetwork_SortsEveryRow_OnBothDevices()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Rows = 4096;
        float[] values = RandomFloats(Rows * SortEight.Width, 1);

        // Ties and already sorted or reversed rows are where a wrong network shows.
        values.AsSpan(0, 8).Fill(3f);
        Enumerable.Range(0, 8).Select(static i => (float)i).ToArray().CopyTo(values, 8);
        Enumerable.Range(0, 8).Select(static i => (float)(8 - i)).ToArray().CopyTo(values, 16);

        using ReadOnlyBuffer<float> input = device.AllocateReadOnlyBuffer<float>(values);
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(values.Length);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(values.Length);

        device.For(Rows, new SortEight(input, gpu));
        device.ForCpu(Rows, new SortEight(input, cpu));

        float[] expected = values.Chunk(SortEight.Width).SelectMany(static row => row.Order()).ToArray();
        Assert.Equal(expected, gpu.ToArray());
        Assert.Equal(expected, cpu.ToArray());
    }

    [MetalFact]
    public void MedianFilter_IsTheMiddleOfTheSortedNeighbourhood()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Width = 97, Height = 61;
        float[] pixels = RandomFloats(Width * Height, 2);

        using ReadOnlyBuffer<float> image = device.AllocateReadOnlyBuffer<float>(pixels);
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(pixels.Length);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(pixels.Length);

        device.For(Width, Height, new Median3x3(image, gpu, Width, Height));
        device.ForCpu(Width, Height, new Median3x3(image, cpu, Width, Height));

        float[] expected = new float[pixels.Length];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                float[] window = new float[9];
                int n = 0;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        window[n++] = pixels[(Math.Clamp(y + dy, 0, Height - 1) * Width) + Math.Clamp(x + dx, 0, Width - 1)];
                    }
                }

                Array.Sort(window);
                expected[(y * Width) + x] = window[4];
            }
        }

        Assert.Equal(expected, gpu.ToArray());
        Assert.Equal(expected, cpu.ToArray());
    }

    [MetalFact]
    public void MatrixArrays_ChainTransformsAndRotations_LikeSystemNumerics()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Threads = 257;
        var random = new Random(3);

        // Small multiples of 1/8: every product and sum below is exact in a float, in any order.
        Float4x4[] matrices = Enumerable.Range(0, 7)
            .Select(_ => MemoryMarshal.Cast<float, Float4x4>(Enumerable.Range(0, 16).Select(_ => random.Next(-8, 9) / 8f).ToArray())[0])
            .ToArray();

        using ReadOnlyBuffer<Float4x4> transforms = device.AllocateReadOnlyBuffer<Float4x4>(matrices);
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(Threads * MatrixArrays.Slots);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(Threads * MatrixArrays.Slots);

        device.For(Threads, new MatrixArrays(transforms, gpu));
        device.ForCpu(Threads, new MatrixArrays(transforms, cpu));

        float[] expected = new float[Threads * MatrixArrays.Slots];

        for (int i = 0; i < Threads; i++)
        {
            System.Numerics.Vector4 point = new(i % 5, 1, -2, 1);

            for (int k = 0; k < 3; k++)
            {
                Float4x4 m = matrices[(i + k) % matrices.Length];
                point = System.Numerics.Vector4.Transform(point, Unsafe.As<Float4x4, System.Numerics.Matrix4x4>(ref m));
            }

            // Row vectors: (1, 2, 3) x rows (0,-1,0) (1,0,0) (0,0,1) = (2, -1, 3), then
            // x rows (1,0,0) (0,0,-1) (0,1,0) = (2, 3, 1).
            float[] slots = [point.X, point.Y, point.Z, point.W, 2, 3, 1];
            slots.CopyTo(expected, i * MatrixArrays.Slots);
        }

        Assert.Equal(expected, gpu.ToArray());
        Assert.Equal(expected, cpu.ToArray());
    }

    [MetalFact]
    public void StructArrays_SortStructsByWeight_ThroughRefElements()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Threads = 1000;
        var random = new Random(4);
        int[] seeds = Enumerable.Range(0, Threads * 4).Select(_ => random.Next(0, 10_000)).ToArray();

        using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer<int>(seeds);
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(Threads * StructArrays.Slots);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(Threads * StructArrays.Slots);

        device.For(Threads, new StructArrays(input, gpu));
        device.ForCpu(Threads, new StructArrays(input, cpu));

        float[] expected = new float[Threads * StructArrays.Slots];

        for (int i = 0; i < Threads; i++)
        {
            // The same exchange sort in plain C#: ties keep whatever order the swaps leave them in.
            var particles = Enumerable.Range(0, 4)
                .Select(k => (Seed: seeds[(i * 4) + k], K: k))
                .Select(static p => (Weight: p.Seed % 11, Position: new Float3(p.Seed % 7, p.Seed % 5, p.K)))
                .ToArray();

            for (int a = 0; a < 4; a++)
            {
                for (int b = a + 1; b < 4; b++)
                {
                    if (particles[b].Weight < particles[a].Weight)
                    {
                        (particles[a], particles[b]) = (particles[b], particles[a]);
                    }
                }
            }

            Float3 sum = new(0, 0, 0);

            for (int k = 0; k < 4; k++)
            {
                sum = (sum * 2f) + particles[k].Position;
                expected[(i * StructArrays.Slots) + k] = particles[k].Weight;
            }

            expected[(i * StructArrays.Slots) + 4] = sum.X;
            expected[(i * StructArrays.Slots) + 5] = sum.Y;
            expected[(i * StructArrays.Slots) + 6] = sum.Z;
        }

        Assert.Equal(expected, gpu.ToArray());
        Assert.Equal(expected, cpu.ToArray());
    }

    [MetalFact]
    public void Helpers_WriteIntoTheCallersArrays()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Threads = 300;

        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(Threads * HelperWritesCallersArray.Slots);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(Threads * HelperWritesCallersArray.Slots);

        device.For(Threads, new HelperWritesCallersArray(gpu));
        device.ForCpu(Threads, new HelperWritesCallersArray(cpu));

        float[] expected = new float[Threads * HelperWritesCallersArray.Slots];

        for (int i = 0; i < Threads; i++)
        {
            float[] floats = Enumerable.Range(0, 6).Select(k => (float)(2 * (i + k))).ToArray();
            int[] ints = Enumerable.Range(0, 4).Select(k => (k + (i % 3)) * (k + (i % 3))).ToArray();
            float[] slots = [.. floats, .. ints.Select(static n => (float)n), floats.Sum() - ints.Sum()];
            slots.CopyTo(expected, i * HelperWritesCallersArray.Slots);
        }

        Assert.Equal(expected, gpu.ToArray());
        Assert.Equal(expected, cpu.ToArray());
    }

    [MetalFact]
    public void Initializers_ConstantTablesRunTimeElementsAndZeroedArrays()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Threads = 512;
        var random = new Random(5);
        int[] values = Enumerable.Range(0, Threads).Select(_ => random.Next()).ToArray();

        using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer<int>(values);
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(Threads * Initializers.Slots);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(Threads * Initializers.Slots);

        device.For(Threads, new Initializers(input, gpu));
        device.ForCpu(Threads, new Initializers(input, cpu));

        float[] expected = new float[Threads * Initializers.Slots];

        for (int i = 0; i < Threads; i++)
        {
            float[] weights = [0.5f, 0.25f, 0.125f, 0.0625f];
            float weighted = 0;

            for (int k = 0; k < 4; k++)
            {
                weighted += weights[k] * (i + k);
            }

            int primeSum = new[] { 2, 3, 5, 7, 11 }.Sum(p => p * (i % p));
            int[] bins = new int[4];

            for (int k = 0; k < 16; k++)
            {
                bins[(values[i] >> k) & 3]++;
            }

            float[] slots = [weighted, primeSum, i + (i * 2) + values[i], .. bins.Select(static b => (float)b)];
            slots.CopyTo(expected, i * Initializers.Slots);
        }

        // The sum of i, 2i and a random int is rounded to a float the same way everywhere.
        Assert.Equal(expected, gpu.ToArray());
        Assert.Equal(expected, cpu.ToArray());
    }

    [MetalFact]
    public void InlineArrays_CrossTheBoundary_AsConstantsBufferElementsAndStructFields()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Threads = 200;
        Assert.Equal(16, Unsafe.SizeOf<Quad>());
        Assert.Equal(20, Unsafe.SizeOf<Tagged>());

        Quad scale = default;
        scale[0] = 1;
        scale[1] = 2;
        scale[2] = 0.5f;
        scale[3] = -1;

        Quad[] quads = new Quad[Threads];
        Tagged[] tagged = new Tagged[Threads];

        for (int i = 0; i < Threads; i++)
        {
            for (int k = 0; k < 4; k++)
            {
                quads[i][k] = i + (k * 0.25f);
                tagged[i].Values[k] = -k;
            }

            tagged[i].Tag = i % 7;
        }

        (Quad[] Quads, Tagged[] Tagged) Run(bool onCpu)
        {
            using ReadWriteBuffer<Quad> quadBuffer = device.AllocateReadWriteBuffer<Quad>(quads);
            using ReadWriteBuffer<Tagged> taggedBuffer = device.AllocateReadWriteBuffer<Tagged>(tagged);
            var shader = new InlineArrays(quadBuffer, taggedBuffer, scale);

            if (onCpu)
            {
                device.ForCpu(Threads, shader);
            }
            else
            {
                device.For(Threads, shader);
            }

            return (quadBuffer.ToArray(), taggedBuffer.ToArray());
        }

        (Quad[] gpuQuads, Tagged[] gpuTagged) = Run(onCpu: false);
        (Quad[] cpuQuads, Tagged[] cpuTagged) = Run(onCpu: true);

        for (int i = 0; i < Threads; i++)
        {
            float sum = 0;

            for (int k = 0; k < 4; k++)
            {
                float expected = (quads[i][k] * scale[k]) + tagged[i].Tag;
                Assert.Equal(expected, gpuQuads[i][k]);
                Assert.Equal(expected, cpuQuads[i][k]);
                sum += expected;
            }

            Assert.Equal(tagged[i].Tag + 1, gpuTagged[i].Tag);
            Assert.Equal(tagged[i].Tag + 1, cpuTagged[i].Tag);

            for (int k = 0; k < 4; k++)
            {
                float expected = k == i % 4 ? sum : -k;
                Assert.Equal(expected, gpuTagged[i].Values[k]);
                Assert.Equal(expected, cpuTagged[i].Values[k]);
            }
        }
    }

    [MetalFact]
    public void LocalArrays_WorkAlongsideGroupSharedMemorySimdGroupsAndThreadgroupAtomics()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Groups = 24, Threads = Groups * SortThenReduceGroups.GroupSize, Width = SortThenReduceGroups.Width;
        var random = new Random(6);
        int[] values = Enumerable.Range(0, Threads * Width).Select(_ => random.Next(-1000, 1000)).ToArray();

        using ReadOnlyBuffer<int> input = device.AllocateReadOnlyBuffer<int>(values);

        (int[] Sorted, int[] Totals) Run(bool onCpu)
        {
            using ReadWriteBuffer<int> sorted = device.AllocateReadWriteBuffer<int>(values.Length);
            using ReadWriteBuffer<int> totals = device.AllocateReadWriteBuffer<int>(Groups);
            var shader = new SortThenReduceGroups(input, sorted, totals);

            if (onCpu)
            {
                device.ForCpu(Threads, shader);
            }
            else
            {
                device.For(Threads, shader);
            }

            return (sorted.ToArray(), totals.ToArray());
        }

        int[][] rows = values.Chunk(Width).Select(static row => row.Order().ToArray()).ToArray();
        int[] expectedSorted = rows.SelectMany(static row => row).ToArray();
        int[] expectedTotals = Enumerable.Range(0, Groups)
            .Select(g => rows.Skip(g * SortThenReduceGroups.GroupSize).Take(SortThenReduceGroups.GroupSize).Sum(static row => row.Select(static (v, k) => v * (k + 1)).Sum()))
            .ToArray();

        (int[] gpuSorted, int[] gpuTotals) = Run(onCpu: false);
        (int[] cpuSorted, int[] cpuTotals) = Run(onCpu: true);

        Assert.Equal(expectedSorted, gpuSorted);
        Assert.Equal(expectedSorted, cpuSorted);
        Assert.Equal(expectedTotals, gpuTotals);
        Assert.Equal(expectedTotals, cpuTotals);
    }

    [MetalFact]
    public void OutOfRangeIndex_ThrowsOnTheCpu_TheDebuggingAidTheGpuDoesNotHave()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> output = device.AllocateReadWriteBuffer<float>(8);

        device.ForCpu(8, new IndexLocalArray(output, 3));
        Assert.Equal([0, 0, 0, 1, 0, 0, 0, 1], output.ToArray());

        Assert.Throws<IndexOutOfRangeException>(() => device.ForCpu(8, new IndexLocalArray(output, 4)));
        Assert.Throws<IndexOutOfRangeException>(() => device.ForCpu(8, new IndexLocalArray(output, -1)));

        // In range, the GPU computes the same.
        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(8);
        device.For(8, new IndexLocalArray(gpu, 3));
        Assert.Equal(output.ToArray(), gpu.ToArray());
    }
}
