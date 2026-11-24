using System;
using SharpCompute;

namespace Other { public struct Item { public float A; public Float3 V; } }

namespace SharpCompute.Tests.EndToEnd
{
    public struct Item { public int K; public Float2 W; }
    public struct Outer { public Item Inner; public Other.Item Far; public Half H; public float Tail; }

    // E1: identifiers that are MSL / C++ keywords or metal:: names
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct Keywords(ReadWriteBuffer<float> buffer, float constant, int device) : IComputeShader
    {
        public void Execute()
        {
            int kernel = ThreadIds.X;
            float half = buffer[kernel] * 0.5f;
            float thread = half + constant;
            float mix = thread * device;
            float vertex = mix + 1f, fragment = vertex * 2f;
            float template = fragment, namespace_ = template, auto = namespace_, signed = auto, unsigned = signed, register = unsigned, typedef = register, union = typedef, inline = union, friend = inline, asm = friend;
            buffer[kernel] = asm;
        }
    }

    // E2/E3: same simple struct name in two namespaces, nested structs, Half field
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct Nested(ReadWriteBuffer<Outer> items, Other.Item extra) : IComputeShader
    {
        public void Execute()
        {
            int i = ThreadIds.X;
            Outer o = items[i];
            o.Inner.K += 3;
            o.Inner.W = o.Inner.W.YX * 2f;
            o.Far.V = o.Far.V + extra.V;
            o.Far.A = extra.A - o.Far.A;
            o.Tail = (float)o.H * 4f;
            o.H = (Half)(o.Tail + 0.5f);
            items[i] = o;
        }
    }

    // E5/E6: integer semantics and implicit conversions
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct Ints(ReadWriteBuffer<int> a, ReadWriteBuffer<uint> u, ReadWriteBuffer<float> f) : IComputeShader
    {
        public void Execute()
        {
            int i = ThreadIds.X;
            int v = a[i];
            int q = v / 3, r = v % 3, s = v >> 2, m = v * 1000003, n = -v, b = ~v & 0xFF | 0x100 ^ v;
            uint w = u[i];
            uint x = w / 3u + w % 7u + (w >> 3) + (w << 29) + (uint)v;
            float g = v / 2;          // integer division, then converted
            float h = v / 2f;         // float division
            float k = i;              // implicit int -> float
            a[i] = q + r * 10 + s * 100 + (m ^ n) + b + (int)w + (int)(h * 3.7f) + (int)(-h * 3.7f);
            a[i] += 1;
            u[i] = x;
            u[i] >>= 1;
            f[i] = g + h * 0.5f + k + (w > 100u ? 1 : 0) + (uint)i;
        }
    }

    // E7: literals and constants
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct Literals(ReadWriteBuffer<float> o, ReadWriteBuffer<int> oi) : IComputeShader
    {
        private const float Third = 1f / 3f;
        public void Execute()
        {
            int i = ThreadIds.X;
            const float tiny = 1e-7f;
            const int big = 0x7FFFFFF0;
            float v = 0f;
            if (i == 0) v = tiny;
            else if (i == 1) v = float.MaxValue;
            else if (i == 2) v = float.Epsilon;
            else if (i == 3) v = MathF.PI;
            else if (i == 4) v = float.NaN;
            else if (i == 5) v = float.PositiveInfinity;
            else if (i == 6) v = float.NegativeInfinity;
            else if (i == 7) v = Third;
            else if (i == 8) v = -0f;
            else if (i == 9) v = 16777217f;
            else if (i == 10) v = 0.1f;
            else if (i == 11) v = 123456789.0f;
            else if (i == 12) v = 3.4028235E+38f;
            else if (i == 13) v = float.MinValue;
            else if (i == 14) v = 1.17549435E-38f;
            else if (i == 15) v = MathF.E;
            o[i] = v;
            int w = 0;
            if (i == 0) w = int.MinValue;
            else if (i == 1) w = int.MaxValue;
            else if (i == 2) w = big;
            else if (i == 3) w = unchecked((int)uint.MaxValue);
            else if (i == 4) w = -2147483647;
            else if (i == 5) w = 'A';
            else if (i == 6) w = unchecked((int)0xFFFF_FFF0u);
            else if (i == 7) w = 1_000_000;
            oi[i] = w;
        }
    }

