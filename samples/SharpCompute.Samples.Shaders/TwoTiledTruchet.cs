// Ported from https://www.shadertoy.com/view/tsSfWK — "Two Tiled Truchet" by Shane.
// License: Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported (CC BY-NC-SA 3.0).
// Adapted from the ComputeSharp sample port (MIT, © Sergio Pedri) to SharpCompute.

namespace SharpCompute.Samples.Shaders;

/// <summary>
/// A shader creating an animated two-tiles Truchet arrangement.
/// </summary>
/// <param name="time">The current time since the start of the application.</param>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct TwoTiledTruchet(float time) : IComputeShader<Float4>
{
    internal const string Author = "Shane";

    internal const string SourceUrl = "https://www.shadertoy.com/view/tsSfWK";

    /// <summary>
    /// Calculates the Truchet distance field.
    /// </summary>
    /// <param name="p">The input value to compute the distance field for.</param>
    /// <param name="ang">The resulting Truchet angle.</param>
    /// <returns>The distance field for the input value.</returns>
    private Float2 DistanceField(Float2 p, out Float2 ang)
    {
        // Fast hash for a pair of floats
        static float Hash21(Float2 p)
        {
            return Gpu.Fract(Gpu.Sin(Gpu.Dot(p, new Float2(27.619f, 57.583f))) * 43758.5453f);
        }

        // HLSL's port of the GLSL mod intrinsic
        static float Mod(float x, float y)
        {
            return x - (y * Gpu.Floor(x / y));
        }

        Float2 ip2 = Gpu.Floor(p / 2.0f);
        Float2 ip = Gpu.Floor(p);
        float rnd = Hash21(ip);
        float rnd3 = Hash21(ip + 0.57f);
        // port: new Float2(...) for the implicit scalar-to-vector conversions of `d = 1e5f` and `ang = 0`.
        Float2 d = new(1e5f);

        p -= ip + 0.5f;

        if (Mod(ip2.X + ip2.Y, 2.0f) < 0.5f)
        {
            d = Gpu.Abs(p);
            ang = p.YX;

            if (Mod(ip.X + 1.0f, 2.0f) < 0.5f)
            {
                ang.X *= -1.0f;

            }

            if (Mod(ip.Y + 1.0f, 2.0f) > 0.5f)
            {
                ang.Y *= -1.0f;
            }

            if (rnd3 < 0.5f)
            {
                d = d.YX;
                ang = ang.YX;
            }

            ang += 0.5f;
            ang *= 3.0f;

        }
        else
        {
            if (rnd < 0.5f)
            {
                p = p.YX * new Float2(1, -1);
            }

            d.X = Gpu.Length(p - 0.5f) - 0.5f;
            d.Y = Gpu.Length(p + 0.5f) - 0.5f;

            d = Gpu.Abs(d);

            ang = new(0f);
            ang.X = -Gpu.Atan((p.Y - 0.5f) / (p.X - 0.5f));
            ang.Y = -Gpu.Atan((p.Y + 0.5f) / (p.X + 0.5f));

            if (Mod(ip.X + ip.Y, 2.0f) < 0.5f)
            {
                ang *= -1.0f;
            }

            if (rnd < 0.5f)
            {
                ang *= -1.0f;
            }

            if (rnd3 < 0.5f)
            {
                d = d.YX;
                ang = ang.YX;
            }

            ang *= 4.0f / 6.2831853f;
            ang *= 2.0f;
        }

        ang = Gpu.Fract(ang + (time / 4.0f));

        return d;
    }

    /// <inheritdoc/>
    public Float4 Execute()
    {
        // port: (Float2) cast of the Int2 ThreadIds.XY (HLSL converts implicitly).
        Float2 uv = ((Float2)ThreadIds.XY - ((Float2)DispatchSize.XY * 0.5f)) / DispatchSize.Y;
        float gSc = 7.0f;
        Float2 p = (uv * gSc) - (new Float2(-1, -0.5f) * time / 2.0f);
        float sf = 2.0f / DispatchSize.Y * gSc;
        float lSc = 6.0f;
        float lw = 1.0f / lSc / gSc;
        Float2 d = DistanceField(p, out Float2 ang) - (2.5f / lSc);
        Float3 col = new(1.0f, 0.9f, 0.8f);

        for (int i = 0; i < 2; i++)
        {
            // port: `i == 0 ? v.X : v.Y` for the vector indexer v[i] (SharpCompute vectors have none).
            float di = (i == 0 ? d.X : d.Y) - (lw / 4.0f);
            float tracks = Gpu.Clamp(Gpu.Sin(((i == 0 ? ang.X : ang.Y) * 6.2831f) + (time * 6.0f)) * 4.0f, 0.0f, 1.0f);
            float gap = 1.0f + lw;

            // port: new Float3(c) for the implicit scalar-to-vector conversions of the Lerp targets.
            col = Gpu.Lerp(col, new Float3(0), (1.0f - Gpu.Smoothstep(0.0f, sf * 6.0f, di)) * 0.35f);
            col = Gpu.Lerp(col, new Float3(0), 1.0f - Gpu.Smoothstep(0.0f, sf, di));
            col = Gpu.Lerp(col, new(1.0f, 0.9f, 0.8f), 1.0f - Gpu.Smoothstep(0.0f, sf, di + (lw * 2.0f)));
            col = Gpu.Lerp(col, new Float3(0), 1.0f - Gpu.Smoothstep(0.0f, sf, di + (gap / lSc)));
            col = Gpu.Lerp(col, new Float3(1), 1.0f - Gpu.Smoothstep(0.0f, sf, di + (gap / lSc) + lw));
            col = Gpu.Lerp(col, new Float3(0), 1.0f - Gpu.Smoothstep(0.0f, sf, di + (2.0f * gap / lSc)));
            col = Gpu.Lerp(col, new Float3(1 * tracks), 1.0f
 - Gpu.Smoothstep(0.0f, sf, di + (2.0f * gap / lSc) + lw));
        }

        Float3 rgb = Gpu.Sqrt(Gpu.Max(col, 0.0f));
        Float4 color = new(rgb.X, rgb.Y, rgb.Z, 1.0f);

        return color;
    }
}
