using SharpCompute.Presentation;

namespace SharpCompute.Tests;

/// <summary>
/// The window-free half of <see cref="MetalWindow.MouseWheel"/>: turning the raw values of an AppKit
/// scroll-wheel event into <see cref="MouseWheelEventArgs"/>. The other half, reading those values
/// from a real event, is the opt-in window scenario in <c>MetalWindowTests</c>.
/// </summary>
public class MouseWheelTests
{
    // NSEventModifierFlags.
    private const nuint CapsLock = 1 << 16;
    private const nuint Shift = 1 << 17;
    private const nuint Control = 1 << 18;
    private const nuint Option = 1 << 19;
    private const nuint Command = 1 << 20;
    private const nuint NumericPad = 1 << 21;
    private const nuint Function = 1 << 23;

    // A 320 × 200 point view on a Retina display, and the same view at 1x.
    private const double ViewWidth = 320;
    private const double ViewHeight = 200;

    [Fact]
    public void EventArgs_CarryWhatTheyWereGiven()
    {
        MouseWheelEventArgs e = new(12.5, 7.25, -1.5f, 3f, isPrecise: true, isMomentum: true, KeyModifiers.Shift | KeyModifiers.Command);

        Assert.Equal(12.5, e.X);
        Assert.Equal(7.25, e.Y);
        Assert.Equal(-1.5f, e.DeltaX);
        Assert.Equal(3f, e.DeltaY);
        Assert.True(e.IsPrecise);
        Assert.True(e.IsMomentum);
        Assert.Equal(KeyModifiers.Shift | KeyModifiers.Command, e.Modifiers);
    }

    [Fact]
    public void Position_OnRetina_IsDrawablePixelsFromTheTopLeft()
    {
        // 100 points from the left, 50 points above the bottom edge.
        MouseWheelEventArgs e = Translate(100, 50, scale: 2, deltaY: 1)!;

        Assert.Equal(200, e.X);
        Assert.Equal(300, e.Y);
    }

    [Fact]
    public void Position_At1x_OnlyFlipsY()
    {
        MouseWheelEventArgs e = Translate(100.5, 50.25, scale: 1, deltaY: 1)!;

        Assert.Equal(100.5, e.X);
        Assert.Equal(149.75, e.Y);
    }

