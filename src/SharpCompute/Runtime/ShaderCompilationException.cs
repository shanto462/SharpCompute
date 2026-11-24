using System;

namespace SharpCompute;

/// <summary>
/// Thrown when Metal rejects a shader: the MSL fails to compile, the entry point is missing from
/// the compiled library, or the compute pipeline state cannot be created.
/// </summary>
public sealed class ShaderCompilationException : MetalException
{
    public ShaderCompilationException(string message, string mslSource)
        : this(message, mslSource, innerException: null)
    {
    }

    public ShaderCompilationException(string message, string mslSource, Exception? innerException)
        : base(message, innerException)
    {
        MslSource = mslSource ?? string.Empty;
    }

    /// <summary>The complete MSL translation unit that was handed to the Metal compiler.</summary>
    public string MslSource { get; }
}
