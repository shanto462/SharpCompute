using System;
using System.Linq;
using System.Text;
using SharpCompute;

namespace SharpCompute.Tests.EndToEnd;

// Generated shaders dispatched through the real runtime and compared with CPU references.
internal static class MainSuite
{
    public static int Run(GraphicsDevice device, StringBuilder log)
    {
        int failures = 0;
        void Check(string name, bool ok, string detail = "") { log.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name} {detail}"); if (!ok) failures++; }
        void Try(string name, Action a) { try { a(); } catch (Exception e) { log.AppendLine($"[FAIL] {name}: {e.GetType().Name}: {e.Message.Split('\n').Take(12).Aggregate((x, y) => x + "\n        " + y)}"); failures++; } }

        Try("MultiplyByTwo", () =>
        {
            using var b = device.AllocateReadWriteBuffer<float>([1, 2, 3, 4]);
            device.For(b.Length, new MultiplyByTwo(b, 2f));
            Check("MultiplyByTwo", b.ToArray().SequenceEqual(new float[] { 2, 4, 6, 8 }), string.Join(",", b.ToArray()));
        });

        Try("Saxpy", () =>
        {
            float[] xs = Enumerable.Range(0, 1000).Select(i => i * 0.5f).ToArray(), ys = Enumerable.Range(0, 1000).Select(i => 1000f - i).ToArray();
            using var x = device.AllocateReadOnlyBuffer<float>(xs); using var y = device.AllocateReadWriteBuffer<float>(ys);
            var off = new Float3(10f, 20f, 30f);
            device.For(1000, new Saxpy(x, y, 3f, off));
            float[] got = y.ToArray(); int bad = 0;
            for (int i = 0; i < 1000; i++) { float o = i % 3 == 0 ? 10f : i % 3 == 1 ? 20f : 30f; float e = MathF.FusedMultiplyAdd(3f, xs[i], ys[i]) + o; float e2 = 3f * xs[i] + ys[i] + o; if (got[i] != e && got[i] != e2) bad++; }
            Check("Saxpy (primary ctor, Float3 constant)", bad == 0, $"{bad} mismatches; first: {got[0]}, {got[1]}, {got[2]}");
        });

        Try("WriteIndex2D", () =>
        {
            int w = 37, h = 19;
            using var b = device.AllocateReadWriteBuffer<uint>(w * h);
            device.For(w, h, new WriteIndex2D(b));
            Check("WriteIndex2D (2D, DispatchSize)", b.ToArray().Select((v, i) => v == (uint)i).All(t => t), $"last={b.ToArray()[^1]}");
        });

        Try("Kitchen", () =>
        {
            int n = 257;
            float[] d = Enumerable.Range(0, n).Select(i => i * 0.37f - 20f).ToArray(); int[] iv = Enumerable.Range(0, n).Select(i => i * 7 - 300).ToArray();
            using var data = device.AllocateReadWriteBuffer<float>(d); using var ints = device.AllocateReadOnlyBuffer<int>(iv);
            float scale = 1.5f; int bias = 11; uint mask = 0xF0F0u;
            device.For(n, new Kitchen(data, ints, scale, bias, mask));
            float[] got = data.ToArray(); int bad = 0; string first = "";
            for (int i = 0; i < n; i++)
            {
                float v = d[i];
                float acc = 0f; float pv = v * 0.25f; for (int k = 0; k < 4; k++) { acc = acc * pv + scale; if (acc > 100f) break; }
                float r = acc + (v % 3f) + MathF.Floor(v * 0.5f) + Gpu.Clamp(v, 1f, 5f) + MathF.Sqrt(MathF.Abs(v));
                int j = (iv[(i + 1) % n] + bias) / 2; uint u = ((uint)j ^ mask) >> 1;
                int w = 0; while (w < 3) { w++; if (w == 2) continue; r += 1f; }
                do { r -= 0.5f; w--; } while (w > 0);
                float e = r + j + (float)u + (i == n - 1 ? 1000f : 0f);
                if (MathF.Abs(got[i] - e) > MathF.Abs(e) * 1e-5f + 1e-4f) { bad++; if (first == "") first = $"i={i} gpu={got[i]} cpu={e}"; }
            }
            Check("Kitchen (Length, helper, loops, %, MathF, Gpu, casts)", bad == 0, $"{bad} mismatches {first}");
        });

        Try("Vectors", () =>
        {
            int n = 100;
            Float3[] pts = Enumerable.Range(0, n).Select(i => new Float3(i * 0.1f, 5f - i * 0.2f, i * 0.03f)).ToArray(); Float2[] uvs = Enumerable.Range(0, n).Select(i => new Float2(i / 100f, 1f - i / 100f)).ToArray();
            using var points = device.AllocateReadWriteBuffer<Float3>(pts); using var uv = device.AllocateReadOnlyBuffer<Float2>(uvs);
            var tint = new Float4(0.5f, 0.25f, 2f, 1f); var shift = new Int2(1, -2);
            device.For(n, new Vectors(points, uv, tint, shift));
            Float3[] got = points.ToArray(); int bad = 0; string first = "";
            for (int i = 0; i < n; i++)
            {
                Float3 p = pts[i]; Float2 t = uvs[i] * 2f - new Float2(1f, 0.5f);
                p.XY = p.YX + t; p.Z = Gpu.Dot(p, new Float3(tint.X, tint.Y, tint.Z)) + tint.W;
                Int2 s = shift << 1; p += new Float3(s.X, s.Y, 0f);
                Float3 q = (p % new Float3(4f, 4f, 4f)) * 0.5f; Float3 e = -q + Gpu.Max(p, new Float3(0f)); e.X += 1f;
                bool ok(float a, float b) => MathF.Abs(a - b) <= MathF.Abs(b) * 1e-5f + 1e-5f;
                if (!ok(got[i].X, e.X) || !ok(got[i].Y, e.Y) || !ok(got[i].Z, e.Z)) { bad++; if (first == "") first = $"i={i} gpu={got[i]} cpu={e}"; }
            }
            Check("Vectors (Float3 elements, swizzles, operators, constants)", bad == 0, $"{bad} mismatches {first}");
        });

        Try("Step", () =>
        {
            int w = 20, h = 12;
            Particle[] ps = Enumerable.Range(0, w * h).Select(i => new Particle { Position = new Float3(i, i * 2, i * 3), Mass = 1f + i % 4, Uv = new Float2(i, -i), Id = i }).ToArray();
            using var particles = device.AllocateReadWriteBuffer<Particle>(ps);
            var prm = new Params { Dt = 0.5f, Gravity = new Float3(0f, -10f, 1f), Flags = 7u };
            device.For(w, h, new Step(particles, prm));
            Particle[] got = particles.ToArray(); int bad = 0; string first = "";
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int i = y * w + x; Particle e = ps[i];
                e.Position += prm.Gravity * prm.Dt * e.Mass; e.Uv = e.Uv.YX; e.Id += 7 + (x % 8) + (y / 8) * 100;
                Particle g = got[i];
                bool ok = g.Position.X == e.Position.X && g.Position.Y == e.Position.Y && g.Position.Z == e.Position.Z && g.Mass == e.Mass && g.Uv.X == e.Uv.X && g.Uv.Y == e.Uv.Y && g.Id == e.Id;
                if (!ok) { bad++; if (first == "") first = $"i={i} gpu=({g.Position},{g.Mass},{g.Uv},{g.Id}) cpu=({e.Position},{e.Mass},{e.Uv},{e.Id})"; }
            }
            Check("Step (user struct elements + struct constant, 8x8 groups, GroupIds)", bad == 0, $"{bad} mismatches {first}");
        });
        return failures;
    }
}