    [Theory]
    [InlineData(1, 4, -10, 4f, -10f)]
    [InlineData(2, 4, -10, 8f, -20f)]
    [InlineData(2, 0.5, 0, 1f, 0f)]
    [InlineData(3, -1.5, 2.25, -4.5f, 6.75f)]
    public void PreciseDeltas_ScaleToDrawablePixels_AndKeepTheirSigns(double scale, double deltaX, double deltaY, float expectedX, float expectedY)
    {
        MouseWheelEventArgs e = Translate(100, 50, scale, deltaX, deltaY, isPrecise: true)!;

        Assert.True(e.IsPrecise);
        Assert.Equal(expectedX, e.DeltaX);
        Assert.Equal(expectedY, e.DeltaY);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LineDeltas_PassThroughUnscaled(double scale)
    {
        // A single slow notch of a wheel reports a fraction of a line.
        MouseWheelEventArgs e = Translate(100, 50, scale, deltaX: -3, deltaY: 0.1, isPrecise: false)!;

        Assert.False(e.IsPrecise);
        Assert.Equal(-3f, e.DeltaX);
        Assert.Equal(0.1f, e.DeltaY);
    }

    [Fact]
    public void PreciseDeltas_UseEachAxisOwnScale_WhenTheDrawableWasRounded()
    {
        // A 100.4 × 50.4 point view at 2x has a 201 × 101 pixel drawable.
        MouseWheelEventArgs e = MetalWindow.TranslateWheel(10, 10, 100.4, 50.4, 201, 101, 10, 10, isPrecise: true, isMomentum: false, 0)!;

        Assert.Equal((float)(10 * 201 / 100.4), e.DeltaX);
        Assert.Equal((float)(10 * 101 / 50.4), e.DeltaY);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Momentum_IsReportedAsGiven(bool isMomentum)
    {
        MouseWheelEventArgs e = Translate(100, 50, scale: 2, deltaY: 1, isPrecise: true, isMomentum: isMomentum)!;

        Assert.Equal(isMomentum, e.IsMomentum);
    }

    [Theory]
    [InlineData(0u, KeyModifiers.None)]
    [InlineData(1u << 17, KeyModifiers.Shift)]
    [InlineData(1u << 18, KeyModifiers.Control)]
    [InlineData(1u << 19, KeyModifiers.Option)]
    [InlineData(1u << 20, KeyModifiers.Command)]
    [InlineData((1u << 17) | (1u << 20), KeyModifiers.Shift | KeyModifiers.Command)]
    [InlineData((1u << 17) | (1u << 18) | (1u << 19) | (1u << 20), KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Option | KeyModifiers.Command)]
    public void Modifiers_ComeFromTheEventFlags(uint flags, KeyModifiers expected)
    {
        Assert.Equal(expected, Translate(100, 50, scale: 2, deltaY: 1, flags: flags)!.Modifiers);
    }

    [Fact]
    public void Modifiers_IgnoreFlagsThatAreNotModifierKeys()
    {
        // Caps Lock, the numeric-pad and Fn flags, and the device-dependent low bits AppKit always sets.
        nuint noise = CapsLock | NumericPad | Function | 0x100;

        Assert.Equal(KeyModifiers.None, Translate(100, 50, scale: 2, deltaY: 1, flags: noise)!.Modifiers);
        Assert.Equal(KeyModifiers.Option, Translate(100, 50, scale: 2, deltaY: 1, flags: noise | Option)!.Modifiers);
        Assert.Equal(
            KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Command,
            Translate(100, 50, scale: 2, deltaY: 1, flags: noise | Shift | Control | Command)!.Modifiers);
    }

    [Theory]
    [InlineData(0, 200, true)]          // top-left pixel
    [InlineData(319.5, 0.25, true)]     // bottom-right pixel
    [InlineData(160, 100, true)]
    [InlineData(100, 210, false)]       // the title bar, above the content
    [InlineData(100, 200.01, false)]
    [InlineData(100, 0, false)]         // the bottom edge is one past the last row
    [InlineData(100, -5, false)]
    [InlineData(-0.01, 50, false)]
    [InlineData(320, 50, false)]        // the right edge is one past the last column
    [InlineData(400, 50, false)]
    [InlineData(double.NaN, 50, false)]
    [InlineData(100, double.PositiveInfinity, false)]
    public void OnlyThePointerOverTheContentRaisesAnEvent(double viewX, double viewY, bool raised)
    {
        foreach (double scale in new[] { 1.0, 2.0 })
        {
            MouseWheelEventArgs? e = Translate(viewX, viewY, scale, deltaY: 1);

            Assert.Equal(raised, e is not null);

            if (e is not null)
            {
                Assert.InRange(e.X, 0, (ViewWidth * scale) - double.Epsilon);
                Assert.InRange(e.Y, 0, (ViewHeight * scale) - double.Epsilon);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnEventWithoutMovementIsNotRaised(bool isPrecise)
    {
        // The events that mark the start and the end of a trackpad gesture have zero deltas.
        Assert.Null(Translate(100, 50, scale: 2, deltaX: 0, deltaY: 0, isPrecise: isPrecise));
        Assert.Null(Translate(100, 50, scale: 2, deltaX: 0, deltaY: -0.0, isPrecise: isPrecise));

        // One axis is enough.
        Assert.NotNull(Translate(100, 50, scale: 2, deltaX: 0.25, deltaY: 0, isPrecise: isPrecise));
        Assert.NotNull(Translate(100, 50, scale: 2, deltaX: 0, deltaY: -0.25, isPrecise: isPrecise));
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(320, 0)]
    [InlineData(-1, 200)]
    public void ADegenerateViewRaisesNothing(double viewWidth, double viewHeight)
    {
        Assert.Null(MetalWindow.TranslateWheel(0, 0, viewWidth, viewHeight, 1, 1, 1, 1, isPrecise: true, isMomentum: false, 0));
    }

    private static MouseWheelEventArgs? Translate(
        double viewX, double viewY, double scale, double deltaX = 0, double deltaY = 0, bool isPrecise = false, bool isMomentum = false, nuint flags = 0) =>
        MetalWindow.TranslateWheel(
            viewX, viewY, ViewWidth, ViewHeight, (int)(ViewWidth * scale), (int)(ViewHeight * scale), deltaX, deltaY, isPrecise, isMomentum, flags);
}
