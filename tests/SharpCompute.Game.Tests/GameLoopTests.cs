using System;
using System.Collections.Generic;
using SharpCompute.Presentation;

namespace SharpCompute.Game.Tests;

/// <summary>The fixed-timestep accumulator and the loop that drives steps and frames from it.</summary>
public sealed class GameLoopTests
{
    private const double Step = 1.0 / 120;

    [Fact]
    public void Advance_ReturnsWholeSteps_AndKeepsTheRemainder()
    {
        FixedTimestep timestep = new(0.25);

        Assert.Equal(0, timestep.Advance(0.2));
        Assert.Equal(0.8, timestep.Alpha, 9);

        // 0.2 + 0.1 = 0.3: one step, 0.05 left over.
        Assert.Equal(1, timestep.Advance(0.1));
        Assert.Equal(0.05, timestep.Accumulator, 9);
        Assert.Equal(0.2, timestep.Alpha, 9);
        Assert.Equal(1, timestep.TotalSteps);
    }

    [Fact]
    public void Advance_OverManyUnevenFrames_SimulatesExactlyTheElapsedTime()
    {
        FixedTimestep timestep = new(Step);
        double[] frames = [1.0 / 60, 1.0 / 144, 0.031, 0.0004, 1.0 / 60, 0.05, 0.0083];
        double elapsed = 0;
        long steps = 0;

        for (int i = 0; i < 700; i++)
        {
            double dt = frames[i % frames.Length];
            elapsed += dt;
            steps += timestep.Advance(dt);
            Assert.InRange(timestep.Alpha, 0.0, 1.0);
        }

        // Never ahead of real time, and never a whole step behind it.
        Assert.Equal(steps, timestep.TotalSteps);
        Assert.InRange(elapsed - (steps * Step), -1e-9, Step + 1e-9);
    }

    [Fact]
    public void Advance_ClampsALongFrame_ToTheFrameLimit()
    {
        FixedTimestep timestep = new(0.01, maxFrameSeconds: 0.05, maxStepsPerFrame: 100);

        // Five seconds at a breakpoint count as 0.05 s: five steps (give or take rounding), not five hundred.
        Assert.InRange(timestep.Advance(5.0), 4, 5);
        Assert.True(timestep.Accumulator < 0.01 + 1e-12);
    }

