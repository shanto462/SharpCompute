// Ported from https://www.shadertoy.com/view/wlscWX — "Pyramid Pattern" by Shane.
// License: Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported (CC BY-NC-SA 3.0).
// Adapted from the ComputeSharp sample port (MIT, © Sergio Pedri) to SharpCompute.

namespace SharpCompute.Samples.Shaders;

/// <summary>
/// An offset grid of square-based pyramids whose tips have been offset according to an underlying directional noise field.
/// The faux lighting is provided via bump mapping. No raymarching was harmed during the making of this example.
/// </summary>
/// <param name="time">The current time since the start of the application.</param>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct PyramidPattern(float time) : IComputeShader<Float4>
{
    internal const string Author = "Shane";

    internal const string SourceUrl = "https://www.shadertoy.com/view/wlscWX";

    // Standard 2D rotation formula.
    private static Float2x2 Rotate2x2(in float a)
    {
        float c = Gpu.Cos(a);
        float s = Gpu.Sin(a);

        return new(c, -s, s, c);
    }

    // IQ's Float2 to float hash.
    private static float Hash21(Float2 p)
    {
        return Gpu.Fract(Gpu.Sin(Gpu.Dot(p, new Float2(27.619f, 57.583f))) * 43758.5453f);
    }

    // Float2 to Float2 hash.
    private static Float2 Hash22B(Float2 p)
    {
        float n = Gpu.Sin(Gpu.Dot(p, new Float2(41, 289)));

        return (Gpu.Fract(new Float2(262144, 32768) * n) * 2.0f) - 1.0f;
    }

    // Based on IQ's gradient noise formula.
    private static float N2D3G(Float2 p)
    {
        Float2 i = Gpu.Floor(p);

        p -= i;

        Float4 v = default;

        v.X = Gpu.Dot(Hash22B(i), p);
        // port: new Float2(1, 0) / new Float2(0, 1) for Float2.UnitX / UnitY (SharpCompute has none).
        v.Y = Gpu.Dot(Hash22B(i + new Float2(1, 0)), p - new Float2(1, 0));
        v.Z = Gpu.Dot(Hash22B(i + new Float2(0, 1)), p - new Float2(0, 1));

        v.W = Gpu.Dot(Hash22B(i + 1.0f), p - 1.0f);

        p = p * p * (3.0f - (2.0f * p));

        return Gpu.Lerp(Gpu.Lerp(v.X, v.Y, p.X), Gpu.Lerp(v.Z, v.W, p.X), p.Y);
    }

    // Two layers of noise.
    private static float FBM(Float2 p)
    {
        return (N2D3G(p) * 0.66f) + (N2D3G(p * 2.0f) * 0.34f);
    }

    // HLSL mod from GLSL
    private static float Mod(float x, float y)
    {
        return x - (y * Gpu.Floor(x / y));
    }

    private float BMap(Float2 p)
    {
        p = Gpu.Mul(p, Rotate2x2(-3.14159f / 5.0f));

        if (Mod(Gpu.Floor(p.Y), 2.0f) < 0.5f)
        {
            p.X += 0.5f;
        }

        Float2 ip = Gpu.Floor(p);

        p -= ip + 0.5f;

        float ang = (-3.14159f * 3.0f / 5.0f) + (FBM((ip / 8.0f) + (time / 3.0f)) * 6.2831f * 2.0f);

        Float2 offs = new Float2(Gpu.Cos(ang), Gpu.Sin(ang)) * 0.35f;

        if (p.X < offs.X)
        {
            p.X = 1.0f - ((p.X + 0.5f) / Gpu.Abs(offs.X + 0.5f));
        }
        else
        {
            p.X = (p.X - offs.X) / (0.5f - offs.X);
        }

        if (p.Y < offs.Y)
        {
            p.Y = 1.0f - ((p.Y + 0.5f) / Gpu.Abs(offs.Y + 0.5f));
        }
        else
        {
            p.Y = (p.Y - offs.Y) / (0.5f - offs.Y);
        }

        return 1.0f - Gpu.Max(p.X, p.Y);
    }

    // Standard function-based bump mapping function, with an edge value  included for good measure
    private Float3 DoBumpMap(in Float2 p, in Float3 n, float bumpfactor, ref float edge)
    {
        Float2 e = new(0.025f, 0);

        float f = BMap(p);
        float fx = BMap(p - e.XY);
        float fy = BMap(p - e.YX);
        float fx2 = BMap(p + e.XY);
        float fy2 = BMap(p + e.YX);

        Float3 grad = new Float3(fx - fx2, fy - fx2, 0) / e.X / 2.0f;

        edge = Gpu.Length(new Float2(fx, fy) + new Float2(fx2, fy2) - (f * 2.0f));
        edge = Gpu.Smoothstep(0.0f, 1.0f, edge / e.X);

        grad -= n * Gpu.Dot(n, grad);

        return Gpu.Normalize(n + (grad * bumpfactor));
    }

    // A hatch-like algorithm, or a stipple... or some kind of textured pattern
    private float DoHatch(Float2 p, float res)
    {
        p *= res / 16.0f;

        float hatch = Gpu.Clamp((Gpu.Sin((p.X - p.Y) * 3.14159f * 200.0f) * 2.0f) + 0.5f, 0.0f, 1.0f);

        float hRnd = Hash21(Gpu.Floor(p * 6.0f) + 0.73f);
        if (hRnd > 0.66f)
        {
            hatch = hRnd;
        }

        return hatch;
    }

    /// <inheritdoc/>
    public Float4 Execute()
    {
        Int2 coordinate = new(ThreadIds.X, DispatchSize.Y - ThreadIds.Y);
        float iRes = Gpu.Min(DispatchSize.Y, 800.0f);
        // port: (Float2) cast of the Int2 coordinate (HLSL converts implicitly).
        Float2 uv = ((Float2)coordinate - ((Float2)DispatchSize.XY * 0.5f)) / iRes;

        Float3 rd = Gpu.Normalize(new Float3(uv, 0.5f));
        const float gSc = 10.0f;
        Float2 p = (uv * gSc) + new Float2(0, time / 2.0f);
        Float2 oP = p;
        float m = BMap(p);
        Float3 n = new(0, 0, -1);
        float edge = 0.0f;
        float bumpFactor = 0.25f;

        n = DoBumpMap(p, n, bumpFactor, ref edge);

        Float3 lp = new Float3(-0.0f + (Gpu.Sin(time) * 0.3f), 0.0f + (Gpu.Cos(time * 1.3f) * 0.3f), -1) - new Float3(uv, 0);
        float lDist = Gpu.Max(Gpu.Length(lp), 0.001f);
        Float3 ld = lp / lDist;
        float diff = Gpu.Max(Gpu.Dot(n, ld), 0.0f);

        diff = Gpu.Pow(diff, 4.0f);

        float spec = Gpu.Pow(Gpu.Max(Gpu.Dot(Gpu.Reflect(-ld, n), -rd), 0.0f), 16.0f);
        float fre = Gpu.Min(Gpu.Pow(Gpu.Max(1.0f + Gpu.Dot(n, rd), 0.0f), 4.0f), 3.0f);
        Float3 col = 0.15f * (diff + 0.251f + (spec * new Float3(1, 0.7f, 0.3f) * 9.0f) + (fre * new Float3(0.1f, 0.3f, 1) * 12.0f));
        float rf = Gpu.Smoothstep(0.0f, 0.35f, (BMap(Gpu.Reflect(rd, n).XY * 2.0f) * FBM(Gpu.Reflect(rd, n).XY * 3.0f)) + 0.1f);

        col += col * col * rf * rf * new Float3(1, 0.1f, 0.1f) * 15.0f;

        float shade = (m * 0.83f) + 0.17f;

        col *= shade;
        col *= 1.0f - (edge * 0.8f);

        float hatch = DoHatch(oP / gSc, iRes);

        col *= (hatch * 0.5f) + 0.7f;

        return new(Gpu.Sqrt(Gpu.Max(col, 0.0f)), 1);
    }
}
