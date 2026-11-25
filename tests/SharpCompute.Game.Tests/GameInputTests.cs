using System;
using System.Numerics;
using SharpCompute.Presentation;

namespace SharpCompute.Game.Tests;

/// <summary>Per-step key and mouse state, its pressed and released edges, and action mapping.</summary>
public sealed class GameInputTests
{
    [Fact]
    public void NothingIsDown_BeforeAnyEvent()
    {
        GameInput input = new();
        input.BeginStep();

        Assert.False(input.IsDown(Key.Space));
        Assert.False(input.WasPressed(Key.Space));
        Assert.False(input.WasReleased(Key.Space));
        Assert.False(input.IsDown(MouseButton.Left));
        Assert.False(input.HasMousePosition);
        Assert.Equal(Vector2.Zero, input.MousePosition);
    }

    [Fact]
    public void AHeldKey_IsPressedOnce_ThenDown_ThenReleasedOnce()
    {
        GameInput input = new();

        input.Press(Key.Z);
        Assert.False(input.IsDown(Key.Z));       // not until the step begins

        input.BeginStep();
        Assert.True(input.IsDown(Key.Z));
        Assert.True(input.WasPressed(Key.Z));
        Assert.False(input.WasReleased(Key.Z));

        input.BeginStep();
        Assert.True(input.IsDown(Key.Z));
        Assert.False(input.WasPressed(Key.Z));

        input.Release(Key.Z);
        Assert.True(input.IsDown(Key.Z));        // the step's snapshot does not change mid-step

        input.BeginStep();
        Assert.False(input.IsDown(Key.Z));
        Assert.True(input.WasReleased(Key.Z));

        input.BeginStep();
        Assert.False(input.WasReleased(Key.Z));
    }

    [Fact]
    public void ATapBetweenTwoSteps_IsNotLost()
    {
        GameInput input = new();
        input.BeginStep();

        input.Press(Key.Space);
        input.Release(Key.Space);

        input.BeginStep();
        Assert.True(input.IsDown(Key.Space));
        Assert.True(input.WasPressed(Key.Space));
        Assert.False(input.WasReleased(Key.Space));

        input.BeginStep();
        Assert.False(input.IsDown(Key.Space));
        Assert.False(input.WasPressed(Key.Space));
        Assert.True(input.WasReleased(Key.Space));
    }

    [Fact]
    public void AReleaseAndRepress_BetweenTwoSteps_IsASecondPress()
    {
        GameInput input = new();
        input.Press(Key.X);
        input.BeginStep();

        input.Release(Key.X);
        input.Press(Key.X);
        input.BeginStep();

        Assert.True(input.IsDown(Key.X));
        Assert.True(input.WasPressed(Key.X));
        Assert.False(input.WasReleased(Key.X));
    }

    [Fact]
    public void DiscardPresses_ForgetsTaps_ButKeepsWhatIsHeld()
    {
        GameInput input = new();
        input.Press(Key.Space);
        input.Release(Key.Space);
        input.Press(Key.A);
        input.DiscardPresses();
        input.BeginStep();

        Assert.False(input.IsDown(Key.Space));
        Assert.False(input.WasPressed(Key.Space));
        Assert.True(input.IsDown(Key.A));
        Assert.True(input.WasPressed(Key.A));     // it went from up to down as far as the steps can tell
    }

    [Fact]
    public void ReleaseAll_ReleasesEverything()
    {
        GameInput input = new();
        input.Press(Key.A);
        input.Press(MouseButton.Right);
        input.BeginStep();
        input.Press(Key.B);
        input.ReleaseAll();
        input.BeginStep();

        Assert.False(input.IsDown(Key.A));
        Assert.True(input.WasReleased(Key.A));
        Assert.False(input.IsDown(Key.B));
        Assert.False(input.IsDown(MouseButton.Right));
        Assert.True(input.WasReleased(MouseButton.Right));
    }

    [Fact]
    public void UnknownKeysAndButtons_AreIgnored()
    {
        GameInput input = new();
        input.Press(Key.Unknown);
        input.Press((Key)9999);
        input.Press((Key)(-3));
        input.Press(MouseButton.None);
        input.Press((MouseButton)17);
        input.Release((Key)9999);
        input.Release(MouseButton.None);
        input.BeginStep();

        Assert.False(input.IsDown(Key.Unknown));
        Assert.False(input.WasPressed((Key)9999));
        Assert.False(input.WasReleased((Key)(-3)));
        Assert.False(input.IsDown(MouseButton.None));
        Assert.False(input.WasPressed((MouseButton)17));
    }

    [Fact]
    public void EveryKeyOfTheEnum_IsTracked()
    {
        GameInput input = new();

        foreach (Key key in Enum.GetValues<Key>())
        {
            input.Press(key);
        }

        input.BeginStep();

        foreach (Key key in Enum.GetValues<Key>())
        {
            Assert.Equal(key != Key.Unknown, input.IsDown(key));
        }
    }

