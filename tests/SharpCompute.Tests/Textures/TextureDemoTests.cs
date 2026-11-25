using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using SharpCompute.Presentation;
using SharpCompute.Samples.Textures;
using SharpCompute.Tests.CpuExecution;
using Program = SharpCompute.Samples.Textures.Program;

namespace SharpCompute.Tests.Textures;

/// <summary>
/// The textures demo without its window: the sampler explorer's frames for every format, what each
/// panel shows inside and outside the texture, the ping-pong simulation, zooming around the pointer
/// with the wheel, and the headless mode.
/// </summary>
public sealed class TextureDemoTests
{
    // A frame whose six panels are whole pixels (400 × 330 each), so every panel maps its pixels to
    // exactly the same texture coordinates and the panels can be compared pixel by pixel.
    private const int Width = 1200;
    private const int Height = 720;

    private static readonly Sampler[] Samplers =
        [Sampler.PointClamp, Sampler.PointRepeat, Sampler.PointMirror, Sampler.LinearClamp, Sampler.LinearRepeat, Sampler.LinearMirror];

    // ---- the pieces -------------------------------------------------------------------------------

    [Fact]
    public void Font_CoversTheLabelCharacters()
    {
        Assert.Equal(0u, PixelFont.Masks[PixelFont.Index(' ')]);

        foreach (char c in "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ:-/.!+(),=<>_[]")
        {
            Assert.NotEqual(0, PixelFont.Index(c));
            Assert.NotEqual(0u, PixelFont.Masks[PixelFont.Index(c)]);
            Assert.True(PixelFont.Masks[PixelFont.Index(c)] < 1u << 15);
        }

        Assert.Equal(PixelFont.Index('X'), PixelFont.Index('x'));
        Assert.Equal(0, PixelFont.Index('§'));
        Assert.Equal(PixelFont.Masks.Length, PixelFont.Masks.Distinct().Count());
        Assert.Equal((0, 6, 14), (PixelFont.Measure("", 2), PixelFont.Measure("A", 2), PixelFont.Measure("AB", 2)));

        // Everything the format labels say can be drawn.
        foreach (SourceFormat format in SourceImage.Formats)
        {
            Assert.All(format.Type + format.MetalFormat + format.Reads, static c => Assert.True(c == ' ' || PixelFont.Index(c) != 0, $"No glyph for '{c}'."));
        }
    }

    [MetalFact]
    public void TextLayer_DrawsScaledGlyphsOnPlates_AndUploadsThem()
    {
        using TextLayer layer = new(GraphicsDevice.GetDefault(), 40, 24);

        // "T" is ###|.#.|.#.|.#.|.#. ; at scale 2 its top bar is 6 × 2 pixels at (10, 6).
        layer.Label("T", 10, 6, scale: 2, padding: 3);

        Assert.Equal(TextLayer.Ink, layer[10, 6]);
        Assert.Equal(TextLayer.Ink, layer[15, 7]);
        Assert.Equal(TextLayer.Plate, layer[10, 8]);   // below the bar, left of the stem
        Assert.Equal(TextLayer.Ink, layer[12, 15]);    // the stem's last row
        Assert.Equal(TextLayer.Plate, layer[7, 3]);    // the plate's corner
        Assert.Equal(TextLayer.Plate, layer[18, 18]);
        Assert.Equal(0, layer[6, 3]);
        Assert.Equal(0, layer[19, 19]);

        // Drawing is clipped, not an error.
        layer.Text("W", 38, 22, scale: 3);
        layer.Fill(-5, -5, 7, 7, TextLayer.Plate);
        Assert.Equal(TextLayer.Plate, layer[1, 1]);

        Assert.All(layer.Texture.ToArray(), static p => Assert.Equal(0, p.R));
        layer.Upload();

        R8[] uploaded = layer.Texture.ToArray();

        Assert.Equal(TextLayer.Ink, uploaded[(6 * 40) + 10].R);
        Assert.Equal(TextLayer.Plate, uploaded[(8 * 40) + 10].R);

        layer.Clear();
        Assert.Equal(0, layer[10, 6]);
    }

    [Fact]
    public void SourceImage_HasFourDifferentEdges_AndLooksDifferentMirrored()
    {
        const int size = SourceImage.Size;
        Rgba32[] tile = SourceImage.Create();

        Assert.Equal(size * size, tile.Length);
        Assert.Equal(tile, SourceImage.Create());
        Assert.All(tile, static p => Assert.Equal(255, p.A));

        // One color along each edge, all four different: that is what clamping smears outwards.
        Rgba32 top = tile[5], bottom = tile[((size - 1) * size) + 5], left = tile[5 * size], right = tile[(5 * size) + size - 1];

        Assert.Equal(4, new[] { top, bottom, left, right }.Distinct().Count());
        Assert.All(Enumerable.Range(0, size), i => Assert.Equal(top, tile[i]));
        Assert.All(Enumerable.Range(1, size - 2), i => Assert.Equal(left, tile[i * size]));

        // Mirrored either way it is a different picture, so Mirror and Repeat can be told apart.
        Rgba32[] flippedX = Enumerable.Range(0, tile.Length).Select(i => tile[(i / size * size) + (size - 1 - (i % size))]).ToArray();
        Rgba32[] flippedY = Enumerable.Range(0, tile.Length).Select(i => tile[((size - 1 - (i / size)) * size) + (i % size)]).ToArray();

        Assert.True(tile.Zip(flippedX).Count(static p => !p.First.Equals(p.Second)) > 100);
        Assert.True(tile.Zip(flippedY).Count(static p => !p.First.Equals(p.Second)) > 100);
        Assert.True(tile.Distinct().Count() > 100, "The tile should be colorful.");
        Assert.Equal(8, SourceImage.Formats.Length);
    }

    [Theory]
    [InlineData(1280, 720, 3, 2)]
    [InlineData(2560, 1440, 6, 5)]
    [InlineData(640, 360, 1, 1)]
    [InlineData(320, 200, 1, 1)]
    public void Layout_ScalesTheTextAndSplitsTheRestIntoSixPanels(int width, int height, int labelScale, int textScale)
    {
        ExplorerLayout layout = new(width, height);

        Assert.Equal((labelScale, textScale), (layout.LabelScale, layout.TextScale));
        Assert.Equal(height, layout.TopBar + (2 * layout.Cell.Y) + layout.BottomBar, 0.01f);
        Assert.Equal(width, 3 * layout.Cell.X, 0.01f);
        Assert.Equal(layout.Origin.X, layout.PanelOrigin(0).X);
        Assert.Equal(layout.Origin.Y + layout.Cell.Y, layout.PanelOrigin(5).Y, 0.01f);
        Assert.Equal(2 * layout.Cell.X, layout.PanelOrigin(5).X, 0.01f);
    }

