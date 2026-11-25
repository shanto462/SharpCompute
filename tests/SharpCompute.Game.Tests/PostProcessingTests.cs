using System;
using System.Numerics;

namespace SharpCompute.Game.Tests;

/// <summary>The post-processing passes on the GPU: bloom, the HDR composite, shockwaves, vignette and the upscale.</summary>
public sealed class PostProcessingTests
{
    private const int Width = 64;
    private const int Height = 48;

    private static float Luma(Rgba32 c) => ((0.2126f * c.R) + (0.7152f * c.G) + (0.0722f * c.B)) / 255f;

    /// <summary>A black frame with a white 4 × 4 block in the middle.</summary>
    private static FrameBuffer FrameWithBlock(GraphicsDevice device, byte value = 255)
    {
        FrameBuffer frame = new(device, Width, Height);
        frame.Clear(new Rgba32(0, 0, 0, 255));
        Span<uint> pixels = frame.AsSpan();

        for (int y = 22; y < 26; y++)
        {
            for (int x = 30; x < 34; x++)
            {
                pixels[(y * Width) + x] = FrameBuffer.Pack(new Rgba32(value, value, value, 255));
            }
        }

        return frame;
    }

    private static ReadWriteBuffer<Float4> Hdr(GraphicsDevice device, Func<int, int, Float4> pixel)
    {
        ReadWriteBuffer<Float4> hdr = device.AllocateReadWriteBuffer<Float4>(Width * Height);
        Span<Float4> span = hdr.AsSpan();

        for (int i = 0; i < span.Length; i++)
        {
            span[i] = pixel(i % Width, i / Width);
        }

        return hdr;
    }

    [MetalFact]
    public void Bloom_BrightensAroundBrightPixels_AndLeavesDarkAreasAlone()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using FrameBuffer frame = FrameWithBlock(device);
        using Bloom bloom = new(device, Width, Height);

        using (ComputeContext context = device.CreateComputeContext())
        {
            bloom.Record(context, frame);
            Assert.Equal(7, context.PendingCount);
        }

        // The block itself stays white; the black just outside it now glows, less so further out.
        Assert.Equal(new Rgba32(255, 255, 255, 255), frame[31, 23]);
        float near = Luma(frame[28, 23]), further = Luma(frame[24, 23]), far = Luma(frame[14, 23]);
        Assert.True(near > 0.15f, $"Two pixels from the block glows (luma {near}).");
        Assert.True(further < near && further > 0.01f, $"The glow fades with distance ({near} → {further}).");
        Assert.True(far < further);

