// Ported from https://www.shadertoy.com/view/ttVBzd — "Extruded Truchet Pattern" by Shane.
// License: Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported (CC BY-NC-SA 3.0).
// Adapted from the ComputeSharp sample port (MIT, © Sergio Pedri) to SharpCompute.

namespace SharpCompute.Samples.Shaders;

/// <summary>
/// A basic extruded square grid-based blobby Truchet pattern.
/// </summary>
/// <param name="time">The current time since the start of the application.</param>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ExtrudedTruchetPattern(float time) : IComputeShader<Float4>
{
    internal const string Author = "Shane";

    internal const string SourceUrl = "https://www.shadertoy.com/view/ttVBzd";

    /// <summary>
    /// Curve shape - Round: 0, Semi-round: 1, Octagonal: 2, Superellipse: 3, Straight: 4.
    /// </summary>
    private static readonly int SHAPE = 0;


    // Standard 2D rotation formula.
    private static Float2x2 Rotate2x2(float a)
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

    // Various distance metrics.
    private static float Distance(Float2 p)
    {
        if (SHAPE == 0)
        {
            return Gpu.Length(p);
        }
        else
        {
            p = Gpu.Abs(p);
        }

        switch (SHAPE)
        {
            case 1: return Gpu.Max(Gpu.Length(p), ((p.X + p.Y) * 0.7071f) + 0.015f);
            case 2: return Gpu.Max((p.X + p.Y) * 0.7071f, Gpu.Max(p.X, p.Y));
            // port: new Float2(1) for the implicit scalar-to-vector conversion in Dot(..., 1).
            case 3: return Gpu.Pow(Gpu.Dot(Gpu.Pow(p, 3), new Float2(1)), 1.0f / 3.0f);
            default: return (p.X + p.Y) * 0.7071f;
        }
    }

    // GLSL's mod function
    private static float Mod(float x, float y)
    {
        return x - (y * Gpu.Floor(x / y));
    }

    // A standard square grid 2D blobby Truchet routine: Render circles
    // in opposite corners of a tile, reverse the pattern on alternate
    // checker tiles, and randomly rotate.
    private static float Truchet(Float2 p)
    {
        const float sc = 0.5f;
        Float2 ip = Gpu.Floor(p / sc) + 0.5f;

        p -= ip * sc;

        float rnd = Gpu.Fract(Gpu.Sin(Gpu.Dot(ip, new Float2(1, 113))) * 45758.5453f);

        if (rnd < 0.5f)
        {
            p.Y = -p.Y;
        }

        float d = Gpu.Min(Distance(p - (0.5f * sc)), Distance(p + (0.5f * sc))) - (0.5f * sc);

        if (SHAPE == 4)
        {
            d += (0.5f - (0.5f / Gpu.Sqrt(2.0f))) * sc;
        }

        if (rnd < 0.5f)
        {
            d = -d;
        }

        if (Mod(ip.X + ip.Y, 2.0f) < 0.5f)
        {
            d = -d;
        }

        return d - 0.03f;
    }

    // The scene's distance function: There'd be faster ways to do this, but it's
    // more readable this way. Plus, this  is a pretty simple scene, so it's 
    // efficient enough.
    // port: HLSL's mutable `static int objID` global (per-thread state, written by every M call)
    // has no MSL equivalent, so M reports the object ID through an out parameter instead. Object
    // ID: either the back plane or the metaballs.
    private static float M(Float3 p)
    {
        // port: a named local, not a discard (`out _` is not supported by the generator).
        return M(p, out int unusedObjID);

    }

    private static float M(Float3 p, out int objID)
    {
        float fl = -p.Z;
        float obj = Truchet(p.XY);

        obj = Gpu.Max(obj, Gpu.Abs(p.Z) - 0.125f) - (Gpu.Smoothstep(0.03f, 0.25f, -obj) * 0.1f);

        float studs = 1e5f;
        const float sc = 0.5f;
        Float2 q = p.XY + (0.5f * sc);
        Float2 iq = Gpu.Floor(q / sc) + 0.5f;

        q -= iq * sc;

        if (Mod(iq.X + iq.Y, 2.0f) > 0.5f)
        {
            studs = Gpu.Max(Gpu.Length(q) - (0.1f * sc) - 0.02f, Gpu.Abs(p.Z) - 0.26f);
        }

        objID = fl < obj && fl < studs ? 0 : obj < studs ? 1 : 2;

        return Gpu.Min(Gpu.Min(fl, obj), studs);
    }

    // Cheap shadows are hard. In fact, I'd almost say, shadowing particular scenes with limited 
    // iterations is impossible... However, I'd be very grateful if someone could prove me wrong. :)
    private static float SoftShadow(Float3 ro, Float3 lp, Float3 n, float k)
    {
        const int iter = 24;

        ro += n * 0.0015f;

        Float3 rd = lp - ro;
        float shade = 1.0f;
        float t = 0;
        float end = Gpu.Max(Gpu.Length(rd), 0.0001f);

        rd /= end;

        for (int i = 0; i < iter; i++)
        {
            float d = M(ro + (rd * t));

            shade = Gpu.Min(shade, k * d / t);
            t += Gpu.Clamp(d, 0.01f, 0.25f);

            if (d < 0.0f || t > end)
            {
                break;
            }
        }

        return Gpu.Max(shade, 0.0f);
    }

    // I keep a collection of occlusion routines... OK, that sounded really nerdy. :)
    // Anyway, I like this one. I'm assuming it's based on IQ's original.
    private float CalculateAO(in Float3 p, in Float3 n)
    {
        float sca = 2.0f;
        float occ = 0.0f;

        for (int i = (int)Gpu.Min(time, 0f); i < 5; i++)
        {
            float hr = (i + 1f) * 0.15f / 5.0f;
            float d = M(p + (n * hr));

            occ += (hr - d) * sca;
            sca *= 0.7f;

            if (sca > 1e5f)
            {
                break;
            }
        }

        return Gpu.Clamp(1.0f - occ, 0.0f, 1.0f);
    }

    // Standard normal function.
    private static Float3 Normal(in Float3 p)
    {
        Float2 e = new(0.001f, 0);

        return Gpu.Normalize(new Float3(
            M(p + e.XYY) - M(p - e.XYY),
            M(p + e.YXY) - M(p - e.YXY),
            M(p + e.YYX) - M(p - e.YYX)));
    }

    /// <inheritdoc/>
    public Float4 Execute()
    {
        Int2 coords = new(ThreadIds.X, DispatchSize.Y - ThreadIds.Y);
        // port: (Float2) cast of the Int2 coords (HLSL converts implicitly).
        Float2 u = ((Float2)coords - ((Float2)DispatchSize.XY * 0.5f)) / DispatchSize.Y;
        Float3 r = Gpu.Normalize(new Float3(u, 1));
        Float3 o = new(0, time / 2.0f, -3);
        Float3 l = o + new Float3(0.25f, 0.25f, 2.0f);

        r.YZ = Gpu.Mul(Rotate2x2(0.15f), r.YZ);
        r.XZ = Gpu.Mul(Rotate2x2(-Gpu.Cos(time * 3.14159f / 32.0f) / 8.0f), r.XZ);
        r.XY = Gpu.Mul(Rotate2x2(Gpu.Sin(time * 3.14159f / 32.0f) / 8.0f), r.XY);

        float d;
        float t = Hash21((r.XY * 57.0f) + Gpu.Fract(time)) * 0.5f;
        float glow = 0;
        // port: the object ID of the last M call in the march (the HLSL static global).
        int objID = 0;

        for (int i = 0; i < 96; i++)
        {
            d = M(o + (r * t), out objID);

            if (Gpu.Abs(d) < 0.001f)
            {
                break;
            }

            t += d * 0.7f;
            glow += 0.2f / (1.0f + (Gpu.Abs(d) * 5.0f));
        }

        int gObjID = objID;
        Float3 p = o + (r * t);
        Float3 n = Normal(p);
        Float2 uv = p.XY;
        float sc = 0.5f;
        Float2 iuv = Gpu.Floor(uv / sc) + 0.5f;

        uv -= iuv * sc;

        Float2 uv2 = p.XY + (0.5f * sc);
        Float2 iuv2 = Gpu.Floor(uv2 / sc) + 0.5f;

        uv2 -= iuv2 * sc;

        float bord = Gpu.Max(Gpu.Abs(uv.X), Gpu.Abs(uv.Y)) - (0.5f * sc);

        bord = Gpu.Abs(bord) - 0.002f;
        d = Truchet(p.XY);

        float lSc = 20.0f;
        float pat = ((Gpu.Abs(Gpu.Fract(((uv.X - uv.Y) * lSc) - 0.5f) - 0.5f) * 2.0f) - 0.5f) / lSc;
        float pat2 = ((Gpu.Abs(Gpu.Fract(((uv.X + uv.Y) * lSc) + 0.5f) - 0.5f) * 2.0f) - 0.5f) / lSc;
        // port: new FloatN(c) for the implicit scalar-to-vector conversions in Dot(..., 0.5f) and
        // in the Lerp calls below.
        float sf = Gpu.Dot(Gpu.Sin(p.XY - Gpu.Cos(p.YX * 2.0f)), new Float2(0.5f));
        float sf2 = Gpu.Dot(Gpu.Sin((p.XY * 1.5f) - Gpu.Cos(p.YX * 3.0f)), new Float2(0.5f));
        Float4 col1 = Gpu.Lerp(new Float4(1, 0.75f, 0.6f, 0), new Float4(1, 0.85f, 0.65f, 0), Gpu.Smoothstep(-0.5f, 0.5f, sf));
        Float4 col2 = Gpu.Lerp(new Float4(0.4f, 0.7f, 1, 0), new Float4(0.3f, 0.85f, 0.95f, 0), Gpu.Smoothstep(-0.5f, 0.5f, sf2) * 0.5f);

        col1 = Gpu.Pow(col1, 1.6f);
        col2 = Gpu.Lerp(col1.ZYXW, Gpu.Pow(col2, 1.4f), 0.666f);

        Float4 oCol;

        if (gObjID == 0)
        {
            oCol = Gpu.Lerp(col2, new Float4(0), (1.0f - Gpu.Smoothstep(0, 0.01f, pat2)) * 0.35f);
            oCol = Gpu.Lerp(oCol, new Float4(0), (1.0f - Gpu.Smoothstep(0, 0.01f, bord)) * 0.8f);

            if (Mod(iuv.X + iuv.Y, 2.0f) > 0.5f)
            {
                oCol *= 0.8f;
            }

            oCol = Gpu.Lerp(oCol, new Float4(0), (1.0f - Gpu.Smoothstep(0, 0.01f, d - 0.015f)) * 0.8f);
        }
        else if (gObjID == 1)
        {
            oCol = new Float4(Gpu.Lerp(1, 0, 1.0f - Gpu.Smoothstep(0, 0.01f, d + 0.05f)));

            Float4 fCol = Gpu.Lerp(col1, new Float4(0), (1.0f - Gpu.Smoothstep(0, 0.01f, pat)) * 0.35f);
            fCol = Gpu.Lerp(fCol, new Float4(0), (1.0f - Gpu.Smoothstep(0, 0.01f, bord)) * 0.8f);

            if (Mod(iuv.X + iuv.Y, 2.0f) < 0.5f)
            {
                fCol *= 0.8f;
            }

            oCol = Gpu.Lerp(oCol, fCol, 1 - Gpu.Smoothstep(0, 0.01f, d + 0.08f));
            oCol = Gpu.Lerp(oCol, new Float4(0), (1 - Gpu.Smoothstep(0, 0.01f, Gpu.Length(uv2) - 0.08f)) * 0.8f);
        }
        else
        {
            oCol = col1;

            float ht = 0.26f;

            if (Mod(iuv2.X + iuv2.Y + 1.0f, 2.0f) > 0.5f)
            {
                oCol = col2;
                ht = 0.05f;
            }

            float mark = Gpu.Length(uv2);
            float markRim = Gpu.Max(Gpu.Abs(mark - 0.07f), Gpu.Abs(p.Z + ht)) - 0.003f;

            oCol = Gpu.Lerp(oCol, new Float4(0), (1 - Gpu.Smoothstep(0, 0.01f, markRim)) * 0.8f);
            oCol = Gpu.Lerp(oCol, new Float4(0), (1 - Gpu.Smoothstep(0, 0.01f, mark - 0.015f)) * 0.8f);
        }

        Float3 ld = l - p;
        float lDist = Gpu.Length(ld);

        ld /= lDist;

        float at = 1.0f / (1.0f + (lDist * lDist * 0.125f));
        float sh = SoftShadow(p, l, n, 8);
        float ao = CalculateAO(p, n);
        float df = Gpu.Max(Gpu.Dot(n, ld), 0);
        float sp = Gpu.Pow(Gpu.Max(Gpu.Dot(Gpu.Reflect(r, n), ld), 0), 32);

        Float4 c = oCol * ((df * sh) + (sp * sh) + 0.5f) * at * ao;

        c = Gpu.Sqrt(Gpu.Max(c, 0));

        // port: alpha forced to 1. The colors carry w = 0, which Shadertoy and ComputeSharp's swap
        // chain ignore, but the gallery's RGBA texture and PNG keep it (a transparent image).
        return new Float4(c.XYZ, 1);

    }
}