    [Fact]
    public void PanelUv_CentersTheTexture_AndFollowsZoomPanAndRotation()
    {
        Float2 cell = new(400f, 300f);

        // The middle of a panel is the middle of the texture; the panel's height spans `zoom` textures.
        AssertUv(0.5f, 0.5f, SamplerGrid.PanelUv(new Float2(0.5f, 0.5f), cell, 3f, default, 0f));
        AssertUv(0.5f, -1f, SamplerGrid.PanelUv(new Float2(0.5f, 0f), cell, 3f, default, 0f));
        AssertUv(0.5f, 2f, SamplerGrid.PanelUv(new Float2(0.5f, 1f), cell, 3f, default, 0f));

        // Square texels: a panel 4/3 as wide as it is high spans 4/3 as much horizontally.
        AssertUv(2.5f, 0.5f, SamplerGrid.PanelUv(new Float2(1f, 0.5f), cell, 3f, default, 0f));

        AssertUv(0.75f, 0.25f, SamplerGrid.PanelUv(new Float2(0.5f, 0.5f), cell, 3f, new Float2(0.25f, -0.25f), 0f));

        // A quarter turn: the panel's right edge looks down the texture's v axis.
        AssertUv(0.5f, 2.5f, SamplerGrid.PanelUv(new Float2(1f, 0.5f), cell, 3f, default, MathF.PI / 2f));

        static void AssertUv(float u, float v, Float2 actual)
        {
            Assert.Equal(u, actual.X, 1e-5f);
            Assert.Equal(v, actual.Y, 1e-5f);
        }
    }

    // ---- sampler explorer -------------------------------------------------------------------------