        // Symmetric around the block, and the far corners are untouched.
        Assert.Equal(frame[28, 23], frame[35, 24]);
        Assert.Equal(frame[31, 19], frame[32, 28]);
        Assert.Equal(new Rgba32(0, 0, 0, 255), frame[1, 1]);
        Assert.Equal(new Rgba32(0, 0, 0, 255), frame[Width - 2, Height - 2]);
    }

    [MetalFact]
    public void Bloom_StrengthScalesTheGlow_AndZeroLeavesTheFrameExactlyAsItWas()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using Bloom bloom = new(device, Width, Height);
        float[] glow = new float[3];
        float[] strengths = [0f, 0.5f, 1f];

        for (int i = 0; i < strengths.Length; i++)
        {
            using FrameBuffer frame = FrameWithBlock(device);
            uint[] before = frame.Packed.ToArray();

            using (ComputeContext context = device.CreateComputeContext())
            {
                bloom.Record(context, frame, strengths[i]);
            }

            glow[i] = Luma(frame[28, 23]);

            if (strengths[i] == 0f)
            {
                Assert.Equal(before, frame.Packed.ToArray());
            }
        }

        Assert.Equal(0f, glow[0]);
        Assert.True(glow[1] > 0.05f && glow[2] > glow[1] * 1.7f, $"Glow at strengths 0.5 and 1: {glow[1]}, {glow[2]}.");
    }

    [MetalFact]
    public void Bloom_IgnoresWhatIsBelowTheThreshold()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using FrameBuffer dim = FrameWithBlock(device, value: 70);     // 0.27, under the default 0.3
        using Bloom bloom = new(device, Width, Height);
        uint[] before = dim.Packed.ToArray();

        using (ComputeContext context = device.CreateComputeContext())
        {
            bloom.Record(context, dim);
        }

        Assert.Equal(before, dim.Packed.ToArray());

        // Lower the threshold and the same block glows.
        bloom.Threshold = 0.1f;

        using (ComputeContext context = device.CreateComputeContext())
        {
            bloom.Record(context, dim);
        }

        Assert.True(dim[28, 23].R > 0);
    }

    [MetalFact]
    public void Bloom_OfAnHdrImage_FillsATightAndAWideLevel()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float4> hdr = Hdr(device, static (x, y) => x is >= 30 and < 34 && y is >= 22 and < 26 ? new Float4(4f, 2f, 1f, 1f) : new Float4(0.1f, 0.1f, 0.1f, 1f));
        using Bloom bloom = new(device, Width, Height);

        using (ComputeContext context = device.CreateComputeContext())
        {
            bloom.Record(context, hdr);
            Assert.Equal(6, context.PendingCount);
        }

        Assert.Equal(32 * 24, bloom.Half.Length);
        Assert.Equal(16 * 12, bloom.Quarter.Length);
        ReadOnlySpan<Float4> half = bloom.Half.AsReadOnlySpan(), quarter = bloom.Quarter.AsReadOnlySpan();

        // Brightest at the block (half-resolution 15..16, 11..12), keeping its hue (red > green > blue).
        Float4 center = half[(11 * 32) + 15];
        Assert.True(center.X > 0.3f && center.X > center.Y && center.Y > center.Z, $"Center of the half level: {center}.");
        Assert.True(half[(11 * 32) + 12].X < center.X && half[(11 * 32) + 12].X > 0f);

        // The background (0.1) is under the threshold: far from the block both levels are black.
        Assert.Equal(0f, half[(2 * 32) + 2].X);
        Assert.Equal(0f, quarter[(1 * 16) + 1].X);

        // The quarter level reaches further: four quarter texels out is 16 pixels, where the half level has faded more.
        float wide = quarter[(5 * 16) + 4].X, tight = half[(11 * 32) + 8].X;
        Assert.True(wide > tight, $"16 pixels left of the block: quarter {wide}, half {tight}.");

        foreach (Float4 texel in half)
        {
            Assert.True(texel.X >= 0f && texel.Y >= 0f && texel.Z >= 0f);
        }
    }

    [MetalFact]
    public void Bloom_ValidatesSizes()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using Bloom bloom = new(device, Width, Height);
        using FrameBuffer wrong = new(device, Width, Height - 1);
        using ReadWriteBuffer<Float4> small = device.AllocateReadWriteBuffer<Float4>(10);
        using ComputeContext context = device.CreateComputeContext();

        Assert.Throws<ArgumentException>(() => bloom.Record(context, wrong));
        Assert.Throws<ArgumentException>(() => bloom.Record(context, small));
        Assert.Throws<ArgumentNullException>(() => bloom.Record(null!, wrong));
        Assert.Throws<ArgumentNullException>(() => bloom.Record(context, (FrameBuffer)null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bloom(device, 3, 48));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bloom(device, 64, 0));
        Assert.Equal(0, context.PendingCount);
    }

    [MetalFact]
    public void Bloom_WorksOnSizesThatDoNotDivideByFour()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using FrameBuffer frame = new(device, 37, 23);
        frame.Clear(new Rgba32(0, 0, 0, 255));
        frame.AsSpan()[(22 * 37) + 36] = 0xFFFFFFFFu;         // the very last pixel, which no half-level texel covers
        frame.AsSpan()[(10 * 37) + 18] = 0xFFFFFFFFu;
        using Bloom bloom = new(device, 37, 23);

        using (ComputeContext context = device.CreateComputeContext())
        {
            bloom.Record(context, frame, 2f);
        }

        Assert.Equal(18 * 11, bloom.Half.Length);
        Assert.Equal(9 * 5, bloom.Quarter.Length);
        Assert.True(Luma(frame[17, 10]) > 0f);
        Assert.Equal(new Rgba32(255, 255, 255, 255), frame[36, 22]);
    }

    [MetalFact]
    public void Composite_ToneMapsAUniformImage_ToTheSameValueEverywhere()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float4> hdr = Hdr(device, static (_, _) => new Float4(0.5f, 1f, 4f, 1f));
        using Bloom bloom = new(device, Width, Height);
        using ShockwaveDistortion shockwaves = new(device);
        using FrameBuffer output = new(device, Width, Height);
        HdrComposite composite = new() { BloomStrength = 0f, Exposure = 1.35f };

        using (ComputeContext context = device.CreateComputeContext())
        {
            bloom.Record(context, hdr);
            composite.Record(context, hdr, bloom, shockwaves, output);
        }

        // 1 − exp(−c × exposure), as 8 bits; alpha opaque.
        Rgba32 expected = new((byte)MathF.Round((1f - MathF.Exp(-0.5f * 1.35f)) * 255f), (byte)MathF.Round((1f - MathF.Exp(-1.35f)) * 255f), (byte)MathF.Round((1f - MathF.Exp(-4f * 1.35f)) * 255f), 255);

        foreach (Rgba32 pixel in output.Pixels)
        {
            Assert.InRange(pixel.R, expected.R - 1, expected.R + 1);
            Assert.InRange(pixel.G, expected.G - 1, expected.G + 1);
            Assert.InRange(pixel.B, expected.B - 1, expected.B + 1);
            Assert.Equal(255, pixel.A);
        }

        Assert.Equal(output[0, 0], output[Width - 1, Height - 1]);
    }

    [MetalFact]
    public void Composite_AddsBloomFlashAndVignette()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float4> hdr = Hdr(device, static (x, y) => x is >= 30 and < 34 && y is >= 22 and < 26 ? new Float4(5f, 5f, 5f, 1f) : new Float4(0.2f, 0.2f, 0.2f, 1f));
        using Bloom bloom = new(device, Width, Height);
        using ShockwaveDistortion shockwaves = new(device);
        using FrameBuffer output = new(device, Width, Height);

        Rgba32[] Render(HdrComposite composite)
        {
            using ComputeContext context = device.CreateComputeContext();
            bloom.Record(context, hdr);
            composite.Record(context, hdr, bloom, shockwaves, output);
            context.Execute();
            return output.Pixels.ToArray();
        }

        static Rgba32 At(Rgba32[] image, int x, int y) => image[(y * Width) + x];

        Rgba32[] plain = Render(new HdrComposite { BloomStrength = 0f });
        Rgba32[] bloomed = Render(new HdrComposite { BloomStrength = 1f });
        Rgba32[] flashed = Render(new HdrComposite { BloomStrength = 0f, Flash = new Vector3(0.5f, 0f, 0f) });
        Rgba32[] vignetted = Render(new HdrComposite { BloomStrength = 0f, Vignette = 1f });

        // Bloom: a halo next to the bright block, nothing in the far corner.
        Assert.True(Luma(At(bloomed, 27, 23)) > Luma(At(plain, 27, 23)) + 0.1f);
        Assert.Equal(At(plain, 2, 2), At(bloomed, 2, 2));

        // Flash: more red everywhere, the other channels untouched.
        Assert.True(At(flashed, 5, 5).R > At(plain, 5, 5).R + 40);
        Assert.Equal(At(plain, 5, 5).G, At(flashed, 5, 5).G);

        // Vignette: the middle keeps its brightness, the corner loses about half.
        Assert.Equal(At(plain, 32, 30), At(vignetted, 32, 30) with { R = At(plain, 32, 30).R });
        Assert.InRange(At(vignetted, 32, 24).G, At(plain, 32, 24).G - 1, At(plain, 32, 24).G);
        Assert.InRange(At(vignetted, 0, 0).G / (float)At(plain, 0, 0).G, 0.45f, 0.55f);
    }

    [MetalFact]
    public void Composite_Offset_ShiftsTheImage_ByWholePixelsExactly()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float4> hdr = Hdr(device, static (x, y) => new Float4(x / 64f, y / 48f, ((x * 7) + (y * 3)) % 10 / 10f, 1f));
        using Bloom bloom = new(device, Width, Height);
        using ShockwaveDistortion shockwaves = new(device);
        using FrameBuffer still = new(device, Width, Height), shifted = new(device, Width, Height);
        HdrComposite composite = new() { BloomStrength = 0f };

        using (ComputeContext context = device.CreateComputeContext())
        {
            bloom.Record(context, hdr);
            composite.Record(context, hdr, bloom, shockwaves, still);
            composite.Offset = new Vector2(3f, -2f);
            composite.Record(context, hdr, bloom, shockwaves, shifted);
        }

        // Each output pixel looks up the scene 3 right and 2 up; the edges clamp.
        Assert.Equal(still[23, 18], shifted[20, 20]);
        Assert.Equal(still[40, 5], shifted[37, 7]);
        Assert.Equal(still[Width - 1, 0], shifted[Width - 1, 1]);
    }

    [MetalFact]
    public void Composite_DistortsOnlyNearARing()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        // Vertical stripes four pixels wide, so any horizontal displacement shows.
        using ReadWriteBuffer<Float4> hdr = Hdr(device, static (x, _) => x / 4 % 2 == 0 ? new Float4(1f, 1f, 1f, 1f) : new Float4(0.1f, 0.1f, 0.1f, 1f));
        using Bloom bloom = new(device, Width, Height);
        using ShockwaveDistortion shockwaves = new(device);
        using FrameBuffer calm = new(device, Width, Height), rippled = new(device, Width, Height);
        HdrComposite composite = new() { BloomStrength = 0f };

        using (ComputeContext context = device.CreateComputeContext())
        {
            bloom.Record(context, hdr);
            composite.Record(context, hdr, bloom, shockwaves, calm);
            Assert.True(shockwaves.Add(new Vector2(32f, 24f), radius: 10f, width: 3f, strength: 4f));
            composite.Record(context, hdr, bloom, shockwaves, rippled);
        }

        int changedNear = 0, changedFar = 0;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32f, 24f));
                bool changed = !calm[x, y].Equals(rippled[x, y]);

                if (MathF.Abs(d - 10f) < 5f)
                {
                    changedNear += changed ? 1 : 0;
                }
                else if (MathF.Abs(d - 10f) > 12f)
                {
                    changedFar += changed ? 1 : 0;
                }
            }
        }

        Assert.True(changedNear > 50, $"Pixels near the ring move ({changedNear} changed).");
        Assert.Equal(0, changedFar);

        // The color split: where the stripes shift, red and blue no longer agree.
        bool fringe = false;

        foreach (Rgba32 pixel in rippled.Pixels)
        {
            fringe |= Math.Abs(pixel.R - pixel.B) > 20;
        }

        Assert.True(fringe);
    }

    [MetalFact]
    public void Composite_ValidatesSizes()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<Float4> hdr = device.AllocateReadWriteBuffer<Float4>(Width * Height);
        using Bloom bloom = new(device, Width, Height);
        using Bloom otherBloom = new(device, Width, Height + 4);
        using ShockwaveDistortion shockwaves = new(device);
        using FrameBuffer output = new(device, Width, Height), smaller = new(device, Width, Height - 4);
        using ComputeContext context = device.CreateComputeContext();
        HdrComposite composite = new();

        Assert.Throws<ArgumentException>(() => composite.Record(context, hdr, bloom, shockwaves, smaller));
        Assert.Throws<ArgumentException>(() => composite.Record(context, hdr, otherBloom, shockwaves, output));
        Assert.Throws<ArgumentNullException>(() => composite.Record(context, hdr, bloom, null!, output));
        Assert.Throws<ArgumentNullException>(() => composite.Record(context, null!, bloom, shockwaves, output));
        Assert.Equal(0, context.PendingCount);
    }

    [MetalFact]
    public void Shockwave_DistortsAFrame_NearTheRing_AndCopiesTheRestExactly()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using FrameBuffer source = new(device, Width, Height), destination = new(device, Width, Height);
        Span<uint> pixels = source.AsSpan();

        for (int i = 0; i < pixels.Length; i++)
        {
            int x = i % Width, y = i / Width;
            pixels[i] = FrameBuffer.Pack(new Rgba32((byte)(x / 4 % 2 * 255), (byte)(y / 4 % 2 * 255), (byte)(x * 3), (byte)(100 + y)));
        }

        using ShockwaveDistortion shockwaves = new(device, capacity: 2);
        Assert.Equal(2, shockwaves.Capacity);
        Assert.True(shockwaves.Add(new Vector2(20f, 24f), 8f, 3f, 5f));
        Assert.True(shockwaves.Add(new Vector2(48f, 24f), 6f, 2f, 3f));
        Assert.False(shockwaves.Add(new Vector2(0f, 0f), 6f, 2f, 3f));      // full: dropped
        Assert.Equal(2, shockwaves.Count);

        using (ComputeContext context = device.CreateComputeContext())
        {
            shockwaves.Record(context, source, destination);
        }

        int changed = 0;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                Vector2 p = new(x + 0.5f, y + 0.5f);
                bool differs = !source[x, y].Equals(destination[x, y]);
                changed += differs ? 1 : 0;
                Assert.Equal(source[x, y].A, destination[x, y].A);      // alpha is carried over, never resampled

                if (MathF.Abs(Vector2.Distance(p, new Vector2(20f, 24f)) - 8f) > 12f && MathF.Abs(Vector2.Distance(p, new Vector2(48f, 24f)) - 6f) > 9f)
                {
                    Assert.False(differs, $"({x}, {y}) is out of both rings' reach but changed.");
                }
            }
        }

        Assert.True(changed > 100, $"Only {changed} pixels moved.");

        // No rings: an exact copy.
        shockwaves.Clear();
        Assert.Equal(0, shockwaves.Count);

        using (ComputeContext context = device.CreateComputeContext())
        {
            shockwaves.Record(context, source, destination);
        }

        Assert.Equal(source.Packed.ToArray(), destination.Packed.ToArray());
    }

    [MetalFact]
    public void Shockwave_OnAUniformFrame_ChangesNothing()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using FrameBuffer source = new(device, Width, Height), destination = new(device, Width, Height);
        source.Clear(new Rgba32(37, 141, 203, 255));
        using ShockwaveDistortion shockwaves = new(device);
        shockwaves.Add(new Vector2(32f, 24f), 12f, 4f, 9f);

        using (ComputeContext context = device.CreateComputeContext())
        {
            shockwaves.Record(context, source, destination);
        }

        Assert.Equal(source.Packed.ToArray(), destination.Packed.ToArray());
    }

    [MetalFact]
    public void Shockwave_ValidatesItsArguments()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using FrameBuffer a = new(device, Width, Height), b = new(device, Width - 1, Height);
        using ShockwaveDistortion shockwaves = new(device);
        using ComputeContext context = device.CreateComputeContext();

        Assert.Throws<ArgumentException>(() => shockwaves.Record(context, a, a));
        Assert.Throws<ArgumentException>(() => shockwaves.Record(context, a, b));
        Assert.Throws<ArgumentOutOfRangeException>(() => shockwaves.Add(Vector2.Zero, 5f, 0f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => shockwaves.Add(Vector2.Zero, 5f, float.NaN, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShockwaveDistortion(device, 0));
        Assert.Equal(0, shockwaves.Count);
        Assert.Equal(0, context.PendingCount);
    }

    [MetalFact]
    public void Vignette_DarkensTheCorners_NotTheCenter()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using FrameBuffer frame = new(device, Width, Height);
        frame.Clear(new Rgba32(200, 100, 40, 77));

        using (ComputeContext context = device.CreateComputeContext())
        {
            Vignette.Record(context, frame, strength: 1f);
        }

        // The exact center keeps everything; the corner (u = v = −0.5) keeps half; the middle of the top edge three quarters.
        Assert.Equal(new Rgba32(200, 100, 40, 77), frame[32, 24]);
        Assert.Equal(new Rgba32(100, 50, 20, 77), frame[0, 0]);
        Assert.Equal(new Rgba32(150, 75, 30, 77), frame[32, 0]);
        Assert.True(frame[Width - 1, Height - 1].R is > 100 and < 112);     // just short of the far corner
        Assert.True(frame[16, 12].R < frame[24, 18].R);

        // Strength 0 is the identity; a huge strength clamps to black instead of wrapping.
        frame.Clear(new Rgba32(200, 100, 40, 77));

        using (ComputeContext context = device.CreateComputeContext())
        {
            Vignette.Record(context, frame, strength: 0f);
        }

        Assert.All(frame.Pixels.ToArray(), static p => Assert.Equal(new Rgba32(200, 100, 40, 77), p));

        using (ComputeContext context = device.CreateComputeContext())
        {
            Vignette.Record(context, frame, strength: 100f);
        }

        Assert.Equal(new Rgba32(0, 0, 0, 77), frame[0, 0]);
        Assert.Equal(new Rgba32(200, 100, 40, 77), frame[32, 24]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Vignette.Record(device.CreateComputeContext(), frame, -0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Vignette.Record(device.CreateComputeContext(), frame, float.NaN));
    }

    [MetalTheory]
    [InlineData(40, 27, 4, 4, 1)]          // 8 × 6 at scale 4 is 32 × 24: borders on both axes
    [InlineData(8, 6, 1, 0, 0)]            // the same size: a plain copy
    [InlineData(24, 18, 3, 0, 0)]          // an exact multiple: no borders
    [InlineData(100, 13, 2, 42, 0)]        // limited by the height
    [InlineData(6, 4, 1, -1, -1)]          // smaller than the frame: cropped, centered
    public void Upscale_IsExact_WholePixelBlocks_WithBlackBorders(int targetWidth, int targetHeight, int scale, int originX, int originY)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using FrameBuffer frame = new(device, 8, 6);
        Span<uint> pixels = frame.AsSpan();
        Random random = new(5);

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (uint)random.Next() << 1;       // random colors, and alphas that must not matter
        }

        using ReadWriteTexture2D<Rgba32> target = device.AllocateReadWriteTexture2D<Rgba32>(targetWidth, targetHeight);
        Assert.Equal(new UpscaleLayout(scale, originX, originY), PixelUpscale.Layout(8, 6, targetWidth, targetHeight));

        using (ComputeContext context = device.CreateComputeContext())
        {
            PixelUpscale.Record(context, frame, target);
        }

        Rgba32[] result = target.ToArray();

        for (int y = 0; y < targetHeight; y++)
        {
            for (int x = 0; x < targetWidth; x++)
            {
                int sx = (int)Math.Floor((x - originX) / (double)scale), sy = (int)Math.Floor((y - originY) / (double)scale);
                Rgba32 expected = sx is >= 0 and < 8 && sy is >= 0 and < 6 ? frame[sx, sy] with { A = 255 } : new Rgba32(0, 0, 0, 255);
                Assert.Equal(expected, result[(y * targetWidth) + x]);
            }
        }
    }

    [Theory]
    [InlineData(640, 360, 1, 0, 0)]
    [InlineData(2560, 1440, 4, 0, 0)]
    [InlineData(1300, 740, 2, 10, 10)]
    [InlineData(2000, 1200, 3, 40, 60)]
    [InlineData(320, 200, 1, -160, -80)]
    public void Layout_UsesTheLargestWholeScale_Centered(int width, int height, int scale, int originX, int originY)
    {
        UpscaleLayout layout = PixelUpscale.Layout(640, 360, width, height);
        Assert.Equal(new UpscaleLayout(scale, originX, originY), layout);

        // Pixel blocks map back exactly, and the middle of the target is the middle of the frame.
        Assert.Equal(new Vector2(10f, 20f), layout.ToFrame(originX + (10 * scale), originY + (20 * scale)));
        Assert.Equal(new Vector2(10.5f, 20.5f), layout.ToFrame(originX + (10.5 * scale), originY + (20.5 * scale)));
        Assert.Equal(320f, layout.ToFrame(width / 2.0, height / 2.0).X, 0.5f + (1f / scale));

        Assert.Throws<ArgumentOutOfRangeException>(() => PixelUpscale.Layout(0, 360, width, height));
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelUpscale.Layout(640, 360, width, 0));
    }
}
