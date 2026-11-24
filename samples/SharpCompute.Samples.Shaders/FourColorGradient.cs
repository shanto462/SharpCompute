// Ported from https://www.shadertoy.com/view/7lc3R4 — "Four color gradient" by Inigo Quilez.
// License: Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported (CC BY-NC-SA 3.0).
// Adapted from the ComputeSharp sample port (MIT, © Sergio Pedri) to SharpCompute.

namespace SharpCompute.Samples.Shaders;

/// <summary>
/// A shader which interpolates four different colors into a rotating gradient pattern.
/// </summary>
/// <param name="time">The current time since the start of the application.</param>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FourColorGradient(float time) : IComputeShader<Float4>
{
    internal const string Author = "Inigo Quilez";

    internal const string SourceUrl = "https://www.shadertoy.com/view/7lc3R4";

    // Colors to blend together
    private static readonly Float3 ColorOne = new(0.999f, 0.999f, 0.999f);
    private static readonly Float3 ColorTwo = new(0.999f, 0f, 0f);
    private static readonly Float3 ColorThree = new(0f, 0.999f, 0f);
    private static readonly Float3 ColorFour = new(0f, 0f, 0.999f);

    /// <summary>
    /// Standard 2D rotation formula.
    /// </summary>
    private static Float2x2 Rotate(float a)
    {
        float c = Gpu.Cos(a);
        float s = Gpu.Sin(a);

        return new(c, s, -s, c);
    }

    /// <summary>
    /// Generate a hash from a vector.
    /// </summary>
    /// <param name="p">Vector to hash.</param>
    /// <returns>Hashed vector.</returns>
    private static Float2 Hash(Float2 p)
    {
        p = new Float2(Gpu.Dot(p, new Float2(2127.1f, 81.17f)), Gpu.Dot(p, new Float2(1269.5f, 283.37f)));

        return Gpu.Fract(Gpu.Sin(p) * 43758.5453f);
    }

    /// <summary>
    /// Generate a smoothed noise pattern.
    /// </summary>
    /// <param name="p">Position vector.</param>
    /// <returns>Depth value.</returns>
    private static float Noise(Float2 p)
    {
        Float2 i = Gpu.Floor(p);
        Float2 f = Gpu.Fract(p);
        Float2 u = f * f * (3.0f - (2.0f * f));

        // Mix -> Lerp
        float n = Gpu.Lerp(
            Gpu.Lerp(
                Gpu.Dot(-1.0f + (2.0f * Hash(i + new Float2(0.0f, 0.0f))), f - new Float2(0.0f, 0.0f)),
                Gpu.Dot(-1.0f + (2.0f * Hash(i + new Float2(1.0f, 0.0f))), f - new Float2(1.0f, 0.0f)),
                u.X),
            Gpu.Lerp(
                Gpu.Dot(-1.0f + (2.0f * Hash(i + new Float2(0.0f, 1.0f))), f - new Float2(0.0f, 1.0f)),
                Gpu.Dot(-1.0f + (2.0f * Hash(i + new Float2(1.0f, 1.0f))), f - new Float2(1.0f, 1.0f)),
                u.X),
            u.Y);

        return 0.5f + (0.5f * n);
    }

    /// <inheritdoc/>
    public Float4 Execute()
    {
        Float2 fragCoord = new(ThreadIds.X, DispatchSize.Y - ThreadIds.Y);
        // port: (Float2) cast of the Int2 DispatchSize.XY (HLSL converts implicitly).
        Float2 uv = fragCoord / (Float2)DispatchSize.XY;

        float ratio = DispatchSize.X / DispatchSize.Y;
        Float2 tuv = uv;

        tuv -= 0.5f;

        // Rotate with noise
        float degree = Noise(new Float2(time * 0.1f, tuv.X * tuv.Y));

        tuv.Y *= 1.0f / ratio;
        tuv.XY = Gpu.Mul(tuv.XY, Rotate(Gpu.Radians(((degree - 0.5f) * 720.0f) + 180.0f)));
        tuv.Y *= ratio;

        // Wave warp with sin
        float frequency = 5.0f;
        float amplitude = 30.0f;
        float speed = time * 2.0f;

        tuv.X += Gpu.Sin((tuv.Y * frequency) + speed) / amplitude;
        tuv.Y += Gpu.Sin((tuv.X * frequency * 1.5f) + speed) / (amplitude * 0.5f);

        // Draw the image
        Float3 layer1 = Gpu.Lerp(ColorOne, ColorTwo, Gpu.Smoothstep(-0.3f, 0.2f, Gpu.Mul(tuv.XY, Rotate(Gpu.Radians(-5.0f))).X));
        Float3 layer2 = Gpu.Lerp(ColorThree, ColorFour, Gpu.Smoothstep(-0.3f, 0.2f, Gpu.Mul(tuv.XY, Rotate(Gpu.Radians(-5.0f))).X));
        Float3 col = Gpu.Lerp(layer1, layer2, Gpu.Smoothstep(0.5f, -0.3f, tuv.Y));

        return new(col, 1.0f);
    }
}