    // E8/E9: logic, bools, nested ternaries, helper overloads and helper chains
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct Logic(ReadWriteBuffer<float> o, int threshold) : IComputeShader
    {
        private static float Pick(float a, float b, bool first) => first ? a : b;
        private static int Pick(int a, int b) => a > b ? a : b;
        private static float Chain(float v, int depth) { float t = Pick(v, -v, depth > 2); return t + Pick(depth, 1); }
        private static bool Odd(int v) { return (v & 1) != 0; }

        public void Execute()
        {
            int i = ThreadIds.X;
            bool big = i > threshold, odd = Odd(i);
            bool both = big && odd || !big && !odd;
            float v = both ? (big ? 3f : 2f) : (odd ? 1f : 0f);
            if (!(i < 3) && (i != 7 || big) && i <= 100 && i >= 0) v += 10f;
            v += Chain(v, i % 5);
            bool flip = false;
            for (int a = 0, b = 6; a < b; a++, b--) { flip = !flip; if (a == 1) continue; v += a * b; }
            o[i] = flip ? -v : v;
        }
    }

    // E15/E16/E17: swizzles on temporaries and nested members, default values, conversions
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct Swizzles(ReadWriteBuffer<Float4> o, ReadWriteBuffer<Outer> items) : IComputeShader
    {
        public void Execute()
        {
            int i = ThreadIds.X;
            Float4 v = o[i];
            var a = (v.XYZ + new Float3(1f, 2f, 3f)).ZYX;
            Float2 b = new Float3(v.W, 2f, 3f).ZX;
            Float3 z = default;
            Float2 n = new Float2();
            Int2 ii = (Int2)(v.XY * 10f);
            UInt3 uu = (UInt3)new Int3(ii.X, ii.Y, 7);
            Float2 back = (Float2)ii + (Float2)uu.XY;
            v.XW = b;
            v.YZ += a.XY + n + z.XY + back * 0.01f;
            o[i] = v;
            o[i].WZYX = o[i];
            o[i].X += 1f;
            items[i].Inner.W.Y = 5f;
            items[i].Far.V.XZ = new Float2(1f, 2f);
            items[i].Inner.K++;
        }
    }

    // E18: Math / MathF / Gpu mappings
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct MathMap(ReadWriteBuffer<float> o, ReadOnlyBuffer<float> x) : IComputeShader
    {
        public void Execute()
        {
            int i = ThreadIds.X;
            float v = x[i];
            float r = MathF.Sin(v) + MathF.Cos(v) + MathF.Tan(v * 0.1f) + MathF.Exp(v * 0.1f) + MathF.Log(MathF.Abs(v) + 1f) + MathF.Pow(MathF.Abs(v), 1.5f)
                    + MathF.Min(v, 2f) + MathF.Max(v, -2f) + MathF.Ceiling(v) + MathF.Round(v) + MathF.Truncate(v) + MathF.Atan2(v, 2f) + MathF.Sign(v)
                    + Math.Min(i, 5) + Math.Max(i, 3) + Math.Abs(i - 10) + Math.Clamp(v, -1f, 1f) + Math.Clamp(i, 2, 4)
                    + Gpu.Lerp(v, 1f, 0.25f) + Gpu.Smoothstep(0f, 4f, v) + Gpu.Saturate(v) + Gpu.Fract(v) + Gpu.Step(1f, v) + Gpu.Length(new Float2(v, 1f));
            o[i] = r;
        }
    }

    // E10/E13/E14: no buffers bound from constants only is not possible to observe, so: fields + primary ctor mixed, an unused capture
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct Mixed(ReadWriteBuffer<float> o, float unusedScalar, ReadOnlyBuffer<int> unusedBuffer) : IComputeShader
    {
        private readonly float extra = 2.5f;
        
        public void Execute() => o[ThreadIds.X] = ThreadIds.X * extra + DispatchSize.X;
    }
}
