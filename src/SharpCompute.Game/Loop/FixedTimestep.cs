using System;

namespace SharpCompute.Game;

/// <summary>
/// The arithmetic of a fixed-timestep loop: frame time goes into an accumulator, whole steps come
/// out, and what is left over is the interpolation <see cref="Alpha"/> for rendering. Simulating in
/// equal steps keeps a game deterministic and independent of the display's refresh rate.
/// </summary>
/// <remarks>
/// <para>
/// Two limits keep a slow frame from snowballing. A frame longer than <see cref="MaxFrameSeconds"/>
/// (a hitch, a breakpoint, a dragged window) counts as that long: the game slows down instead of
/// jumping. And at most <see cref="MaxStepsPerFrame"/> steps run per frame; time beyond them stays
/// in the accumulator and is caught up over the following frames.
/// </para>
/// <para>Not thread-safe: advance it from the thread that runs the game.</para>
/// </remarks>
public sealed class FixedTimestep
{
    private double _accumulator;

    /// <param name="stepSeconds">Length of one simulation step, e.g. <c>1.0 / 120</c>.</param>
    /// <param name="maxFrameSeconds">The longest frame time counted; longer frames are clamped to it.</param>
    /// <param name="maxStepsPerFrame">The most steps <see cref="Advance"/> returns for one frame.</param>
    /// <exception cref="ArgumentOutOfRangeException">An argument is not positive (or not finite).</exception>
    public FixedTimestep(double stepSeconds, double maxFrameSeconds = 0.1, int maxStepsPerFrame = 12)
    {
        if (!(stepSeconds > 0) || !double.IsFinite(stepSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(stepSeconds), stepSeconds, "The step must be a positive, finite number of seconds.");
        }

        if (!(maxFrameSeconds > 0) || !double.IsFinite(maxFrameSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrameSeconds), maxFrameSeconds, "The frame limit must be a positive, finite number of seconds.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStepsPerFrame);
        StepSeconds = stepSeconds;
        MaxFrameSeconds = maxFrameSeconds;
        MaxStepsPerFrame = maxStepsPerFrame;
    }

    /// <summary>Seconds of game time per step.</summary>
    public double StepSeconds { get; }

    /// <summary>The longest frame time <see cref="Advance"/> counts.</summary>
    public double MaxFrameSeconds { get; }

    /// <summary>The most steps <see cref="Advance"/> returns for one frame.</summary>
    public int MaxStepsPerFrame { get; }

    /// <summary>
    /// While true, <see cref="Advance"/> returns no steps and discards the time, so resuming does not
    /// replay the pause as a burst of steps.
    /// </summary>
    public bool Paused { get; set; }

    /// <summary>Game time waiting to be simulated: less than one step unless the step limit was hit.</summary>
    public double Accumulator => _accumulator;

    /// <summary>
    /// How far the current moment lies between the last step (0) and the next (1). Render with
    /// <c>previous + (current - previous) * Alpha</c> for motion smoother than the step rate.
    /// </summary>
    public double Alpha => Math.Min(_accumulator / StepSeconds, 1.0);

    /// <summary>Steps handed out since construction or <see cref="Reset"/>.</summary>
    public long TotalSteps { get; private set; }

    /// <summary>
    /// Adds a frame's elapsed time and returns how many steps to simulate now. Negative and NaN
    /// times count as zero.
    /// </summary>
    public int Advance(double deltaSeconds)
    {
        if (Paused)
        {
            _accumulator = 0;
            return 0;
        }

        // Math.Min would let NaN through; the comparison does not.
        _accumulator += deltaSeconds > 0 ? Math.Min(deltaSeconds, MaxFrameSeconds) : 0;
        int steps = 0;

        while (_accumulator >= StepSeconds && steps < MaxStepsPerFrame)
        {
            _accumulator -= StepSeconds;
            steps++;
        }

        TotalSteps += steps;
        return steps;
    }

    /// <summary>Drops accumulated time and the step count (a new game, a loaded level).</summary>
    public void Reset()
    {
        _accumulator = 0;
        TotalSteps = 0;
    }
}
