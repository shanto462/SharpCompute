// Original SharpCompute sample (MIT).
namespace SharpCompute.Samples.ComputeLab;

/// <summary>
/// Shader code the lab's pages share: a static class, called from any shader as
/// <c>ShaderMath.Hash(x)</c>. The generator translates a method to MSL from its C# source, once in
/// every shader that calls it, so this file has to be part of the project that contains the shaders
/// (link it into a second project to share it further). Nothing here keeps state: the methods are
/// ordinary C#, which is what <c>ForCpu</c> runs.
/// </summary>
public static class ShaderMath
{
    /// <summary>A full turn in radians. A <c>const</c> is inlined where it is used.</summary>
    public const float Tau = 6.2831853f;

    /// <summary>
    /// The direction to the sun and its color. A <c>static readonly</c> field becomes a program-scope
    /// constant in MSL; the call in the initializer is evaluated when the shader is generated.
    /// </summary>
    public static readonly Float3 SunDirection = Gpu.Normalize(new Float3(0.55f, 0.75f, -0.45f));
    public static readonly Float3 SunColor = new(3.2f, 2.9f, 2.5f);

    /// <summary>A well-mixed 32-bit hash: every input bit affects every output bit.</summary>
    public static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }

    /// <summary>The sky seen along <paramref name="direction"/> (a unit vector): a gradient from the horizon up, and a glow around the sun.</summary>
    public static Float3 Sky(Float3 direction)
    {
        float height = Gpu.Saturate((direction.Y * 0.5f) + 0.5f);
        Float3 gradient = Gpu.Lerp(new Float3(0.62f, 0.7f, 0.82f), new Float3(0.1f, 0.25f, 0.65f), Gpu.Smoothstep(0.48f, 0.95f, height));
        float glow = Gpu.Pow(Gpu.Saturate(Gpu.Dot(direction, SunDirection)), 48f);

        return gradient + (SunColor * (glow * 0.6f));
    }

    /// <summary>Light of any brightness as a display color in 0..1: a filmic curve, then gamma 2.</summary>
    public static Float3 Tonemap(Float3 light)
    {
        Float3 x = light * 0.6f;
        Float3 curve = (x * ((x * 2.51f) + 0.03f)) / ((x * ((x * 2.43f) + 0.59f)) + 0.14f);

        return Gpu.Sqrt(Gpu.Saturate(curve));
    }

    /// <summary>
    /// One of two values. A generic method becomes one MSL function per set of type arguments it is
    /// called with: <c>ShaderMath_Pick_float3</c>, <c>ShaderMath_Pick_float</c>.
    /// </summary>
    public static T Pick<T>(bool condition, T whenTrue, T whenFalse) => condition ? whenTrue : whenFalse;
}
