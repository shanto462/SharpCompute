using System;
using System.Threading;
using SharpCompute.Interop;

namespace SharpCompute;

// GPU execution time of the work this device ran: LastGpuTime for the synchronous calls here,
// ComputeContext.LastGpuTime for a context's batches.
public sealed partial class GraphicsDevice
{
    private static int s_lastTimingId;

    // The calling thread's most recent synchronous command buffer: the device it ran on and how long
    // the GPU took. Per thread because a device is shared: a field of the device would hand one
    // thread the time of another thread's dispatch. One slot, not one per device, keeps the write on
    // the dispatch path to two thread-static stores; a device is told apart by an id rather than a
    // reference, so that a thread which once dispatched does not keep its device alive.
    [ThreadStatic]
    private static int t_lastGpuTimeDevice;

    [ThreadStatic]
    private static long t_lastGpuTimeTicks;

    private readonly int _timingId = Interlocked.Increment(ref s_lastTimingId);

    /// <summary>
    /// How long the GPU spent executing the command buffer the calling thread most recently ran on
    /// this device: the time between Metal's <c>GPUStartTime</c> and <c>GPUEndTime</c>. It leaves out
    /// what a stopwatch around the call includes (encoding, the commit, scheduling, and waking the
    /// waiting thread), so it is the number to compare kernels by.
    /// </summary>
    /// <value>
    /// The time of the last call on this thread that committed a command buffer and waited for it:
    /// <c>For</c>, <c>ForEach</c>, <c>ForIndirect</c>, <c>WriteDispatchArguments</c>, the GPU part of
    /// <c>ForSplit</c>, <c>Copy</c>, <c>Fill</c>, <c>Clear</c>, and <see cref="ComputeContext.Execute"/>
    /// or <see cref="ComputeContext.Dispose"/> of a context of this device.
    /// <see cref="TimeSpan.Zero"/> when this thread has run nothing yet, or when its latest such call
    /// was on another device. A call with nothing to do (an empty grid, a copy of 0 elements, a
    /// context with nothing pending) creates no command buffer and leaves the value unchanged.
    /// </value>
    /// <remarks>
    /// <para>
    /// <b>One time per command buffer.</b> Each synchronous dispatch is its own command buffer, so
    /// after <c>For</c> this is the kernel's time. Apple GPUs can sample a timestamp only between
    /// encoders, not between the dispatches of one, so work batched in a
    /// <see cref="ComputeContext"/> has one time for the whole batch
    /// (<see cref="ComputeContext.LastGpuTime"/>). Asynchronous work (<c>ForAsync</c>,
    /// <c>ForEachAsync</c>) does not set this property, because it completes on another thread: run
    /// it through a context to time it. For a command buffer the GPU failed, the value is whatever
    /// interval Metal recorded, possibly zero.
    /// </para>
    /// <para>
    /// The interval is elapsed time on the GPU, not time that was exclusively this command buffer's:
    /// other work the GPU runs meanwhile (another process, the window server) lengthens it, and a
    /// first dispatch after an idle spell includes the GPU's ramp-up, so time a few dispatches and
    /// take the median.
    /// </para>
    /// <para>
    /// <b>Threading.</b> Every thread sees its own value, so threads that dispatch on one device at
    /// the same time do not disturb each other's reading, and nothing is locked or allocated to keep
    /// it. Read it on the thread that made the call, before that thread awaits anything.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// device.For(width, height, new Blur(source, destination, radius));
    /// Console.WriteLine($"blur: {device.LastGpuTime.TotalMilliseconds:F2} ms on the GPU");
    /// </code>
    /// </example>
    public TimeSpan LastGpuTime => t_lastGpuTimeDevice == _timingId ? new TimeSpan(t_lastGpuTimeTicks) : TimeSpan.Zero;

    /// <summary>Makes <paramref name="ticks"/> the calling thread's <see cref="LastGpuTime"/> on this device.</summary>
    internal void SetLastGpuTime(long ticks)
    {
        t_lastGpuTimeDevice = _timingId;
        t_lastGpuTimeTicks = ticks;
    }

    /// <summary>
    /// The GPU execution time of a completed <paramref name="commandBuffer"/> in <see cref="TimeSpan"/>
    /// ticks: <c>GPUEndTime − GPUStartTime</c>, both host times in seconds.
    /// </summary>
    internal static long GpuTimeTicks(nint commandBuffer) =>
        GpuTimeTicks(ObjC.SendDouble(commandBuffer, Selectors.GPUStartTime), ObjC.SendDouble(commandBuffer, Selectors.GPUEndTime));

    /// <summary>
    /// <paramref name="gpuEnd"/> − <paramref name="gpuStart"/> seconds as <see cref="TimeSpan"/> ticks.
    /// Zero for anything that is not a forward interval: Metal leaves both times 0 for a command
    /// buffer that never reached the GPU, and a time must never come out negative or not-a-number.
    /// </summary>
    internal static long GpuTimeTicks(double gpuStart, double gpuEnd)
    {
        double seconds = gpuEnd - gpuStart;

        // Written so that NaN fails the test; the upper bound keeps the conversion inside a long.
        return seconds > 0 && seconds < TimeSpan.MaxValue.TotalSeconds ? (long)(seconds * TimeSpan.TicksPerSecond) : 0;
    }
}
