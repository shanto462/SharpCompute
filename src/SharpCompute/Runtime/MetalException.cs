using System;

namespace SharpCompute;

/// <summary>Base type for errors reported by Metal: shader compilation and GPU execution failures.</summary>
public class MetalException : Exception
{
    public MetalException(string message)
        : base(message)
    {
    }

    public MetalException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
