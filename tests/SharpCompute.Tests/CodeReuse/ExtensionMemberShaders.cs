using SharpCompute;

namespace SharpCompute.Tests.CodeReuse;

/// <summary>A user struct with an extension operator, next to the one it declares itself.</summary>
public struct Tally
{
    public float Sum;
    public float Count;
}

/// <summary>
/// C# 14 static extension members: a property, a method and operators, on an [MslType] vector and
/// on a user struct. Each is a static method, translated like any static helper.
/// </summary>
public static class ExtensionMath
{
    extension(Float3)
    {
        public static Float3 Unit => new(1f, 0f, 0f);

        public static Float3 Ramp(float x) => new(x, x + 1f, x + 2f);

        /// <summary>A rotating product, so that the operand order shows in the result.</summary>
        public static Float3 operator ^(Float3 a, Float3 b) => new(a.X * b.Y, a.Y * b.Z, a.Z * b.X);
    }

    extension(Tally)
    {
        public static Tally Empty => default;

        public static Tally operator +(Tally a, Tally b)
        {
            Tally sum = default;
            sum.Sum = a.Sum + b.Sum;
            sum.Count = a.Count + b.Count + 1000f;
            return sum;
        }
    }
}

/// <summary>Static extension members and extension operators, written to four slots per thread.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct StaticExtensions(ReadWriteBuffer<float> output) : IComputeShader
{
    public const int Slots = 4;

    public void Execute()
    {
        int i = ThreadIds.X;
        Float3 a = Float3.Ramp(i) + Float3.Unit;
        Float3 b = new(5f, 7f, 11f);
        Float3 c = a ^ b;

        Tally t = Tally.Empty;
        t.Sum = c.X;
        t.Count = 1f;
        Tally u = t + t;

        output[(i * Slots) + 0] = c.X;
        output[(i * Slots) + 1] = c.Y + c.Z;
        output[(i * Slots) + 2] = u.Sum;
        output[(i * Slots) + 3] = u.Count;
    }
}
