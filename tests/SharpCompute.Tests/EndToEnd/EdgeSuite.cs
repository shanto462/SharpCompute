using System;
using System.Linq;
using System.Text;
using SharpCompute;

namespace SharpCompute.Tests.EndToEnd;

internal static class EdgeSuite
{
    static int failures;
    static StringBuilder log = new();
    static void Check(string name, int bad, string first) { log.AppendLine($"[{(bad == 0 ? "PASS" : "FAIL")}] {name}: {bad} mismatches {first}"); if (bad != 0) failures++; }
    static void Try(string name, Action a) { try { a(); } catch (Exception e) { string[] lines = e.Message.Split('\n'); log.AppendLine($"[FAIL] {name}: {e.GetType().Name}: {string.Join("\n        ", lines.Take(14))}"); failures++; } }
    static bool Near(float a, float b) => (float.IsNaN(a) && float.IsNaN(b)) || a == b || MathF.Abs(a - b) <= MathF.Abs(b) * 2e-5f + 1e-5f;
    static bool Same(float a, float b) => FloatBits.Same(a, b);

    public static int Run(GraphicsDevice device, StringBuilder output)
    {
        failures = 0;
        log = output;
        Try("Keywords", () =>
        {
            float[] d = Enumerable.Range(0, 64).Select(i => i * 1.5f).ToArray();
            using var b = device.AllocateReadWriteBuffer<float>(d);
            device.For(64, new Keywords(b, 3f, 4));
            float[] g = b.ToArray(); int bad = 0; string first = "";
            for (int i = 0; i < 64; i++) { float e = ((d[i] * 0.5f + 3f) * 4 + 1f) * 2f; if (g[i] != e) { bad++; if (first == "") first = $"i={i} gpu={g[i]} cpu={e}"; } }
            Check("Keywords (MSL keyword identifiers)", bad, first);
        });

        Try("Nested", () =>
        {
            Outer[] src = Enumerable.Range(0, 50).Select(i => new Outer { Inner = new Item { K = i, W = new Float2(i, i + 0.5f) }, Far = new Other.Item { A = i * 2f, V = new Float3(i, -i, 1f) }, H = (Half)(i * 0.25f), Tail = -1f }).ToArray();
            using var items = device.AllocateReadWriteBuffer<Outer>(src);
            var extra = new Other.Item { A = 100f, V = new Float3(1f, 2f, 3f) };
            device.For(50, new Nested(items, extra));
            Outer[] g = items.ToArray(); int bad = 0; string first = "";
            for (int i = 0; i < 50; i++)
            {
                Outer o = src[i]; o.Inner.K += 3; o.Inner.W = o.Inner.W.YX * 2f; o.Far.V = o.Far.V + extra.V; o.Far.A = extra.A - o.Far.A; o.Tail = (float)o.H * 4f; o.H = (Half)(o.Tail + 0.5f);
                bool ok = g[i].Inner.K == o.Inner.K && g[i].Inner.W.X == o.Inner.W.X && g[i].Inner.W.Y == o.Inner.W.Y && g[i].Far.A == o.Far.A && g[i].Far.V.X == o.Far.V.X && g[i].Far.V.Y == o.Far.V.Y && g[i].Far.V.Z == o.Far.V.Z && g[i].Tail == o.Tail && g[i].H == o.H;
                if (!ok) { bad++; if (first == "") first = $"i={i} gpu=(K {g[i].Inner.K}, W {g[i].Inner.W}, A {g[i].Far.A}, V {g[i].Far.V}, H {g[i].H}, Tail {g[i].Tail}) cpu=(K {o.Inner.K}, W {o.Inner.W}, A {o.Far.A}, V {o.Far.V}, H {o.H}, Tail {o.Tail})"; }
            }
            Check($"Nested (same-named structs, nested structs, Half; sizeof(Outer)={System.Runtime.CompilerServices.Unsafe.SizeOf<Outer>()})", bad, first);
        });

        Try("Ints", () =>
        {
            int n = 200;
            int[] av = Enumerable.Range(0, n).Select(i => (i - 100) * 37 + (i % 2 == 0 ? 0 : -1000003)).ToArray(); uint[] uv = Enumerable.Range(0, n).Select(i => (uint)(i * 0x01234567u + 99u)).ToArray();
            using var a = device.AllocateReadWriteBuffer<int>(av); using var u = device.AllocateReadWriteBuffer<uint>(uv); using var f = device.AllocateReadWriteBuffer<float>(n);
            device.For(n, new Ints(a, u, f));
            int[] ga = a.ToArray(); uint[] gu = u.ToArray(); float[] gf = f.ToArray(); int bad = 0; string first = "";
            for (int i = 0; i < n; i++)
            {
                unchecked
                {
                    int v = av[i];
                    int q = v / 3, r = v % 3, s = v >> 2, m = v * 1000003, nn = -v, b = ~v & 0xFF | 0x100 ^ v;
                    uint w = uv[i];
                    uint x = w / 3u + w % 7u + (w >> 3) + (w << 29) + (uint)v;
                    float g = v / 2; float h = v / 2f; float k = i;
                    int ea = q + r * 10 + s * 100 + (m ^ nn) + b + (int)w + (int)(h * 3.7f) + (int)(-h * 3.7f); ea += 1;
                    uint eu = x; eu >>= 1;
                    float ef = g + h * 0.5f + k + (w > 100u ? 1 : 0) + (uint)i;
                    if (ga[i] != ea || gu[i] != eu || !Near(gf[i], ef)) { bad++; if (first == "") first = $"i={i} v={v} w={w}: gpu=({ga[i]}, {gu[i]}, {gf[i]}) cpu=({ea}, {eu}, {ef})"; }
                }
            }
            Check("Ints (int/uint semantics, implicit conversions)", bad, first);
        });

        Try("Literals", () =>
        {
            using var o = device.AllocateReadWriteBuffer<float>(16); using var oi = device.AllocateReadWriteBuffer<int>(16);
            device.For(16, new Literals(o, oi));
            float[] ef = [1e-7f, float.MaxValue, float.Epsilon, MathF.PI, float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1f / 3f, -0f, 16777217f, 0.1f, 123456789.0f, 3.4028235E+38f, float.MinValue, 1.17549435E-38f, MathF.E];
            // Every thread stores to both buffers, so both have the grid's length; threads 8 to 15 store an int of 0.
            int[] ei = [int.MinValue, int.MaxValue, 0x7FFFFFF0, -1, -2147483647, 65, unchecked((int)0xFFFFFFF0u), 1_000_000, 0, 0, 0, 0, 0, 0, 0, 0];
            float[] g = o.ToArray(); int[] gi = oi.ToArray(); int bad = 0; string first = "";
            for (int i = 0; i < 16; i++) if (!Same(g[i], ef[i])) { bad++; first += $" f[{i}] gpu={g[i]:R} cpu={ef[i]:R};"; }
            for (int i = 0; i < 16; i++) if (gi[i] != ei[i]) { bad++; first += $" i[{i}] gpu={gi[i]} cpu={ei[i]};"; }
            Check("Literals (float/int literal round-trip)", bad, first);
        });

        Try("Logic", () =>
        {
            int n = 120; using var o = device.AllocateReadWriteBuffer<float>(n);
            device.For(n, new Logic(o, 40));
            float[] g = o.ToArray(); int bad = 0; string first = "";
            for (int i = 0; i < n; i++)
            {
                bool big = i > 40, odd = (i & 1) != 0; bool both = big && odd || !big && !odd;
                float v = both ? (big ? 3f : 2f) : (odd ? 1f : 0f);
                if (!(i < 3) && (i != 7 || big) && i <= 100 && i >= 0) v += 10f;
                int depth = i % 5; float t = depth > 2 ? v : -v; v += t + (depth > 1 ? depth : 1);
                bool flip = false;
                for (int a = 0, b = 6; a < b; a++, b--) { flip = !flip; if (a == 1) continue; v += a * b; }
                float e = flip ? -v : v;
                if (g[i] != e) { bad++; if (first == "") first = $"i={i} gpu={g[i]} cpu={e}"; }
            }
            Check("Logic (bools, ternaries, helper overloads, multi-var for)", bad, first);
        });

        Try("Swizzles", () =>
        {
            int n = 40;
            Float4[] src = Enumerable.Range(0, n).Select(i => new Float4(i * 0.5f, 1f + i, -i * 0.25f, 2f)).ToArray();
            Outer[] its = Enumerable.Range(0, n).Select(i => new Outer { Inner = new Item { K = i, W = new Float2(1f, 2f) }, Far = new Other.Item { A = 9f, V = new Float3(7f, 8f, 9f) }, H = (Half)1f, Tail = 3f }).ToArray();
            using var o = device.AllocateReadWriteBuffer<Float4>(src); using var items = device.AllocateReadWriteBuffer<Outer>(its);
            device.For(n, new Swizzles(o, items));
            Float4[] g = o.ToArray(); Outer[] gi = items.ToArray(); int bad = 0; string first = "";
            for (int i = 0; i < n; i++)
            {
                Float4 v = src[i];
                var a = (v.XYZ + new Float3(1f, 2f, 3f)).ZYX; Float2 b = new Float3(v.W, 2f, 3f).ZX; Float3 z = default; Float2 nn = new Float2();
                Int2 ii = (Int2)(v.XY * 10f); UInt3 uu = (UInt3)new Int3(ii.X, ii.Y, 7); Float2 back = (Float2)ii + (Float2)uu.XY;
                v.XW = b; v.YZ += a.XY + nn + z.XY + back * 0.01f;
                Float4 e = v; e.WZYX = e; e.X += 1f;
                bool ok = Near(g[i].X, e.X) && Near(g[i].Y, e.Y) && Near(g[i].Z, e.Z) && Near(g[i].W, e.W)
                    && gi[i].Inner.W.X == 1f && gi[i].Inner.W.Y == 5f && gi[i].Far.V.X == 1f && gi[i].Far.V.Y == 8f && gi[i].Far.V.Z == 2f && gi[i].Inner.K == i + 1 && gi[i].Tail == 3f;
                if (!ok) { bad++; if (first == "") first = $"i={i} gpu={g[i]} cpu={e}; item W={gi[i].Inner.W} V={gi[i].Far.V} K={gi[i].Inner.K}"; }
            }
            Check("Swizzles (temporaries, nested member writes, default, conversions)", bad, first);
        });

        Try("MathMap", () =>
        {
            int n = 64; float[] xs = Enumerable.Range(0, n).Select(i => i * 0.173f - 5f).ToArray();
            using var o = device.AllocateReadWriteBuffer<float>(n); using var x = device.AllocateReadOnlyBuffer<float>(xs);
            device.For(n, new MathMap(o, x));
            float[] g = o.ToArray(); int bad = 0; string first = "";
            for (int i = 0; i < n; i++)
            {
                float v = xs[i];
                float r = MathF.Sin(v) + MathF.Cos(v) + MathF.Tan(v * 0.1f) + MathF.Exp(v * 0.1f) + MathF.Log(MathF.Abs(v) + 1f) + MathF.Pow(MathF.Abs(v), 1.5f)
                    + MathF.Min(v, 2f) + MathF.Max(v, -2f) + MathF.Ceiling(v) + MathF.Round(v) + MathF.Truncate(v) + MathF.Atan2(v, 2f) + MathF.Sign(v)
                    + Math.Min(i, 5) + Math.Max(i, 3) + Math.Abs(i - 10) + Math.Clamp(v, -1f, 1f) + Math.Clamp(i, 2, 4)
                    + Gpu.Lerp(v, 1f, 0.25f) + Gpu.Smoothstep(0f, 4f, v) + Gpu.Saturate(v) + Gpu.Fract(v) + Gpu.Step(1f, v) + Gpu.Length(new Float2(v, 1f));
                if (MathF.Abs(g[i] - r) > MathF.Abs(r) * 1e-4f + 1e-3f) { bad++; if (first == "") first = $"i={i} x={v} gpu={g[i]} cpu={r}"; }
            }
            Check("MathMap (MathF / Math / Gpu mappings)", bad, first);
        });

        Try("Mixed", () =>
        {
            int n = 10; using var o = device.AllocateReadWriteBuffer<float>(n); using var unused = device.AllocateReadOnlyBuffer<int>([1]);
            device.For(n, new Mixed(o, 0f, unused));
            float[] g = o.ToArray(); int bad = 0; string first = "";
            for (int i = 0; i < n; i++) { float e = i * 2.5f + n; if (g[i] != e) { bad++; if (first == "") first = $"i={i} gpu={g[i]} cpu={e}"; } }
            Check("Mixed (field with initializer + primary ctor, unused captures)", bad, first);
        });
        return failures;
    }
}
