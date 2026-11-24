using System;
using SharpCompute;

namespace SharpCompute.Tests.EndToEnd;

// 1. fields + explicit thread group size (the README sample)
[ThreadGroupSize(64, 1, 1)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MultiplyByTwo : IComputeShader
{
    public readonly ReadWriteBuffer<float> buffer;
    public readonly float factor;

    public MultiplyByTwo(ReadWriteBuffer<float> buffer, float factor) { this.buffer = buffer; this.factor = factor; }

    public void Execute() => buffer[ThreadIds.X] *= factor;
}

// 2. primary constructor captures, no [ThreadGroupSize], read-only buffer, vector constant
[GeneratedComputeShaderDescriptor]
public readonly partial struct Saxpy(ReadOnlyBuffer<float> x, ReadWriteBuffer<float> y, float a, Float3 offset) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        float o = (i % 3) == 0 ? offset.X : (i % 3) == 1 ? offset.Y : offset.Z;
        y[i] = a * x[i] + y[i] + o;
    }
}

// 3. 2D dispatch, DispatchSize, uint buffer
[GeneratedComputeShaderDescriptor]
public readonly partial struct WriteIndex2D(ReadWriteBuffer<uint> buffer) : IComputeShader
{
    public void Execute()
    {
        Int2 id = ThreadIds.XY;
        buffer[id.Y * DispatchSize.X + id.X] = (uint)(id.Y * DispatchSize.X + id.X);
    }
}

// 4. buffer.Length (hidden length constant), loops, static helper, float %, MathF + Gpu intrinsics, casts
[GeneratedComputeShaderDescriptor]
public readonly partial struct Kitchen(ReadWriteBuffer<float> data, ReadOnlyBuffer<int> ints, float scale, int bias, uint mask) : IComputeShader
{
    private static float Poly(float v, float k)
    {
        float acc = 0f;
        for (int i = 0; i < 4; i++)
        {
            acc = acc * v + k;
            if (acc > 100f) break;
        }
        return acc;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        int n = data.Length;
        float v = data[i];
        float r = Poly(v * 0.25f, scale) + (v % 3f) + MathF.Floor(v * 0.5f) + Gpu.Clamp(v, 1f, 5f) + MathF.Sqrt(MathF.Abs(v));
        int j = (ints[(i + 1) % n] + bias) / 2;
        uint u = ((uint)j ^ mask) >> 1;
        int w = 0;
        while (w < 3) { w++; if (w == 2) continue; r += 1f; }
        do { r -= 0.5f; w--; } while (w > 0);
        data[i] = r + j + (float)u + (i == n - 1 ? 1000f : 0f);
    }
}

// 5. vector buffer elements (12-byte stride), swizzles, vector operators, vector constant
[GeneratedComputeShaderDescriptor]
public readonly partial struct Vectors(ReadWriteBuffer<Float3> points, ReadOnlyBuffer<Float2> uv, Float4 tint, Int2 shift) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        Float3 p = points[i];
        Float2 t = uv[i] * 2f - new Float2(1f, 0.5f);
        p.XY = p.YX + t;
        p.Z = Gpu.Dot(p, new Float3(tint.X, tint.Y, tint.Z)) + tint.W;
        Int2 s = shift << 1;
        p += new Float3(s.X, s.Y, 0f);
        Float3 q = (p % new Float3(4f, 4f, 4f)) * 0.5f;
        points[i] = -q + Gpu.Max(p, new Float3(0f));
        points[i].X += 1f;
    }
}

// 6. user struct elements and a user struct constant
public struct Particle
{
    public Float3 Position;
    public float Mass;
    public Float2 Uv;
    public int Id;
}

public struct Params
{
    public float Dt;
    public Float3 Gravity;
    public uint Flags;
}

[ThreadGroupSize(8, 8, 1)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct Step(ReadWriteBuffer<Particle> particles, Params p) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.Y * DispatchSize.X + ThreadIds.X;
        Particle part = particles[i];
        part.Position += p.Gravity * p.Dt * part.Mass;
        part.Uv = part.Uv.YX;
        part.Id += (int)p.Flags + GroupIds.X + ThreadGroupIds.Y * 100;
        particles[i] = part;
    }
}
