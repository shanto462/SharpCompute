using System;

namespace SharpCompute.Differential.Tests;

/// <summary>
/// The helpers of the model shaders: what an Apple GPU does that the C# of a case does not say.
/// The model shaders run with <c>ForCpu</c> only, so these run as C#; they are written in the shader
/// language anyway, because the generator translates every shader of the project.
/// </summary>
/// <remarks>All members are pure functions of their arguments and so thread-safe.</remarks>
public static class GpuModel
{
    /// <summary>A subnormal float as the GPU's float arithmetic reads and writes it: a zero of the same sign.</summary>
    public static float Flush(float value)
    {
        uint bits = Gpu.AsUInt(value);
        return (bits & 0x7F800000u) == 0 ? Gpu.AsFloat(bits & 0x80000000u) : value;
    }

    public static Float2 Flush(Float2 value) => new(Flush(value.X), Flush(value.Y));

    public static Float3 Flush(Float3 value) => new(Flush(value.X), Flush(value.Y), Flush(value.Z));

    public static Float4 Flush(Float4 value) => new(Flush(value.X), Flush(value.Y), Flush(value.Z), Flush(value.W));

    /// <summary>
    /// <see cref="Flush(float)"/>, and the smallest normal as well: a result the GPU flushes when it
    /// rounds up to <c>±2^-126</c> from below, since it decides tininess before rounding.
    /// </summary>
    public static float FlushTiny(float value)
    {
        uint bits = Gpu.AsUInt(value);
        return (bits & 0x7FFFFFFFu) <= 0x00800000u ? Gpu.AsFloat(bits & 0x80000000u) : value;
    }

    public static Float2 FlushTiny(Float2 value) => new(FlushTiny(value.X), FlushTiny(value.Y));

    public static Float3 FlushTiny(Float3 value) => new(FlushTiny(value.X), FlushTiny(value.Y), FlushTiny(value.Z));

    public static Float4 FlushTiny(Float4 value) => new(FlushTiny(value.X), FlushTiny(value.Y), FlushTiny(value.Z), FlushTiny(value.W));

    /// <summary>
    /// A half fused multiply-add, rounded once: what the GPU makes of <c>a * b + c</c> on halves.
    /// There is no scalar overload of <see cref="Gpu.Fma(Half2, Half2, Half2)"/>, so it goes through one.
    /// </summary>
    public static Half Fma(Half a, Half b, Half c) => Gpu.Fma(new Half2(a), new Half2(b), new Half2(c)).X;
}
