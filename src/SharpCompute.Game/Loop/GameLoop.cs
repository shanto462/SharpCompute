using System;
using SharpCompute.Presentation;

namespace SharpCompute.Game;

/// <summary>What one frame of a <see cref="GameLoop"/> simulated, for the render callback.</summary>
/// <param name="Index">0 for the first frame, then one more per frame.</param>
/// <param name="Steps">Simulation steps run for this frame (0 when the display outpaces the step rate, or while paused).</param>
/// <param name="StepSeconds">Game seconds per step.</param>
/// <param name="Alpha">How far the frame lies between the last step (0) and the next (1), for interpolation.</param>
/// <param name="DeltaSeconds">Wall-clock seconds since the previous frame (headless: the simulated time).</param>
public readonly record struct GameFrame(long Index, int Steps, double StepSeconds, double Alpha, double DeltaSeconds)
{
    /// <summary>
    /// Game seconds simulated for this frame (<see cref="Steps"/> × <see cref="StepSeconds"/>): what
    /// to advance GPU-side animation such as particles by, so it stays in step with the simulation.
    /// </summary>
    public float SimulatedSeconds => (float)(Steps * StepSeconds);
}

/// <summary>
/// A fixed-timestep game loop: each frame it runs as many simulation steps as the elapsed time
/// calls for (see <see cref="FixedTimestep"/>), then renders once. The same loop drives a
/// <see cref="MetalWindow"/> (<see cref="Run"/>), a headless run of a fixed number of steps
/// (<see cref="RunHeadless"/>, for tests and for writing frames to disk), or your own frame source
/// (<see cref="Tick"/>).
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="Input"/> is set, the loop calls <see cref="GameInput.BeginStep"/> before every
/// step, so the step callback reads input with correct pressed and released edges however many
/// steps a frame runs. While paused no steps run; key presses made during the pause are dropped
/// instead of firing on the first step after it (held keys still count).
/// </para>
/// <para>
/// Exceptions from a callback propagate out of <see cref="Tick"/>, <see cref="Run"/> and
/// <see cref="RunHeadless"/>. Not thread-safe; with a window, everything happens on the main thread.
/// </para>
/// </remarks>
public sealed class GameLoop
{
    /// <summary>A loop that steps every <paramref name="stepSeconds"/>; see <see cref="FixedTimestep"/> for the limits.</summary>
    /// <exception cref="ArgumentOutOfRangeException">An argument is not positive (or not finite).</exception>
    public GameLoop(double stepSeconds, double maxFrameSeconds = 0.1, int maxStepsPerFrame = 12)
        : this(new FixedTimestep(stepSeconds, maxFrameSeconds, maxStepsPerFrame))
    {
    }

    /// <summary>A loop over an existing timestep.</summary>
    public GameLoop(FixedTimestep timestep) => Timestep = timestep ?? throw new ArgumentNullException(nameof(timestep));

    /// <summary>The accumulator this loop advances.</summary>
    public FixedTimestep Timestep { get; }

    /// <summary>Game seconds per step.</summary>
    public double StepSeconds => Timestep.StepSeconds;

    /// <summary>Input to latch before every step, or null.</summary>
    public GameInput? Input { get; set; }

    /// <summary>While true, frames still render but no steps run.</summary>
    public bool Paused
    {
        get => Timestep.Paused;
        set => Timestep.Paused = value;
    }

    /// <summary>Frames run so far: the <see cref="GameFrame.Index"/> of the next one.</summary>
    public long FrameCount { get; private set; }

    /// <summary>
    /// Runs one frame: <paramref name="step"/> once per due simulation step, then
    /// <paramref name="render"/> once.
    /// </summary>
    /// <param name="deltaSeconds">Seconds since the previous frame.</param>
    /// <param name="step">Advances the game by <see cref="StepSeconds"/>.</param>
    /// <param name="render">Draws the frame; may be null when only simulating.</param>
    public GameFrame Tick(double deltaSeconds, Action step, Action<GameFrame>? render = null)
    {
        ArgumentNullException.ThrowIfNull(step);
        int steps = Timestep.Advance(deltaSeconds);

        if (Paused)
        {
            Input?.DiscardPresses();
        }

        return RunFrame(steps, deltaSeconds, step, render);
    }

    /// <summary>
    /// Drives the loop from <paramref name="window"/>'s frame callback until the window closes. The
    /// render callback normally ends by presenting a texture to the window.
    /// </summary>
    /// <exception cref="InvalidOperationException">Not on the main thread, or the window is already running.</exception>
    public void Run(MetalWindow window, Action step, Action<GameFrame> render)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(render);
        window.Run(frame => Tick(frame.DeltaTime, step, render));
    }

    /// <summary>
    /// Runs exactly <paramref name="steps"/> simulation steps with no window and no clock, rendering
    /// after every <paramref name="stepsPerFrame"/> of them (and after a shorter last group). The
    /// result depends only on the callbacks, so a test or a frame dump is reproducible.
    /// <see cref="Paused"/> is ignored.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="steps"/> is negative or <paramref name="stepsPerFrame"/> is not positive.</exception>
    public void RunHeadless(int steps, int stepsPerFrame, Action step, Action<GameFrame>? render = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(steps);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepsPerFrame);
        ArgumentNullException.ThrowIfNull(step);

        for (int done = 0; done < steps; done += stepsPerFrame)
        {
            int count = Math.Min(stepsPerFrame, steps - done);
            RunFrame(count, count * StepSeconds, step, render);
        }
    }

    private GameFrame RunFrame(int steps, double deltaSeconds, Action step, Action<GameFrame>? render)
    {
        for (int i = 0; i < steps; i++)
        {
            Input?.BeginStep();
            step();
        }

        GameFrame frame = new(FrameCount++, steps, StepSeconds, Timestep.Alpha, deltaSeconds);
        render?.Invoke(frame);
        return frame;
    }
}
