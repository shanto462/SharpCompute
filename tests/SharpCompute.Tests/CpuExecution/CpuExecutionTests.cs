using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompute.Samples.Shaders;
using SharpCompute.Tests.EndToEnd;
using static SharpCompute.Tests.CpuExecution.CpuComparison;

namespace SharpCompute.Tests.CpuExecution;

/// <summary><c>ForCpu</c> / <c>ForEachCpu</c>: generated shaders run as C# on the CPU, compared with the same dispatch on the GPU.</summary>
public class CpuExecutionTests
{
    [Fact]
    public void Builtins_OutsideADispatch_ThrowAndNameTheCpuEntryPoints()
    {
        Func<object>[] reads =
        [
            () => ThreadIds.X, () => ThreadIds.Y, () => ThreadIds.Z, () => ThreadIds.XY, () => ThreadIds.XYZ,
            () => ThreadIds.Normalized.X, () => ThreadIds.Normalized.Y, () => ThreadIds.Normalized.Z, () => ThreadIds.Normalized.XY, () => ThreadIds.Normalized.XYZ,
            () => DispatchSize.X, () => DispatchSize.Y, () => DispatchSize.Z, () => DispatchSize.XY, () => DispatchSize.XYZ,
            () => GroupIds.X, () => GroupIds.Y, () => GroupIds.Z, () => GroupIds.XY, () => GroupIds.XYZ,
            () => ThreadGroupIds.X, () => ThreadGroupIds.Y, () => ThreadGroupIds.Z, () => ThreadGroupIds.XY, () => ThreadGroupIds.XYZ,
        ];

        foreach (Func<object> read in reads)
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(read);
            Assert.Contains("Execute", exception.Message);
            Assert.Contains("ForCpu", exception.Message);
        }
    }

    [MetalTheory]
    [InlineData(9, 5, 3)]
    [InlineData(4, 2, 2)]
    [InlineData(1, 1, 1)]
    [InlineData(17, 1, 6)]
    public void Builtins3D_MatchTheGpu(int x, int y, int z)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int count = x * y * z;
        using ReadWriteBuffer<int> gpuIds = device.AllocateReadWriteBuffer<int>(count * 12);
        using ReadWriteBuffer<Float3> gpuNormalized = device.AllocateReadWriteBuffer<Float3>(count);
        using ReadWriteBuffer<int> cpuIds = device.AllocateReadWriteBuffer<int>(count * 12);
        using ReadWriteBuffer<Float3> cpuNormalized = device.AllocateReadWriteBuffer<Float3>(count);

        device.For(x, y, z, new RecordBuiltins3D(gpuIds, gpuNormalized));
        device.ForCpu(x, y, z, new RecordBuiltins3D(cpuIds, cpuNormalized));

        Assert.Equal(gpuIds.ToArray(), cpuIds.ToArray());
        Assert.Equal(Flatten(gpuNormalized.ToArray()), Flatten(cpuNormalized.ToArray()));

        // And the values are the ones the definitions give (ThreadGroupSize(4, 2, 2)).
        int[] ids = cpuIds.ToArray();
        int last = count - 1;
        Assert.Equal([x - 1, y - 1, z - 1, x, y, z, (x - 1) % 4, (y - 1) % 2, (z - 1) % 2, (x - 1) / 4, (y - 1) / 2, (z - 1) / 2], ids[(last * 12)..((last * 12) + 12)]);
    }

    [MetalTheory]
    [InlineData(37, 19)]
    [InlineData(16, 16)]
    [InlineData(1, 40)]
    public void Builtins2D_WithTheDefaultThreadgroupSize_MatchTheGpu(int x, int y)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> gpuIds = device.AllocateReadWriteBuffer<int>(x * y * 8);
        using ReadWriteBuffer<Float2> gpuNormalized = device.AllocateReadWriteBuffer<Float2>(x * y);
        using ReadWriteBuffer<int> cpuIds = device.AllocateReadWriteBuffer<int>(x * y * 8);
        using ReadWriteBuffer<Float2> cpuNormalized = device.AllocateReadWriteBuffer<Float2>(x * y);

        device.For(x, y, new RecordBuiltins2D(gpuIds, gpuNormalized));
        device.ForCpu(x, y, new RecordBuiltins2D(cpuIds, cpuNormalized));

        Assert.Equal(gpuIds.ToArray(), cpuIds.ToArray());
        Assert.Equal(gpuNormalized.ToArray(), cpuNormalized.ToArray());
    }

    [MetalTheory]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(1000)]
    public void Builtins1D_MatchTheGpu(int x)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> gpuIds = device.AllocateReadWriteBuffer<int>(x * 4);
        using ReadWriteBuffer<float> gpuNormalized = device.AllocateReadWriteBuffer<float>(x);
        using ReadWriteBuffer<int> cpuIds = device.AllocateReadWriteBuffer<int>(x * 4);
        using ReadWriteBuffer<float> cpuNormalized = device.AllocateReadWriteBuffer<float>(x);

        device.For(x, new RecordBuiltins1D(gpuIds, gpuNormalized));
        device.ForCpu(x, new RecordBuiltins1D(cpuIds, cpuNormalized));

        Assert.Equal(gpuIds.ToArray(), cpuIds.ToArray());
        Assert.Equal(gpuNormalized.ToArray(), cpuNormalized.ToArray());
    }

    [MetalFact]
    public void EndToEndShaders_MatchTheGpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int n = 1000;
        float[] data = Enumerable.Range(0, n).Select(static i => (i * 0.37f) - 50f).ToArray();
        int[] ints = Enumerable.Range(0, n).Select(static i => (i * 7919) % 1013).ToArray();

        // Kitchen: loops, helpers, float %, MathF and Gpu intrinsics, casts.
        using ReadWriteBuffer<float> kitchenGpu = device.AllocateReadWriteBuffer<float>(data);
        using ReadWriteBuffer<float> kitchenCpu = device.AllocateReadWriteBuffer<float>(data);
        using ReadOnlyBuffer<int> intBuffer = device.AllocateReadOnlyBuffer<int>(ints);
        device.For(n, new Kitchen(kitchenGpu, intBuffer, 1.5f, 3, 0x5A5Au));
        device.ForCpu(n, new Kitchen(kitchenCpu, intBuffer, 1.5f, 3, 0x5A5Au));
        AssertClose(kitchenGpu.ToArray(), kitchenCpu.ToArray());

        // Saxpy with a vector constant.
        using ReadOnlyBuffer<float> x = device.AllocateReadOnlyBuffer<float>(data);
        using ReadWriteBuffer<float> saxpyGpu = device.AllocateReadWriteBuffer<float>(data);
        using ReadWriteBuffer<float> saxpyCpu = device.AllocateReadWriteBuffer<float>(data);
        device.For(n, new Saxpy(x, saxpyGpu, 2.5f, new Float3(1f, 2f, 3f)));
        device.ForCpu(n, new Saxpy(x, saxpyCpu, 2.5f, new Float3(1f, 2f, 3f)));
        AssertClose(saxpyGpu.ToArray(), saxpyCpu.ToArray());

        // Vector elements, swizzles and vector operators.
        Float3[] points = Enumerable.Range(0, n).Select(static i => new Float3(i * 0.1f, -i * 0.2f, i * 0.05f)).ToArray();
        Float2[] uv = Enumerable.Range(0, n).Select(static i => new Float2(i / (float)n, 1f - (i / (float)n))).ToArray();
        using ReadOnlyBuffer<Float2> uvBuffer = device.AllocateReadOnlyBuffer<Float2>(uv);
        using ReadWriteBuffer<Float3> vectorsGpu = device.AllocateReadWriteBuffer<Float3>(points);
        using ReadWriteBuffer<Float3> vectorsCpu = device.AllocateReadWriteBuffer<Float3>(points);
        device.For(n, new Vectors(vectorsGpu, uvBuffer, new Float4(0.5f, 1f, -1f, 2f), new Int2(1, -2)));
        device.ForCpu(n, new Vectors(vectorsCpu, uvBuffer, new Float4(0.5f, 1f, -1f, 2f), new Int2(1, -2)));
        AssertClose(Flatten(vectorsGpu.ToArray()), Flatten(vectorsCpu.ToArray()));

        // User struct elements, a struct constant, 2D with an explicit 8×8 threadgroup and GroupIds/ThreadGroupIds.
        Particle[] particles = Enumerable.Range(0, 24 * 10).Select(static i => new Particle { Position = new Float3(i, 2 * i, 3 * i), Mass = 1f + (i % 5), Uv = new Float2(i, -i), Id = i }).ToArray();
        Params parameters = new() { Dt = 0.25f, Gravity = new Float3(0f, -9.81f, 0.5f), Flags = 3 };
        using ReadWriteBuffer<Particle> stepGpu = device.AllocateReadWriteBuffer<Particle>(particles);
        using ReadWriteBuffer<Particle> stepCpu = device.AllocateReadWriteBuffer<Particle>(particles);
        device.For(24, 10, new Step(stepGpu, parameters));
        device.ForCpu(24, 10, new Step(stepCpu, parameters));
        Particle[] stepExpected = stepGpu.ToArray(), stepActual = stepCpu.ToArray();
        Assert.Equal(stepExpected.Select(static p => p.Id), stepActual.Select(static p => p.Id));
        AssertClose(Flatten(stepExpected.Select(static p => p.Position).ToArray()), Flatten(stepActual.Select(static p => p.Position).ToArray()));

        // Integer-only: identical.
        using ReadWriteBuffer<uint> indexGpu = device.AllocateReadWriteBuffer<uint>(37 * 19);
        using ReadWriteBuffer<uint> indexCpu = device.AllocateReadWriteBuffer<uint>(37 * 19);
        device.For(37, 19, new WriteIndex2D(indexGpu));
        device.ForCpu(37, 19, new WriteIndex2D(indexCpu));
        Assert.Equal(indexGpu.ToArray(), indexCpu.ToArray());
    }

    [MetalFact]
    public void MandelbrotSample_MatchesTheGpu_UpToRounding()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int width = 160, height = 90;
        using ReadWriteBuffer<uint> gpu = device.AllocateReadWriteBuffer<uint>(width * height);
        using ReadWriteBuffer<uint> cpu = device.AllocateReadWriteBuffer<uint>(width * height);

        device.For(width, height, new Mandelbrot(gpu, width, height, 256, new Float2(-0.5f, 0f), 1.2f));
        device.ForCpu(width, height, new Mandelbrot(cpu, width, height, 256, new Float2(-0.5f, 0f), 1.2f));

        // fma contraction can move a point near the set's boundary across an escape iteration, which
        // changes its color completely; everywhere else the smooth coloring agrees to a step or two.
        AssertPixelsClose(ToRgba(gpu.ToArray()), ToRgba(cpu.ToArray()), maxStep: 2, outlierFraction: 0.01, "Mandelbrot");
    }

    [MetalFact]
    public void PixelShader_FloatToUnorm8_MatchesTheGpuExactly()
    {
        // Every unorm8 boundary (k / 255 and (k + 0.5) / 255) with its float neighbours, plus the
        // out-of-range, infinite and NaN cases, then random values.
        var values = new System.Collections.Generic.List<float> { 0f, -0f, 1f, -1f, 2f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.Epsilon, -float.Epsilon, 1e-30f, 0.99999994f };

        for (int k = 0; k <= 255; k++)
        {
            foreach (float center in new[] { k / 255f, (k + 0.5f) / 255f })
            {
                values.Add(center);
                values.Add(MathF.BitDecrement(center));
                values.Add(MathF.BitIncrement(center));
            }
        }

        Random random = new(42);

        while (values.Count % 4 != 0 || values.Count < 8192)
        {
            values.Add((random.NextSingle() * 1.2f) - 0.1f);
        }

        Float4[] colors = Enumerable.Range(0, values.Count / 4).Select(i => new Float4(values[4 * i], values[(4 * i) + 1], values[(4 * i) + 2], values[(4 * i) + 3])).ToArray();
        const int width = 64;
        int height = colors.Length / width;

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<Float4> colorBuffer = device.AllocateReadOnlyBuffer<Float4>(colors);
        using ReadWriteTexture2D<Rgba32> gpu = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        using ReadWriteTexture2D<Rgba32> cpu = device.AllocateReadWriteTexture2D<Rgba32>(width, height);

        device.ForEach(gpu, new ColorFromBuffer(colorBuffer));
        device.ForEachCpu(cpu, new ColorFromBuffer(colorBuffer));

        Rgba32[] expected = gpu.ToArray(), actual = cpu.ToArray();
        string[] mismatches = Enumerable.Range(0, expected.Length)
            .Where(i => !expected[i].Equals(actual[i]))
            .Select(i => $"pixel {i}: color {colors[i].X:R}, {colors[i].Y:R}, {colors[i].Z:R}, {colors[i].W:R}: GPU {expected[i]}, CPU {actual[i]}")
            .ToArray();

        Assert.True(mismatches.Length == 0, $"{mismatches.Length} pixels differ:\n" + string.Join("\n", mismatches.Take(40)));
    }

    [MetalTheory]
    [InlineData(1, 1)]
    [InlineData(17, 3)]
    [InlineData(256, 144)]
    public void HelloWorld_MatchesTheGpu(int width, int height)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> gpu = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        using ReadWriteTexture2D<Rgba32> cpu = device.AllocateReadWriteTexture2D<Rgba32>(width, height);

        device.ForEach(gpu, new HelloWorld(1.25f));
        device.ForEachCpu(cpu, new HelloWorld(1.25f));

        AssertPixelsClose(gpu.ToArray(), cpu.ToArray(), maxStep: 1, outlierFraction: 0, "HelloWorld");
    }

    [MetalTheory]
    [InlineData("ColorfulInfinity", 0.02)]
    [InlineData("FourColorGradient", 0.02)]
    [InlineData("FractalTiling", 0.02)]
    [InlineData("Octagrams", 0.02)]
    [InlineData("TwoTiledTruchet", 0.02)]
    [InlineData("MengerJourney", 0.02)]
    [InlineData("ProteanClouds", 0.02)]
    [InlineData("TerracedHills", 0.02)]
    [InlineData("ExtrudedTruchetPattern", 0.02)]
    // These two hash cell ids with fract(sin(dot(p, k)) * 262144): the GPU's and the CPU's sin agree to
    // a few ULP, and the multiplier turns that into a different random value for some cells.
    [InlineData("PyramidPattern", 0.06)]
    [InlineData("TriangleGridContouring", 0.2)]
    public void GalleryPorts_MatchTheGpu_UpToRounding(string name, double outlierFraction)
    {
        // The ports are internal to the samples assembly, so they are created and dispatched by reflection.
        Type type = typeof(HelloWorld).Assembly.GetType("SharpCompute.Samples.Shaders." + name, throwOnError: true)!;
        object shader = Activator.CreateInstance(type, 1.5f)!;

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> gpu = device.AllocateReadWriteTexture2D<Rgba32>(128, 72);
        using ReadWriteTexture2D<Rgba32> cpu = device.AllocateReadWriteTexture2D<Rgba32>(128, 72);

        Invoke(device, nameof(GraphicsDevice.ForEach), 2, type, gpu, shader);
        Invoke(device, nameof(GraphicsDevice.ForEachCpu), 2, type, cpu, shader);

        // Raymarchers and fractal loops amplify last-bit differences (fma contraction, transcendental
        // ULPs) into visible ones at a few edges; the bulk of the image must agree.
        AssertPixelsClose(gpu.ToArray(), cpu.ToArray(), maxStep: 3, outlierFraction, name);
    }

    [MetalFact]
    public void ZeroDimensions_AreNoOps_AndNegativeOnesThrow()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> data = device.AllocateReadWriteBuffer<int>(4);

        device.ForCpu(0, new FirstThreadOutOfRange(data));
        device.ForCpu(4, 0, new FirstThreadOutOfRange(data));
        device.ForCpu(4, 4, 0, new FirstThreadOutOfRange(data));

        Assert.Throws<ArgumentOutOfRangeException>(() => device.ForCpu(-1, new FirstThreadOutOfRange(data)));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.ForCpu(1, -1, new FirstThreadOutOfRange(data)));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.ForCpu(1, 1, -1, new FirstThreadOutOfRange(data)));
        Assert.Equal([0, 0, 0, 0], data.ToArray());
    }

    [MetalFact]
    public void InvalidArguments_FailUpFront_LikeAGpuDispatch()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        ArgumentNullException missing = Assert.Throws<ArgumentNullException>(() => device.ForCpu(4, default(MultiplyByTwo)));
        Assert.Contains("MultiplyByTwo.buffer", missing.Message);

        ReadWriteBuffer<float> disposed = device.AllocateReadWriteBuffer<float>(4);
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => device.ForCpu(4, new MultiplyByTwo(disposed, 2f)));

        using GraphicsDevice other = GraphicsDevice.CreateSystemDefault();
        using ReadWriteBuffer<float> foreign = other.AllocateReadWriteBuffer<float>([1f, 2f]);
        ArgumentException wrongDevice = Assert.Throws<ArgumentException>(() => device.ForCpu(2, new MultiplyByTwo(foreign, 2f)));
        Assert.Contains("different GraphicsDevice", wrongDevice.Message);
        Assert.Equal([1f, 2f], foreign.ToArray());

        using ReadWriteTexture2D<Rgba32> foreignTexture = other.AllocateReadWriteTexture2D<Rgba32>(2, 2);
        Assert.Throws<ArgumentException>(() => device.ForEachCpu(foreignTexture, new HelloWorld(0f)));
        Assert.Throws<ArgumentNullException>(() => device.ForEachCpu(null!, new HelloWorld(0f)));

        ReadWriteTexture2D<Rgba32> disposedTexture = device.AllocateReadWriteTexture2D<Rgba32>(2, 2);
        disposedTexture.Dispose();
        Assert.Throws<ObjectDisposedException>(() => device.ForEachCpu(disposedTexture, new HelloWorld(0f)));
    }

    [MetalFact]
    public void DisposedDevice_Throws()
    {
        GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>([1f, 2f]);
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(2, 2);
        device.ForCpu(2, new MultiplyByTwo(buffer, 2f));

        device.Dispose();

        Assert.Throws<ObjectDisposedException>(() => device.ForCpu(2, new MultiplyByTwo(buffer, 2f)));
        Assert.Throws<ObjectDisposedException>(() => device.ForCpu(0, new MultiplyByTwo(buffer, 2f)));
        Assert.Throws<ObjectDisposedException>(() => device.ForEachCpu(texture, new HelloWorld(0f)));
        Assert.Equal([2f, 4f], buffer.ToArray());
    }

    [MetalFact]
    public void AnExceptionInTheShader_IsRethrownUnwrapped_AndTheBuiltinsAreReset()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<int> data = device.AllocateReadWriteBuffer<int>(100_000);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => device.ForCpu(data.Length, new FirstThreadOutOfRange(data)));
        Assert.Equal(-1, exception.ActualValue);

        // The calling thread took part in the dispatch; it is no longer "inside" a shader.
        Assert.Throws<InvalidOperationException>(() => ThreadIds.X);
    }

    [MetalFact]
    public void Builtins_AreResetOnTheCallingThread_AfterADispatch()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> data = device.AllocateReadWriteBuffer<float>(10);

        device.ForCpu(data.Length, new SlowWrite(data, 1));

        Assert.Throws<InvalidOperationException>(() => ThreadIds.X);
        Assert.Throws<InvalidOperationException>(() => DispatchSize.X);
        Assert.Equal(Enumerable.Range(0, 10).Select(static i => (float)i), data.ToArray());
    }

    [MetalFact]
    public void ConcurrentDispatches_OnTheCpuAndTheGpu_AreIndependent()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] sizes = [1, 7, 255, 256, 1000, 4097, 30_000, 65_536];

        Parallel.For(0, sizes.Length * 2, i =>
        {
            int x = sizes[i % sizes.Length];
            using ReadWriteBuffer<int> ids = device.AllocateReadWriteBuffer<int>(x * 4);
            using ReadWriteBuffer<float> normalized = device.AllocateReadWriteBuffer<float>(x);

            if (i < sizes.Length)
            {
                device.ForCpu(x, new RecordBuiltins1D(ids, normalized));
            }
            else
            {
                device.For(x, new RecordBuiltins1D(ids, normalized));
            }

            int[] expected = Enumerable.Range(0, x).SelectMany(t => new[] { t, x, t % 256, t / 256 }).ToArray();
            Assert.Equal(expected, ids.ToArray());
        });
    }

    [MetalFact]
    public void DisposingABufferDuringACpuDispatch_FailsCleanly()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadWriteBuffer<float> data = device.AllocateReadWriteBuffer<float>(200_000);
        using ManualResetEventSlim started = new();

        Task dispatch = Task.Run(() =>
        {
            started.Set();
            device.ForCpu(data.Length, new SlowWrite(data, 200));
        });

        started.Wait();
        Thread.Sleep(5);
        data.Dispose();

        // The dispatch either finished first or hit the disposed buffer; it never touches freed memory
        // (the dispatch's lease keeps the MTLBuffer alive until it returns).
        try
        {
            dispatch.GetAwaiter().GetResult();
        }
        catch (ObjectDisposedException)
        {
        }

        Assert.Throws<ObjectDisposedException>(() => device.ForCpu(data.Length, new SlowWrite(data, 1)));
    }

    private static Rgba32[] ToRgba(uint[] packed) =>
        packed.Select(static p => new Rgba32((byte)p, (byte)(p >> 8), (byte)(p >> 16), (byte)(p >> 24))).ToArray();
}
