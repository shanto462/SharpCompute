// Ported from https://www.shadertoy.com/view/WtjyzR — "Colorful Infinity" by Benoit Marini.
// License: Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported (CC BY-NC-SA 3.0).
// Adapted from the ComputeSharp sample port (MIT, © Sergio Pedri) to SharpCompute.

namespace SharpCompute.Samples.Shaders;

/// <summary>
/// A shader creating an abstract and colorful animation.
/// </summary>
/// <param name="time">The current time since the start of the application.</param>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ColorfulInfinity(float time) : IComputeShader<Float4>
{
    internal const string Author = "Benoit Marini";

    internal const string SourceUrl = "https://www.shadertoy.com/view/WtjyzR";

    /// <summary>
    /// The total number of layers for the final animation.
    /// </summary>
    private const float NumberOfLayers = 16.0f;

    /// <summary>
    /// The number of iterations to calculate a texel value.
    /// </summary>
    private const int NumberOfIterations = 23;

    /// <summary>
    /// Makes some magic happen.
    /// </summary>
    private Float4 Tex(Float3 p)
    {
        float t = time + 78.0f;
        Float4 o = new(p.X, p.Y, p.Z, 3.0f * Gpu.Sin(t * 0.1f));
        Float4 dec =
            new Float4(1.0f, 0.9f, 0.1f, 0.15f) +
            new Float4(0.06f * Gpu.Cos(t * 0.1f), 0, 0, 0.14f * Gpu.Cos(t * 0.23f));

        for (int i = 0; i++ < NumberOfIterations;)
        {
            o.XZYW = Gpu.Abs((o / Gpu.Dot(o, o)) - dec);
        }

        return o;
    }

    /// <inheritdoc/>
    public Float4 Execute()
    {
        // port: (Float2) cast of the Int2 ThreadIds.XY (HLSL converts implicitly).
        Float2 uv = ((Float2)ThreadIds.XY - ((Float2)DispatchSize.XY * 0.5f)) / DispatchSize.Y;
        // port: new Float3(0f) for the implicit scalar-to-vector conversion of `float3 col = 0`.
        Float3 col = new(0f);
        float t = time * 0.3f;

        for (float i = 0.0f; i <= 1.0f; i += 1.0f / NumberOfLayers)
        {
            float d = Gpu.Fract(i + t);
            float s = Gpu.Lerp(5.0f, 0.5f, d);
            float f = d * Gpu.Smoothstep(1.0f, 0.9f, d);

            col += Tex(new Float3(uv.X * s, uv.Y * s, i * 4.0f)).XYZ * f;
        }

        col /= NumberOfLayers;
        col *= new Float3(2, 1.0f, 2.0f);
        col = Gpu.Pow(col, 0.5f);

        return new(col.X, col.Y, col.Z, 1.0f);
    }
}