    [Fact]
    public void MouseButtons_HaveEdges_LikeKeys()
    {
        GameInput input = new();
        input.Press(MouseButton.Left);
        input.BeginStep();
        Assert.True(input.IsDown(MouseButton.Left));
        Assert.True(input.WasPressed(MouseButton.Left));
        Assert.False(input.IsDown(MouseButton.Right));

        input.Release(MouseButton.Left);
        input.BeginStep();
        Assert.True(input.WasReleased(MouseButton.Left));

        // A click between two steps.
        input.Press(MouseButton.Middle);
        input.Release(MouseButton.Middle);
        input.BeginStep();
        Assert.True(input.WasPressed(MouseButton.Middle));
    }

    [Fact]
    public void MousePosition_IsSampledPerStep_WithADelta()
    {
        GameInput input = new();
        input.MoveMouse(100, 50);
        Assert.False(input.HasMousePosition);

        input.BeginStep();
        Assert.True(input.HasMousePosition);
        Assert.Equal(new Vector2(100, 50), input.MousePosition);
        Assert.Equal(Vector2.Zero, input.MouseDelta);    // the first position is not a movement

        input.MoveMouse(104, 50);
        input.MoveMouse(110, 45);
        Assert.Equal(new Vector2(100, 50), input.MousePosition);

        input.BeginStep();
        Assert.Equal(new Vector2(110, 45), input.MousePosition);
        Assert.Equal(new Vector2(10, -5), input.MouseDelta);

        input.BeginStep();
        Assert.Equal(Vector2.Zero, input.MouseDelta);
    }

    [Fact]
    public void AnAction_IsDown_WhileAnyOfItsBindingsIs()
    {
        GameInput input = new();
        ActionMap actions = input.Actions;
        actions.Bind("jump", Key.Space, Key.Up);
        actions.Bind("fire", MouseButton.Left);
        actions.Bind("fire", Key.X);

        input.BeginStep();
        Assert.False(actions.IsDown("jump"));
        Assert.False(actions.WasPressed("jump"));

        input.Press(Key.Up);
        input.BeginStep();
        Assert.True(actions.IsDown("jump"));
        Assert.True(actions.WasPressed("jump"));
        Assert.False(actions.IsDown("fire"));

        // A second binding goes down, the first comes up: still down, not released.
        input.Press(Key.Space);
        input.Release(Key.Up);
        input.BeginStep();
        Assert.True(actions.IsDown("jump"));
        Assert.True(actions.WasPressed("jump"));
        Assert.False(actions.WasReleased("jump"));

        input.Release(Key.Space);
        input.BeginStep();
        Assert.False(actions.IsDown("jump"));
        Assert.True(actions.WasReleased("jump"));

        input.Press(MouseButton.Left);
        input.BeginStep();
        Assert.True(actions.WasPressed("fire"));
        input.Release(MouseButton.Left);
        input.Press(Key.X);
        input.BeginStep();
        Assert.True(actions.IsDown("fire"));
        Assert.False(actions.WasReleased("fire"));
    }

    [Fact]
    public void Bind_Accumulates_AndIgnoresDuplicates_AndUnbindRemoves()
    {
        GameInput input = new();
        ActionMap actions = input.Actions;
        actions.Bind("left", Key.A);
        actions.Bind("left", Key.A, Key.Left);

        Assert.True(actions.IsBound("left"));
        Assert.False(actions.IsBound("Left"));          // names are case-sensitive
        Assert.Equal(["left"], actions.Names);

        input.Press(Key.Left);
        input.BeginStep();
        Assert.True(actions.IsDown("left"));

        Assert.True(actions.Unbind("left"));
        Assert.False(actions.Unbind("left"));
        Assert.False(actions.IsBound("left"));
        Assert.Empty(actions.Names);
    }

    [Fact]
    public void AskingForAnUnboundAction_Throws_NamingTheBoundOnes()
    {
        ActionMap actions = new GameInput().Actions;
        actions.Bind("jump", Key.Space);

        ArgumentException e = Assert.Throws<ArgumentException>(() => actions.IsDown("jmup"));
        Assert.Contains("jmup", e.Message, StringComparison.Ordinal);
        Assert.Contains("jump", e.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => actions.WasPressed("fire"));
        Assert.Throws<ArgumentException>(() => actions.WasReleased("fire"));
        Assert.Throws<ArgumentNullException>(() => actions.IsDown(null!));
    }

    [Fact]
    public void Bind_RejectsEmptyNames_AndUnbindableInputs()
    {
        ActionMap actions = new GameInput().Actions;

        Assert.Throws<ArgumentException>(() => actions.Bind("", Key.A));
        Assert.Throws<ArgumentNullException>(() => actions.Bind(null!, Key.A));
        Assert.Throws<ArgumentException>(() => actions.Bind("x", Key.Unknown));
        Assert.Throws<ArgumentException>(() => actions.Bind("x", MouseButton.None));
        Assert.Throws<ArgumentNullException>(() => new GameInput(null!));
    }
}
