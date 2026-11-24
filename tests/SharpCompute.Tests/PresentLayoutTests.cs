using SharpCompute.Presentation;

namespace SharpCompute.Tests;

/// <summary>The window-free geometry of <see cref="MetalWindow"/>: letterboxing, coordinate conversion and key mapping.</summary>
public class PresentLayoutTests
{
    [Theory]
    [InlineData(1280, 720, 1280, 720, 0, 0, 1280, 720)]     // same size
    [InlineData(1280, 720, 2560, 1440, 0, 0, 2560, 1440)]   // same aspect, Retina
    [InlineData(1280, 720, 1800, 1400, 0, 193, 1800, 1013)] // wider source: bars top and bottom (1800·720/1280 = 1012.5 → 1013)
    [InlineData(1280, 720, 1000, 1000, 0, 218, 1000, 563)]  // 562.5 → 563, centered with the odd pixel at the bottom
    [InlineData(720, 1280, 1000, 1000, 218, 0, 563, 1000)]  // taller source: bars left and right
    [InlineData(4, 2, 8, 8, 0, 2, 8, 4)]
    [InlineData(1, 1, 7, 3, 2, 0, 3, 3)]
    [InlineData(1000, 1, 10, 10, 0, 4, 10, 1)]              // never collapses below one pixel
    public void Fit_PreservesAspectAndCenters(int sw, int sh, int tw, int th, int x, int y, int w, int h)
    {
        Assert.Equal(new PixelRect(x, y, w, h), PresentLayout.Destination(sw, sh, tw, th, PresentScaling.Fit));
    }

    [Theory]
    [InlineData(1280, 720, 1800, 1400)]
    [InlineData(3, 7, 5, 2)]
    public void Stretch_FillsTheTarget(int sw, int sh, int tw, int th)
    {
        Assert.Equal(new PixelRect(0, 0, tw, th), PresentLayout.Destination(sw, sh, tw, th, PresentScaling.Stretch));
    }

