// Ported from https://www.shadertoy.com/view/WtfGDX — "Triangle Grid Contouring" by Shane.
// License: Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported (CC BY-NC-SA 3.0).
// Adapted from the ComputeSharp sample port (MIT, © Sergio Pedri) to SharpCompute.

namespace SharpCompute.Samples.Shaders;

/// <summary>
/// Utilizing a 2D simplex grid to construct the isolines of a noise function.
/// </summary>
/// <param name="time">The current time since the start of the application.</param>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct TriangleGridContouring(float time) : IComputeShader<Float4>
{
    internal const string Author = "Shane";

    internal const string SourceUrl = "https://www.shadertoy.com/view/WtfGDX";

    // Standard 2D rotation formula.
    private static Float2x2 Rotate2x2(in float a)
    {
        float c = Gpu.Cos(a);
        float s = Gpu.Sin(a);

        return new(c, -s, s, c);
    }

    // Float2 to Float2 hash.
    private Float2 Hash22(Float2 p)
    {
        float n = Gpu.Sin(Gpu.Dot(p, new Float2(41, 289)));

        p = Gpu.Fract(new Float2(262144, 32768) * n);

        return Gpu.Sin((p * 6.2831853f) + time);
    }

    // Based on IQ's gradient noise formula.
    private float N2D3G(Float2 p)
    {
        Float2 i = Gpu.Floor(p);

        p -= i;

        Float4 v = default;

        v.X = Gpu.Dot(Hash22(i), p);
        // port: new Float2(1, 0) / new Float2(0, 1) for Float2.UnitX / UnitY (SharpCompute has none).
        v.Y = Gpu.Dot(Hash22(i + new Float2(1, 0)), p - new Float2(1, 0));
        v.Z = Gpu.Dot(Hash22(i + new Float2(0, 1)), p - new Float2(0, 1));
        v.W = Gpu.Dot(Hash22(i + 1.0f), p - 1.0f);

        p = p * p * p * ((p * ((p * 6.0f) - 15.0f)) + 10.0f);

        return Gpu.Lerp(Gpu.Lerp(v.X, v.Y, p.X), Gpu.Lerp(v.Z, v.W, p.X), p.Y);
    }

    // The isofunction. Just a single noise function, but it can be more elaborate.
    private float IsoFunction(in Float2 p)
    {
        return N2D3G((p / 4.0f) + 0.07f);
    }

    // Unsigned distance to the segment joining "a" and "b".
    private static float DistLine(Float2 a, Float2 b)
    {
        b = a - b;

        float h = Gpu.Clamp(Gpu.Dot(a, b) / Gpu.Dot(b, b), 0.0f, 1.0f);

        return Gpu.Length(a - (b * h));
    }

    // Based on IQ's signed distance to the segment joining "a" and "b".
    private static float DistEdge(Float2 a, Float2 b)
    {
        return Gpu.Dot((a + b) * 0.5f, Gpu.Normalize((b - a).YX * new Float2(-1, 1)));
    }

    // Interpolating along the edge connecting vertices v1 and v2 with respect to the isovalue.
    private static Float2 Interpolate(in Float2 p1, in Float2 p2, float v1, float v2, float isovalue)
    {
        return Gpu.Lerp(p1, p2, ((isovalue - v1) / (v2 - v1) * 0.75f) + (0.25f / 2.0f));
    }

    // Isoline function.
    private static int IsoLine(
        Float3 n3,
        Float2 ip0,
        Float2 ip1,
        Float2 ip2,
        float isovalue,
        float i,
        ref Float2 p0,
        ref Float2 p1)
    {
        // port: new Float2(1e5f) for the implicit scalar-to-vector conversions.
        p0 = new Float2(1e5f);
        p1 = new Float2(1e5f);

        int iTh = 0;

        if (n3.X > isovalue)
        {
            iTh += 4;
        }

        if (n3.Y > isovalue)
        {
            iTh += 2;
        }

        if (n3.Z > isovalue)
        {
            iTh += 1;
        }

        if (iTh == 1 || iTh == 6)
        {
            p0 = Interpolate(ip1, ip2, n3.Y, n3.Z, isovalue);
            p1 = Interpolate(ip2, ip0, n3.Z, n3.X, isovalue);

        }
        else if (iTh == 2 || iTh == 5)
        {
            p0 = Interpolate(ip0, ip1, n3.X, n3.Y, isovalue);
            p1 = Interpolate(ip1, ip2, n3.Y, n3.Z, isovalue);

        }
        else if (iTh == 3 || iTh == 4)
        {
            p0 = Interpolate(ip0, ip1, n3.X, n3.Y, isovalue);
            p1 = Interpolate(ip2, ip0, n3.Z, n3.X, isovalue);
        }

        if (iTh >= 4 && iTh <= 6)
        {
            Float2 tmp = p0;

            p0 = p1;
            p1 = tmp;
        }

        if (i == 0.0f)
        {
            Float2 tmp = p0;

            p0 = p1;
            p1 = tmp;
        }

        return iTh;
    }

    private Float3 SimplexContour(Float2 p)
    {
        const float gSc = 8.0f;

        p *= gSc;

        Float2 oP = p;

        p += new Float2(N2D3G(p * 3.5f), N2D3G((p * 3.5f) + 7.3f)) * 0.015f;

        Float2 s = Gpu.Floor(p + ((p.X + p.Y) * 0.36602540378f));

        p -= s - ((s.X + s.Y) * 0.211324865f);

        float i = p.X < p.Y ? 1.0f : 0.0f;
        Float2 ioffs = new(1.0f - i, i);
        // port: new Float2(c) for the implicit scalar-to-vector conversions.
        Float2 ip0 = new(0);
        Float2 ip1 = ioffs - 0.2113248654f;
        Float2 ip2 = new(0.577350269f);
        Float2 ctr = (ip0 + ip1 + ip2) / 3.0f;

        ip0 -= ctr;
        ip1 -= ctr;
        ip2 -= ctr;
        p -= ctr;

        Float3 n3 = default;

        n3.X = IsoFunction(s);
        n3.Y = IsoFunction(s + ioffs);
        n3.Z = IsoFunction(s + 1.0f);

        float d = 1e5f;
        float d2 = 1e5f;
        float d3 = 1e5f;
        float d4 = 1e5f;
        float d5 = 1e5f;
        float isovalue = 0;
        Float2 p0 = default;
        Float2 p1 = default;
        int iTh = IsoLine(n3, ip0, ip1, ip2, isovalue, i, ref p0, ref p1);

        d = Gpu.Min(d, DistEdge(p - p0, p - p1));

        if (iTh == 7)
        {
            d = 0;
        }

        d3 = Gpu.Min(d3, DistLine(p - p0, p - p1));
        d4 = Gpu.Min(d4, Gpu.Min(Gpu.Length(p - p0), Gpu.Length(p - p1)));

        float tri = Gpu.Min(Gpu.Min(DistLine(p - ip0, p - ip1), DistLine(p - ip1, p - ip2)), DistLine(p - ip2, p - ip0));

        d5 = Gpu.Min(d5, tri);
        d5 = Gpu.Min(d5, Gpu.Length(p) - 0.02f);
        isovalue = -0.15f;

        int iTh2 = IsoLine(n3, ip0, ip1, ip2, isovalue, i, ref p0, ref p1);

        d2 = Gpu.Min(d2, DistEdge(p - p0, p - p1));

        if (iTh2 == 7)
        {
            d2 = 0.0f;
        }

        if (iTh == 7)
        {
            d2 = 1e5f;
        }

        d2 = Gpu.Max(d2, -d);
        d3 = Gpu.Min(d3, DistLine(p - p0, p - p1));
        d4 = Gpu.Min(d4, Gpu.Min(Gpu.Length(p - p0), Gpu.Length(p - p1)));
        d4 -= 0.075f;
        d3 -= 0.0125f;
        d /= gSc;
        d2 /= gSc;
        d3 /= gSc;
        d4 /= gSc;
        d5 /= gSc;

        Float3 col = new(1, 0.85f, 0.6f);
        float sf = 0.004f;

        if (d > 0.0f && d2 > 0.0f)
        {
            col = new Float3(1, 1.8f, 3) * 0.45f;
        }

        if (d > 0.0f)
        {
            col = Gpu.Lerp(col, new Float3(1, 1.85f, 3) * 0.3f, 1.0f - Gpu.Smoothstep(0.0f, sf, d2 - 0.012f));
        }

        col = Gpu.Lerp(col, new Float3(1.1f, 0.85f, 0.6f), 1.0f - Gpu.Smoothstep(0.0f, sf, d2));
        col = Gpu.Lerp(col, new Float3(1.5f, 0.9f, 0.6f) * 0.6f, 1.0f - Gpu.Smoothstep(0.0f, sf, d - 0.012f));
        col = Gpu.Lerp(col, new Float3(1, 0.8f, 0.6f) * new Float3(0.7f, 1.0f, 0.75f) * 0.95f, 1.0f - Gpu.Smoothstep(0.0f, sf, d));

        // port: new Float3(1) / new Float3(0) for new Float3(1) / new Float3(0) (SharpCompute has none).
        if (d2 > 0.0f)
        {
            col *= ((Gpu.Abs(Gpu.Dot(n3, new Float3(1))) * 1.25f) + 1.25f) / 2.0f;
        }
        else
        {
            col *= Gpu.Max(2.0f - ((Gpu.Dot(n3, new Float3(1)) + 1.45f) / 1.25f), 0.0f);
        }

        float pat = Gpu.Abs(Gpu.Fract((tri * 12.5f) + 0.4f) - 0.5f) * 2.0f;

        col *= (pat * 0.425f) + 0.75f;
        col = Gpu.Lerp(col, new Float3(0), (1.0f - Gpu.Smoothstep(0.0f, sf, d5)) * 0.95f);
        col = Gpu.Lerp(col, new Float3(0), 1.0f - Gpu.Smoothstep(0.0f, sf, d3));
        col = Gpu.Lerp(col, new Float3(0), 1.0f - Gpu.Smoothstep(0.0f, sf, d4));
        col = Gpu.Lerp(col, new Float3(1), 1.0f - Gpu.Smoothstep(0.0f, sf, d4 + 0.005f));

        Float2 q = oP * 1.5f;

        col = Gpu.Min(col, 1.0f);

        float gr = Gpu.Sqrt(Gpu.Dot(col, new Float3(0.299f, 0.587f, 0.114f))) * 1.25f;
        float ns = (((N2D3G(q * 4.0f * new Float2(1.0f / 3.0f, 3)) * 0.64f) + (N2D3G(q * 8.0f * new Float2(1.0f / 3.0f, 3)) * 0.34f)) * 0.5f) + 0.5f;

        ns = gr - ns;
        q = Gpu.Mul(q, Rotate2x2(3.14159f / 3.0f));

        float ns2 = (((N2D3G(q * 4.0f * new Float2(1.0f / 3.0f, 3)) * 0.64f) + (N2D3G(q * 8.0f * new Float2(1.0f / 3.0f, 3)) * 0.34f)) * 0.5f) + 0.5f;

        ns2 = gr - ns2;
        ns = Gpu.Smoothstep(0.0f, 1.0f, Gpu.Min(ns, ns2));
        col = Gpu.Lerp(col, col * (ns + 0.35f), 0.4f);

        return col;
    }

    /// <inheritdoc/>
    public Float4 Execute()
    {
        Int2 coordinate = new(ThreadIds.X, DispatchSize.Y - ThreadIds.Y);
        // port: (Float2) casts of the Int2 coordinate (HLSL converts implicitly).
        Float2 uv = ((Float2)coordinate - ((Float2)DispatchSize.XY * 0.5f)) / Gpu.Min(650.0f, DispatchSize.Y);
        Float2 p = Gpu.Mul(Rotate2x2(3.14159f / 12.0f), uv) + (new Float2(0.8660254f, 0.5f) * time / 16.0f);
        Float3 col = SimplexContour(p);

        uv = (Float2)coordinate / (Float2)DispatchSize.XY;

        col *= Gpu.Pow(16.0f * uv.X * uv.Y * (1.0f - uv.X) * (1.0f - uv.Y), 0.0625f) + 0.1f;

        return new(Gpu.Sqrt(Gpu.Max(col, 0.0f)), 1);
    }
}
