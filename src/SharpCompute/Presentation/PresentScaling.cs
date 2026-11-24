namespace SharpCompute.Presentation;

/// <summary>How <see cref="MetalWindow.Present"/> fits a frame whose size differs from the window's drawable.</summary>
public enum PresentScaling
{
    /// <summary>
    /// Scale uniformly to the largest size that fits and center it, filling the rest with black
    /// (letterbox or pillarbox). The aspect ratio is preserved.
    /// </summary>
    Fit,

    /// <summary>Scale each axis independently to fill the whole drawable. The aspect ratio may change.</summary>
    Stretch,
}
