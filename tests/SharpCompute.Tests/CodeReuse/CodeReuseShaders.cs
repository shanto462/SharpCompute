using SharpCompute;

namespace SharpCompute.Tests.CodeReuse;

// Code that shaders share, and the shaders that use it. Everything here runs unchanged on the GPU
// (For) and on the CPU (ForCpu): the shared code is ordinary C#.

/// <summary>Static helpers of a type of their own: constants, plain and generic methods, a property, an extension method.</summary>
public static class SharedMath
{
    public const float Tau = 6.2831853f;

    public static readonly Float3 LumaWeights = new(0.2126f, 0.7152f, 0.0722f);

    public static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }

    public static float Unit(uint x) => (Hash(x) >> 8) * (1f / 16777216f);

    public static float Luminance(Float3 color) => Gpu.Dot(color, LumaWeights);

    public static Float3 Palette(float t) => (new Float3(Gpu.Cos(t * Tau), Gpu.Sin(t * Tau), t) * 0.5f) + 0.5f;

    public static T Pick<T>(bool condition, T whenTrue, T whenFalse) => condition ? whenTrue : whenFalse;

    public static void Swap<T>(ref T a, ref T b)
    {
        T held = a;
        a = b;
        b = held;
    }

    /// <summary>The flat index of the thread: thread built-ins in a helper of another type.</summary>
    public static int Cell => (ThreadIds.Y * DispatchSize.X) + ThreadIds.X;

    /// <summary>An atomic operation in a helper of another type, on a buffer it is handed.</summary>
    public static int Count(ReadWriteBuffer<int> bins, int bin) => Gpu.AtomicAdd(ref bins[bin], 1);

    public static float Squared(this float value) => value * value;
}

/// <summary>A second type with a method called <c>Hash</c>: the two become two MSL functions.</summary>
public static class OtherMath
{
    public static uint Hash(uint x) => x * 2654435761u;
}

/// <summary>Static helpers, generic ones at two types, a static property that reads built-ins, and an extension method.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SharedColors(ReadWriteBuffer<Float3> output, float gain) : IComputeShader
{
    public void Execute()
    {
        int i = SharedMath.Cell;
        float t = SharedMath.Unit((uint)i);
        Float3 color = SharedMath.Palette(t) * gain;
        float low = t;
        float high = SharedMath.Luminance(color);

        if (low > high)
        {
            SharedMath.Swap(ref low, ref high);
        }

        Float3 other = new(low, high, (high - low).Squared());
        SharedMath.Swap(ref color, ref other);
        output[i] = SharedMath.Pick(i % 3 == 0, color, other) + SharedMath.Pick(i % 2 == 0, 0f, 1f);
    }
}

/// <summary>The same helpers in a second shader, with an atomic in one of them and two methods called <c>Hash</c>.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SharedBins(ReadWriteBuffer<int> bins, ReadWriteBuffer<uint> hashes, int binCount) : IComputeShader
{
    public void Execute()
    {
        int i = SharedMath.Cell;
        uint a = SharedMath.Hash((uint)i);
        uint b = OtherMath.Hash((uint)i);

        hashes[i] = a ^ b;
        SharedMath.Count(bins, (int)(a % (uint)binCount));
    }
}

/// <summary>A user struct with everything a struct can share: constructors, methods that read and that modify, a property, operators and conversions.</summary>
public struct Body
{
    public Float2 Position;
    public Float2 Velocity;
    public float Mass = 1f;

    public Body(Float2 position, Float2 velocity)
    {
        Position = position;
        Velocity = velocity;
    }

    public Body(Float2 position) : this(position, new Float2(0f, 0f)) => Mass = 2f;

    public readonly float Energy => 0.5f * Mass * Gpu.Dot(Velocity, Velocity);

    public readonly Float2 Momentum() => Velocity * Mass;

    public void Step(float dt) => Position += Velocity * dt;

    public void Kick(Float2 impulse)
    {
        Velocity += impulse / Mass;
        Step(0.5f);
    }

    public static Body operator +(Body a, Body b) => new((a.Position + b.Position) * 0.5f, a.Velocity + b.Velocity);

    public static Body operator -(Body body) => new(body.Position, -body.Velocity);

    public static explicit operator float(Body body) => body.Mass;

    public static implicit operator Body(Float2 position) => new(position);
}

/// <summary>
/// Methods of <see cref="Body"/> called on a local, on elements of a read-write and of a read-only
/// buffer, on a threadgroup element and on captured state, plus its operators, conversions and constructors.
/// </summary>
[ThreadGroupSize(8)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MoveBodies(ReadWriteBuffer<Body> bodies, ReadOnlyBuffer<Body> seeds, ReadWriteBuffer<float> report, Body anchor, float dt) : IComputeShader
{
    [GroupShared]
    private static readonly Body[] scratch = new Body[8];

    public void Execute()
    {
        int i = ThreadIds.X;
        Body local = seeds[i];

        local.Kick(new Float2(0.5f, -0.25f));
        bodies[i].Step(dt);
        bodies[i].Kick(local.Momentum());
        scratch[GroupIds.X] = local;
        scratch[GroupIds.X].Step(dt * 2f);

        // Calls C# makes on a copy: nothing they do is kept, on the CPU or on the GPU.
        seeds[i].Step(100f);
        anchor.Kick(new Float2(100f, 100f));

        Body merged = -(bodies[i] + scratch[GroupIds.X]);
        merged += anchor;
        Body placed = local.Position;
        Body fresh = new Body(merged.Position, placed.Velocity);

        report[(i * 4) + 0] = merged.Energy + anchor.Energy;
        report[(i * 4) + 1] = (float)placed + placed.Position.X + fresh.Mass;
        report[(i * 4) + 2] = seeds[i].Position.X + seeds[i].Energy + anchor.Position.Y;
        report[(i * 4) + 3] = scratch[GroupIds.X].Position.Y + merged.Momentum().X;
    }
}

/// <summary>Instance members of the shader itself, called on <c>this</c>: a method, properties, an indexer and a generic method.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Curve(ReadWriteBuffer<float> output, float scale, float offset) : IComputeShader
{
    private float Slope => scale * 2f;

    private static float Unit => 1f;

    private float this[int index] => (index * Slope) + offset;

    private T Choose<T>(T a, T b) => offset > 0f ? a : b;

    private float Apply(float x) => this[(int)x] + this.Choose(Unit, -Unit);

    public void Execute() => output[ThreadIds.X] = this.Apply(ThreadIds.X) + Choose(new Float2(3f, 4f), new Float2(5f, 6f)).Y;
}