    [MetalTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Samplers_EveryFormat_RendersAFrameThatIsNotBlank_AndReadsWhatTheLabelSays(int format)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using TextureDemo demo = new(device);
        using ReadWriteTexture2D<Rgba32> target = device.AllocateReadWriteTexture2D<Rgba32>(Width, Height);

        demo.Explorer.Format = format;
        demo.Render(target);

        Rgba32[] frame = target.ToArray();
        TextLayer labels = demo.Labels!;

        Assert.True(frame.Distinct().Count() > 40, $"Only {frame.Distinct().Count()} colors: the frame is (nearly) blank.");
        Assert.All(frame, static p => Assert.Equal(255, p.A));

        // The text is there: ink is white, and the bars behind it are dark.
        int ink = 0;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (labels[x, y] == TextLayer.Ink)
                {
                    ink++;
                    Assert.Equal(new Rgba32(255, 255, 255, 255), frame[(y * Width) + x]);
                }
            }
        }

        Assert.True(ink > 3000, $"Only {ink} pixels of text.");
        Assert.True(frame[(2 * Width) + Width - 3].R < 40, "The top bar should be dark.");

        // What the first pass read from this format, against the tile it was made from.
        Rgba32[] tile = SourceImage.Create();
        Float4[] decoded = demo.Explorer.Decoded.ToArray();
        string type = SourceImage.Formats[format].Type;

        for (int i = 0; i < tile.Length; i++)
        {
            Rgba32 p = tile[i];
            float gray = SourceImage.Luminance(p) / 255f;

            Float4 expected = type switch
            {
                "Rgba32" or "Rgba16Float" or "Float4" => new Float4(p.R / 255f, p.G / 255f, p.B / 255f, 1f),
                "Rgba32Srgb" => new Float4(Linear(p.R), Linear(p.G), Linear(p.B), 1f),
                "Rg16" => new Float4(p.R / 255f, p.G / 255f, 0f, 1f),
                _ => new Float4(gray, 0f, 0f, 1f),   // R8, Half, float: one channel; the others read (0, 0, 1)
            };

            // Bytes are exact; halves round value / 255 to 11 bits; the GPU decodes sRGB to 12 bits.
            float tolerance = type is "Half" or "Rgba16Float" ? 5e-4f : type == "Rgba32Srgb" ? 1.3e-4f : 0f;

            Assert.Equal(expected.X, decoded[i].X, tolerance);
            Assert.Equal(expected.Y, decoded[i].Y, tolerance);
            Assert.Equal(expected.Z, decoded[i].Z, tolerance);
            Assert.Equal(1f, decoded[i].W);
        }

        static float Linear(byte value)
        {
            double c = value / 255.0;
            return (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }
    }

    [MetalFact]
    public void Samplers_PanelsAgreeInsideTheTexture_AndDifferOutsideIt()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using TextureDemo demo = new(device);
        using ReadWriteTexture2D<Rgba32> target = device.AllocateReadWriteTexture2D<Rgba32>(Width, Height);

        demo.Render(target);

        Rgba32[] frame = target.ToArray();
        TextLayer labels = demo.Labels!;
        ExplorerLayout layout = new(Width, Height);
        SamplerExplorer explorer = demo.Explorer;
        int cellWidth = (int)layout.Cell.X, cellHeight = (int)layout.Cell.Y;

        Assert.Equal((400f, 330f), (layout.Cell.X, layout.Cell.Y));

        // For every pair of panels: how many pixels differ where both show the texture itself
        // (0 < uv < 1) and where both show what the address mode makes of the outside.
        (double Inside, double Outside) Differences(int first, int second)
        {
            int inside = 0, insideDifferent = 0, outside = 0, outsideDifferent = 0;
            Float2 a = layout.PanelOrigin(first), b = layout.PanelOrigin(second);

            for (int y = 8; y < cellHeight - 8; y++)
            {
                for (int x = 8; x < cellWidth - 8; x++)
                {
                    int ax = (int)a.X + x, ay = (int)a.Y + y, bx = (int)b.X + x, by = (int)b.Y + y;

                    if (labels[ax, ay] != 0 || labels[bx, by] != 0)
                    {
                        continue;
                    }

                    Float2 uv = layout.Uv(first, ax, ay, explorer.Zoom, explorer.Pan, explorer.Angle);
                    bool different = !frame[(ay * Width) + ax].Equals(frame[(by * Width) + bx]);

                    // Away from the outline (and from the half texel where linear filtering already blends in the outside).
                    if (uv.X > 0.06f && uv.X < 0.94f && uv.Y > 0.06f && uv.Y < 0.94f)
                    {
                        inside++;
                        insideDifferent += different ? 1 : 0;
                    }
                    else if (uv.X < -0.06f || uv.X > 1.06f || uv.Y < -0.06f || uv.Y > 1.06f)
                    {
                        outside++;
                        outsideDifferent += different ? 1 : 0;
                    }
                }
            }

            Assert.True(inside > 5000 && outside > 50000, $"Panels {first}/{second}: {inside} pixels inside, {outside} outside.");
            return (insideDifferent / (double)inside, outsideDifferent / (double)outside);
        }

        // Panels 0..2 are Point × Clamp, Repeat, Mirror; 3..5 the same with Linear.
        foreach (int row in new[] { 0, 3 })
        {
            (double inside, double outside) clampRepeat = Differences(row, row + 1);
            (double inside, double outside) clampMirror = Differences(row, row + 2);
            (double inside, double outside) repeatMirror = Differences(row + 1, row + 2);

            // Inside [0, 1] the address mode does not matter: the same pixels, exactly.
            Assert.Equal((0.0, 0.0, 0.0), (clampRepeat.inside, clampMirror.inside, repeatMirror.inside));

            // Outside, Clamp smears the edge where Repeat and Mirror draw more copies, and every other copy of Mirror is flipped.
            Assert.True(clampRepeat.outside > 0.5, $"Clamp vs Repeat differ at {clampRepeat.outside:P0} of the outside.");
            Assert.True(clampMirror.outside > 0.5, $"Clamp vs Mirror differ at {clampMirror.outside:P0} of the outside.");
            Assert.True(repeatMirror.outside > 0.3, $"Repeat vs Mirror differ at {repeatMirror.outside:P0} of the outside.");
        }

        // The filter shows inside too: Linear blends neighbouring texels where Point picks one.
        for (int column = 0; column < 3; column++)
        {
            (double inside, double outside) pointLinear = Differences(column, column + 3);

            Assert.True(pointLinear.inside > 0.3, $"Point vs Linear differ at only {pointLinear.inside:P0} of the texture.");
        }

        // Clamp really is the edge texel: left of the texture every pixel of a row has that row's edge color.
        Float2 corner = layout.PanelOrigin(0);
        int rowY = (int)corner.Y + (cellHeight / 2);
        Float2 leftmost = layout.Uv(0, (int)corner.X + 10, rowY, explorer.Zoom, explorer.Pan, explorer.Angle);

        Assert.True(leftmost.X < -0.5f && leftmost.Y > 0.3f && leftmost.Y < 0.7f);
        Assert.Equal(new Rgba32(230, 57, 70, 255), frame[(rowY * Width) + (int)corner.X + 10]);
        Assert.Equal(frame[(rowY * Width) + (int)corner.X + 10], frame[(rowY * Width) + (int)corner.X + 60]);
    }

    [MetalFact]
    public void Samplers_TheGpuFrameMatchesTheSameShaderOnTheCpu_AndEachPanelMatchesItsSampler()
    {
        // 600 × 360: panels of 200 × 165 whole pixels.
        const int width = 600, height = 360;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using TextureDemo demo = new(device);
        using ReadWriteTexture2D<Rgba32> gpu = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        using ReadWriteTexture2D<Rgba32> cpu = device.AllocateReadWriteTexture2D<Rgba32>(width, height);

        demo.Explorer.Pan = new Float2(0.13f, -0.21f);
        demo.Explorer.Angle = 0.3f;
        demo.Render(gpu);

        ExplorerLayout layout = new(width, height);
        SamplerExplorer explorer = demo.Explorer;

        device.ForEachCpu(cpu, explorer.Shader(layout, demo.Labels!));

        // Linear filtering differs by the GPU's weight precision, a point sample only on a texel boundary.
        Rgba32[] frame = gpu.ToArray();
        CpuComparison.AssertPixelsClose(frame, cpu.ToArray(), maxStep: 2, outlierFraction: 0.002, "sampler grid");

        // Independently of the shader: a pixel well inside each panel is that panel's sampler at that coordinate.
        using ReadOnlyTexture2D<Float4> image = explorer.Decoded.AsReadOnly();

        for (int panel = 0; panel < ExplorerLayout.PanelCount; panel++)
        {
            Float2 corner = layout.PanelOrigin(panel);

            foreach ((int dx, int dy) in new[] { (30, 60), (100, 82), (170, 120), (60, 150), (150, 40) })
            {
                int x = (int)corner.X + dx, y = (int)corner.Y + dy;
                Float2 uv = layout.Uv(panel, x, y, explorer.Zoom, explorer.Pan, explorer.Angle);
                Float4 expected = image.Sample(Samplers[panel], uv);
                Float2 fromCenter = new(MathF.Abs(uv.X - 0.5f), MathF.Abs(uv.Y - 0.5f));

                // Not on the white outline of the unit square, and (for Point) not on a texel boundary.
                if (MathF.Abs(MathF.Max(fromCenter.X, fromCenter.Y) - 0.5f) < 0.05f || NearTexelBoundary(uv))
                {
                    continue;
                }

                Rgba32 actual = frame[(y * width) + x];

                Assert.Equal(expected.X * 255f, actual.R, 2.01f);
                Assert.Equal(expected.Y * 255f, actual.G, 2.01f);
                Assert.Equal(expected.Z * 255f, actual.B, 2.01f);
            }
        }

        static bool NearTexelBoundary(Float2 uv)
        {
            float fx = (uv.X * SourceImage.Size) - MathF.Floor(uv.X * SourceImage.Size), fy = (uv.Y * SourceImage.Size) - MathF.Floor(uv.Y * SourceImage.Size);
            return fx < 0.05f || fx > 0.95f || fy < 0.05f || fy > 0.95f;
        }
    }

    [MetalFact]
    public void Samplers_ZoomPanRotationAndFormatChangeTheFrame_AndResetRestoresIt()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using TextureDemo demo = new(device);
        using ReadWriteTexture2D<Rgba32> target = device.AllocateReadWriteTexture2D<Rgba32>(640, 360);

        Rgba32[] Frame()
        {
            demo.Render(target);
            return target.ToArray();
        }

        Rgba32[] original = Frame();

        Assert.Equal(original, Frame());

        demo.Explorer.Zoom = 1.2f;
        Rgba32[] zoomed = Frame();
        demo.Explorer.Pan = new Float2(0.4f, 0.1f);
        Rgba32[] panned = Frame();
        demo.Rotating = true;
        demo.Advance(2.0);
        Rgba32[] rotated = Frame();

        Assert.NotEqual(original, zoomed);
        Assert.NotEqual(zoomed, panned);
        Assert.NotEqual(panned, rotated);
        Assert.True(demo.Explorer.Angle > 0.1f);

        demo.Explorer.ResetView();
        Assert.Equal(original, Frame());

        // The format changes the picture and the label; the index wraps around both ways.
        demo.Explorer.Format = 2;
        Rgba32[] oneChannel = Frame();

        Assert.NotEqual(original, oneChannel);

        demo.Explorer.Format = -1;
        Assert.Equal(7, demo.Explorer.Format);
        demo.Explorer.Format = 8;
        Assert.Equal(0, demo.Explorer.Format);
        Assert.Equal(original, Frame());

        // Rotation only applies to the sampler explorer.
        demo.Mode = DemoMode.PingPong;
        float angle = demo.Explorer.Angle;
        demo.Advance(1.0);
        Assert.Equal(angle, demo.Explorer.Angle);
    }

    [MetalTheory]
    [InlineData(0f, 37.0, -12.0)]
    [InlineData(0.7f, 37.0, -12.0)]
    [InlineData(-2.1f, -80.0, 45.0)]
    public void Samplers_DraggingMovesThePictureWithThePointer(float angle, double dx, double dy)
    {
        using SamplerExplorer explorer = new(GraphicsDevice.GetDefault()) { Angle = angle, Zoom = 1.7f, Pan = new Float2(0.2f, -0.3f) };
        ExplorerLayout layout = new(Width, Height);

        for (int panel = 0; panel < ExplorerLayout.PanelCount; panel++)
        {
            int x = (int)layout.PanelOrigin(panel).X + 150, y = (int)layout.PanelOrigin(panel).Y + 170;
            Float2 before = layout.Uv(panel, x, y, explorer.Zoom, explorer.Pan, explorer.Angle);
            Float2 pan = explorer.Pan;

            explorer.DragBy(dx, dy, layout);

            // What was under the pointer is still under it after the drag.
            Float2 after = layout.Uv(panel, x + (int)dx, y + (int)dy, explorer.Zoom, explorer.Pan, explorer.Angle);

            Assert.Equal(before.X, after.X, 1e-4f);
            Assert.Equal(before.Y, after.Y, 1e-4f);
            explorer.Pan = pan;
        }

        // Zooming is clamped at both ends and keeps the center of every panel where it is (the pixel
        // next to the center is half a pixel off it, hence the tolerance).
        Float2 center = layout.Uv(0, 200, layout.TopBar + 165, explorer.Zoom, explorer.Pan, explorer.Angle);

        explorer.ZoomBy(0.5f);
        Assert.Equal(0.85f, explorer.Zoom, 1e-5f);

        Float2 zoomed = layout.Uv(0, 200, layout.TopBar + 165, explorer.Zoom, explorer.Pan, explorer.Angle);

        Assert.Equal(center.X, zoomed.X, 2e-3f);
        Assert.Equal(center.Y, zoomed.Y, 2e-3f);
        Assert.Equal(explorer.Pan.X + 0.5f, zoomed.X, 2e-3f);

        explorer.ZoomBy(1e-9f);
        Assert.Equal(0.05f, explorer.Zoom);
        explorer.ZoomBy(1e9f);
        Assert.Equal(16f, explorer.Zoom);
    }

    // ---- ping-pong --------------------------------------------------------------------------------

    [MetalFact]
    public void PingPong_StepsChangeTheField_AndAreDeterministic()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReactionDiffusion first = new(device);
        using ReactionDiffusion second = new(device);

        Float4[] start = first.Current.ToArray();

        Assert.Equal(ReactionDiffusion.Width * ReactionDiffusion.Height, start.Length);
        Assert.Equal(Flat(start), Flat(second.Current.ToArray()));
        Assert.Contains(start, static p => p.Y == 1f);     // the seeded spots of B
        Assert.Contains(start, static p => p.Y == 0f && p.X == 1f);

        // An odd number of steps ends in the other texture, an even number back in the first.
        ReadWriteTexture2D<Float4> initial = first.Current;
        first.Run(1);
        Assert.NotSame(initial, first.Current);
        first.Run(31);
        Assert.Same(initial, first.Current);
        Assert.Equal(32, first.Steps);

        second.Run(32);

        float[] after = Flat(first.Current.ToArray());

        Assert.NotEqual(Flat(start), after);
        Assert.Equal(after, Flat(second.Current.ToArray()));
        Assert.All(after, static v => Assert.InRange(v, 0f, 1f));

        // It keeps moving, and B spreads from the spots.
        first.Run(200);

        Float4[] later = first.Current.ToArray();

        Assert.NotEqual(after, Flat(later));
        Assert.True(later.Count(static p => p.Y > 0.1f) > start.Count(static p => p.Y > 0.1f), "B should have grown.");

        // Restarting gives the same field again.
        first.Reset();
        Assert.Equal(0, first.Steps);
        Assert.Equal(Flat(start), Flat(first.Current.ToArray()));
    }

    [MetalFact]
    public void PingPong_AStepOnTheGpuMatchesTheSameShaderOnTheCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReactionDiffusion simulation = new(device);

        simulation.Run(40);

        Float4[] state = simulation.Current.ToArray();
        using ReadWriteTexture2D<Float4> source = device.AllocateReadWriteTexture2D<Float4>(state, ReactionDiffusion.Width, ReactionDiffusion.Height);
        using ReadOnlyTexture2D<Float4> view = source.AsReadOnly();
        using ReadWriteTexture2D<Float4> gpu = device.AllocateReadWriteTexture2D<Float4>(ReactionDiffusion.Width, ReactionDiffusion.Height);
        using ReadWriteTexture2D<Float4> cpu = device.AllocateReadWriteTexture2D<Float4>(ReactionDiffusion.Width, ReactionDiffusion.Height);
        ReactionStep step = new(view, gpu, ReactionDiffusion.Feed, ReactionDiffusion.Kill);

        device.For(ReactionDiffusion.Width, ReactionDiffusion.Height, step);
        device.ForCpu(ReactionDiffusion.Width, ReactionDiffusion.Height, new ReactionStep(view, cpu, ReactionDiffusion.Feed, ReactionDiffusion.Kill));

        // The neighbours are point samples (exact on both); the arithmetic differs by fused multiply-adds.
        CpuComparison.AssertClose(Flat(gpu.ToArray()), Flat(cpu.ToArray()), relative: 1e-5f, absolute: 1e-6f);

        // And it is the step the simulation takes.
        simulation.Run(1);
        Assert.Equal(Flat(gpu.ToArray()), Flat(simulation.Current.ToArray()));
    }

    [MetalFact]
    public void PingPong_TheBrushWritesItsCircle_WrapsAround_AndErases()
    {
        const int width = ReactionDiffusion.Width, height = ReactionDiffusion.Height;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReactionDiffusion simulation = new(device);
        Float4[] before = simulation.Current.ToArray();

        // A dab in the middle and one on the top-left corner, with no simulation step in between.
        simulation.Paint(new Float2(0.5f, 0.5f));
        simulation.Paint(new Float2(0f, 0f), radius: 3f);

        using (ComputeContext context = device.CreateComputeContext())
        {
            simulation.RecordSteps(context, steps: 0);
        }

        Float4[] painted = simulation.Current.ToArray();
        int changed = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = x + 0.5f - (width / 2f), dy = y + 0.5f - (height / 2f);
                bool inMiddle = (dx * dx) + (dy * dy) <= ReactionDiffusion.BrushRadius * ReactionDiffusion.BrushRadius;

                // The corner dab reaches the three other corners: the field wraps around.
                float cx = MathF.Min(x + 0.5f, width - (x + 0.5f)), cy = MathF.Min(y + 0.5f, height - (y + 0.5f));
                bool inCorner = (cx * cx) + (cy * cy) <= 9f;

                Float4 was = before[(y * width) + x], now = painted[(y * width) + x];

                if (inMiddle || inCorner)
                {
                    // Painting sets B and keeps A.
                    Assert.Equal((was.X, 1f), (now.X, now.Y));
                    changed++;
                }
                else
                {
                    Assert.Equal((was.X, was.Y), (now.X, now.Y));
                }
            }
        }

        Assert.InRange(changed, 60, 90);   // about π · 4² + π · 3² pixels
        Assert.Equal(1f, painted[((height - 1) * width) + width - 1].Y);

        // Erasing restores the empty state under the brush.
        simulation.Paint(new Float2(0.5f, 0.5f), erase: true);
        simulation.Run(0);

        Float4 center = simulation.Current.ToArray()[((height / 2) * width) + (width / 2)];

        Assert.Equal((1f, 0f), (center.X, center.Y));
    }

    [MetalFact]
    public void PingPong_TheDisplaySamplesTheLatestState_AndTilesItWhenZoomedOut()
    {
        const int width = 640, height = 360;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using TextureDemo demo = new(device) { Mode = DemoMode.PingPong };
        using ReadWriteTexture2D<Rgba32> target = device.AllocateReadWriteTexture2D<Rgba32>(width, height);

        demo.Render(target);

        Rgba32[] first = target.ToArray();

        Assert.Equal(ReactionDiffusion.StepsPerFrame, demo.Reaction.Steps);
        Assert.True(first.Distinct().Count() > 40, "The frame is (nearly) blank.");

        // Each frame advances the simulation; paused, the picture stands still but the brush still paints.
        demo.Render(target);
        Rgba32[] second = target.ToArray();

        Assert.NotEqual(first, second);
        Assert.Equal(2 * ReactionDiffusion.StepsPerFrame, demo.Reaction.Steps);

        demo.Paused = true;
        demo.Render(target);
        Rgba32[] paused = target.ToArray();
        demo.Render(target);

        Assert.Equal(2 * ReactionDiffusion.StepsPerFrame, demo.Reaction.Steps);
        Assert.Equal(paused, target.ToArray());

        demo.Reaction.Paint(demo.Reaction.FrameToField(width / 2, height / 2, width, height));
        demo.Render(target);

        Rgba32[] dabbed = target.ToArray();

        Assert.NotEqual(paused, dabbed);
        Assert.True(dabbed[((height / 2) * width) + (width / 2)].R > 200, "The dab should show as the brightest color.");

        // Zoomed out to two fields across, LinearRepeat tiles it: the right half repeats the left one.
        demo.Reaction.Zoom = 2f;
        demo.Render(target);

        Rgba32[] tiled = target.ToArray();
        TextLayer labels = demo.Labels!;
        int compared = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 8; x < (width / 2) - 8; x++)
            {
                // Not under text, and not on the seam lines the shader draws between copies.
                Float2 uv = demo.Reaction.FrameToField(x + 0.5, y + 0.5, width, height);
                float seamX = MathF.Abs(uv.X - MathF.Round(uv.X)), seamY = MathF.Abs(uv.Y - MathF.Round(uv.Y));

                if (labels[x, y] != 0 || labels[x + (width / 2), y] != 0 || seamX < 0.02f || seamY < 0.02f)
                {
                    continue;
                }

                Rgba32 left = tiled[(y * width) + x], right = tiled[(y * width) + x + (width / 2)];

                Assert.InRange(Math.Abs(left.R - right.R), 0, 2);
                Assert.InRange(Math.Abs(left.G - right.G), 0, 2);
                Assert.InRange(Math.Abs(left.B - right.B), 0, 2);
                compared++;
            }
        }

        Assert.True(compared > 60000, $"Only {compared} pixels compared.");
        Assert.NotEqual(dabbed, tiled);
    }

    [MetalTheory]
    [InlineData(1f)]
    [InlineData(2f)]
    [InlineData(0.5f)]
    public void FrameToField_IsTheDisplaysMapping(float zoom)
    {
        using ReactionDiffusion simulation = new(GraphicsDevice.GetDefault()) { Zoom = zoom };

        Float2 center = simulation.FrameToField(640, 360, 1280, 720);
        Float2 corner = simulation.FrameToField(0, 0, 1280, 720);

        Assert.Equal((0.5f, 0.5f), (center.X, center.Y));
        Assert.Equal((0.5f - (zoom / 2f), 0.5f - (zoom / 2f)), (corner.X, corner.Y));
    }

    // ---- wheel zoom -------------------------------------------------------------------------------

    [Theory]
    [InlineData(1f, 1)] // one notch
    [InlineData(-1f, -1)]
    [InlineData(0.1f, 1)] // a slow notch is still a whole step
    [InlineData(-0.1f, -1)]
    [InlineData(2.5f, 3)] // a faster spin: whole steps, rounded away from zero
    [InlineData(-1.01f, -2)]
    [InlineData(30f, WheelZoom.MaxSteps)] // capped
    [InlineData(-30f, -WheelZoom.MaxSteps)]
    public void WheelZoom_ANotchedWheelZoomsInWholeSteps(float lines, int steps)
    {
        // Positive deltas zoom in, and a factor below 1 is closer.
        float factor = WheelZoom.Factor(Wheel(lines, precise: false), scaleFactor: 2.0);

        Assert.Equal(MathF.Pow(WheelZoom.Step, -steps), factor, 1e-6f);

        // The display's scale does not matter to lines.
        Assert.Equal(factor, WheelZoom.Factor(Wheel(lines, precise: false), scaleFactor: 1.0));
    }

    [Fact]
    public void WheelZoom_ANotchIsOnePressOfPlusOrMinus()
    {
        // What the + and - keys do (TexturesApp.OnKeyDown): the wheel and the keys share one ladder of zooms.
        Assert.Equal(1f / WheelZoom.Step, WheelZoom.Factor(Wheel(1f, precise: false), 2.0), 1e-6f);
        Assert.Equal(WheelZoom.Step, WheelZoom.Factor(Wheel(-1f, precise: false), 2.0), 1e-6f);
    }

    [Fact]
    public void WheelZoom_ATrackpadZoomsSmoothly_ByFingerTravelInPoints()
    {
        // PointsPerStep points of travel are one notch: 80 pixels on a Retina display, 40 on a 1x one.
        Assert.Equal(1f / WheelZoom.Step, WheelZoom.Factor(Wheel(80f, precise: true), 2.0), 1e-6f);
        Assert.Equal(1f / WheelZoom.Step, WheelZoom.Factor(Wheel(40f, precise: true), 1.0), 1e-6f);
        Assert.Equal(WheelZoom.Step, WheelZoom.Factor(Wheel(-80f, precise: true), 2.0), 1e-6f);

        // Smooth: a small delta is a small zoom, not a whole step...
        float small = WheelZoom.Factor(Wheel(1f, precise: true), 2.0);

        Assert.InRange(small, 0.998f, 0.9999f);

        // ...and a gesture zooms the same however it is cut into events.
        float whole = WheelZoom.Factor(Wheel(120f, precise: true), 2.0);
        float parts = 1f;

        foreach (float delta in new[] { 3f, 17.5f, 40f, 0.5f, 59f })
        {
            parts *= WheelZoom.Factor(Wheel(delta, precise: true), 2.0);
        }

        Assert.Equal(whole, parts, 1e-5f);
        Assert.Equal(1f, WheelZoom.Factor(Wheel(64f, precise: true), 2.0) * WheelZoom.Factor(Wheel(-64f, precise: true), 2.0), 1e-6f);

        // One event is capped like a wheel's, and a scale factor that is not one counts as 1.
        Assert.Equal(MathF.Pow(WheelZoom.Step, -WheelZoom.MaxSteps), WheelZoom.Factor(Wheel(1e6f, precise: true), 2.0), 1e-6f);
        Assert.Equal(MathF.Pow(WheelZoom.Step, WheelZoom.MaxSteps), WheelZoom.Factor(Wheel(-1e6f, precise: true), 2.0), 1e-6f);
        Assert.Equal(1f / WheelZoom.Step, WheelZoom.Factor(Wheel(40f, precise: true), 0.0), 1e-6f);
        Assert.Equal(1f / WheelZoom.Step, WheelZoom.Factor(Wheel(40f, precise: true), double.NaN), 1e-6f);
    }

    [Fact]
    public void WheelZoom_IgnoresMomentumSidewaysScrollingAndDeltasThatAreNotNumbers()
    {
        // Inertia after the fingers lift would carry the zoom past where the user stopped.
        Assert.Equal(1f, WheelZoom.Factor(new MouseWheelEventArgs(10, 10, 0f, 25f, isPrecise: true, isMomentum: true, KeyModifiers.None), 2.0));

        // Shift + a notched wheel arrives as a horizontal scroll; only the vertical delta zooms.
        Assert.Equal(1f, WheelZoom.Factor(new MouseWheelEventArgs(10, 10, 3f, 0f, isPrecise: false, isMomentum: false, KeyModifiers.Shift), 2.0));
        Assert.Equal(1f, WheelZoom.Factor(new MouseWheelEventArgs(10, 10, -40f, 0f, isPrecise: true, isMomentum: false, KeyModifiers.None), 2.0));

        foreach (bool precise in new[] { false, true })
        {
            Assert.Equal(1f, WheelZoom.Factor(Wheel(0f, precise), 2.0));
            Assert.Equal(1f, WheelZoom.Factor(Wheel(float.NaN, precise), 2.0));
            Assert.Equal(1f, WheelZoom.Factor(Wheel(float.PositiveInfinity, precise), 2.0));
            Assert.Equal(1f, WheelZoom.Factor(Wheel(float.NegativeInfinity, precise), 2.0));
        }

        Assert.Throws<ArgumentNullException>(() => WheelZoom.Factor(null!, 2.0));
    }

    [Theory]
    [InlineData(0.0, 38.0, 0)] // the grid's own corner
    [InlineData(399.9, 367.9, 0)]
    [InlineData(400.0, 367.9, 1)]
    [InlineData(800.0, 100.0, 2)]
    [InlineData(399.9, 368.0, 3)]
    [InlineData(600.0, 500.0, 4)]
    [InlineData(1199.9, 697.9, 5)]
    [InlineData(600.0, 3.0, 1)] // in the top bar: the panel below
    [InlineData(1000.0, 715.0, 5)] // in the help bar: the panel above
    [InlineData(-50.0, -50.0, 0)] // outside the frame: the nearest panel
    [InlineData(5000.0, 5000.0, 5)]
    [InlineData(-1.0, 5000.0, 3)]
    public void Layout_PanelAt_FindsThePanelUnderAPosition_OrTheNearestOne(double x, double y, int panel)
    {
        // 1200 × 720: a 38-pixel top bar, then two rows of three 400 × 330 panels.
        ExplorerLayout layout = new(Width, Height);

        Assert.Equal((38, 400f, 330f), (layout.TopBar, layout.Cell.X, layout.Cell.Y));
        Assert.Equal(panel, layout.PanelAt(x, y));
    }

    [MetalTheory]
    [InlineData(0f, 0.5f)]
    [InlineData(0f, 1.21f)]
    [InlineData(0.7f, 1f / 1.1f)]
    [InlineData(-2.1f, 3f)]
    public void Samplers_ZoomAtKeepsThePointUnderThePointer(float angle, float factor)
    {
        using SamplerExplorer explorer = new(GraphicsDevice.GetDefault()) { Angle = angle, Zoom = 1.7f, Pan = new Float2(0.2f, -0.3f) };
        ExplorerLayout layout = new(Width, Height);
        Float2 pan = explorer.Pan;
        Float2? moved = null;

        for (int panel = 0; panel < ExplorerLayout.PanelCount; panel++)
        {
            // A pixel well off the middle of the panel; the pointer is at its center.
            int x = (int)layout.PanelOrigin(panel).X + 317, y = (int)layout.PanelOrigin(panel).Y + 41;
            Float2 before = layout.Uv(panel, x, y, explorer.Zoom, explorer.Pan, explorer.Angle);

            explorer.ZoomAt(factor, x + 0.5, y + 0.5, layout);

            Float2 after = layout.Uv(panel, x, y, explorer.Zoom, explorer.Pan, explorer.Angle);

            Assert.Equal(1.7f * factor, explorer.Zoom, 1e-5f);
            Assert.Equal(before.X, after.X, 1e-5f);
            Assert.Equal(before.Y, after.Y, 1e-5f);

            // That took a pan (a zoom around the middle would have moved the point by a third of a texture or more)...
            Assert.True(MathF.Abs(explorer.Pan.X - pan.X) + MathF.Abs(explorer.Pan.Y - pan.Y) > 0.05f, "The view was not panned.");

            // ...and the same one whichever panel the pointer is in, because the panels share a mapping.
            moved ??= explorer.Pan;
            Assert.Equal(moved.Value.X, explorer.Pan.X, 1e-5f);
            Assert.Equal(moved.Value.Y, explorer.Pan.Y, 1e-5f);

            (explorer.Zoom, explorer.Pan) = (1.7f, pan);
        }

        // Around the middle of a panel it is ZoomBy: no pan at all.
        Float2 middle = layout.PanelOrigin(4) + (layout.Cell * 0.5f);

        explorer.ZoomAt(factor, middle.X, middle.Y, layout);
        Assert.Equal(pan.X, explorer.Pan.X, 1e-6f);
        Assert.Equal(pan.Y, explorer.Pan.Y, 1e-6f);
    }

    [MetalFact]
    public void Samplers_ZoomAt_StopsAtTheLimitsWithoutSliding_AndComesBack()
    {
        using SamplerExplorer explorer = new(GraphicsDevice.GetDefault()) { Angle = 0.4f, Pan = new Float2(-1.5f, 0.25f) };
        ExplorerLayout layout = new(Width, Height);
        const int x = 930, y = 601;
        Float2 point = layout.Uv(5, x, y, explorer.Zoom, explorer.Pan, explorer.Angle);

        void AssertPointStays()
        {
            Float2 now = layout.Uv(5, x, y, explorer.Zoom, explorer.Pan, explorer.Angle);

            Assert.Equal(point.X, now.X, 1e-4f);
            Assert.Equal(point.Y, now.Y, 1e-4f);
        }

        // Far past either limit, the point is still under the pointer...
        explorer.ZoomAt(1e-9f, x + 0.5, y + 0.5, layout);
        Assert.Equal(0.05f, explorer.Zoom);
        AssertPointStays();

        // ...and more of the same changes nothing: the view does not creep while the wheel keeps turning.
        Float2 pan = explorer.Pan;

        explorer.ZoomAt(0.5f, x + 0.5, y + 0.5, layout);
        Assert.Equal((0.05f, pan), (explorer.Zoom, explorer.Pan));

        explorer.ZoomAt(1e9f, x + 0.5, y + 0.5, layout);
        Assert.Equal(16f, explorer.Zoom);
        AssertPointStays();
        pan = explorer.Pan;
        explorer.ZoomAt(2f, x + 0.5, y + 0.5, layout);
        Assert.Equal((16f, pan), (explorer.Zoom, explorer.Pan));

        // Many small steps in and the same steps back out, as a trackpad sends them: back where it started.
        (explorer.Zoom, explorer.Pan) = (SamplerExplorer.DefaultZoom, new Float2(-1.5f, 0.25f));

        for (int i = 0; i < 200; i++)
        {
            explorer.ZoomAt(0.99f, x + 0.5, y + 0.5, layout);
        }

        Assert.Equal(SamplerExplorer.DefaultZoom * MathF.Pow(0.99f, 200), explorer.Zoom, 1e-4f);
        AssertPointStays();

        for (int i = 0; i < 200; i++)
        {
            explorer.ZoomAt(1f / 0.99f, x + 0.5, y + 0.5, layout);
        }

        Assert.Equal(SamplerExplorer.DefaultZoom, explorer.Zoom, 1e-3f);
        Assert.Equal(-1.5f, explorer.Pan.X, 1e-3f);
        Assert.Equal(0.25f, explorer.Pan.Y, 1e-3f);
    }

    [MetalFact]
    public void Samplers_ZoomAt_TheRenderedPixelUnderThePointerKeepsItsColor()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using TextureDemo demo = new(device);
        using ReadWriteTexture2D<Rgba32> target = device.AllocateReadWriteTexture2D<Rgba32>(Width, Height);
        ExplorerLayout layout = new(Width, Height);

        // Pixels of the three bilinear panels, inside the texture and clear of its outline, the
        // panel's edge and the label: there the color is a smooth function of the coordinate.
        (int Panel, int X, int Y)[] pointers = [(3, 160, 500), (4, 640, 560), (5, 1030, 510)];

        foreach ((int panel, int x, int y) in pointers)
        {
            demo.Explorer.ResetView();

            Float2 uv = layout.Uv(panel, x, y, demo.Explorer.Zoom, demo.Explorer.Pan, demo.Explorer.Angle);

            Assert.InRange(uv.X, 0.1f, 0.9f);
            Assert.InRange(uv.Y, 0.1f, 0.9f);
            Assert.Equal(panel, layout.PanelAt(x + 0.5, y + 0.5));

            demo.Render(target);

            Rgba32[] before = target.ToArray();

            demo.ZoomAt(0.4f, x + 0.5, y + 0.5, Width, Height);
            demo.Render(target);

            Rgba32[] after = target.ToArray();
            Rgba32 was = before[(y * Width) + x], now = after[(y * Width) + x];

            Assert.NotEqual(before, after);
            // The same coordinate up to float rounding, which can move a bilinear weight by one of its 256 steps.
            Assert.InRange(Math.Abs(was.R - now.R), 0, 2);
            Assert.InRange(Math.Abs(was.G - now.G), 0, 2);
            Assert.InRange(Math.Abs(was.B - now.B), 0, 2);
        }
    }

    [MetalTheory]
    [InlineData(0.5f, 100.5, 620.25)]
    [InlineData(1f / 1.1f, 1279.0, 0.0)]
    [InlineData(1.21f, 37.0, 11.0)]
    [InlineData(3f, 900.0, 700.0)]
    public void PingPong_ZoomAtKeepsTheFieldPointUnderThePointer(float factor, double x, double y)
    {
        const int width = 1280, height = 720;
        using ReactionDiffusion simulation = new(GraphicsDevice.GetDefault()) { Zoom = 1.5f, Pan = new Float2(0.25f, 0.75f) };
        Float2 before = simulation.FrameToField(x, y, width, height);

        simulation.ZoomAt(factor, x, y, width, height);

        Assert.Equal(1.5f * factor, simulation.Zoom, 1e-5f);
        AssertSameFieldPoint(before, simulation.FrameToField(x, y, width, height));

        // The offset is kept to its fraction, which is all a repeating field needs.
        Assert.InRange(simulation.Pan.X, 0f, 1f);
        Assert.InRange(simulation.Pan.Y, 0f, 1f);

        // Away from the pointer the picture did move: this was not a pan.
        Float2 elsewhere = simulation.FrameToField(width - x, height - y, width, height);

        simulation.ZoomAt(1f / factor, x, y, width, height);
        AssertSameFieldPoint(before, simulation.FrameToField(x, y, width, height));

        Float2 back = simulation.FrameToField(width - x, height - y, width, height);

        Assert.True(FieldDistance(elsewhere.X, back.X) + FieldDistance(elsewhere.Y, back.Y) > 0.05f, "Nothing moved away from the pointer.");

        // In and back out around the same point is the view it started from.
        Assert.Equal(1.5f, simulation.Zoom, 1e-5f);
        AssertSameFieldPoint(new Float2(0.75f, 1.25f), simulation.FrameToField(width / 2, height / 2, width, height));
    }

    [MetalFact]
    public void PingPong_ZoomByAndZoomAt_StopAtTheLimits_AndResetViewRestoresTheStart()
    {
        const int width = 1280, height = 720;
        using ReactionDiffusion simulation = new(GraphicsDevice.GetDefault());

        Assert.Equal((1f, default(Float2)), (simulation.Zoom, simulation.Pan));

        // Around the middle of the frame, ZoomAt is ZoomBy: no offset.
        simulation.ZoomAt(0.5f, width / 2, height / 2, width, height);
        Assert.Equal((0.5f, default(Float2)), (simulation.Zoom, simulation.Pan));

        simulation.ZoomBy(3f);
        Assert.Equal((1.5f, default(Float2)), (simulation.Zoom, simulation.Pan));

        simulation.ZoomBy(1e-9f);
        Assert.Equal(ReactionDiffusion.MinZoom, simulation.Zoom);
        simulation.ZoomBy(1e9f);
        Assert.Equal(ReactionDiffusion.MaxZoom, simulation.Zoom);

        // At a limit the point stays under the pointer, and pushing further does not slide the view.
        simulation.Zoom = 1f;

        Float2 point = simulation.FrameToField(200, 650, width, height);

        simulation.ZoomAt(1e-9f, 200, 650, width, height);
        Assert.Equal(ReactionDiffusion.MinZoom, simulation.Zoom);
        AssertSameFieldPoint(point, simulation.FrameToField(200, 650, width, height));

        Float2 pan = simulation.Pan;

        simulation.ZoomAt(0.5f, 200, 650, width, height);
        Assert.Equal((ReactionDiffusion.MinZoom, pan), (simulation.Zoom, simulation.Pan));

        simulation.ZoomAt(1e9f, 200, 650, width, height);
        Assert.Equal(ReactionDiffusion.MaxZoom, simulation.Zoom);
        AssertSameFieldPoint(point, simulation.FrameToField(200, 650, width, height));
        pan = simulation.Pan;
        simulation.ZoomAt(2f, 200, 650, width, height);
        Assert.Equal((ReactionDiffusion.MaxZoom, pan), (simulation.Zoom, simulation.Pan));

        Assert.NotEqual(default, simulation.Pan);
        simulation.ResetView();
        Assert.Equal((1f, default(Float2)), (simulation.Zoom, simulation.Pan));
    }

    [MetalFact]
    public void PingPong_ZoomAt_TheDabUnderThePointerStaysUnderIt_AndTheStartingViewComesBackWithoutSeams()
    {
        const int width = 640, height = 360;
        const int x = 500, y = 100;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using TextureDemo demo = new(device) { Mode = DemoMode.PingPong, Paused = true };
        using ReadWriteTexture2D<Rgba32> target = device.AllocateReadWriteTexture2D<Rgba32>(width, height);

        Rgba32 Pixel(int px, int py)
        {
            demo.Render(target);
            return target.ToArray()[(py * width) + px];
        }

        // Empty field around the pointer to begin with (the simulation is paused, so it stays as painted).
        Assert.True(Pixel(x, y).R < 60 && Pixel(x + 12, y).R < 60, "Pick a place without a starting spot.");

        demo.Reaction.Paint(demo.Reaction.FrameToField(x + 0.5, y + 0.5, width, height));
        demo.Render(target);

        Rgba32[] start = target.ToArray();

        // The dab is 4 field pixels in radius, 8 on this frame: the pointer is on it, 12 pixels along is not.
        Assert.True(start[(y * width) + x].R > 200, "The dab should show as the brightest color.");
        Assert.True(start[(y * width) + x + 12].R < 60);

        // Twice as close around the pointer: the dab is still under it, and now reaches 12 pixels along.
        demo.ZoomAt(0.5f, x + 0.5, y + 0.5, width, height);
        Assert.Equal(0.5f, demo.Reaction.Zoom);
        Assert.NotEqual(default, demo.Reaction.Pan);
        Assert.True(Pixel(x, y).R > 200, "The dab left the pointer when zooming in.");
        Assert.True(Pixel(x + 12, y).R > 200, "The dab did not grow.");

        // Far out, where the frame shows four copies of the field across: still under the pointer.
        demo.ZoomAt(8f, x + 0.5, y + 0.5, width, height);
        Assert.Equal(4f, demo.Reaction.Zoom);
        Assert.True(Pixel(x, y).R > 200, "The dab left the pointer when zooming out.");

        // Back in around the same point, in small steps as a trackpad sends them: the starting view
        // again, without a seam line along the frame's edges (which a view left a hair off would get).
        for (int i = 0; i < 40; i++)
        {
            demo.ZoomAt(MathF.Pow(0.25f, 1f / 40f), x + 0.5, y + 0.5, width, height);
        }

        Assert.Equal(1f, demo.Reaction.Zoom, 1e-4f);
        demo.Reaction.Zoom = 1f;
        demo.Render(target);
        CpuComparison.AssertPixelsClose(start, target.ToArray(), maxStep: 2, outlierFraction: 0.002, "Back at the starting view");

        // The keys' zoom and the sampler explorer's view are separate: neither touched the other mode.
        Assert.Equal((SamplerExplorer.DefaultZoom, default(Float2)), (demo.Explorer.Zoom, demo.Explorer.Pan));
        demo.Mode = DemoMode.Samplers;
        demo.ZoomAt(0.5f, 10, 300, width, height);
        demo.ZoomBy(0.5f);
        Assert.Equal(SamplerExplorer.DefaultZoom / 4f, demo.Explorer.Zoom, 1e-5f);
        Assert.NotEqual(default, demo.Explorer.Pan);
        Assert.Equal(1f, demo.Reaction.Zoom);
        demo.Mode = DemoMode.PingPong;
        demo.ZoomBy(2f);
        Assert.Equal(2f, demo.Reaction.Zoom);
        Assert.Equal(SamplerExplorer.DefaultZoom / 4f, demo.Explorer.Zoom, 1e-5f);
    }

    // ---- headless ---------------------------------------------------------------------------------

    [MetalFact]
    public void Headless_WritesOnePngPerFormatAndMode_TheSameEveryRun()
    {
        string root = Path.Combine(Path.GetTempPath(), "sharpcompute-textures-" + Guid.NewGuid().ToString("N"));
        string first = Path.Combine(root, "a"), second = Path.Combine(root, "b");

        try
        {
            Assert.Equal(0, Program.Main(["--headless", "--out", first, "--size", "640x360"]));
            Assert.Equal(0, Program.RunHeadless(second, 640, 360));

            string[] expected = [.. Enumerable.Range(0, 8).Select(Program.HeadlessFileName), "pingpong.png"];

            Assert.Equal(expected.Order(), Directory.GetFiles(first).Select(Path.GetFileName).Order());
            Assert.Equal(
                ["samplers-rgba32.png", "samplers-rgba32srgb.png", "samplers-r8.png", "samplers-rg16.png", "samplers-half.png", "samplers-rgba16float.png", "samplers-float.png", "samplers-float4.png"],
                expected[..8]);

            foreach (string name in expected)
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(first, name));

                Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A], bytes[..8]);
                Assert.Equal(640, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)));
                Assert.Equal(360, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)));
                Assert.True(bytes.Length > 4000, $"{name} is only {bytes.Length} bytes: a blank frame compresses to almost nothing.");

                // Nothing depends on time or input.
                Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(second, name))), $"{name} differs between two runs.");
            }

            // The formats really are different pictures.
            Assert.Equal(9, expected.Select(name => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(first, name))))).Distinct().Count());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [MetalFact]
    public void Headless_Record_AlsoWritesEveryPingPongFrame_TheLastOneBeingThePicture()
    {
        string root = Path.Combine(Path.GetTempPath(), "sharpcompute-textures-record-" + Guid.NewGuid().ToString("N"));

        try
        {
            Assert.Equal(0, Program.Main(["--headless", "--out", root, "--size", "320x200", "--record"]));

            string[] frames = [.. Enumerable.Range(1, Program.HeadlessPingPongFrames).Select(static i => $"pingpong-{i:D4}.png")];
            string[] stills = [.. Enumerable.Range(0, 8).Select(Program.HeadlessFileName), "pingpong.png"];
            Assert.Equal(frames.Concat(stills).Order(), Directory.GetFiles(root).Select(Path.GetFileName).Order());

            // The simulation moves between frames, and its picture is its last frame.
            byte[] last = File.ReadAllBytes(Path.Combine(root, frames[^1]));
            Assert.True(last.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(root, "pingpong.png"))));
            Assert.False(last.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(root, frames[^2]))));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [MetalTheory]
    [InlineData(0, "--help")]
    [InlineData(1, "--headless")]
    [InlineData(1, "--bogus")]
    [InlineData(1, "--size", "12x12", "--headless")]
    [InlineData(1, "--format", "9")]
    [InlineData(1, "--mode", "other")]
    public void Main_RejectsBadArguments_WithoutOpeningAWindow(int exitCode, params string[] arguments)
    {
        Assert.Equal(exitCode, Program.Main(arguments));
    }

    private static float[] Flat(Float4[] values) => CpuComparison.Flatten(values);

    private static MouseWheelEventArgs Wheel(float deltaY, bool precise) => new(320, 240, 0f, deltaY, precise, isMomentum: false, KeyModifiers.None);

    /// <summary>The same place in the field: the field repeats, so a whole number of copies over is the same point.</summary>
    private static void AssertSameFieldPoint(Float2 expected, Float2 actual)
    {
        Float2 difference = actual - expected;

        Assert.Equal(MathF.Round(difference.X), difference.X, 2e-5f);
        Assert.Equal(MathF.Round(difference.Y), difference.Y, 2e-5f);
    }

    /// <summary>How far apart two field coordinates are along one axis, the short way around the repeating field (0..0.5).</summary>
    private static float FieldDistance(float a, float b) => MathF.Abs((a - b) - MathF.Round(a - b));
}
