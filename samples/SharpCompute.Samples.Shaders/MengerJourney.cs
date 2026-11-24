// Ported from https://www.shadertoy.com/view/Mdf3z7 — "Menger Journey" by Syntopia.
// License: Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported (CC BY-NC-SA 3.0).
// Adapted from the ComputeSharp sample port (MIT, © Sergio Pedri) to SharpCompute.

namespace SharpCompute.Samples.Shaders;

/// <summary>
/// A shader creating a flythrough in a Menger system.
/// </summary>
/// <param name="time">The current time since the start of the application.</param>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct MengerJourney(float time) : IComputeShader<Float4>
{
    internal const string Author = "Syntopia";

    internal const string SourceUrl = "https://www.shadertoy.com/view/Mdf3z7";

    private const int MaxSteps = 30;
    private const float MinimumDistance = 0.0009f;
    private const float NormalDistance = 0.0002f;
    private const int Iterations = 7;
    private const float Scale = 3.0f;
    private const float FieldOfView = 1.0f;
    private const float Jitter = 0.05f;
    private const float FudgeFactor = 0.7f;
    private const float NonLinearPerspective = 2.0f;
    private const float Ambient = 0.32184f;
    private const float Diffuse = 0.5f;

    // port: new Float3(c) for the implicit scalar-to-vector conversions of `LightDir = 1.0f`, `pos = 0` and `color = 1.0f`.
    private static readonly Float3 LightDir = new(1.0f);
    private static readonly Float3 LightColor = new(1.0f, 1.0f, 0.858824f);
    private static readonly Float3 LightDir2 = new(1.0f, -1.0f, 1.0f);
    private static readonly Float3 LightColor2 = new(0, 0.333333f, 1.0f);
    private static readonly Float3 Offset = new(0.92858f, 0.92858f, 0.32858f);

    /// <summary>
    /// Rotates a given <see cref="Float2"/> value by a specified amount.
    /// </summary>
    private static Float2 Rotate(Float2 v, float a)
    {
        return new((Gpu.Cos(a) * v.X) + (Gpu.Sin(a) * v.Y), (-Gpu.Sin(a) * v.X) + (Gpu.Cos(a) * v.Y));
    }

    /// <summary>
    /// Calculates the light at a specified normal and starting color.
    /// Uses two light sources and no specular lights.
    /// </summary>
    private static Float3 GetLight(in Float3 color, in Float3 normal)
    {
        Float3 lightDir = Gpu.Normalize(LightDir);
        float diffuse = Gpu.Max(0.0f, Gpu.Dot(-normal, lightDir));

        Float3 lightDir2 = Gpu.Normalize(LightDir2);
        float diffuse2 = Gpu.Max(0.0f, Gpu.Dot(-normal, lightDir2));

        return
            (diffuse * Diffuse * (LightColor * color)) +
            (diffuse2 * Diffuse * (LightColor2 * color));
    }

    /// <summary>
    /// DE: Infinitely tiled Menger IFS.
    /// For more info on KIFS, see <a href="http://www.fractalforums.com/3d-fractal-generation/kaleidoscopic-%28escape-time-ifs%29/"/>
    /// </summary>
    private float DE(Float3 z)
    {
        // Performs the GLSL mod function.
        static Float3 Mod(Float3 x, float y)
        {
            return x - (y * Gpu.Floor(x / y));
        }

        z = Gpu.Abs(1.0f - Mod(z, 2.0f));

        float d = 1000.0f;

        for (int n = 0; n < Iterations; n++)
        {
            z.XY = Rotate(z.XY, 4.0f + (2.0f * Gpu.Cos(time / 8.0f)));
            z = Gpu.Abs(z);

            if (z.X < z.Y) { z.XY = z.YX; }

            if (z.X < z.Z) { z.XZ = z.ZX; }

            if (z.Y < z.Z) { z.YZ = z.ZY; }

            z = (Scale * z) - (Offset * (Scale - 1.0f));

            if (z.Z < -0.5f * Offset.Z * (Scale - 1.0f))
            {
                z.Z += Offset.Z * (Scale - 1.0f);
            }

            d = Gpu.Min(d, Gpu.Length(z) * Gpu.Pow(Scale, -n - 1.0f));
        }

        return d - 0.001f;
    }

    /// <summary>
    /// Calculates the finite difference normal at a given position.
    /// </summary>
    private Float3 GetNormal(in Float3 pos)
    {
        Float3 e = new(0.0f, NormalDistance, 0.0f);

        return Gpu.Normalize(new Float3(
            DE(pos + e.YXX) - DE(pos - e.YXX),
            DE(pos + e.XYX) - DE(pos - e.XYX),
            DE(pos + e.XXY) - DE(pos - e.XXY)));
    }

    /// <summary>
    /// Performs a ray march operation on a specific position.
    /// </summary>
    private Float4 RayMarch(in Float3 from, Float3 dir, in Float2 fragCoord)
    {
        // Computes a pseudo-random number
        static float Rand(Float2 co)
        {
            return Gpu.Fract(Gpu.Cos(Gpu.Dot(co, new Float2(4.898f, 7.23f))) * 23421.631f);
        }

        float totalDistance = Jitter * Rand(fragCoord.XY + time);
        Float3 dir2 = dir;
        float distance = 0;
        int steps = 0;
        Float3 pos = new(0f);

        for (int i = 0; i < MaxSteps; i++)
        {
            dir.ZY = Rotate(dir2.ZY, totalDistance * Gpu.Cos(time / 4.0f) * NonLinearPerspective);
            pos = from + (totalDistance * dir);
            distance = DE(pos) * FudgeFactor;
            totalDistance += distance;

            if (distance < MinimumDistance)
            {
                break;
            }

            steps = i;
        }

        float smoothStep = steps + (distance / MinimumDistance);
        float ao = 1.1f - (smoothStep / MaxSteps);
        Float3 normal = GetNormal(pos - (dir * NormalDistance * 3.0f));
        Float3 color = new(1.0f);
        Float3 light = GetLight(color, normal);

        color = ((color * Ambient) + light) * ao;

        return new(color.X, color.Y, color.Z, 1.0f);
    }

    /// <inheritdoc/>
    public Float4 Execute()
    {
        Float3 camPos = 0.5f * time * new Float3(1.0f, 0.0f, 0.0f);
        Float3 target = camPos + new Float3(1.0f, 0.0f * Gpu.Cos(time), 0.0f * Gpu.Sin(0.4f * time));
        Float3 camDir = Gpu.Normalize(target - camPos);
        Float3 camUp = new(0.0f, 1.0f, 0.0f);

        camUp = Gpu.Normalize(camUp - (Gpu.Dot(camDir, camUp) * camDir));

        Float3 camRight = Gpu.Normalize(Gpu.Cross(camDir, camUp));
        // port: (Float2) casts of the Int2 DispatchSize.XY and ThreadIds.XY (HLSL converts implicitly).
        Float2 coord = -1.0f + (2.0f * (Float2)ThreadIds.XY / (Float2)DispatchSize.XY);

        coord.X *= (float)DispatchSize.X / DispatchSize.Y;

        Float3 rayDir = Gpu.Normalize(camDir + (((coord.X * camRight) + (coord.Y * camUp)) * FieldOfView));

        return RayMarch(camPos, rayDir, (Float2)ThreadIds.XY);

    }
}
