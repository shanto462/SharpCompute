using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SharpCompute.Interop;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// Textures in shaders: what a shader reads, writes and samples for every pixel format, on the GPU and
/// on the CPU (<c>ForCpu</c> of the same shader), plus binding, views and lifetime.
/// </summary>
public unsafe class TextureShaderTests
{
    /// <summary>One step of an 8-bit normalized channel.</summary>
    private const float Step = 1f / 255f;

    /// <summary>
    /// The largest difference between the GPU's sRGB decode and the CPU's (the exact transfer
    /// function). Measured over all 256 values on an Apple M4 Pro: 1.2206e-4, because the GPU decodes
    /// to 12 bits (every value it returns is k / 4095). That is half a 12-bit step, which is what is
    /// pinned here, with room for the last float bit.
    /// </summary>
    private const float SrgbReadTolerance = (0.5f / 4095f) + 1e-7f;

    private static readonly Type[] AllFormats = [typeof(Rgba32), typeof(Rgba32Srgb), typeof(R8), typeof(Rg16), typeof(Half), typeof(Rgba16Float), typeof(float), typeof(Float4)];

    public static TheoryData<Type> Formats => new(AllFormats);

    public static TheoryData<Type> ReadWriteFormats => new(typeof(Rgba32), typeof(R8), typeof(Half), typeof(Rgba16Float), typeof(float), typeof(Float4));

    public static TheoryData<Type, int> FormatsAndSamplers
    {
        get
        {
            TheoryData<Type, int> data = [];

            foreach (Type format in AllFormats)
            {
                for (int sampler = 0; sampler < 6; sampler++)
                {
                    data.Add(format, sampler);
                }
            }

            return data;
        }
    }

    private static readonly Sampler[] Samplers =
        [Sampler.PointClamp, Sampler.PointRepeat, Sampler.PointMirror, Sampler.LinearClamp, Sampler.LinearRepeat, Sampler.LinearMirror];

    // ---- reading ----------------------------------------------------------------------------------

    [MetalTheory]
    [MemberData(nameof(Formats))]
    public void Read_ReturnsWhatTextureFormatsLoadDecodes(Type format) => RunGeneric(nameof(ReadCase), format, 23, 13);

    private static void ReadCase<T>(int width, int height)
        where T : unmanaged
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        T[] pixels = TestPixels<T>(width * height);
        Float4[] expected = Decode(pixels);

        using ReadOnlyTexture2D<T> texture = device.AllocateReadOnlyTexture2D<T>(pixels, width, height);
        using ReadWriteBuffer<Float4> gpu = device.AllocateReadWriteBuffer<Float4>(pixels.Length);
        using ReadWriteBuffer<Float4> cpu = device.AllocateReadWriteBuffer<Float4>(pixels.Length);

        device.For(width, height, new ReadTexels<T>(texture, gpu));
        device.ForCpu(width, height, new ReadTexels<T>(texture, cpu));

