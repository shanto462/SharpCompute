using System;
using System.Numerics;

namespace SharpCompute.Game.Tests;

/// <summary>Camera follow, deadzone, world clamping, pixel snapping and shake.</summary>
public sealed class Camera2DTests
{
    private const float Dt = 1f / 120f;

    [Fact]
    public void Reset_PutsTheFocusOnTheAnchor()
    {
        Camera2D camera = new(320, 180);
        camera.Reset(new Vector2(1000, 500));
        Assert.Equal(new Vector2(840, 410), camera.Position);

        camera.Anchor = new Vector2(0.25f, 1f);
        camera.Reset(new Vector2(1000, 500));
        Assert.Equal(new Vector2(920, 320), camera.Position);
    }

    [Fact]
    public void Follow_EasesTowardsTheFocus_AtTheFollowRate()
    {
        Camera2D camera = new(320, 180) { FollowRate = 6f };
        camera.Reset(Vector2.Zero);
        Vector2 start = camera.Position;
        Vector2 focus = new(400, 0);

        camera.Follow(focus, Dt);
        float expected = 400f * (1f - MathF.Exp(-Dt * 6f));
        Assert.Equal(start.X + expected, camera.Position.X, 4);
        Assert.Equal(start.Y, camera.Position.Y);

        // After one time constant (1/6 s) about 63 % of the way; eventually all of it.
        for (int i = 1; i < 20; i++)
        {
            camera.Follow(focus, Dt);
        }

        Assert.Equal(start.X + (400f * (1f - MathF.Exp(-1f))), camera.Position.X, 0.5f);

        for (int i = 0; i < 2000; i++)
        {
            camera.Follow(focus, Dt);
        }

        Assert.Equal(start.X + 400f, camera.Position.X, 0.01f);
    }

    [Fact]
    public void Follow_IsFrameRateIndependent()
    {
        Camera2D fine = new(320, 180), coarse = new(320, 180);
        Vector2 focus = new(300, 200);

        for (int i = 0; i < 120; i++)
        {
            fine.Follow(focus, Dt);
        }

        for (int i = 0; i < 30; i++)
        {
            coarse.Follow(focus, 4 * Dt);
        }

        Assert.Equal(fine.Position.X, coarse.Position.X, 0.01f);
        Assert.Equal(fine.Position.Y, coarse.Position.Y, 0.01f);
    }

    [Fact]
    public void Follow_WithAnInfiniteRate_IsRigid_AndWithNoTime_DoesNotMove()
    {
        Camera2D camera = new(100, 100) { FollowRate = float.PositiveInfinity };
        camera.Follow(new Vector2(500, 500), Dt);
        Assert.Equal(new Vector2(450, 450), camera.Position);

        camera.Follow(new Vector2(900, 900), 0f);
        Assert.Equal(new Vector2(450, 450), camera.Position);
        camera.Follow(new Vector2(900, 900), -1f);
        Assert.Equal(new Vector2(450, 450), camera.Position);
    }

    [Fact]
    public void Deadzone_LetsTheFocusMoveFreelyInside_AndFollowsOnlyTheExcess()
    {
        Camera2D camera = new(200, 100) { FollowRate = float.PositiveInfinity, Deadzone = new Vector2(30, 10) };
        camera.Reset(new Vector2(500, 500));
        Vector2 rest = camera.Position;

        // Inside the box on both axes: no movement at all.
        camera.Follow(new Vector2(525, 492), Dt);
        Assert.Equal(rest, camera.Position);

        // 50 to the right: the camera moves the 20 that exceed the 30-pixel half-width.
        camera.Follow(new Vector2(550, 500), Dt);
        Assert.Equal(rest + new Vector2(20, 0), camera.Position);

        // Back to the left edge of the box, and above it by 25: 15 up.
        camera.Follow(new Vector2(490, 475), Dt);
        Assert.Equal(rest + new Vector2(20, -15), camera.Position);

        // Reset ignores the deadzone.
        camera.Reset(new Vector2(510, 500));
        Assert.Equal(rest + new Vector2(10, 0), camera.Position);
    }

    [Fact]
    public void Bounds_KeepTheViewInsideTheWorld()
    {
        Camera2D camera = new(320, 180);
        camera.SetBounds(0, 0, 1000, 600);

        camera.Reset(new Vector2(-500, -500));
        Assert.Equal(Vector2.Zero, camera.Position);

        camera.Reset(new Vector2(5000, 5000));
        Assert.Equal(new Vector2(680, 420), camera.Position);

        // Easing never overshoots the edge either.
        for (int i = 0; i < 600; i++)
        {
            camera.Follow(new Vector2(-9999, 9999), Dt);
            Assert.InRange(camera.Position.X, 0f, 680f);
            Assert.InRange(camera.Position.Y, 0f, 420f);
        }

        Assert.Equal(new Vector2(0, 420), camera.Position);

        camera.ClearBounds();
        camera.Reset(new Vector2(-500, -500));
        Assert.Equal(new Vector2(-660, -590), camera.Position);
    }

