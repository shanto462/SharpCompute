using System;
using System.Linq;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>End-to-end dispatches through hand-written descriptors; these double as ABI conformance tests.</summary>
public class DispatchTests
{
    [MetalFact]
    public void MultiplyByTwo_1D()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>([1, 2, 3, 4]);

        device.For(buffer.Length, new MultiplyByTwo(buffer, 2f));

        Assert.Equal([2f, 4f, 6f, 8f], buffer.ToArray());
    }

    [MetalFact]
    public void Saxpy_BindsTwoBuffersAndAPackedConstantsStruct()
    {
        const int count = 1024;
        const float a = 2.5f;
        Float3 offset = new(100f, 200f, 300f);

        float[] x = Enumerable.Range(0, count).Select(i => (float)i).ToArray();
        float[] y = Enumerable.Range(0, count).Select(i => i * 0.5f).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> xBuffer = device.AllocateReadOnlyBuffer<float>(x);
        using ReadWriteBuffer<float> yBuffer = device.AllocateReadWriteBuffer<float>(y);

        device.For(count, new Saxpy(xBuffer, yBuffer, a, offset));

        float[] offsets = [offset.X, offset.Y, offset.Z];
        float[] expected = Enumerable.Range(0, count).Select(i => a * x[i] + y[i] + offsets[i % 3]).ToArray();

        Assert.Equal(expected, yBuffer.ToArray());
        Assert.Equal(x, xBuffer.ToArray());
    }

    [MetalFact]
    public unsafe void Saxpy_ConstantsMirrorMatchesTheMslLayout()
    {
        // The MSL side static_asserts sizeof == 16 and offsetof(offset) == 4; this is the C# half.
        Assert.Equal(16, sizeof(Saxpy.Constants));
        Assert.Equal(12, sizeof(Float3));
    }

    [MetalFact]
    public void Dispatch2D_WritesRowMajorIndex()
    {
        const int width = 37;
        const int height = 19;

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<uint> buffer = device.AllocateReadWriteBuffer<uint>(width * height);
        buffer.AsSpan().Fill(uint.MaxValue);

        device.For(width, height, new WriteIndex2D(buffer, width));

        Assert.Equal(Enumerable.Range(0, width * height).Select(i => (uint)i), buffer.ToArray());
    }

    [MetalFact]
    public void Dispatch3D_EveryThreadSeesItsPositionAndTheGridSize()
    {
        const int width = 13;
        const int height = 7;
        const int depth = 5;

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<uint> buffer = device.AllocateReadWriteBuffer<uint>(width * height * depth);
        buffer.AsSpan().Fill(uint.MaxValue);

        device.For(width, height, depth, new EncodePosition3D(buffer));

        uint[] expected = new uint[width * height * depth];

        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    expected[(z * height + y) * width + x] = (uint)(x | (y << 10) | (z << 20));
                }
            }
        }

        Assert.Equal(expected, buffer.ToArray());
    }

    [MetalFact]
    public void ReadOnlyBuffer_IsReadableFromTheShader()
    {
        int[] input = Enumerable.Range(-500, 1000).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> source = device.AllocateReadOnlyBuffer<int>(input);
        using ReadWriteBuffer<int> destination = device.AllocateReadWriteBuffer<int>(input.Length);

        device.For(input.Length, new Square(source, destination));

        Assert.Equal(input.Select(v => v * v), destination.ToArray());
        Assert.Equal(input, source.ToArray());
    }

    [MetalFact]
    public void PackedVectorElements_UseTwelveByteStride()
    {
        Float3[] input = Enumerable.Range(0, 100).Select(i => new Float3(i, i + 0.25f, i + 0.5f)).ToArray();
        Float3 bias = new(1f, 2f, 3f);

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float3> buffer = device.AllocateReadWriteBuffer<Float3>(input);

        device.For(buffer.Length, new ScaleVectors(buffer, bias));

        Float3[] result = buffer.ToArray();

        for (int i = 0; i < input.Length; i++)
        {
            Assert.Equal(input[i].X * 2f + bias.X, result[i].X);
            Assert.Equal(input[i].Y * 2f + bias.Y, result[i].Y);
            Assert.Equal(input[i].Z * 2f + bias.Z, result[i].Z);
        }
    }

    [MetalFact]
    public void GridSizeThatIsNotAMultipleOfTheThreadGroup_RunsExactlyTheRequestedThreads()
    {
        // 1000 threads with 64 per group: the last group is partial (non-uniform threadgroups), and
        // the 24 elements past the grid must stay untouched without any bounds guard in the kernel.
        const int grid = 1000;
        const int capacity = 1024;

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(capacity);
        buffer.AsSpan().Fill(7f);

        device.For(grid, new MultiplyByTwo(buffer, 3f));

        float[] result = buffer.ToArray();

        Assert.All(result.Take(grid), value => Assert.Equal(21f, value));
        Assert.All(result.Skip(grid), value => Assert.Equal(7f, value));
    }

    [MetalFact]
    public void ConstantsLargerThan4KB_FallBackToATemporaryBuffer()
    {
        LargeConstants constants = CreateLargeConstants(seed: 0.5f, bias: 1000);

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(LargeConstants.ValueCount);

        device.For(buffer.Length, new ReadLargeConstants(buffer, constants));

        Assert.Equal(
            Enumerable.Range(0, LargeConstants.ValueCount).Select(i => i + 0.5f + 1000f),
            buffer.ToArray());
    }

    [MetalFact]
    public void ConstantsLargerThan4KB_AreSnapshottedPerDispatch()
    {
        // Each dispatch gets its own temporary buffer, released once that dispatch completes.
        // Repeating with different contents shows no dispatch sees another one's constants.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(LargeConstants.ValueCount);

        for (uint iteration = 0; iteration < 200; iteration++)
        {
            device.For(buffer.Length, new ReadLargeConstants(buffer, CreateLargeConstants(seed: 0f, bias: iteration)));

            Assert.Equal(iteration, buffer[0]);
            Assert.Equal(LargeConstants.ValueCount - 1 + iteration, buffer[LargeConstants.ValueCount - 1]);
        }
    }

    [MetalFact]
    public void SafeMath_PreservesNaNAndInfinitySemantics()
    {
        float[] input = [1f, float.NaN, float.PositiveInfinity, 0f, -float.NaN, float.NegativeInfinity, -2.5f, float.Epsilon];

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> inputBuffer = device.AllocateReadOnlyBuffer<float>(input);
        using ReadWriteBuffer<uint> output = device.AllocateReadWriteBuffer<uint>(input.Length);

        device.For(input.Length, new ClassifyNaN(inputBuffer, output));

        // NaN: x != x, isnan(x) and (x - x) != (x - x) all hold → 7. Inf: only Inf - Inf is NaN → 4.
        // Under fast math every one of these comparisons is folded to false.
        Assert.Equal([0u, 7u, 4u, 0u, 7u, 4u, 0u, 0u], output.ToArray());
    }

    [MetalFact]
    public void ZeroSizedDispatch_ReturnsWithoutTouchingTheGpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>([1, 2, 3]);

        device.For(0, new MultiplyByTwo(buffer, 2f));
        device.For(0, 5, new MultiplyByTwo(buffer, 2f));
        device.For(5, 0, new MultiplyByTwo(buffer, 2f));
        device.For(0, 5, 5, new MultiplyByTwo(buffer, 2f));
        device.For(5, 0, 5, new MultiplyByTwo(buffer, 2f));
        device.For(5, 5, 0, new MultiplyByTwo(buffer, 2f));

        Assert.Equal([1f, 2f, 3f], buffer.ToArray());

        // Returning immediately means not even compiling: a shader that cannot compile is fine here.
        device.For(0, default(BrokenSyntax));
    }

    [MetalFact]
    public void NegativeDimension_Throws()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>([1, 2, 3]);

        Assert.Throws<ArgumentOutOfRangeException>(() => device.For(-1, new MultiplyByTwo(buffer, 2f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.For(1, -1, new MultiplyByTwo(buffer, 2f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.For(1, 1, -1, new MultiplyByTwo(buffer, 2f)));

        // Negative wins over zero: a zero elsewhere must not turn the call into a silent no-op.
        Assert.Throws<ArgumentOutOfRangeException>(() => device.For(0, -1, new MultiplyByTwo(buffer, 2f)));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.For(-1, 0, 0, new MultiplyByTwo(buffer, 2f)));

        Assert.Equal([1f, 2f, 3f], buffer.ToArray());
    }

    [MetalFact]
    public void RepeatedDispatches_ReuseTheCachedPipeline()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>([1f]);

        for (int i = 0; i < 20; i++)
        {
            device.For(1, new MultiplyByTwo(buffer, 2f));
        }

        Assert.Equal(1048576f, buffer[0]);
    }

    private static unsafe LargeConstants CreateLargeConstants(float seed, uint bias)
    {
        LargeConstants constants = default;

        for (int i = 0; i < LargeConstants.ValueCount; i++)
        {
            constants.Values[i] = i + seed;
        }

        constants.Bias = bias;
        return constants;
    }
}