    [Fact]
    public void Advance_StopsAtTheStepLimit_AndCatchesUpOverLaterFrames()
    {
        FixedTimestep timestep = new(0.01, maxFrameSeconds: 1.0, maxStepsPerFrame: 4);

        Assert.Equal(4, timestep.Advance(0.095));
        Assert.Equal(0.055, timestep.Accumulator, 9);
        Assert.Equal(1.0, timestep.Alpha);          // behind: clamped, never above 1

        Assert.Equal(4, timestep.Advance(0));
        Assert.Equal(1, timestep.Advance(0));
        Assert.Equal(0, timestep.Advance(0));
        Assert.Equal(9, timestep.TotalSteps);
        Assert.Equal(0.5, timestep.Alpha, 6);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0.0)]
    public void Advance_TreatsNonPositiveAndNaNTime_AsZero(double delta)
    {
        FixedTimestep timestep = new(0.01);
        timestep.Advance(0.005);

        Assert.Equal(0, timestep.Advance(delta));
        Assert.Equal(0.005, timestep.Accumulator, 12);
    }

    [Fact]
    public void Advance_WithInfiniteTime_IsClampedLikeAnyLongFrame()
    {
        FixedTimestep timestep = new(0.01, maxFrameSeconds: 0.03);
        Assert.InRange(timestep.Advance(double.PositiveInfinity), 2, 3);
        Assert.True(double.IsFinite(timestep.Accumulator));
    }

    [Fact]
    public void Paused_RunsNoSteps_AndDoesNotReplayThePauseOnResume()
    {
        FixedTimestep timestep = new(0.01);
        timestep.Advance(0.005);
        timestep.Paused = true;

        Assert.Equal(0, timestep.Advance(10.0));
        Assert.Equal(0, timestep.Accumulator);

        timestep.Paused = false;
        Assert.Equal(1, timestep.Advance(0.011));
        Assert.Equal(1, timestep.TotalSteps);
    }

    [Fact]
    public void Reset_DropsTheAccumulatedTimeAndTheCount()
    {
        FixedTimestep timestep = new(0.01);
        timestep.Advance(0.035);
        timestep.Reset();

        Assert.Equal(0, timestep.Accumulator);
        Assert.Equal(0, timestep.TotalSteps);
        Assert.Equal(0, timestep.Alpha);
    }

    [Theory]
    [InlineData(0.0, 0.1, 12)]
    [InlineData(-0.01, 0.1, 12)]
    [InlineData(double.NaN, 0.1, 12)]
    [InlineData(double.PositiveInfinity, 0.1, 12)]
    [InlineData(0.01, 0.0, 12)]
    [InlineData(0.01, double.NaN, 12)]
    [InlineData(0.01, 0.1, 0)]
    public void Constructor_RejectsNonPositiveArguments(double step, double maxFrame, int maxSteps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedTimestep(step, maxFrame, maxSteps));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameLoop(step, maxFrame, maxSteps));
    }

    [Fact]
    public void Tick_RunsTheDueSteps_ThenRendersOnce_WithTheFrameItSimulated()
    {
        GameLoop loop = new(0.01);
        List<string> calls = [];
        GameFrame seen = default;

        GameFrame returned = loop.Tick(0.025, () => calls.Add("step"), frame =>
        {
            calls.Add("render");
            seen = frame;
        });

        Assert.Equal(["step", "step", "render"], calls);
        Assert.Equal(returned, seen);
        Assert.Equal(0, seen.Index);
        Assert.Equal(2, seen.Steps);
        Assert.Equal(0.01, seen.StepSeconds);
        Assert.Equal(0.5, seen.Alpha, 6);
        Assert.Equal(0.025, seen.DeltaSeconds);
        Assert.Equal(0.02f, seen.SimulatedSeconds);

        // A frame with no step due still renders.
        Assert.Equal(new GameFrame(1, 0, 0.01, 0.7, 0.002), Round(loop.Tick(0.002, () => calls.Add("step"), _ => calls.Add("render"))));
        Assert.Equal(4, calls.Count);
        Assert.Equal(2, loop.FrameCount);
    }

    [Fact]
    public void Tick_WithoutARenderCallback_OnlySimulates()
    {
        GameLoop loop = new(0.01);
        int steps = 0;

        Assert.Equal(3, loop.Tick(0.03, () => steps++).Steps);
        Assert.Equal(3, steps);
        Assert.Throws<ArgumentNullException>(() => loop.Tick(0.01, null!));
    }

    [Fact]
    public void Paused_StillRenders_AndDropsPressesMadeDuringThePause()
    {
        GameInput input = new();
        GameLoop loop = new(0.01) { Input = input, Paused = true };
        int steps = 0, renders = 0;
        bool jumped = false;

        input.Press(Key.Space);
        input.Release(Key.Space);
        input.Press(Key.A);        // still held when the game resumes
        loop.Tick(1.0, () => steps++, _ => renders++);

        Assert.Equal(0, steps);
        Assert.Equal(1, renders);
        Assert.True(loop.Paused);

        loop.Paused = false;
        loop.Tick(0.01, () =>
        {
            steps++;
            jumped |= input.WasPressed(Key.Space);
        });

        Assert.Equal(1, steps);
        Assert.False(jumped);
        Assert.True(input.IsDown(Key.A));
    }

    [Fact]
    public void Tick_LatchesInputBeforeEveryStep()
    {
        GameInput input = new();
        GameLoop loop = new(0.01) { Input = input };
        List<(bool Down, bool Pressed)> seen = [];

        input.Press(Key.Space);
        loop.Tick(0.03, () => seen.Add((input.IsDown(Key.Space), input.WasPressed(Key.Space))));

        // Held for all three steps, pressed only on the first.
        Assert.Equal([(true, true), (true, false), (true, false)], seen);
    }

    [Fact]
    public void RunHeadless_RunsExactlyTheSteps_RenderingEveryGroup()
    {
        GameLoop loop = new(Step) { Paused = true };   // ignored headless
        int steps = 0;
        List<GameFrame> frames = [];

        loop.RunHeadless(7, 3, () => steps++, frames.Add);

        Assert.Equal(7, steps);
        Assert.Equal([3, 3, 1], frames.ConvertAll(static f => f.Steps));
        Assert.Equal([0L, 1L, 2L], frames.ConvertAll(static f => f.Index));
        Assert.Equal((float)(3 * Step), frames[0].SimulatedSeconds);
        Assert.Equal(3 * Step, frames[0].DeltaSeconds);

        loop.RunHeadless(0, 1, () => steps++, frames.Add);
        Assert.Equal(7, steps);
        Assert.Equal(3, frames.Count);
    }

    [Fact]
    public void RunHeadless_IsReproducible()
    {
        static List<int> Play()
        {
            GameInput input = new();
            GameLoop loop = new(Step) { Input = input };
            List<int> heights = [];
            int height = 0, step = 0;

            loop.RunHeadless(60, 2, () =>
            {
                // A scripted player: tap jump on steps 10 and 30.
                if (step is 9 or 29)
                {
                    input.Press(Key.Space);
                    input.Release(Key.Space);
                }

                step++;
                height = input.WasPressed(Key.Space) ? 20 : Math.Max(0, height - 1);
            }, _ => heights.Add(height));

            return heights;
        }

        List<int> first = Play();
        Assert.Equal(30, first.Count);
        Assert.Contains(first, static h => h > 10);
        Assert.Equal(first, Play());
    }

    [Fact]
    public void RunHeadless_ValidatesItsArguments()
    {
        GameLoop loop = new(Step);
        Assert.Throws<ArgumentOutOfRangeException>(() => loop.RunHeadless(-1, 1, static () => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => loop.RunHeadless(1, 0, static () => { }));
        Assert.Throws<ArgumentNullException>(() => loop.RunHeadless(1, 1, null!));
        Assert.Throws<ArgumentNullException>(() => new GameLoop(null!));
    }

    [Fact]
    public void Exceptions_FromCallbacks_Propagate()
    {
        GameLoop loop = new(0.01);
        Assert.Throws<InvalidOperationException>(() => loop.Tick(0.02, static () => throw new InvalidOperationException("step")));
        Assert.Throws<FormatException>(() => loop.Tick(0, static () => { }, static _ => throw new FormatException("render")));
    }

    private static GameFrame Round(GameFrame frame) => frame with { Alpha = Math.Round(frame.Alpha, 6) };
}
