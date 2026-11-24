// Ported from https://www.shadertoy.com/view/Ml2GWy — "Fractal Tiling" by Inigo Quilez.
// License: Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported (CC BY-NC-SA 3.0).
// Adapted from the ComputeSharp sample port (MIT, © Sergio Pedri) to SharpCompute.

namespace SharpCompute.Samples.Shaders;

/// <summary>
/// A shader creating a fractal tiling animation.
/// </summary>
/// <param name="time">The current time since the start of the application.</param>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FractalTiling(float time) : IComputeShader<Float4>
{
    internal const string Author = "Inigo Quilez";

    internal const string SourceUrl = "https://www.shadertoy.com/view/Ml2GWy";

    /// <inheritdoc/>
    public Float4 Execute()
    {
        Float2 position = (((Float2)(256 * ThreadIds.XY)) / DispatchSize.X) + time;
        // port: new Float4(0f) for the implicit scalar-to-vector conversion of `float4 color = 0`.
        Float4 color = new(0f);

        for (int i = 0; i < 6; i++)
        {
            Float2 a = Gpu.Floor(position);
            Float2 b = Gpu.Fract(position);
            Float4 w = Gpu.Fract(
                (Gpu.Sin((a.X * 7) + (31.0f * a.Y) + (0.01f * time)) +
                 new Float4(0.035f, 0.01f, 0, 0.7f))
                 * 13.545317f);

            color.XYZ += w.XYZ *
                   2.0f * Gpu.Smoothstep(0.45f, 0.55f, w.W) *
                   Gpu.Sqrt(16.0f * b.X * b.Y * (1.0f - b.X) * (1.0f - b.Y));

            position /= 2.0f;
            color /= 2.0f;
        }

        color.XYZ = Gpu.Pow(color.XYZ, new Float3(0.7f, 0.8f, 0.5f));
        color.W = 1.0f;

        return color;
    }
}