    [Fact]
    public void Bounds_SmallerThanTheView_CenterTheWorld()
    {
        Camera2D camera = new(320, 180);
        camera.SetBounds(100, 50, 300, 500);      // 200 wide (narrower than the view), 450 tall

        camera.Reset(new Vector2(250, 60));
        Assert.Equal(40f, camera.Position.X);     // 100 + (200 - 320) / 2
        Assert.Equal(50f, camera.Position.Y);     // clamped to the top

        camera.Follow(new Vector2(-4000, 9000), 10f);
        Assert.Equal(40f, camera.Position.X);
        Assert.Equal(320f, camera.Position.Y, 0.01f);    // 500 - 180
    }

    [Fact]
    public void SetBounds_RejectsInvertedOrNaNBounds()
    {
        Camera2D camera = new(320, 180);
        Assert.Throws<ArgumentException>(() => camera.SetBounds(10, 0, 5, 100));
        Assert.Throws<ArgumentException>(() => camera.SetBounds(0, 10, 100, 5));
        Assert.Throws<ArgumentException>(() => camera.SetBounds(0, 0, float.NaN, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Camera2D(0, 180));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Camera2D(320, -1));
    }

    [Theory]
    [InlineData(10.4f, 20.6f, 10, 21)]
    [InlineData(-0.4f, -0.6f, 0, -1)]
    [InlineData(99.5f, 100.5f, 100, 100)]      // ties round to even, as MathF.Round does
    public void Snapped_RoundsToWholePixels(float x, float y, int snappedX, int snappedY)
    {
        Camera2D camera = new(100, 100) { Anchor = Vector2.Zero };
        camera.Reset(new Vector2(x, y));
        Assert.Equal((snappedX, snappedY), camera.Snapped);
    }

    [Fact]
    public void WorldAndScreen_ConvertThroughTheSnappedPosition()
    {
        Camera2D camera = new(100, 100) { Anchor = Vector2.Zero };
        camera.Reset(new Vector2(40.4f, 9.7f));

        Assert.Equal(new Vector2(10, 20), camera.WorldToScreen(new Vector2(50, 30)));
        Assert.Equal(new Vector2(50, 30), camera.ScreenToWorld(new Vector2(10, 20)));
    }

    [Fact]
    public void Shake_OffsetsTheSnappedPosition_WithinItsAmplitude_AndDecays()
    {
        Camera2D camera = new(100, 100) { Anchor = Vector2.Zero };
        camera.Reset(new Vector2(200, 200));
        camera.Shake.Add(6f);
        camera.Shake.Add(2f);                 // a weaker impact does not calm it
        Assert.Equal(6f, camera.Shake.Amplitude);

        bool moved = false;
        float previous = 6f;

        for (int i = 0; i < 40; i++)
        {
            camera.Shake.Update(1f / 60f);
            float amplitude = camera.Shake.Amplitude;
            Assert.True(amplitude < previous);
            previous = amplitude;

            Vector2 offset = camera.Shake.Offset;
            Assert.InRange(offset.X, -amplitude, amplitude);
            Assert.InRange(offset.Y, -amplitude, amplitude);
            (int x, int y) = camera.Snapped;
            Assert.InRange(x, 200 - 6, 200 + 6);
            Assert.InRange(y, 200 - 6, 200 + 6);
            moved |= x != 200 || y != 200;
        }

        Assert.True(moved);
        Assert.Equal(6f * MathF.Exp(-8f * 40f / 60f), camera.Shake.Amplitude, 3);
        Assert.Equal(new Vector2(200, 200), camera.Position);     // the shake never moves the camera itself

        camera.Shake.Reset();
        Assert.Equal(Vector2.Zero, camera.Shake.Offset);
        Assert.Equal((200, 200), camera.Snapped);
    }

    [Fact]
    public void Shake_IsDeterministic()
    {
        ScreenShake a = new(), b = new();
        a.Add(5f);
        b.Add(5f);

        for (int i = 0; i < 20; i++)
        {
            a.Update(0.01f);
            b.Update(0.01f);
            Assert.Equal(a.Offset, b.Offset);
        }

        Assert.NotEqual(Vector2.Zero, a.Offset);
    }
}
