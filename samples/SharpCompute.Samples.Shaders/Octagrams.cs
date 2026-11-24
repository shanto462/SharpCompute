// Ported from https://www.shadertoy.com/view/tlVGDt — "Octagrams" by whisky_shusuky.
// License: Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported (CC BY-NC-SA 3.0).
// Adapted from the ComputeSharp sample port (MIT, © Sergio Pedri) to SharpCompute.

namespace SharpCompute.Samples.Shaders;

/// <summary>
/// A shader creating an octagram animation, inspired by arabesque.
/// </summary>
/// <param name="time">The current time since the start of the application.</param>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct Octagrams(float time) : IComputeShader<Float4>
{
    internal const string Author = "whisky_shusuky";

    internal const string SourceUrl = "https://www.shadertoy.com/view/tlVGDt";

    private static Float2x2 Rotate(float a)
    {
        float c = Gpu.Cos(a);
        float s = Gpu.Sin(a);

        return new(c, s, -s, c);
    }

    private static float Box(Float3 pos, float scale)
    {
        static float SDBox(Float3 p, Float3 b)
        {
            Float3 q = Gpu.Abs(p) - b;

            return Gpu.Length(Gpu.Max(q, 0.0f)) +
                   Gpu.Min(Gpu.Max(q.X, Gpu.Max(q.Y, q.Z)), 0.0f);
        }

        pos *= scale;

        float b = SDBox(pos, new(0.4f, 0.4f, 0.1f)) / 1.5f;

        pos.XY *= 5.0f;
        pos.Y -= 3.5f;
        pos.XY = Gpu.Mul(pos.XY, Rotate(0.75f));

        return -b;
    }

    private static float BoxSet(Float3 pos, float gTime)
    {
        Float3 pos_origin = pos;

        pos = pos_origin;
        pos.Y += Gpu.Sin(gTime * 0.4f) * 2.5f;
        pos.XY = Gpu.Mul(pos.XY, Rotate(0.8f));

        float box1 = Box(pos, 2.0f - (Gpu.Abs(Gpu.Sin(gTime * 0.4f)) * 1.5f));

        pos = pos_origin;
        pos.Y -= Gpu.Sin(gTime * 0.4f) * 2.5f;
        pos.XY = Gpu.Mul(pos.XY, Rotate(0.8f));

        float box2 = Box(pos, 2.0f - (Gpu.Abs(Gpu.Sin(gTime * 0.4f)) * 1.5f));

        pos = pos_origin;
        pos.X += Gpu.Sin(gTime * 0.4f) * 2.5f;
        pos.XY = Gpu.Mul(pos.XY, Rotate(0.8f));

        float box3 = Box(pos, 2.0f - (Gpu.Abs(Gpu.Sin(gTime * 0.4f)) * 1.5f));

        pos = pos_origin;
        pos.X -= Gpu.Sin(gTime * 0.4f) * 2.5f;
        pos.XY = Gpu.Mul(pos.XY, Rotate(0.8f));

        float box4 = Box(pos, 2.0f - (Gpu.Abs(Gpu.Sin(gTime * 0.4f)) * 1.5f));

        pos = pos_origin;
        pos.XY = Gpu.Mul(pos.XY, Rotate(0.8f));

        float box5 = Box(pos, 0.5f) * 6.0f;

        pos = pos_origin;

        float box6 = Box(pos, 0.5f) * 6.0f;
        float result = Gpu.Max(Gpu.Max(Gpu.Max(Gpu.Max(Gpu.Max(box1, box2), box3), box4), box5), box6);

        return result;
    }

    /// <inheritdoc/>
    public Float4 Execute()
    {
        // port: (Float2) cast of the Int2 DispatchSize.XY (HLSL converts implicitly).
        Float2 p = (((Float2)ThreadIds.XY * 2.0f) - (Float2)DispatchSize.XY) / Gpu.Min(DispatchSize.X, DispatchSize.Y);

        Float3 ro = new(0.0f, -0.2f, time * 4.0f);
        Float3 ray = Gpu.Normalize(new Float3(p, 1.5f));

        ray.XY = Gpu.Mul(ray.XY, Rotate(Gpu.Sin(time * 0.03f) * 5.0f));
        ray.YZ = Gpu.Mul(ray.YZ, Rotate(Gpu.Sin(time * 0.05f) * 0.2f));

        float t = 0.1f;
        float ac = 0.0f;

        for (int i = 0; i < 99; i++)
        {
            static Float3 Mod(Float3 x, float y)
            {
                return x - (y * Gpu.Floor(x / y));
            }

            Float3 pos = ro + (ray * t);

            pos = Mod(pos - 2.0f, 4.0f) - 2.0f;

            float gTime = time - (i * 0.01f);
            float d = BoxSet(pos, gTime);

            d = Gpu.Max(Gpu.Abs(d), 0.01f);
            ac += Gpu.Exp(-d * 23.0f);
            t += d * 0.55f;
        }

        Float3 col = (ac * 0.02f) + new Float3(0.0f, 0.2f * Gpu.Abs(Gpu.Sin(time)), 0.5f + (Gpu.Sin(time) * 0.2f));
        Float4 color = new(col, 1);

        return color;
    }
}
