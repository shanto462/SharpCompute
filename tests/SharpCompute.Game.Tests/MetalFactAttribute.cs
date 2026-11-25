namespace SharpCompute.Game.Tests;

/// <summary>A <see cref="FactAttribute"/> that is skipped, not failed, on machines without a usable Metal device.</summary>
public sealed class MetalFactAttribute : FactAttribute
{
    internal const string SkipReason = "No Metal device is available on this machine (GraphicsDevice.IsSupported is false).";

    public MetalFactAttribute()
    {
        if (!GraphicsDevice.IsSupported)
        {
            Skip = SkipReason;
        }
    }
}

/// <summary>A <see cref="TheoryAttribute"/> that is skipped, not failed, on machines without a usable Metal device.</summary>
public sealed class MetalTheoryAttribute : TheoryAttribute
{
    public MetalTheoryAttribute()
    {
        if (!GraphicsDevice.IsSupported)
        {
            Skip = MetalFactAttribute.SkipReason;
        }
    }
}
