namespace SharpCompute.Presentation;

/// <summary>Timing for one iteration of <see cref="MetalWindow.Run"/>.</summary>
/// <param name="Time">Seconds since <see cref="MetalWindow.Run"/> started, from a monotonic clock.</param>
/// <param name="DeltaTime">Seconds since the previous frame; 0 for the first frame.</param>
/// <param name="FrameIndex">0 for the first frame, then incremented once per callback.</param>
public readonly record struct FrameInfo(double Time, double DeltaTime, long FrameIndex);
