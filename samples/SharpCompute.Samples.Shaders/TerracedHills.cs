// Ported from https://www.shadertoy.com/view/MtdSRn — "Terraced Hills" by Shane.
// License: Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported (CC BY-NC-SA 3.0).
// Adapted from the ComputeSharp sample port (MIT, © Sergio Pedri) to SharpCompute.

namespace SharpCompute.Samples.Shaders;

/// <summary>
/// An abstract representation of some terraced hills. In essence, just a very basic terrain layout with a small code footprint.
/// </summary>
/// <param name="time">The current time since the start of the application.</param>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct TerracedHills(float time) : IComputeShader<Float4>
{
    internal const string Author = "Shane";

    internal const string SourceUrl = "https://www.shadertoy.com/view/MtdSRn";

    // 2x2 matrix rotation. Angle vector, courtesy of Fabrice
    private static Float2x2 Rot2(float th)
    {
        Float2 a = Gpu.Sin(new Float2(1.5707963f, 0) + th);

        return new(a.X, -a.Y, a.Y, a.X);
    }

    // The triangle function that Shadertoy user Nimitz has used in various triangle noise demonstrations
    private static float Tri(in float x)
    {
        return Gpu.Abs(x - Gpu.Floor(x) - 0.5f);
    }

    // 2D variant of the triangle function
    private static Float2 Tri(in Float2 x)
    {
        return Gpu.Abs(x - Gpu.Floor(x) - 0.5f);
    }

    // A simple noisey layer made up of a sawtooth combination
    private static float HLayer(Float2 p)
    {
        // port: new Float2(1) for the implicit scalar-to-vector conversion in Dot(..., 1).
        return Gpu.Dot(Tri((p / 1.5f) + Tri((p.YX / 3.0f) + 0.25f)), new Float2(1));
    }

    // Combine layers to create surfaces to render
    private static float HMap(Float2 p)
    {
        float ret = 0, a = 1;

        for (int i = 0; i < 3; i++)
        {
            ret += Gpu.Abs(a) * HLayer(p / a);
            // port: ComputeSharp's `float2x2 * float2` is HLSL mul(M, v) (rows already), hence Mul(M, p).
            p = Gpu.Mul(new Float2x2(0.866025f, -0.57735f, 0.57735f, 0.866025f), p);
            a *= -0.3f;
        }

        ret = Gpu.Smoothstep(-0.2f, 1.0f, ret * ret / 1.39f / 1.39f);

        return (ret * 0.975f) + (Tri(ret * 12.0f) * 0.05f);
    }

    // Distance function. A flat plane perturbed by a height function of some kind.
    private static float Map(Float3 p)
    {
        return (p.Y - (HMap(p.XZ) * 0.35f)) * 0.75f;
    }

    // Tetrahedral normal - courtesy of IQ
    private static Float3 Normal(in Float3 p, ref float edge, float dispatchHeight)
    {
        Float2 e = new Float2(-1.0f, 1.0f) * 0.5f / dispatchHeight;
        float d1 = Map(p + e.YXX), d2 = Map(p + e.XXY);
        float d3 = Map(p + e.XYX), d4 = Map(p + e.YYY);
        float d = Map(p);

        edge = Gpu.Abs(d1 + d2 + d3 + d4 - (d * 4.0f));
        edge = Gpu.Smoothstep(0.0f, 1.0f, Gpu.Sqrt(edge / e.Y * 2.0f));
        e = new Float2(-1.0f, 1.0f) * 0.001f;
        d1 = Map(p + e.YXX);
        d2 = Map(p + e.XXY);
        d3 = Map(p + e.XYX);
        d4 = Map(p + e.YYY);

        return Gpu.Normalize((e.YXX * d1) + (e.XXY * d2) + (e.XYX * d3) + (e.YYY * d4));
    }

    /// <inheritdoc/>
    public Float4 Execute()
    {
        Int2 coordinate = new(ThreadIds.X, DispatchSize.Y - ThreadIds.Y);
        Float3 rd = Gpu.Normalize(new Float3((Float2)coordinate - (DispatchSize.Y * 0.5f), DispatchSize.Y));

        // port: Rot2 already lists rows (the transpose of GLSL's `mat2(a, -a.y, a.x)`), so the
        // original `rot2(.35) * rd.yz` is Mul(M, v) here, which tilts the camera down at the hills.
        rd.YZ = Gpu.Mul(Rot2(0.35f), rd.YZ);



        Float3 ro = new(time * 0.4f, 0.5f, time * 0.2f);

        float t = 0, d;
        for (int i = 0; i < 96; i++)
        {
            d = Map(ro + (rd * t));

            if (Gpu.Abs(d) < 0.001f * ((t * 0.125f) + 1.0f) || t > 20.0f)
            {
                break;
            }

            t += ((Gpu.Step(1.0f, t) * 0.3f) + 0.7f) * d;
        }

        Float3 sp = ro + (rd * t);
        Float3 ld = new(-0.676f, 0.408f, 0.613f);
        float edge = 0;
        Float3 n = Normal(sp, ref edge, DispatchSize.Y);
        float dif = Gpu.Max(Gpu.Dot(ld, n), 0.0f);
        float spe = Gpu.Pow(Gpu.Max(Gpu.Dot(Gpu.Reflect(rd, n), ld), 0.0f), 16.0f);
        float sh = HMap(sp.XZ);
        float rnd = (Gpu.Fract(Gpu.Sin(Gpu.Dot(Gpu.Floor(sp.XZ * 512.0f), new Float2(41.73f, 289.67f))) * 43758.5453f) * 0.5f) + 0.5f;
        Float3 fog = Gpu.Lerp(new Float3(0.75f, 0.77f, 0.78f), new Float3(1.04f, 0.95f, 0.87f), Gpu.Pow(1.0f + Gpu.Dot(rd, ld), 3.0f) * 0.35f);
        Float3 c = Gpu.Lerp(((new Float3(1.1f, 1.05f, 1) * rnd * (dif + 0.1f) * sh) + (fog * spe)) * (1.0f - (edge * 0.7f)), fog * fog, Gpu.Min(1.0f, t * 0.3f));

        return new(c, 1);
    }
}