        // On the CPU the indexer is Load, so this pins the addressing (row padding included).
        AssertBitEqual(expected, cpu.ToArray(), "CPU");
        AssertDecoded<T>(expected, gpu.ToArray());
    }

    [MetalTheory]
    [MemberData(nameof(ReadWriteFormats))]
    public void ReadOfAReadWriteTexture_ReturnsWhatTextureFormatsLoadDecodes(Type format) => RunGeneric(nameof(LoadCase), format, 23, 13);

    private static void LoadCase<T>(int width, int height)
        where T : unmanaged
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        T[] pixels = TestPixels<T>(width * height);
        Float4[] expected = Decode(pixels);

        using ReadWriteTexture2D<T> texture = device.AllocateReadWriteTexture2D<T>(pixels, width, height);
        using ReadWriteBuffer<Float4> gpu = device.AllocateReadWriteBuffer<Float4>(pixels.Length);
        using ReadWriteBuffer<Float4> cpu = device.AllocateReadWriteBuffer<Float4>(pixels.Length);

        device.For(width, height, new LoadTexels<T>(texture, gpu));
        device.ForCpu(width, height, new LoadTexels<T>(texture, cpu));

        AssertBitEqual(expected, cpu.ToArray(), "CPU");
        AssertBitEqual(expected, gpu.ToArray(), "GPU");
    }

    [MetalFact]
    public void SrgbDecode_MatchesTheTransferFunction_AndIsNotTheLinearRead()
    {
        // All 256 values in every channel; alpha is linear, the colors are not.
        Rgba32Srgb[] pixels = Enumerable.Range(0, 256).Select(static i => new Rgba32Srgb((byte)i, (byte)(255 - i), (byte)((i * 7) & 255), (byte)i)).ToArray();

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyTexture2D<Rgba32Srgb> texture = device.AllocateReadOnlyTexture2D<Rgba32Srgb>(pixels, 16, 16);
        using ReadWriteBuffer<Float4> output = device.AllocateReadWriteBuffer<Float4>(256);

        device.For(16, 16, new ReadTexels<Rgba32Srgb>(texture, output));

        Float4[] gpu = output.ToArray();
        float worst = 0;

        for (int i = 0; i < 256; i++)
        {
            worst = MathF.Max(worst, MathF.Abs(gpu[i].X - Srgb(i)));
            worst = MathF.Max(worst, MathF.Abs(gpu[i].Y - Srgb(255 - i)));
            worst = MathF.Max(worst, MathF.Abs(gpu[i].Z - Srgb((i * 7) & 255)));

            Assert.Equal(i / 255f, gpu[i].W);
        }

        Assert.True(worst <= SrgbReadTolerance, $"The GPU's sRGB decode differs from the transfer function by up to {worst:R}.");

        // The tolerance is not slack: the decode really is that coarse (and no coarser than 12 bits).
        Assert.True(worst > 1e-5f, $"The GPU's sRGB decode is closer to the transfer function than expected: {worst:R}.");

        // 0.5 in sRGB is about 0.214 linear: far from the 0.502 a plain unorm read would give.
        Assert.InRange(gpu[128].X, 0.21f, 0.22f);

        static float Srgb(int value)
        {
            double c = value / 255.0;
            return (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }
    }

    // ---- writing ----------------------------------------------------------------------------------

    [MetalTheory]
    [MemberData(nameof(ReadWriteFormats))]
    public void Write_StoresWhatTextureFormatsStoreEncodes(Type format) => RunGeneric(nameof(WriteCase), format, 64, 37);

    private static void WriteCase<T>(int width, int height)
        where T : unmanaged
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Float4[] colors = TestColors<T>(width * height);
        T[] expected = new T[colors.Length];

        fixed (T* destination = expected)
        {
            for (int i = 0; i < colors.Length; i++)
            {
                TextureFormats.Store(destination + i, colors[i]);
            }
        }

        using ReadOnlyBuffer<Float4> source = device.AllocateReadOnlyBuffer<Float4>(colors);
        using ReadWriteTexture2D<T> gpu = device.AllocateReadWriteTexture2D<T>(width, height);
        using ReadWriteTexture2D<T> cpu = device.AllocateReadWriteTexture2D<T>(width, height);

        device.For(width, height, new StoreTexels<T>(gpu, source));
        device.ForCpu(width, height, new StoreTexels<T>(cpu, source));

        AssertSameBytes(expected, cpu.ToArray(), colors, "CPU");
        AssertSameBytes(expected, gpu.ToArray(), colors, "GPU");
    }

    // ---- sampling ---------------------------------------------------------------------------------

    [MetalTheory]
    [MemberData(nameof(FormatsAndSamplers))]
    public void Sample_GpuAndCpuAgree_InsideOnTheEdgesAndOutside(Type format, int sampler)
    {
        // A power-of-two size, where every coordinate below is exact in float, and an odd one, whose rows are padded.
        RunGeneric(nameof(SampleCase), format, sampler, 8, 4);
        RunGeneric(nameof(SampleCase), format, sampler, 5, 3);
    }

    private static void SampleCase<T>(int sampler, int width, int height)
        where T : unmanaged
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        T[] pixels = SamplePixels<T>(width * height);
        Float2[] coordinates = SampleCoordinates(width, height, exactPoints: (width & (width - 1)) == 0);

        Float4[] gpu = SampleOnGpu(device, pixels, width, height, coordinates, sampler, out Float4[] cpu);
        bool linear = Samplers[sampler].Filter == TextureFilter.Linear;

        for (int i = 0; i < coordinates.Length; i++)
        {
            string where = $"{typeof(T).Name} {width}×{height}, {Samplers[sampler]}, uv ({coordinates[i].X:R}, {coordinates[i].Y:R})";

            for (int channel = 0; channel < 4; channel++)
            {
                float g = Channel(gpu[i], channel), c = Channel(cpu[i], channel);

                float tolerance;

                if (!linear)
                {
                    // A point sample is one texel, and at these coordinates (none within float
                    // rounding of a texel boundary, except at the power-of-two size) it is the same
                    // texel on both sides: exact, or for sRGB within the precision of the GPU's decode.
                    // PointSample_PicksTheSameTexelAsTheGpu_WhereThatHolds covers the boundaries.
                    tolerance = typeof(T) == typeof(Rgba32Srgb) ? SrgbReadTolerance : 0f;
                }
                else if (IsEightBit<T>())
                {
                    // Measured: 0.06 of a step at these coordinates. The bound is the stated 2/255.
                    tolerance = 2 * Step;
                }
                else
                {
                    // 1e-5 relative. A half texture is filtered to a half: the GPU rounds the result
                    // to the nearest one (measured: every result is a half, at most 0.507 of the
                    // spacing between halves away from the CPU's), so half a spacing is added for those.
                    float magnitude = MathF.Max(MathF.Abs(g), MathF.Abs(c));
                    tolerance = 1e-5f * magnitude;

                    if (IsHalf<T>())
                    {
                        Assert.True((float)(Half)g == g, $"{where}, channel {channel}: the GPU returned {g:R}, which is not a half.");
                        tolerance += magnitude == 0f ? 0f : 0.5f * MathF.ScaleB(1f, MathF.ILogB(magnitude) - 10);
                    }
                }

                Assert.True(MathF.Abs(g - c) <= tolerance, $"{where}, channel {channel}: GPU {g:R}, CPU {c:R} (tolerance {tolerance:R}).");
            }
        }
    }

    [MetalTheory]
    [InlineData(typeof(float))]
    [InlineData(typeof(Float4))]
    [InlineData(typeof(Half))]
    [InlineData(typeof(Rgba16Float))]
    public void LinearSample_AtArbitraryCoordinates_DiffersByAtMostTheGpusWeightPrecision(Type format) =>
        RunGeneric(nameof(WeightPrecisionCase), format);

    private static void WeightPrecisionCase<T>()
        where T : unmanaged
    {
        // The GPU rounds each interpolation weight to 1/256 (so it is off by up to 1/512), while the
        // CPU interpolates in full float precision. With texels in [0.5, 2] the two bilinear results
        // can therefore differ by up to 2 × 1.5 / 512; they must not differ by more.
        const int width = 16, height = 8;
        const float range = 1.5f;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        T[] pixels = SamplePixels<T>(width * height);
        Random random = new(7);
        Float2[] coordinates = Enumerable.Range(0, 4096).Select(_ => new Float2((random.NextSingle() * 6f) - 2.5f, (random.NextSingle() * 6f) - 2.5f)).ToArray();
        float worst = 0;

        foreach (int sampler in new[] { 3, 4, 5 })
        {
            Float4[] gpu = SampleOnGpu(device, pixels, width, height, coordinates, sampler, out Float4[] cpu);

            for (int i = 0; i < coordinates.Length; i++)
            {
                for (int channel = 0; channel < 4; channel++)
                {
                    worst = MathF.Max(worst, MathF.Abs(Channel(gpu[i], channel) - Channel(cpu[i], channel)));
                }
            }
        }

        // (A half texture's result is also rounded to a half: up to half the spacing of halves below 2.)
        float bound = (2 * range / 512f) + 1e-5f + (IsHalf<T>() ? 0.5f / 1024f : 0f);

        Assert.True(worst <= bound, $"{typeof(T).Name}: the largest GPU/CPU difference is {worst:R} (bound {bound:R}).");

        // And the quantization is real: with exact weights the difference would be around 1e-7.
        Assert.True(worst > 1e-4f, $"{typeof(T).Name}: expected the GPU's 8-bit weights to show, but the largest difference is only {worst:R}.");
    }

    [MetalTheory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(37)]
    [InlineData(100)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(1000)]
    [InlineData(1023)]
    [InlineData(4096)]
    public void PointSample_PicksTheSameTexelAsTheGpu_WhereThatHolds(int size)
    {
        // What holds, measured on an Apple M4 Pro: PointClamp is the GPU's texel at every size and
        // coordinate; so are PointRepeat and PointMirror at power-of-two sizes, and at any size for
        // coordinates in [0, 1). At other sizes, Repeat and Mirror outside [0, 1) can differ within
        // float rounding of a texel boundary (e.g. size 37, u = -1.972973, the float nearest -73/37:
        // the CPU's single-precision u * 37 is exactly -73, texel 1; the GPU reads texel 0). The
        // differences are not pinned by count, only bounded: a neighbouring texel, that close to a boundary.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        bool powerOfTwo = (size & (size - 1)) == 0;

        // On and around every texel boundary (or every 64th of the texture, for large ones) from two
        // sizes below 0 to three sizes above, then random coordinates.
        List<float> positions = [];
        int stride = Math.Max(1, size / 64);

        foreach (double offset in new[] { 0.4, 0.1, 1.0 / 256, 1.0 / 1024, 1e-3, 1e-4, 1e-5, 0 })
        {
            for (int boundary = -2 * size; boundary <= 3 * size; boundary += stride)
            {
                positions.Add((float)((boundary - offset) / size));
                positions.Add((float)((boundary + offset) / size));
            }
        }

        Random random = new(12345 + size);

        for (int i = 0; i < 20000; i++)
        {
            positions.Add((float)((random.NextDouble() * 7) - 3));
        }

        // One texel per value along the axis under test, so a sample says which texel it read.
        Float4[] pixels = Enumerable.Range(0, size).Select(static i => new Float4(i, 0f, 0f, 1f)).ToArray();

        foreach (bool vertical in new[] { false, true })
        {
            Float2[] coordinates = positions.Select(position => vertical ? new Float2(0.5f, position) : new Float2(position, 0.5f)).ToArray();

            using ReadOnlyTexture2D<Float4> texture = vertical
                ? device.AllocateReadOnlyTexture2D<Float4>(pixels, 1, size)
                : device.AllocateReadOnlyTexture2D<Float4>(pixels, size, 1);
            using ReadOnlyBuffer<Float2> uvs = device.AllocateReadOnlyBuffer<Float2>(coordinates);
            using ReadWriteBuffer<Float4> gpuOutput = device.AllocateReadWriteBuffer<Float4>(coordinates.Length);
            using ReadWriteBuffer<Float4> cpuOutput = device.AllocateReadWriteBuffer<Float4>(coordinates.Length);

            for (int sampler = 0; sampler < 3; sampler++)
            {
                device.For(coordinates.Length, new SampleFloat4(texture, uvs, gpuOutput, sampler));
                device.ForCpu(coordinates.Length, new SampleFloat4(texture, uvs, cpuOutput, sampler));

                Float4[] gpu = gpuOutput.ToArray(), cpu = cpuOutput.ToArray();
                bool exact = Samplers[sampler].AddressMode == TextureAddressMode.Clamp || powerOfTwo;

                for (int i = 0; i < gpu.Length; i++)
                {
                    if (gpu[i].X == cpu[i].X)
                    {
                        continue;
                    }

                    float u = positions[i];
                    string where = $"size {size}{(vertical ? " (vertical)" : "")}, {Samplers[sampler]}, coordinate {u:R}: GPU texel {gpu[i].X}, CPU texel {cpu[i].X}";

                    Assert.False(exact, $"{where}. Clamp and power-of-two sizes must agree everywhere.");
                    Assert.False(u >= 0f && u < 1f, $"{where}. Coordinates in [0, 1) must agree.");

                    // The neighbouring texel (texels 0 and size - 1 are neighbours where the texture repeats)...
                    float apart = MathF.Abs(gpu[i].X - cpu[i].X);
                    Assert.True(apart == 1f || apart == size - 1, $"{where}. Not a neighbouring texel.");

                    // ...for a coordinate within a float step of the boundary between them: its exact
                    // position in texels is within size × 2⁻²² of a whole number (measured: up to 0.7 of that).
                    double texels = (double)u * size;
                    Assert.True(Math.Abs(texels - Math.Round(texels)) <= size * Math.Pow(2, -22), $"{where}. Not at a texel boundary: {texels:R} texels.");
                }
            }
        }
    }

    [MetalTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void GeneratedSampleShader_AgreesWithTheHandWrittenOne(int sampler)
    {
        // The per-format tests use a hand-written generic shader; this ties it to what the generator emits.
        const int width = 5, height = 3;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Float4[] pixels = SamplePixels<Float4>(width * height);
        Float2[] coordinates = SampleCoordinates(width, height, exactPoints: false);

        using ReadOnlyTexture2D<Float4> texture = device.AllocateReadOnlyTexture2D<Float4>(pixels, width, height);
        using ReadOnlyBuffer<Float2> uvs = device.AllocateReadOnlyBuffer<Float2>(coordinates);
        using ReadWriteBuffer<Float4> generated = device.AllocateReadWriteBuffer<Float4>(coordinates.Length);
        using ReadWriteBuffer<Float4> generatedCpu = device.AllocateReadWriteBuffer<Float4>(coordinates.Length);
        using ReadWriteBuffer<Float4> handWritten = device.AllocateReadWriteBuffer<Float4>(coordinates.Length);
        using ReadWriteBuffer<Float4> handWrittenCpu = device.AllocateReadWriteBuffer<Float4>(coordinates.Length);

        device.For(coordinates.Length, new SampleFloat4(texture, uvs, generated, sampler));
        device.For(coordinates.Length, new SampleTexels<Float4>(texture, uvs, handWritten, sampler));
        device.ForCpu(coordinates.Length, new SampleFloat4(texture, uvs, generatedCpu, sampler));
        device.ForCpu(coordinates.Length, new SampleTexels<Float4>(texture, uvs, handWrittenCpu, sampler));

        AssertBitEqual(handWritten.ToArray(), generated.ToArray(), "GPU");
        AssertBitEqual(handWrittenCpu.ToArray(), generatedCpu.ToArray(), "CPU");
    }

    [MetalFact]
    public void Sample_OnTheCpu_FollowsTheFilterAndAddressModeDefinitions()
    {
        // Worked by hand from the definitions, independently of the GPU: texels 10, 20, 30, 40 in one row.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyTexture2D<float> row = device.AllocateReadOnlyTexture2D<float>([10f, 20f, 30f, 40f], 4, 1);

        float At(Sampler sampler, float u) => row.Sample(sampler, new Float2(u, 0.5f)).X;

        // Point: floor(u * 4), then the address mode on that index.
        Assert.Equal(10f, At(Sampler.PointClamp, 0.1f));
        Assert.Equal(20f, At(Sampler.PointClamp, 0.3f));
        Assert.Equal(40f, At(Sampler.PointClamp, 0.99f));
        Assert.Equal(10f, At(Sampler.PointClamp, 0f));
        Assert.Equal(40f, At(Sampler.PointClamp, 1f));
        Assert.Equal(40f, At(Sampler.PointClamp, 1.2f));
        Assert.Equal(10f, At(Sampler.PointClamp, -0.2f));
        Assert.Equal(10f, At(Sampler.PointRepeat, 1f));       // index 4 wraps to 0
        Assert.Equal(10f, At(Sampler.PointRepeat, 1.1f));
        Assert.Equal(40f, At(Sampler.PointRepeat, -0.1f));    // index -1 wraps to 3
        Assert.Equal(30f, At(Sampler.PointRepeat, -5.4f));    // floor(-21.6) = -22 wraps to 2
        Assert.Equal(40f, At(Sampler.PointMirror, 1f));       // index 4 mirrors to 3
        Assert.Equal(30f, At(Sampler.PointMirror, 1.3f));     // index 5 mirrors to 2
        Assert.Equal(10f, At(Sampler.PointMirror, -0.1f));    // index -1 mirrors to 0
        Assert.Equal(20f, At(Sampler.PointMirror, -0.3f));    // index -2 mirrors to 1
        Assert.Equal(10f, At(Sampler.PointMirror, 2.1f));     // index 8 starts the second period

        // Linear: texel centers at (i + 0.5) / 4, weights from the fraction of u * 4 - 0.5.
        Assert.Equal(10f, At(Sampler.LinearClamp, 0.125f));
        Assert.Equal(15f, At(Sampler.LinearClamp, 0.25f));
        Assert.Equal(22.5f, At(Sampler.LinearClamp, 0.4375f));
        Assert.Equal(10f, At(Sampler.LinearClamp, 0f));       // taps -1 and 0, both texel 0
        Assert.Equal(40f, At(Sampler.LinearClamp, 1f));
        Assert.Equal(25f, At(Sampler.LinearRepeat, 0f));      // taps 3 and 0, half each
        Assert.Equal(25f, At(Sampler.LinearRepeat, 1f));
        Assert.Equal(10f, At(Sampler.LinearMirror, 0f));      // tap -1 mirrors to 0
        Assert.Equal(40f, At(Sampler.LinearMirror, 1f));      // tap 4 mirrors to 3
        Assert.Equal(35f, At(Sampler.LinearMirror, 1.25f));   // taps 4 → 3 and 5 → 2, half each

        // Two dimensions: the center of a 2×2 texture is the mean of its four texels.
        using ReadOnlyTexture2D<float> square = device.AllocateReadOnlyTexture2D<float>([1f, 2f, 3f, 4f], 2, 2);

        Assert.Equal(2.5f, square.Sample(Sampler.LinearClamp, new Float2(0.5f)).X);
        Assert.Equal(1.5f, square.Sample(Sampler.LinearClamp, new Float2(0.5f, 0.25f)).X);
        Assert.Equal(3f, square.Sample(Sampler.PointClamp, new Float2(0.25f, 0.75f)).X);

        // Channels the format does not store are (0, 0, 1), sampled or read.
        Float4 sampled = square.Sample(Sampler.LinearRepeat, new Float2(0.3f, 0.6f));
        Assert.Equal((0f, 0f, 1f), (sampled.Y, sampled.Z, sampled.W));
    }

    [MetalTheory]
    [InlineData(float.NaN, float.NaN)]
    [InlineData(float.PositiveInfinity, float.NegativeInfinity)]
    [InlineData(1e30f, -1e30f)]
    [InlineData(float.MaxValue, float.MinValue)]
    [InlineData(-0f, 16777216f)]
    public void Sample_OnTheCpu_NeverReadsOutsideTheTexture(float u, float v)
    {
        using ReadOnlyTexture2D<Rgba32> texture = GraphicsDevice.GetDefault().AllocateReadOnlyTexture2D<Rgba32>(3, 5);

        // Whatever the coordinates, the taps are mapped into the texture; nothing throws or faults.
        foreach (Sampler sampler in Samplers)
        {
            texture.Sample(sampler, new Float2(u, v));
        }
    }

    [Theory]
    [InlineData(0, 4, TextureAddressMode.Clamp, 0)]
    [InlineData(3, 4, TextureAddressMode.Clamp, 3)]
    [InlineData(4, 4, TextureAddressMode.Clamp, 3)]
    [InlineData(-1, 4, TextureAddressMode.Clamp, 0)]
    [InlineData(int.MaxValue + 1L, 4, TextureAddressMode.Clamp, 3)]
    [InlineData(int.MinValue, 4, TextureAddressMode.Clamp, 0)]
    [InlineData(4, 4, TextureAddressMode.Repeat, 0)]
    [InlineData(7, 4, TextureAddressMode.Repeat, 3)]
    [InlineData(-1, 4, TextureAddressMode.Repeat, 3)]
    [InlineData(-4, 4, TextureAddressMode.Repeat, 0)]
    [InlineData(-5, 4, TextureAddressMode.Repeat, 3)]
    [InlineData(int.MaxValue + 1L, 3, TextureAddressMode.Repeat, 2)]
    [InlineData(int.MinValue, 3, TextureAddressMode.Repeat, 1)]
    [InlineData(3, 4, TextureAddressMode.Mirror, 3)]
    [InlineData(4, 4, TextureAddressMode.Mirror, 3)]
    [InlineData(7, 4, TextureAddressMode.Mirror, 0)]
    [InlineData(8, 4, TextureAddressMode.Mirror, 0)]
    [InlineData(-1, 4, TextureAddressMode.Mirror, 0)]
    [InlineData(-4, 4, TextureAddressMode.Mirror, 3)]
    [InlineData(-5, 4, TextureAddressMode.Mirror, 3)]
    [InlineData(-8, 4, TextureAddressMode.Mirror, 0)]
    [InlineData(5, 1, TextureAddressMode.Mirror, 0)]
    [InlineData(-5, 1, TextureAddressMode.Repeat, 0)]
    public void Address_MapsEveryIndexIntoTheTexture(long index, int size, TextureAddressMode mode, int expected)
    {
        Assert.Equal(expected, Texture2D<float>.Address(index, size, mode));
    }

    // ---- passes that feed each other --------------------------------------------------------------

    [MetalFact]
    public void PingPong_BetweenTwoReadWriteTextures_ThenSampledThroughAsReadOnly_InOneContext()
    {
        const int width = 37, height = 23, iterations = 6, outputWidth = 96, outputHeight = 64;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Random random = new(11);
        float[] seed = Enumerable.Range(0, width * height).Select(_ => random.NextSingle()).ToArray();

        using ReadWriteTexture2D<float> a = device.AllocateReadWriteTexture2D<float>(seed, width, height);
        using ReadWriteTexture2D<float> b = device.AllocateReadWriteTexture2D<float>(width, height);
        using ReadWriteTexture2D<Rgba32> magnified = device.AllocateReadWriteTexture2D<Rgba32>(outputWidth, outputHeight);

        using (ComputeContext context = device.CreateComputeContext())
        {
            // Each dispatch reads what the previous one wrote, so the textures swap roles (and texture indices) every step.
            for (int i = 0; i < iterations; i++)
            {
                context.For(width, height, (i & 1) == 0 ? new DiffuseStep(a, b) : new DiffuseStep(b, a));
            }

            // `a` holds the last step. A read-write texture cannot be sampled; its read-only view can.
            using ReadOnlyTexture2D<float> view = a.AsReadOnly();
            context.For(outputWidth, outputHeight, new Magnify(view, magnified));

            Assert.Equal(iterations + 1, context.PendingCount);
        }

        // The relaxation, in plain C#.
        float[] current = [.. seed], next = new float[seed.Length];

        for (int i = 0; i < iterations; i++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float sum = current[(y * width) + Math.Max(x - 1, 0)] + current[(y * width) + Math.Min(x + 1, width - 1)] +
                        current[(Math.Max(y - 1, 0) * width) + x] + current[(Math.Min(y + 1, height - 1) * width) + x];
                    next[(y * width) + x] = sum * 0.25f;
                }
            }

            (current, next) = (next, current);
        }

        CpuExecution.CpuComparison.AssertClose(current, a.ToArray(), relative: 1e-6f, absolute: 1e-7f);

        // The same passes on the CPU, where each ForCpu has finished before the next one starts.
        using ReadWriteTexture2D<float> cpuA = device.AllocateReadWriteTexture2D<float>(seed, width, height);
        using ReadWriteTexture2D<float> cpuB = device.AllocateReadWriteTexture2D<float>(width, height);
        using ReadWriteTexture2D<Rgba32> cpuMagnified = device.AllocateReadWriteTexture2D<Rgba32>(outputWidth, outputHeight);

        for (int i = 0; i < iterations; i++)
        {
            device.ForCpu(width, height, (i & 1) == 0 ? new DiffuseStep(cpuA, cpuB) : new DiffuseStep(cpuB, cpuA));
        }

        using ReadOnlyTexture2D<float> cpuView = cpuA.AsReadOnly();
        device.ForCpu(outputWidth, outputHeight, new Magnify(cpuView, cpuMagnified));

        CpuExecution.CpuComparison.AssertClose(a.ToArray(), cpuA.ToArray(), relative: 1e-6f, absolute: 1e-7f);

        // The magnified image is not flat (so the view really was sampled after the last step), and
        // matches the CPU to within the filter's weight precision and the rounding to 8 bits.
        Rgba32[] gpuPixels = magnified.ToArray();

        Assert.True(gpuPixels.Select(static p => p.R).Distinct().Count() > 16);
        Assert.All(gpuPixels, static p => Assert.Equal((128, 255), (p.B, p.A)));
        CpuExecution.CpuComparison.AssertPixelsClose(gpuPixels, cpuMagnified.ToArray(), maxStep: 1, outlierFraction: 0, "Magnify");
    }

    [MetalFact]
    public void PixelShader_WithInputTextures_BindsThemAfterTheOutput_OnEveryDispatchPath()
    {
        const int width = 48, height = 40;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyTexture2D<Rgba32Srgb> image = device.AllocateReadOnlyTexture2D<Rgba32Srgb>(SamplePixels<Rgba32Srgb>(13 * 7), 13, 7);
        using ReadOnlyTexture2D<R8> mask = device.AllocateReadOnlyTexture2D<R8>(SamplePixels<R8>(5 * 9).Select(static p => new R8((byte)(p.R | 128))).ToArray(), 5, 9);
        Composite shader = new(image, mask, 0.9f);

        using ReadWriteTexture2D<Rgba32> direct = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        using ReadWriteTexture2D<Rgba32> batched = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        using ReadWriteTexture2D<Rgba32> asynchronous = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        using ReadWriteTexture2D<Rgba32> split = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        using ReadWriteTexture2D<Rgba32> cpu = device.AllocateReadWriteTexture2D<Rgba32>(width, height);

        device.ForEach(direct, shader);

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.ForEach(batched, shader);
        }

        device.ForEachAsync(asynchronous, shader).GetAwaiter().GetResult();
        device.ForEachSplit(split, shader, 0.5);
        device.ForEachCpu(cpu, shader);

        Rgba32[] expected = direct.ToArray();

        Assert.True(expected.Select(static p => p.R).Distinct().Count() > 16, "The image is flat: the input textures were not read.");
        Assert.Equal(expected, batched.ToArray());
        Assert.Equal(expected, asynchronous.ToArray());

        // Linear filtering on the GPU and the CPU differs by the weight precision; after scaling to 8 bits that is a step.
        CpuExecution.CpuComparison.AssertPixelsClose(expected, cpu.ToArray(), maxStep: 2, outlierFraction: 0, "Composite on the CPU");
        CpuExecution.CpuComparison.AssertPixelsClose(expected, split.ToArray(), maxStep: 2, outlierFraction: 0, "Composite, split");
    }

    [MetalTheory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void ForSplit_WithATextureReadingShader_EqualsOneGpuDispatch(double fraction)
    {
        const int width = 61, height = 70;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Rgba32[] pixels = TestPixels<Rgba32>(width * height);

        using ReadOnlyTexture2D<Rgba32> texture = device.AllocateReadOnlyTexture2D<Rgba32>(pixels, width, height);
        using ReadWriteBuffer<Float4> gpu = device.AllocateReadWriteBuffer<Float4>(pixels.Length);
        using ReadWriteBuffer<Float4> split = device.AllocateReadWriteBuffer<Float4>(pixels.Length);

        device.For(width, height, new TextureToBuffer(texture, gpu));
        SplitDispatchInfo info = device.ForSplit(width, height, new TextureToBuffer(texture, split), fraction);

        Assert.Equal(height, info.CpuExtent + info.GpuExtent);
        AssertBitEqual(Decode(pixels), gpu.ToArray(), "GPU");
        AssertBitEqual(gpu.ToArray(), split.ToArray(), "split");
    }

    // ---- a thread reads what it wrote -------------------------------------------------------------

    /// <summary>The three shapes of a write followed by a read: in the kernel, in helpers, and around a loop.</summary>
    public static TheoryData<string, int, int> WriteThenReadCases
    {
        get
        {
            TheoryData<string, int, int> data = [];

            foreach (string shader in new[] { nameof(WriteThenRead), nameof(WriteThenReadInHelpers), nameof(AccumulateInPlace) })
            {
                // A handful of threads, a single row of many, and a grid of several threadgroups.
                data.Add(shader, 8, 1);
                data.Add(shader, 4096, 1);
                data.Add(shader, 61, 70);
            }

            return data;
        }
    }

    /// <summary>
    /// A thread that writes a pixel and then reads that texture must get its own value back, as it
    /// does in C# and on the CPU. Metal only guarantees that after <c>texture.fence()</c>, which the
    /// generator emits after the write; without it the GPU returns the pixel from before the write
    /// (the textures here start at zero, and no thread ever writes zero).
    /// </summary>
    [MetalTheory]
    [MemberData(nameof(WriteThenReadCases))]
    public void WriteThenRead_AThreadReadsWhatItWrote_OnTheGpuAsOnTheCpu(string shader, int width, int height)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int count = width * height;
        R8[] wantR8 = new R8[count];
        Rgba32[] wantRgba = new Rgba32[count];
        float[] wantSingle = new float[count];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;

                if (shader == nameof(AccumulateInPlace))
                {
                    int step = 1 + (((x * 7) + (y * 13)) & 31);

                    wantR8[i] = new R8((byte)(AccumulateInPlace.Turns * step));
                    wantRgba[i] = new Rgba32(
                        (byte)(AccumulateInPlace.Turns * step), (byte)(AccumulateInPlace.Turns * (63 - step)), (byte)(AccumulateInPlace.Turns * (step ^ 21)), 255);
                    wantSingle[i] = AccumulateInPlace.Turns * step * 0.25f;
                }
                else
                {
                    int value = 1 + (((x * 7) + (y * 13)) % 255);

                    wantR8[i] = new R8((byte)value);
                    wantRgba[i] = new Rgba32((byte)value, (byte)(255 - value), (byte)(value ^ 85), 255);
                    wantSingle[i] = value * 0.5f;
                }
            }
        }

        // What a thread reads back is its pixel as a shader sees it, for each of the three textures.
        Float4[] decodedR8 = Decode(wantR8), decodedRgba = Decode(wantRgba), decodedSingle = Decode(wantSingle);
        Float4[] wantSeen = new Float4[count * 3];

        for (int i = 0; i < count; i++)
        {
            (wantSeen[i * 3], wantSeen[(i * 3) + 1], wantSeen[(i * 3) + 2]) = (decodedR8[i], decodedRgba[i], decodedSingle[i]);
        }

        Float4[] Run(bool onCpu, out R8[] storedR8, out Rgba32[] storedRgba, out float[] storedSingle)
        {
            using ReadWriteTexture2D<R8> r8 = device.AllocateReadWriteTexture2D<R8>(new R8[count], width, height);
            using ReadWriteTexture2D<Rgba32> rgba = device.AllocateReadWriteTexture2D<Rgba32>(new Rgba32[count], width, height);
            using ReadWriteTexture2D<float> single = device.AllocateReadWriteTexture2D<float>(new float[count], width, height);
            using ReadWriteBuffer<Float4> seen = device.AllocateReadWriteBuffer<Float4>(count * 3);

            switch (shader)
            {
                case nameof(WriteThenRead):
                    Dispatch(new WriteThenRead(r8, rgba, single, seen));
                    break;
                case nameof(WriteThenReadInHelpers):
                    Dispatch(new WriteThenReadInHelpers(r8, rgba, single, seen));
                    break;
                default:
                    Dispatch(new AccumulateInPlace(r8, rgba, single, seen));
                    break;
            }

            (storedR8, storedRgba, storedSingle) = (r8.ToArray(), rgba.ToArray(), single.ToArray());
            return seen.ToArray();

            void Dispatch<T>(in T instance)
                where T : struct, IComputeShader, IComputeShaderDescriptor<T>
            {
                if (onCpu)
                {
                    device.ForCpu(width, height, in instance);
                }
                else
                {
                    device.For(width, height, in instance);
                }
            }
        }

        Float4[] cpuSeen = Run(onCpu: true, out R8[] cpuR8, out Rgba32[] cpuRgba, out float[] cpuSingle);
        Float4[] gpuSeen = Run(onCpu: false, out R8[] gpuR8, out Rgba32[] gpuRgba, out float[] gpuSingle);

        // The CPU is the reference for what the C# means, and it is what was worked out above...
        AssertBitEqual(wantSeen, cpuSeen, "CPU, read back (three entries per thread: R8, Rgba32, float)");
        AssertEveryPixel(wantR8, cpuR8, width, "CPU, stored R8");
        AssertEveryPixel(wantRgba, cpuRgba, width, "CPU, stored Rgba32");
        AssertEveryPixel(wantSingle, cpuSingle, width, "CPU, stored float");

        // ...and the GPU agrees, in what each thread read back and in what it left in the textures.
        AssertBitEqual(cpuSeen, gpuSeen, "GPU, read back (three entries per thread: R8, Rgba32, float)");
        AssertEveryPixel(wantR8, gpuR8, width, "GPU, stored R8");
        AssertEveryPixel(wantRgba, gpuRgba, width, "GPU, stored Rgba32");
        AssertEveryPixel(wantSingle, gpuSingle, width, "GPU, stored float");
    }

    // ---- split dispatches that write textures -----------------------------------------------------

    public static TheoryData<Type> SplitWriteFormats => new(typeof(R8), typeof(Rgba32), typeof(float));

    /// <summary>
    /// The other direction of <see cref="ForSplit_WithATextureReadingShader_EqualsOneGpuDispatch"/>:
    /// the CPU part and the GPU part write the same texture at the same time, each its own rows.
    /// Every pixel must end up with its own value: none skipped at the cut, none written by the
    /// wrong side's coordinates, none lost to the other side's writes.
    /// </summary>
    [MetalTheory]
    [MemberData(nameof(SplitWriteFormats))]
    public void ForSplit_WithATextureWritingShader_WritesEveryPixel(Type format)
    {
        if (format == typeof(R8))
        {
            AssertSplitWritesEveryPixel(
                static target => new WritePositionR8(target),
                static (x, y) => new R8((byte)PositionValue(x, y)),
                static (x, y) => new R8((byte)~PositionValue(x, y)));
        }
        else if (format == typeof(Rgba32))
        {
            AssertSplitWritesEveryPixel(
                static target => new WritePositionRgba32(target),
                static (x, y) => new Rgba32((byte)PositionValue(x, y), (byte)(255 - PositionValue(x, y)), (byte)(PositionValue(x, y) ^ 85), 255),
                static (_, _) => new Rgba32(1, 2, 3, 0));
        }
        else
        {
            AssertSplitWritesEveryPixel(
                static target => new WritePositionSingle(target),
                static (x, y) => (float)((x * 7) + (y * 13)),
                static (_, _) => -1f);
        }

        static int PositionValue(int x, int y) => ((x * 7) + (y * 13)) & 255;
    }

    /// <summary>
    /// Runs <paramref name="create"/>'s shader split over textures of three sizes (rows that are and are
    /// not a multiple of the threadgroup height, and one small enough for a share to round to a whole
    /// side) at three CPU shares, a few times each because the two sides race, and compares every pixel
    /// with <paramref name="expected"/>. Each run starts from <paramref name="stale"/>, a value no pixel
    /// should keep, so a pixel that neither side wrote cannot pass.
    /// </summary>
    private static void AssertSplitWritesEveryPixel<TPixel, TShader>(
        Func<ReadWriteTexture2D<TPixel>, TShader> create, Func<int, int, TPixel> expected, Func<int, int, TPixel> stale)
        where TPixel : unmanaged
        where TShader : struct, IComputeShader, IComputeShaderDescriptor<TShader>
    {
        const int runs = 3;
        GraphicsDevice device = GraphicsDevice.GetDefault();

        foreach ((int width, int height) in new[] { (250, 130), (1021, 517), (64, 64) })
        {
            TPixel[] want = new TPixel[width * height], before = new TPixel[width * height], actual = new TPixel[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    want[(y * width) + x] = expected(x, y);
                    before[(y * width) + x] = stale(x, y);
                    Assert.False(SamePixel(in want[(y * width) + x], in before[(y * width) + x]), "The stale value must differ from the expected one at every pixel.");
                }
            }

            using ReadWriteTexture2D<TPixel> texture = device.AllocateReadWriteTexture2D<TPixel>(width, height);
            TShader shader = create(texture);

            // The expectation itself, against the GPU alone.
            texture.CopyFrom(before);
            device.For(width, height, in shader);
            texture.CopyTo(actual);
            AssertEveryPixel(want, actual, width, $"{typeof(TPixel).Name} {width}×{height}, one GPU dispatch");

            foreach (double fraction in new[] { 0.1, 0.5, 0.9 })
            {
                for (int run = 0; run < runs; run++)
                {
                    texture.CopyFrom(before);

                    SplitDispatchInfo info = device.ForSplit(width, height, in shader, fraction);

                    texture.CopyTo(actual);
                    AssertEveryPixel(want, actual, width, $"{typeof(TPixel).Name} {width}×{height}, cpuFraction {fraction}, run {run} ({info.CpuExtent} rows on the CPU)");

                    // Cut along the rows; and an even share of any of these grids gives both sides some.
                    Assert.Equal(1, info.Axis);
                    Assert.Equal(height, info.CpuExtent + info.GpuExtent);

                    if (fraction == 0.5)
                    {
                        Assert.InRange(info.CpuExtent, 1, height - 1);
                    }
                }
            }
        }
    }

    /// <summary>Every pixel equal, reporting how many are not and where the first one is (a wrong cut shows as whole rows).</summary>
    private static void AssertEveryPixel<TPixel>(TPixel[] expected, TPixel[] actual, int width, string what)
        where TPixel : unmanaged
    {
        Assert.Equal(expected.Length, actual.Length);

        // The pixel types are plain bytes or floats without padding, so equal pixels are equal bytes.
        if (MemoryMarshal.AsBytes<TPixel>(expected).SequenceEqual(MemoryMarshal.AsBytes<TPixel>(actual)))
        {
            return;
        }

        int wrong = 0, first = -1;

        for (int i = 0; i < expected.Length; i++)
        {
            if (!SamePixel(in expected[i], in actual[i]))
            {
                wrong++;
                first = first < 0 ? i : first;
            }
        }

        Assert.Fail($"{what}: {wrong} of {expected.Length} pixels are wrong, the first at ({first % width}, {first / width}): expected {expected[first]}, got {actual[first]}.");
    }

    private static bool SamePixel<TPixel>(in TPixel a, in TPixel b)
        where TPixel : unmanaged =>
        MemoryMarshal.AsBytes(new ReadOnlySpan<TPixel>(in a)).SequenceEqual(MemoryMarshal.AsBytes(new ReadOnlySpan<TPixel>(in b)));

    // ---- lifetime ---------------------------------------------------------------------------------

    [MetalFact]
    public void TextureDisposedWhileABatchThatBindsItIsPending_StaysAliveUntilTheGpuIsDone()
    {
        const int width = 19, height = 11;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Rgba32[] pixels = TestPixels<Rgba32>(width * height);

        using ReadWriteBuffer<Float4> output = device.AllocateReadWriteBuffer<Float4>(pixels.Length);
        ReadOnlyTexture2D<Rgba32> source = device.AllocateReadOnlyTexture2D<Rgba32>(pixels, width, height);
        nint texture = ObjC.Retain(source.Handle.DangerousGetHandle());
        nint storage = ObjC.Retain(source.BufferHandle.DangerousGetHandle());

        try
        {
            using (ComputeContext context = device.CreateComputeContext())
            {
                context.For(width, height, new TextureToBuffer(source, output));

                // The batch leases the texture and its storage: Dispose must not drop a reference yet.
                nuint textureCount = ComputeContextTests.RetainCount(texture);
                nuint storageCount = ComputeContextTests.RetainCount(storage);

                source.Dispose();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                Assert.Equal(textureCount, ComputeContextTests.RetainCount(texture));
                Assert.Equal(storageCount, ComputeContextTests.RetainCount(storage));
                Assert.Throws<ObjectDisposedException>(() => source.ToArray());
                Assert.Throws<ObjectDisposedException>(() => context.For(width, height, new TextureToBuffer(source, output)));
                Assert.Equal(1, context.PendingCount);
            }

            AssertBitEqual(Decode(pixels), output.ToArray(), "GPU");

            // Once the batch completed its leases were dropped, so the deferred releases ran. The texture
            // goes first; the buffer it is a view of loses the texture's reference with it.
            ComputeContextTests.WaitForRetainCount(texture, 1);
            ObjC.Release(texture);
            texture = 0;
            ComputeContextTests.WaitForRetainCount(storage, 1);
        }
        finally
        {
            ObjC.Release(texture);
            ObjC.Release(storage);
        }
    }

    [MetalFact]
    public void TextureDisposedWhileAnAsyncDispatchIsInFlight_StillCompletes()
    {
        const int width = 64, height = 64;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Rgba32[] pixels = TestPixels<Rgba32>(width * height);

        using ReadWriteBuffer<Float4> output = device.AllocateReadWriteBuffer<Float4>(pixels.Length);
        ReadOnlyTexture2D<Rgba32> source = device.AllocateReadOnlyTexture2D<Rgba32>(pixels, width, height);

        Task task = device.ForAsync(width, height, new TextureToBuffer(source, output));
        source.Dispose();
        task.GetAwaiter().GetResult();

        AssertBitEqual(Decode(pixels), output.ToArray(), "GPU");
    }

    [MetalFact]
    public void DisposingAView_ReleasesNothing_AndLeavesTheSourceUsable()
    {
        const int width = 9, height = 5;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        float[] values = Enumerable.Range(0, width * height).Select(static i => i * 0.5f).ToArray();

        using ReadWriteTexture2D<float> source = device.AllocateReadWriteTexture2D<float>(values, width, height);
        using ReadWriteBuffer<Float4> output = device.AllocateReadWriteBuffer<Float4>(values.Length);
        nint texture = source.Handle.DangerousGetHandle();
        nuint retained = ComputeContextTests.RetainCount(texture);

        ReadOnlyTexture2D<float> view = source.AsReadOnly();

        // The same MTLTexture and storage, not a copy.
        Assert.Same(source.Handle, view.Handle);
        Assert.Same(source.BufferHandle, view.BufferHandle);
        Assert.Equal((width, height), (view.Width, view.Height));
        Assert.Same(device, view.Device);
        Assert.Equal(retained, ComputeContextTests.RetainCount(texture));
        Assert.Equal(values, view.ToArray());
        Assert.Equal(4f, view[8, 0].X);

        source[8, 0] = new Float4(99f);
        Assert.Equal(99f, view[8, 0].X);
        source[8, 0] = new Float4(4f);

        view.Dispose();
        view.Dispose();

        // The view is gone...
        Assert.Throws<ObjectDisposedException>(() => view.ToArray());
        Assert.Throws<ObjectDisposedException>(() => view.CopyFrom(values));
        Assert.Throws<ObjectDisposedException>(() => view[0, 0]);
        Assert.Throws<ObjectDisposedException>(() => view.Sample(Sampler.PointClamp, default));
        Assert.Throws<ObjectDisposedException>(() => device.For(width, height, new ReadTexels<float>(view, output)));
        Assert.Throws<ObjectDisposedException>(() => device.ForCpu(width, height, new ReadTexels<float>(view, output)));

        // ...and the source is untouched: same reference count, same pixels, still bindable, and it can make another view.
        Assert.Equal(retained, ComputeContextTests.RetainCount(texture));
        Assert.Equal(values, source.ToArray());

        using ReadOnlyTexture2D<float> second = source.AsReadOnly();
        device.For(width, height, new ReadTexels<float>(second, output));

        Assert.Equal(values, output.ToArray().Select(static p => p.X));

        device.For(width, height, new LoadTexels<float>(source, output));

        Assert.Equal(values, output.ToArray().Select(static p => p.X));
    }

    [MetalFact]
    public void DisposingTheSource_DisposesItsViews_ButNotWorkThatAlreadyBoundThem()
    {
        const int width = 9, height = 5;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        float[] values = Enumerable.Range(0, width * height).Select(static i => i + 0.25f).ToArray();

        ReadWriteTexture2D<float> source = device.AllocateReadWriteTexture2D<float>(values, width, height);
        using ReadWriteBuffer<Float4> output = device.AllocateReadWriteBuffer<Float4>(values.Length);
        ReadOnlyTexture2D<float> view = source.AsReadOnly();

        using (ComputeContext context = device.CreateComputeContext())
        {
            // Binding the view leases the source's texture, so the pending dispatch survives the Dispose.
            context.For(width, height, new ReadTexels<float>(view, output));
            source.Dispose();

            Assert.Throws<ObjectDisposedException>(() => context.For(width, height, new ReadTexels<float>(view, output)));
            Assert.Equal(1, context.PendingCount);
        }

        Assert.Equal(values, output.ToArray().Select(static p => p.X));

        // The view counts as disposed, without having been disposed itself.
        Assert.True(view.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => view.ToArray());
        Assert.Throws<ObjectDisposedException>(() => view.CopyTo(new float[values.Length]));
        Assert.Throws<ObjectDisposedException>(() => view[0, 0]);
        Assert.Throws<ObjectDisposedException>(() => view[new Int2(1, 1)]);
        Assert.Throws<ObjectDisposedException>(() => view.Sample(Sampler.LinearClamp, new Float2(0.5f)));
        Assert.Throws<ObjectDisposedException>(() => device.For(width, height, new ReadTexels<float>(view, output)));
        Assert.Throws<ObjectDisposedException>(() => device.ForCpu(width, height, new ReadTexels<float>(view, output)));
        Assert.Throws<ObjectDisposedException>(() => source.AsReadOnly());

        // Disposing it now is still a no-op.
        view.Dispose();
    }

    [MetalFact]
    public void TextureFromAnotherDevice_IsRejected_OnEveryDispatchPath()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using GraphicsDevice other = GraphicsDevice.CreateStandInForAnotherDevice();
        using ReadOnlyTexture2D<Rgba32> foreign = other.AllocateReadOnlyTexture2D<Rgba32>(4, 4);
        using ReadWriteTexture2D<float> foreignTarget = other.AllocateReadWriteTexture2D<float>(4, 4);
        using ReadOnlyTexture2D<float> foreignView = foreignTarget.AsReadOnly();
        using ReadWriteTexture2D<float> target = device.AllocateReadWriteTexture2D<float>(4, 4);
        using ReadWriteBuffer<Float4> output = device.AllocateReadWriteBuffer<Float4>(16);
        TextureToBuffer shader = new(foreign, output);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => device.For(4, 4, shader));

        Assert.Equal("texture", exception.ParamName);
        Assert.StartsWith("The texture bound at index 0 was allocated from a different GraphicsDevice", exception.Message);

        Assert.Throws<ArgumentException>(() => device.ForCpu(4, 4, shader));
        Assert.Throws<ArgumentException>(() => device.ForSplit(4, 4, shader, 0.5));
        ForAsyncTests.ThrowsSynchronously<ArgumentException>(() => device.ForAsync(4, 4, shader));

        using (ComputeContext context = device.CreateComputeContext())
        {
            Assert.Throws<ArgumentException>(() => context.For(4, 4, shader));
            Assert.Equal(0, context.PendingCount);
        }

        // The second texture of a shader is checked too, as is a view of a foreign texture.
        exception = Assert.Throws<ArgumentException>(() => device.For(4, 4, new DiffuseStep(target, foreignTarget)));
        Assert.StartsWith("The texture bound at index 1 ", exception.Message);
        Assert.Throws<ArgumentException>(() => device.For(4, 4, new ReadTexels<float>(foreignView, output)));

        // Nothing was written, and the device still dispatches.
        Assert.All(output.ToArray(), static p => Assert.Equal(0f, p.X));
        device.For(4, 4, new DiffuseStep(target, target.Device.AllocateReadWriteTexture2D<float>(4, 4)));
    }

    // ---- binding ----------------------------------------------------------------------------------

    [MetalTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(30)]
    public void SetTexture_IndicesInRange_AreAccepted(int index)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyTexture2D<float> texture = device.AllocateReadOnlyTexture2D<float>(2, 2);

        device.For(4, new BindTextureAtIndex(texture, index));
        device.ForCpu(4, new BindTextureAtIndex(texture, index));

        using ComputeContext context = device.CreateComputeContext();
        context.For(4, new BindTextureAtIndex(texture, index));
    }

    [MetalTheory]
    [InlineData(-1)]
    [InlineData(31)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void SetTexture_IndicesOutsideTheArgumentTable_Throw(int index)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyTexture2D<float> texture = device.AllocateReadOnlyTexture2D<float>(2, 2);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => device.For(4, new BindTextureAtIndex(texture, index)));

        Assert.StartsWith("Texture binding indices must be in the range 0..30.", exception.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => device.ForCpu(4, new BindTextureAtIndex(texture, index)));

        using (ComputeContext context = device.CreateComputeContext())
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => context.For(4, new BindTextureAtIndex(texture, index)));
        }

        // Still usable afterwards: the encoder of the failed dispatch was ended properly.
        device.For(4, new BindTextureAtIndex(texture, 0));
    }

    [MetalFact]
    public void SetTexture_NullOrDisposedTexture_Throws()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadOnlyTexture2D<float> disposed = device.AllocateReadOnlyTexture2D<float>(2, 2);
        disposed.Dispose();

        Assert.Throws<ArgumentNullException>(() => device.For(4, new BindTextureAtIndex(null!, 0)));
        Assert.Throws<ArgumentNullException>(() => device.ForCpu(4, new BindTextureAtIndex(null!, 0)));
        Assert.Throws<ObjectDisposedException>(() => device.For(4, new BindTextureAtIndex(disposed, 0)));
        Assert.Throws<ObjectDisposedException>(() => device.ForCpu(4, new BindTextureAtIndex(disposed, 0)));

        // A generated shader names the field instead.
        using ReadWriteBuffer<Float4> output = device.AllocateReadWriteBuffer<Float4>(4);
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => device.For(2, 2, new TextureToBuffer(null!, output)));

        Assert.Equal("TextureToBuffer.source", exception.ParamName);
    }

    [Fact]
    public void SetTexture_OnADefaultWriter_IsRejectedInsteadOfMessagingNil()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            ShaderArgumentWriter writer = default;
            writer.SetTexture<float>(0, null!);
        });
    }

    // ---- allocation and CPU access ----------------------------------------------------------------

    public static TheoryData<Type, int, int> FormatsAndOddSizes
    {
        get
        {
            TheoryData<Type, int, int> data = [];

            foreach (Type format in AllFormats)
            {
                foreach ((int width, int height) in new[] { (1, 1), (2, 3), (3, 5), (5, 1), (7, 2), (13, 3), (17, 9), (31, 2), (33, 1) })
                {
                    data.Add(format, width, height);
                }
            }

            return data;
        }
    }

    [MetalTheory]
    [MemberData(nameof(FormatsAndOddSizes))]
    public void LinearTexture_AtOddWidths_PadsItsRows_AndTheGpuSeesTheSameLayout(Type format, int width, int height) =>
        RunGeneric(nameof(OddWidthCase), format, width, height);

    private static void OddWidthCase<T>(int width, int height)
        where T : unmanaged
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        TextureFormat info = TextureFormats.Get<T>()!.Value;
        nuint alignment = device.GetMinimumLinearTextureAlignment(info.PixelFormat);
        T[] pixels = TestPixels<T>(width * height);

        using ReadOnlyTexture2D<T> texture = device.AllocateReadOnlyTexture2D<T>(pixels, width, height);
        using ReadWriteBuffer<Float4> output = device.AllocateReadWriteBuffer<Float4>(pixels.Length);

        Assert.Equal((nuint)0, texture.BytesPerRow % alignment);
        Assert.True(texture.BytesPerRow >= (nuint)(width * sizeof(T)));
        Assert.True(texture.BytesPerRow < (nuint)(width * sizeof(T)) + alignment);
        Assert.True(pixels.AsSpan().SequenceEqual(texture.ToArray()));

        // The GPU reads every pixel where the CPU put it, so both agree on the stride.
        device.For(width, height, new ReadTexels<T>(texture, output));
        AssertDecoded<T>(Decode(pixels), output.ToArray());

        if (!info.SupportsReadWrite)
        {
            Assert.Throws<NotSupportedException>(() => device.AllocateReadWriteTexture2D<T>(width, height));
            return;
        }

        // And it writes them where the CPU reads them.
        Float4[] colors = Decode(pixels);
        using ReadOnlyBuffer<Float4> source = device.AllocateReadOnlyBuffer<Float4>(colors);
        using ReadWriteTexture2D<T> target = device.AllocateReadWriteTexture2D<T>(width, height);

        Assert.Equal(texture.BytesPerRow, target.BytesPerRow);

        device.For(width, height, new StoreTexels<T>(target, source));

        // Decoding a pixel and storing it again is the identity for every read-write format.
        AssertSameBytes(pixels, target.ToArray(), colors, "GPU");
    }

    [MetalFact]
    public void TextureFormats_MapEveryElementTypeToItsMetalPixelFormat()
    {
        // MTLPixelFormat values from Metal's headers.
        Assert.Equal(new TextureFormat(70, "RGBA8Unorm", 4, PixelEncoding.Unorm8, true), TextureFormats.Get<Rgba32>());
        Assert.Equal(new TextureFormat(71, "RGBA8Unorm_sRGB", 4, PixelEncoding.Srgb8, false), TextureFormats.Get<Rgba32Srgb>());
        Assert.Equal(new TextureFormat(10, "R8Unorm", 1, PixelEncoding.Unorm8, true), TextureFormats.Get<R8>());
        Assert.Equal(new TextureFormat(30, "RG8Unorm", 2, PixelEncoding.Unorm8, false), TextureFormats.Get<Rg16>());
        Assert.Equal(new TextureFormat(25, "R16Float", 1, PixelEncoding.Float16, true), TextureFormats.Get<Half>());
        Assert.Equal(new TextureFormat(115, "RGBA16Float", 4, PixelEncoding.Float16, true), TextureFormats.Get<Rgba16Float>());
        Assert.Equal(new TextureFormat(55, "R32Float", 1, PixelEncoding.Float32, true), TextureFormats.Get<float>());
        Assert.Equal(new TextureFormat(125, "RGBA32Float", 4, PixelEncoding.Float32, true), TextureFormats.Get<Float4>());

        Assert.Null(TextureFormats.Get<int>());
        Assert.Null(TextureFormats.Get<Float3>());

        // A pixel is as large as its format says: channels × 1, 2 or 4 bytes.
        Assert.Equal((4, 4, 1, 2, 2, 8, 4, 16), (sizeof(Rgba32), sizeof(Rgba32Srgb), sizeof(R8), sizeof(Rg16), sizeof(Half), sizeof(Rgba16Float), sizeof(float), sizeof(Float4)));
    }

    [MetalFact]
    public void UnsupportedElementTypes_AreRejected_WithTheSupportedOnes()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => device.AllocateReadOnlyTexture2D<int>(4, 4));

        Assert.Equal("'Int32' is not a texture element type; use Rgba32, Rgba32Srgb, R8, Rg16, Half, Rgba16Float, float or Float4.", exception.Message);
        Assert.Throws<NotSupportedException>(() => device.AllocateReadOnlyTexture2D<Float2>(4, 4));
        Assert.Throws<NotSupportedException>(() => device.AllocateReadOnlyTexture2D<Float3>([default], 1, 1));
        Assert.Throws<NotSupportedException>(() => device.AllocateReadWriteTexture2D<uint>(4, 4));
        Assert.Throws<NotSupportedException>(() => device.AllocateReadWriteTexture2D<Int4>([default], 1, 1));

        // Formats the GPU cannot read and write in one shader are read-only.
        exception = Assert.Throws<NotSupportedException>(() => device.AllocateReadWriteTexture2D<Rg16>(4, 4));

        Assert.Equal(
            "ReadWriteTexture2D<Rg16> is not supported: Apple GPUs cannot read and write RG8Unorm in the same shader. " +
            "Use ReadOnlyTexture2D<Rg16>, or one of Rgba32, R8, Half, Rgba16Float, float or Float4 for a read-write texture.",
            exception.Message);
        Assert.Throws<NotSupportedException>(() => device.AllocateReadWriteTexture2D<Rgba32Srgb>(4, 4));

        // The element type is checked before the size.
        Assert.Throws<NotSupportedException>(() => device.AllocateReadOnlyTexture2D<int>(0, 0));
    }

    [MetalTheory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(16385, 1)]
    [InlineData(1, 16385)]
    public void ReadOnlyTexture_InvalidSize_Throws(int width, int height)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        Assert.Throws<ArgumentOutOfRangeException>(() => device.AllocateReadOnlyTexture2D<R8>(width, height));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.AllocateReadOnlyTexture2D<R8>(default, width, height));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.AllocateReadWriteTexture2D<Half>(default, width, height));
    }

    [MetalFact]
    public void AllocateFromPixels_CopiesThem_AndRejectsTooMany()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Rg16[] pixels = TestPixels<Rg16>(15);

        using ReadOnlyTexture2D<Rg16> readOnly = device.AllocateReadOnlyTexture2D<Rg16>(pixels, 5, 3);
        using ReadWriteTexture2D<Half> readWrite = device.AllocateReadWriteTexture2D<Half>([(Half)1f, (Half)2f, (Half)3f], 3, 1);

        Assert.Same(device, readOnly.Device);
        Assert.Equal((5, 3), (readOnly.Width, readOnly.Height));
        Assert.Equal(pixels, readOnly.ToArray());
        Assert.Equal([(Half)1f, (Half)2f, (Half)3f], readWrite.ToArray());

        // Fewer pixels than the texture holds: the rest stays zero, as with CopyFrom.
        using ReadOnlyTexture2D<Rg16> partial = device.AllocateReadOnlyTexture2D<Rg16>(pixels.AsSpan(0, 7), 5, 3);

        Assert.Equal(pixels.Take(7).Concat(new Rg16[8]), partial.ToArray());

        using ReadOnlyTexture2D<Rg16> empty = device.AllocateReadOnlyTexture2D<Rg16>(5, 3);

        Assert.All(empty.ToArray(), static p => Assert.Equal(default, p));

        Assert.Throws<ArgumentException>(() => device.AllocateReadOnlyTexture2D<Rg16>(pixels, 7, 2));
        Assert.Throws<ArgumentException>(() => device.AllocateReadWriteTexture2D<Half>(new Half[5], 2, 2));
    }

    [MetalFact]
    public void Indexers_OnTheCpu_DecodeAndEncodeLikeTheGpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyTexture2D<Rg16> twoChannels = device.AllocateReadOnlyTexture2D<Rg16>([new Rg16(51, 102), new Rg16(255, 0), new Rg16(1, 2)], 3, 1);

        // Missing channels read as (0, 0, 1).
        AssertColor(new Float4(0.2f, 0.4f, 0f, 1f), twoChannels[0, 0]);
        AssertColor(new Float4(1f, 0f, 0f, 1f), twoChannels[new Int2(1, 0)]);

        using ReadWriteTexture2D<R8> gray = device.AllocateReadWriteTexture2D<R8>(3, 2);

        gray[2, 1] = new Float4(0.5f, 0.9f, 0.9f, 0.9f);   // only red is stored
        gray[new Int2(0, 0)] = new Float4(7f);             // clamped to 1
        gray[1, 0] = new Float4(float.NaN);                // NaN stores 0

        Assert.Equal([new R8(255), new R8(0), new R8(0), new R8(0), new R8(0), new R8(128)], gray.ToArray());
        AssertColor(new Float4(128 / 255f, 0f, 0f, 1f), gray[2, 1]);
        AssertColor(new Float4(1f, 0f, 0f, 1f), gray[new Int2(0, 0)]);

        using ReadWriteTexture2D<Rgba16Float> wide = device.AllocateReadWriteTexture2D<Rgba16Float>(2, 2);

        wide[1, 1] = new Float4(-2.5f, 1e10f, 0.1f, 1f);

        // Halves: exact where representable, otherwise the next half towards zero (0.1 is 0x2E66,
        // just below 0.1), and the largest half for a finite value beyond it.
        Assert.Equal(new Rgba16Float((Half)(-2.5f), Half.MaxValue, BitConverter.UInt16BitsToHalf(0x2E66), (Half)1f), wide.ToArray()[3]);
        AssertColor(new Float4(-2.5f, 65504f, (float)BitConverter.UInt16BitsToHalf(0x2E66), 1f), wide[1, 1]);
    }

    [MetalTheory]
    [InlineData(-1, 0, "x")]
    [InlineData(4, 0, "x")]
    [InlineData(int.MaxValue, 0, "x")]
    [InlineData(int.MinValue, 2, "x")]
    [InlineData(0, -1, "y")]
    [InlineData(0, 3, "y")]
    [InlineData(3, int.MinValue, "y")]
    public void Indexers_OnTheCpu_AreBoundsChecked(int x, int y, string parameter)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyTexture2D<Rgba32> readOnly = device.AllocateReadOnlyTexture2D<Rgba32>(4, 3);
        using ReadWriteTexture2D<Rgba32> readWrite = device.AllocateReadWriteTexture2D<Rgba32>(4, 3);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => readOnly[x, y]);

        Assert.Equal(parameter, exception.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => readOnly[new Int2(x, y)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => readWrite[x, y]);
        Assert.Throws<ArgumentOutOfRangeException>(() => readWrite[new Int2(x, y)]);
        Assert.Throws<ArgumentOutOfRangeException>(() => readWrite[x, y] = default);
        Assert.Throws<ArgumentOutOfRangeException>(() => readWrite[new Int2(x, y)] = default);

        // The corners are in range.
        _ = readOnly[0, 0];
        _ = readOnly[3, 2];
        readWrite[3, 2] = new Float4(1f);
    }

    [MetalFact]
    public void DisposedTexture_EveryShaderMemberThrowsObjectDisposedException()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadOnlyTexture2D<Rgba32> readOnly = device.AllocateReadOnlyTexture2D<Rgba32>(4, 3);
        ReadWriteTexture2D<Rgba32> readWrite = device.AllocateReadWriteTexture2D<Rgba32>(4, 3);

        readOnly.Dispose();
        readWrite.Dispose();
        readWrite.Dispose();

        Assert.Throws<ObjectDisposedException>(() => readOnly[0, 0]);
        Assert.Throws<ObjectDisposedException>(() => readOnly[new Int2(0, 0)]);
        Assert.Throws<ObjectDisposedException>(() => readOnly[-1, 99]);
        Assert.Throws<ObjectDisposedException>(() => readOnly.Sample(Sampler.PointClamp, default));
        Assert.Throws<ObjectDisposedException>(() => readOnly.ToArray());
        Assert.Throws<ObjectDisposedException>(() => readWrite[0, 0]);
        Assert.Throws<ObjectDisposedException>(() => readWrite[0, 0] = default);
        Assert.Throws<ObjectDisposedException>(() => readWrite[new Int2(0, 0)] = default);
        Assert.Throws<ObjectDisposedException>(() => readWrite.AsReadOnly());

        // The size stays readable, as a buffer's Length does.
        Assert.Equal((4, 3), (readOnly.Width, readOnly.Height));
    }

    [Fact]
    public void Samplers_AreTheSixCombinationsOfFilterAndAddressMode()
    {
        Assert.Equal(
            [
                (TextureFilter.Point, TextureAddressMode.Clamp),
                (TextureFilter.Point, TextureAddressMode.Repeat),
                (TextureFilter.Point, TextureAddressMode.Mirror),
                (TextureFilter.Linear, TextureAddressMode.Clamp),
                (TextureFilter.Linear, TextureAddressMode.Repeat),
                (TextureFilter.Linear, TextureAddressMode.Mirror),
            ],
            Samplers.Select(static s => (s.Filter, s.AddressMode)));

        Assert.Equal("Sampler(Linear, Mirror)", Sampler.LinearMirror.ToString());
        Assert.Equal((TextureFilter.Point, TextureAddressMode.Clamp), (default(Sampler).Filter, default(Sampler).AddressMode));
    }

    [Fact]
    public void PixelTypes_FormatThemselves()
    {
        Assert.Equal("R8(7)", new R8(7).ToString());
        Assert.Equal("Rg16(7, 8)", new Rg16(7, 8).ToString());
        Assert.Equal("Rgba32Srgb(1, 2, 3, 4)", new Rgba32Srgb(1, 2, 3, 4).ToString());
        Assert.Equal("Rgba16Float(1, 0.5, -2, 0)", new Rgba16Float((Half)1f, (Half)0.5f, (Half)(-2f), (Half)0f).ToString());
    }

    // ---- helpers ----------------------------------------------------------------------------------

    /// <summary>Calls the generic test case <paramref name="method"/> for the pixel type <paramref name="format"/>.</summary>
    private static void RunGeneric(string method, Type format, params object[] arguments)
    {
        MethodInfo info = typeof(TextureShaderTests).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(format);

        try
        {
            info.Invoke(null, arguments);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
        }
    }

    private static bool IsEightBit<T>() =>
        typeof(T) == typeof(Rgba32) || typeof(T) == typeof(Rgba32Srgb) || typeof(T) == typeof(R8) || typeof(T) == typeof(Rg16);

    private static bool IsHalf<T>() => typeof(T) == typeof(Half) || typeof(T) == typeof(Rgba16Float);

    /// <summary>
    /// Pixels that cover the format: every byte value in every channel of the 8-bit formats; zeros, ones,
    /// the extremes, subnormals and infinities of the float formats, then random values.
    /// </summary>
    private static T[] TestPixels<T>(int count)
        where T : unmanaged
    {
        T[] pixels = new T[count];
        Span<byte> bytes = MemoryMarshal.AsBytes(pixels.AsSpan());
        Random random = new(count);

        if (IsEightBit<T>())
        {
            int channels = sizeof(T);

            for (int i = 0; i < bytes.Length; i++)
            {
                int pixel = i / channels, channel = i % channels;

                // An odd multiplier per channel: each channel runs through all 256 values, in a different order.
                bytes[i] = (byte)((pixel * ((2 * channel) + 1)) + (channel * 37));
            }
        }
        else if (IsHalf<T>())
        {
            Span<Half> halves = MemoryMarshal.Cast<byte, Half>(bytes);
            Half[] special = [(Half)0f, (Half)1f, (Half)(-1f), Half.MaxValue, Half.MinValue, Half.Epsilon, -Half.Epsilon, Half.PositiveInfinity, Half.NegativeInfinity, (Half)0.5f, (Half)(1f / 3f), Half.NegativeZero];

            for (int i = 0; i < halves.Length; i++)
            {
                halves[i] = i < special.Length ? special[i] : (Half)((random.NextSingle() * 16f) - 8f);
            }
        }
        else
        {
            Span<float> floats = MemoryMarshal.Cast<byte, float>(bytes);
            float[] special = [0f, 1f, -1f, float.MaxValue, float.MinValue, float.Epsilon, -float.Epsilon, float.PositiveInfinity, float.NegativeInfinity, 0.5f, 1f / 3f, -0f, 1.17549435e-38f, 16777217f];

            for (int i = 0; i < floats.Length; i++)
            {
                floats[i] = i < special.Length ? special[i] : (random.NextSingle() * 2000f) - 1000f;
            }
        }

        return pixels;
    }

    /// <summary>Pixels for the sampling tests: random bytes for the 8-bit formats, values in [0.5, 2] for the float ones.</summary>
    private static T[] SamplePixels<T>(int count)
        where T : unmanaged
    {
        T[] pixels = new T[count];
        Span<byte> bytes = MemoryMarshal.AsBytes(pixels.AsSpan());
        Random random = new(count + 1);

        if (IsEightBit<T>())
        {
            random.NextBytes(bytes);
        }
        else if (IsHalf<T>())
        {
            foreach (ref Half value in MemoryMarshal.Cast<byte, Half>(bytes))
            {
                value = (Half)(0.5f + (random.NextSingle() * 1.5f));
            }
        }
        else
        {
            foreach (ref float value in MemoryMarshal.Cast<byte, float>(bytes))
            {
                value = 0.5f + (random.NextSingle() * 1.5f);
            }
        }

        return pixels;
    }

    /// <summary>Colors to write: every rounding boundary of a normalized channel, values outside [0, 1], extremes, then random values.</summary>
    private static Float4[] TestColors<T>(int count)
        where T : unmanaged
    {
        List<float> values = [0f, -0f, 1f, -1f, 2f, 0.5f, 1f / 3f, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, float.MinValue, 65504f, 65519.99f, 65520f, 1e-5f, 6.1035156e-5f, 5.9604645e-8f, 2.9802322e-8f, 1e-10f, float.Epsilon, 0.99999994f, 1.0000001f];

        if (IsEightBit<T>())
        {
            // NaN stores 0 in a normalized channel; in a float channel its payload is unspecified, so it is left out there.
            values.Add(float.NaN);

            for (int k = 0; k <= 255; k++)
            {
                foreach (float center in new[] { k / 255f, (k + 0.5f) / 255f })
                {
                    values.Add(MathF.BitDecrement(center));
                    values.Add(center);
                    values.Add(MathF.BitIncrement(center));
                }
            }
        }

        if (IsHalf<T>())
        {
            // Halfway between two neighbouring halves, where round-to-nearest-even has to pick the even one.
            for (int k = 0; k < 64; k++)
            {
                float lower = (float)BitConverter.UInt16BitsToHalf((ushort)(0x3C00 + k));
                float upper = (float)BitConverter.UInt16BitsToHalf((ushort)(0x3C01 + k));
                float middle = (lower + upper) / 2f;

                values.Add(MathF.BitDecrement(middle));
                values.Add(middle);
                values.Add(MathF.BitIncrement(middle));
            }
        }

        Random random = new(count + 2);
        float scale = IsEightBit<T>() ? 1.2f : 200f;

        while (values.Count < count * 4)
        {
            values.Add((random.NextSingle() * scale) - (scale / 12f));
        }

        return Enumerable.Range(0, count).Select(i => new Float4(values[4 * i], values[(4 * i) + 1], values[(4 * i) + 2], values[(4 * i) + 3])).ToArray();
    }

    /// <summary>
    /// Coordinates inside, on the edges of and outside [0, 1] whose bilinear weights are multiples of
    /// 1/256, which is the precision the GPU interpolates with (other coordinates are covered by
    /// <see cref="LinearSample_AtArbitraryCoordinates_DiffersByAtMostTheGpusWeightPrecision"/>). Coordinates exactly on a boundary
    /// between two texels are only used when <paramref name="exactPoints"/> says the products are exact.
    /// </summary>
    private static Float2[] SampleCoordinates(int width, int height, bool exactPoints)
    {
        List<float> us = Axis(width), vs = Axis(height);
        List<Float2> coordinates = [];

        for (int i = 0; i < us.Count; i++)
        {
            // Pair each u with a v from elsewhere in the list, so that both axes interpolate and wrap.
            coordinates.Add(new Float2(us[i], vs[(i * 7) % vs.Count]));
        }

        return [.. coordinates];

        List<float> Axis(int size)
        {
            List<float> axis = [0f, 1f, -0f, -1f, 2f, 0.5f];

            for (int texel = -size - 2; texel <= (2 * size) + 2; texel++)
            {
                // Texel centers, and offsets of k/256 of a texel from them.
                foreach (int k in new[] { 0, 37, 64, 128, 192, 255 })
                {
                    if (k == 128 && !exactPoints)
                    {
                        continue;
                    }

                    axis.Add((texel + 0.5f + (k / 256f)) / size);
                }
            }

            return axis;
        }
    }

    /// <summary>Samples at <paramref name="coordinates"/> with the hand-written generic shader, on the GPU and on the CPU.</summary>
    private static Float4[] SampleOnGpu<T>(GraphicsDevice device, T[] pixels, int width, int height, Float2[] coordinates, int sampler, out Float4[] cpu)
        where T : unmanaged
    {
        using ReadOnlyTexture2D<T> texture = device.AllocateReadOnlyTexture2D<T>(pixels, width, height);
        using ReadOnlyBuffer<Float2> uvs = device.AllocateReadOnlyBuffer<Float2>(coordinates);
        using ReadWriteBuffer<Float4> gpuOutput = device.AllocateReadWriteBuffer<Float4>(coordinates.Length);
        using ReadWriteBuffer<Float4> cpuOutput = device.AllocateReadWriteBuffer<Float4>(coordinates.Length);

        device.For(coordinates.Length, new SampleTexels<T>(texture, uvs, gpuOutput, sampler));
        device.ForCpu(coordinates.Length, new SampleTexels<T>(texture, uvs, cpuOutput, sampler));

        cpu = cpuOutput.ToArray();
        return gpuOutput.ToArray();
    }

    private static Float4[] Decode<T>(T[] pixels)
        where T : unmanaged
    {
        Float4[] decoded = new Float4[pixels.Length];

        fixed (T* source = pixels)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                decoded[i] = TextureFormats.Load(source + i);
            }
        }

        return decoded;
    }

    /// <summary>What the GPU read against <see cref="TextureFormats.Load"/>: bit-exact, except for the sRGB decode of the color channels.</summary>
    private static void AssertDecoded<T>(Float4[] expected, Float4[] gpu)
        where T : unmanaged
    {
        if (typeof(T) != typeof(Rgba32Srgb))
        {
            AssertBitEqual(expected, gpu, "GPU");
            return;
        }

        Assert.Equal(expected.Length, gpu.Length);

        for (int i = 0; i < expected.Length; i++)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                float e = Channel(expected[i], channel), g = Channel(gpu[i], channel);
                Assert.True(MathF.Abs(e - g) <= SrgbReadTolerance, $"Pixel {i}, channel {channel}: expected {e:R}, GPU {g:R}.");
            }

            Assert.Equal(expected[i].W, gpu[i].W);
        }
    }

    private static void AssertBitEqual(Float4[] expected, Float4[] actual, string side)
    {
        Assert.Equal(expected.Length, actual.Length);

        ReadOnlySpan<uint> e = MemoryMarshal.Cast<Float4, uint>(expected), a = MemoryMarshal.Cast<Float4, uint>(actual);

        for (int i = 0; i < e.Length; i++)
        {
            Assert.True(
                e[i] == a[i],
                $"{side}: pixel {i / 4}, channel {i % 4}: expected {BitConverter.UInt32BitsToSingle(e[i]):R} (0x{e[i]:X8}), got {BitConverter.UInt32BitsToSingle(a[i]):R} (0x{a[i]:X8}).");
        }
    }

    private static void AssertSameBytes<T>(T[] expected, T[] actual, Float4[] colors, string side)
        where T : unmanaged
    {
        Assert.Equal(expected.Length, actual.Length);

        int size = sizeof(T);
        ReadOnlySpan<byte> e = MemoryMarshal.AsBytes(expected.AsSpan()), a = MemoryMarshal.AsBytes(actual.AsSpan());
        List<string> mismatches = [];

        for (int i = 0; i < expected.Length; i++)
        {
            if (!e.Slice(i * size, size).SequenceEqual(a.Slice(i * size, size)))
            {
                mismatches.Add(
                    $"pixel {i}: color ({colors[i].X:R}, {colors[i].Y:R}, {colors[i].Z:R}, {colors[i].W:R}): " +
                    $"expected {Convert.ToHexString(e.Slice(i * size, size))}, got {Convert.ToHexString(a.Slice(i * size, size))}");
            }
        }

        Assert.True(mismatches.Count == 0, $"{side}, {typeof(T).Name}: {mismatches.Count} of {expected.Length} pixels differ:\n" + string.Join("\n", mismatches.Take(40)));
    }

    private static void AssertColor(Float4 expected, Float4 actual) =>
        Assert.Equal((expected.X, expected.Y, expected.Z, expected.W), (actual.X, actual.Y, actual.Z, actual.W));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Channel(Float4 value, int channel) => channel switch
    {
        0 => value.X,
        1 => value.Y,
        2 => value.Z,
        _ => value.W,
    };
}