    [Fact]
    public void Fit_StaysInsideTheTargetAndTouchesTwoEdges()
    {
        for (int sw = 1; sw <= 40; sw += 3)
        {
            for (int sh = 1; sh <= 40; sh += 5)
            {
                for (int tw = 1; tw <= 50; tw += 7)
                {
                    for (int th = 1; th <= 50; th += 4)
                    {
                        PixelRect r = PresentLayout.Destination(sw, sh, tw, th, PresentScaling.Fit);

                        Assert.True(r.X >= 0 && r.Y >= 0 && r.Width >= 1 && r.Height >= 1);
                        Assert.True(r.X + r.Width <= tw && r.Y + r.Height <= th);
                        Assert.True(r.Width == tw || r.Height == th, $"{sw}x{sh} in {tw}x{th} → {r}");

                        // Centered: the two bars differ by at most one pixel.
                        Assert.InRange(tw - r.Width - (2 * r.X), 0, 1);
                        Assert.InRange(th - r.Height - (2 * r.Y), 0, 1);

                        // Aspect within half a pixel of exact on the scaled axis. (Pick the axis from the
                        // aspect ratios: after rounding, a near-fit can touch all four edges.)
                        if ((long)sw * th >= (long)sh * tw)
                        {
                            Assert.True(r.Height == 1 || System.Math.Abs((r.Height * (double)sw / sh) - tw) <= (0.5 * sw / sh) + 1e-9);
                        }
                        else
                        {
                            Assert.True(r.Width == 1 || System.Math.Abs((r.Width * (double)sh / sw) - th) <= (0.5 * sh / sw) + 1e-9);
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(0, 0, 100, 100, 100, 100, true)]
    [InlineData(0, 0, 200, 100, 100, 100, true)]   // 2x horizontally
    [InlineData(0, 0, 300, 300, 100, 100, true)]   // 3x
    [InlineData(0, 0, 150, 150, 100, 100, false)]
    [InlineData(10, 0, 50, 100, 100, 100, false)]  // downscale
    public void PixelExact_OnlyForIntegerUpscales(int x, int y, int w, int h, int sw, int sh, bool expected)
    {
        Assert.Equal(expected, PresentLayout.IsPixelExact(new PixelRect(x, y, w, h), sw, sh));
    }

    [Fact]
    public void DrawableToFrame_InvertsTheLetterbox()
    {
        // 1280×720 frame shown in a 1800×1400 drawable: rect (0, 193, 1800, 1013).
        PixelRect rect = PresentLayout.Destination(1280, 720, 1800, 1400, PresentScaling.Fit);

        Assert.True(PresentLayout.DrawableToFrame(0, 193, rect, 1280, 720, out double fx, out double fy));
        Assert.Equal(0, fx, 9);
        Assert.Equal(0, fy, 9);

        Assert.True(PresentLayout.DrawableToFrame(900, 193 + 506.5, rect, 1280, 720, out fx, out fy));
        Assert.Equal(640, fx, 9);
        Assert.Equal(360, fy, 6);

        // In the top bar and below the frame: outside, but still converted for callers that clamp.
        Assert.False(PresentLayout.DrawableToFrame(900, 100, rect, 1280, 720, out _, out fy));
        Assert.True(fy < 0);
        Assert.False(PresentLayout.DrawableToFrame(900, 193 + 1013, rect, 1280, 720, out _, out fy));
        Assert.Equal(720, fy, 9);
        Assert.False(PresentLayout.DrawableToFrame(1800, 500, rect, 1280, 720, out fx, out _));
        Assert.Equal(1280, fx, 9);
    }

    [Theory]
    // AppKit view points (bottom-left origin) → drawable pixels (top-left origin) at 2x.
    [InlineData(0, 720, 1280, 720, 2560, 1440, 0, 0)]       // top-left corner
    [InlineData(0, 0, 1280, 720, 2560, 1440, 0, 1440)]      // bottom-left corner
    [InlineData(640, 360, 1280, 720, 2560, 1440, 1280, 720)] // center
    [InlineData(10.25, 700.5, 1280, 720, 2560, 1440, 20.5, 39)]
    [InlineData(100, 50, 200, 100, 200, 100, 100, 50)]       // 1x
    [InlineData(-5, 110, 200, 100, 400, 200, -10, -20)]       // drag outside the view
    public void ViewToDrawable_FlipsAndScales(double vx, double vy, double vw, double vh, int pw, int ph, double ex, double ey)
    {
        (double x, double y) = PresentLayout.ViewToDrawable(vx, vy, vw, vh, pw, ph);
        Assert.Equal(ex, x, 9);
        Assert.Equal(ey, y, 9);
    }

    [Fact]
    public void ViewToDrawable_EmptyViewMapsToOrigin()
    {
        Assert.Equal((0d, 0d), PresentLayout.ViewToDrawable(5, 5, 0, 0, 100, 100));
    }

    [Theory]
    [InlineData(1280, 720, 2, 2560, 1440)]
    [InlineData(1280, 720, 1, 1280, 720)]
    [InlineData(640.3, 360.7, 2, 1281, 721)]
    [InlineData(0, 0, 2, 1, 1)]
    [InlineData(100000, 10, 2, 16384, 20)]
    public void DrawableSize_RoundsAndClamps(double vw, double vh, double scale, int ew, int eh)
    {
        Assert.Equal((ew, eh), PresentLayout.DrawableSize(vw, vh, scale));
    }

    [Theory]
    [InlineData(0x7B, '', Key.Left)]
    [InlineData(0x7C, '', Key.Right)]
    [InlineData(0x7E, '', Key.Up)]
    [InlineData(0x7D, '', Key.Down)]
    [InlineData(0x35, '\u001B', Key.Escape)]
    [InlineData(0x31, ' ', Key.Space)]
    [InlineData(0x24, '\r', Key.Enter)]
    [InlineData(0x01, 's', Key.S)]   // the S key on QWERTY
    [InlineData(0x0C, 'a', Key.A)]   // the key in QWERTY's Q position produces 'a' on AZERTY → A
    [InlineData(0x03, 'F', Key.F)]   // Shift+F
    [InlineData(0x1D, '0', Key.D0)]
    [InlineData(0x19, '9', Key.D9)]
    [InlineData(0x2B, ',', Key.Unknown)]
    public void KeyMap_UsesCodesForNamedKeysAndCharactersForLetters(int keyCode, char character, Key expected)
    {
        Assert.Equal(expected, KeyMap.Resolve((ushort)keyCode, character));
    }

    [Theory]
    [InlineData(null, '\0')]
    [InlineData("", '\0')]
    [InlineData("", '\0')]  // NSLeftArrowFunctionKey
    [InlineData("a", 'a')]
    [InlineData("é", 'é')]
    public void KeyMap_ReportsPrintableCharactersOnly(string? characters, char expected)
    {
        Assert.Equal(expected, KeyMap.ToCharacter(characters));
    }
}
