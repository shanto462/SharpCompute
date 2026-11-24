using System.Globalization;
using System.Threading;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>Focused checks of the statement and expression mapping. Every MSL result is compiled with Metal.</summary>
public class TranslationTests
{
    private const string Shader = "Tests.TestShader";

    /// <summary>Translates an <c>Execute</c> body and returns the kernel body.</summary>
    private static string Kernel(string body, string extraMembers = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(body, extraMembers)).Verify(Shader).KernelBody;

    private static void AssertKernel(string expected, string body, string extraMembers = "") =>
        Assert.Equal(expected.ReplaceLineEndings("\n"), Kernel(body, extraMembers));

    [Fact]
    public void Literals_UseInvariantFormattingAndSuffixes()
    {
        AssertKernel(
            """
            float a = 1.0f;
            float b = 1.0f;
            float c = 0.5f;
            float d = 1E+10f;
            float e = 1.5E-05f;
            float f = -3.0f;
            float g = 1.0f;
            uint h = 7u;
            uint i = 4294967295u;
            uint j = 2u;
            int k = 255;
            int l = -5;
            bool m = true;
            buffer[0] = a + b + c + d + e + f + g + float(h + i + j) + float(k + l) + float(m ? 1 : 0);
            """,
            """
            float a = 1f;
            float b = 1;
            float c = .5f;
            float d = 1e10f;
            float e = 1.5e-5f;
            float f = -3;
            float g = 1.0F;
            uint h = 7u;
            uint i = uint.MaxValue;
            uint j = 2;
            int k = 0xFF;
            int l = -5;
            bool m = true;
            buffer[0] = a + b + c + d + e + f + g + (h + i + j) + (k + l) + (m ? 1 : 0);
            """);
    }

    [Fact]
    public void Literals_AreNotAffectedByTheCurrentCulture()
    {
        CultureInfo previous = Thread.CurrentThread.CurrentCulture;

        try
        {
            // German uses a decimal comma; MSL must still get `1.5f`.
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            AssertKernel("buffer[0] = 1.5f + 1234.5f;", "buffer[0] = 1.5f + 1234.5f;");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Constants_OfOtherTypesAreInlined_ShaderConstantsBecomeGlobals()
    {
        // MathF.PI and int.MaxValue are inlined; the shader's own consts become program-scope
        // constants (`::Quarter`), except where a constant is converted, which folds to a literal.
        AssertKernel(
            """
            const float Local = 2.5f;
            float a = 3.1415927f + ::Quarter * Local;
            float b = 3.4028235E+38f + -3.4028235E+38f;
            float c = INFINITY + -INFINITY + NAN;
            int d = 2147483647 + (-2147483647 - 1) + ::Sixteen;
            float e = a / 16.0f + float(d) * ::Quarter;
            buffer[0] = a + b + c + float(d) + e;
            """,
            """
            const float Local = 2.5f;
            float a = MathF.PI + Quarter * Local;
            float b = float.MaxValue + float.MinValue;
            float c = float.PositiveInfinity + float.NegativeInfinity + float.NaN;
            int d = int.MaxValue + int.MinValue + Sixteen;
            float e = a / Sixteen + d * Quarter;
            buffer[0] = a + b + c + d + e;
            """,
            """
            private const float Quarter = 0.25f;
            private const int Sixteen = 16;
            """);
    }

    [Fact]
    public void FloatRemainder_BecomesFmod_IntegerRemainderStaysNative()
    {
        AssertKernel(
            """
            float f = buffer[0];
            int i = int(__tid.x);
            uint u = 9u;
            float a = metal::fmod(f, 3.0f);
            float b = metal::fmod(f, float(i));
            float c = metal::fmod(float(i), f) * 2.0f;
            int d = i % 3;
            uint e = u % 4u;
            f = metal::fmod(f, 2.0f);
            i %= 5;
            buffer[0] = a + b + c + float(d) + float(e) + f;
            """,
            """
            float f = buffer[0];
            int i = ThreadIds.X;
            uint u = 9;
            float a = f % 3;
            float b = f % i;
            float c = i % f * 2;
            int d = i % 3;
            uint e = u % 4;
            f %= 2f;
            i %= 5;
            buffer[0] = a + b + c + d + e + f;
            """);
    }

    [Fact]
    public void Casts_BecomeConstructorStyleConversions()
    {
        AssertKernel(
            """
            float f = buffer[0];
            int i = int(f);
            uint u = uint(i);
            float g = float(u) + float(i);
            int j = int(f * 2.0f) + int(u);
            half h = half(f);
            float k = float(h) + f;
            buffer[0] = g + float(j) + k;
            """,
            """
            float f = buffer[0];
            int i = (int)f;
            uint u = (uint)i;
            float g = (float)u + i;
            int j = (int)(f * 2) + (int)u;
            Half h = (Half)f;
            float k = (float)h + (float)f;
            buffer[0] = g + j + k;
            """);
    }

    [Fact]
    public void Operators_KeepCSharpPrecedenceAndAvoidClangWarnings()
    {
        AssertKernel(
            """
            int i = int(__tid.x);
            float f = buffer[i];
            bool a = (i & 1) == 0;
            bool b = !a || (f > 0.0f && f <= 1.0f);
            int c = (i << 3) | (((i >> 1) & 255) ^ ~i);
            int d = i + 2 * 3 - (i - 1) / 2;
            int e = i - -i;
            int g = -(-i);
            int h = int(uint(i) >> 2);
            uint u = 16u;
            u = u >> 1;
            bool k = a != b && i >= 2 && i < 100;
            f = (f + 1.0f) * (f - 1.0f);
            buffer[i] = a | (b ^ k) ? f : float(c + d + e + g + h + int(u));
            """,
            """
            int i = ThreadIds.X;
            float f = buffer[i];
            bool a = (i & 1) == 0;
            bool b = !a || f > 0 && f <= 1;
            int c = i << 3 | (i >> 1) & 0xFF ^ ~i;
            int d = i + 2 * 3 - (i - 1) / 2;
            int e = i - -i;
            int g = -(-i);
            int h = i >>> 2;
            uint u = 16;
            u = u >>> 1;
            bool k = a != b && i >= 2 && i < 100;
            f = (f + 1) * (f - 1);
            buffer[i] = a | b ^ k ? f : c + d + e + g + h + (int)u;
            """);
    }

    [Fact]
    public void Assignments_CompoundAssignmentsAndIncrements()
    {
        AssertKernel(
            """
            int i = int(__tid.x);
            float f = 0.0f;
            float g;
            f = g = buffer[i];
            f += 1.0f;
            f -= float(i);
            f *= g;
            f /= 2.0f;
            i <<= 1;
            i >>= 2;
            i |= 4;
            i &= 255;
            i ^= 3;
            i++;
            ++i;
            i--;
            --i;
            int j = i++;
            j = --i + j;
            buffer[j] += f;
            buffer[i]++;
            """,
            """
            int i = ThreadIds.X;
            float f = 0;
            float g;
            f = g = buffer[i];
            f += 1;
            f -= i;
            f *= g;
            f /= 2;
            i <<= 1;
            i >>= 2;
            i |= 4;
            i &= 0xFF;
            i ^= 3;
            i++;
            ++i;
            i--;
            --i;
            int j = i++;
            j = --i + j;
            buffer[j] += f;
            buffer[i]++;
            """);
    }

    [Fact]
    public void ControlFlow_IfForWhileDoBreakContinueReturn()
    {
        AssertKernel(
            """
            int i = int(__tid.x);
            float sum = 0.0f;
            if (i < 0)
            {
                return;
            }
            for (int j = 0, k = 10; j < k; j++, k -= 2)
            {
                if (j == 3)
                {
                    continue;
                }
                else if (j > 5)
                {
                    break;
                }
                else
                {
                    sum += float(j);
                }
            }
            for (; ; )
            {
                break;
            }
            while (sum > 100.0f)
            {
                sum *= 0.5f;
            }
            do
            {
                sum += 1.0f;
            }
            while (sum < 1.0f);
            {
                float scoped = sum;
                sum = scoped * 2.0f;
            }
            buffer[i] = sum;
            """,
            """
            int i = ThreadIds.X;
            float sum = 0;
            if (i < 0) return;
            for (int j = 0, k = 10; j < k; j++, k -= 2)
            {
                if (j == 3) continue;
                else if (j > 5) break;
                else sum += j;
            }
            for (;;) { break; }
            while (sum > 100) sum *= 0.5f;
            do { sum += 1; } while (sum < 1);
            {
                float scoped = sum;
                sum = scoped * 2;
            }
            buffer[i] = sum;
            """);
    }

    [Fact]
    public void Switch_WithConstantLabels()
    {
        AssertKernel(
            """
            int i = int(__tid.x);
            float f = 0.0f;
            switch (i % 4)
            {
                case 0:
                {
                    f = 1.0f;
                    break;
                }
                case 1:
                case ::Two:
                {
                    float t = 2.0f;
                    f = t;
                    break;
                }
                default:
                {
                    return;
                }
            }
            buffer[i] = f;
            """,
            """
            int i = ThreadIds.X;
            float f = 0;
            switch (i % 4)
            {
                case 0:
                    f = 1;
                    break;
                case 1:
                case Two:
                    float t = 2;
                    f = t;
                    break;
                default:
                    return;
            }
            buffer[i] = f;
            """,
            "private const int Two = 2;");
    }

    [Fact]
    public void Ternary_NestedAndVarLocals()
    {
        AssertKernel(
            """
            int i = int(__tid.x);
            float v = buffer[i];
            float sign = v > 0.0f ? 1.0f : v < 0.0f ? -1.0f : 0.0f;
            float picked = (i > 3 ? v : sign) * 2.0f;
            buffer[i] = picked;
            """,
            """
            var i = ThreadIds.X;
            var v = buffer[i];
            var sign = v > 0 ? 1f : v < 0 ? -1f : 0f;
            var picked = (i > 3 ? v : sign) * 2;
            buffer[i] = picked;
            """);
    }

    [Fact]
    public void Fields_BuffersAreKernelParameters_ConstantsLiveInTheConstantsStruct()
    {
        AssertKernel(
            """
            float scaled = input[int(__tid.x)] * __c.factor + __c.offset;
            buffer[int(__tid.x)] = scaled * __c.factor;
            """,
            """
            float scaled = input[ThreadIds.X] * this.factor + offset;
            this.buffer[ThreadIds.X] = scaled * factor;
            """,
            """
            public readonly ReadOnlyBuffer<float> input;
            public readonly float offset;
            """);
    }

    [Fact]
    public void BufferLength_AddsAHiddenConstantOnlyWhenUsed()
    {
        ShaderDescriptor withLength = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body("if (ThreadIds.X < buffer.Length) buffer[ThreadIds.X] = buffer.Length;")).Verify(Shader);

        Assert.Equal(
            """
            if (int(__tid.x) < int(__c.__length_buffer))
            {
                buffer[int(__tid.x)] = float(int(__c.__length_buffer));
            }
            """.ReplaceLineEndings("\n"),
            withLength.KernelBody);
        Assert.Contains("uint __length_buffer;", withLength.MslSource);
        withLength.AssertLayoutMatchesClr("__Constants", withLength.ConstantsType!);

        ShaderDescriptor withoutLength = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body("buffer[ThreadIds.X] = factor;")).Verify(Shader);

        Assert.DoesNotContain("__length_", withoutLength.MslSource);
    }

    [Fact]
    public void Builtins_AreMappedAndOnlyUsedOnesBecomeParameters()
    {
        ShaderDescriptor all = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(
            """
            int a = ThreadIds.X + ThreadIds.Y + ThreadIds.Z;
            Int2 b = ThreadIds.XY;
            Int3 c = ThreadIds.XYZ;
            int d = DispatchSize.X + DispatchSize.XY.Y + DispatchSize.XYZ.Z;
            int e = GroupIds.X + GroupIds.XY.Y;
            int f = ThreadGroupIds.Z + ThreadGroupIds.XYZ.X;
            buffer[a] = b.X + c.Z + d + e + f;
            """)).Verify(Shader);

        Assert.Equal(
            """
            int a = int(__tid.x) + int(__tid.y) + int(__tid.z);
            int2 b = int2(__tid.xy);
            int3 c = int3(__tid.xyz);
            int d = int(__grid.x) + int2(__grid.xy).y + int3(__grid.xyz).z;
            int e = int(__gtid.x) + int2(__gtid.xy).y;
            int f = int(__gid.z) + int3(__gid.xyz).x;
            buffer[a] = float(b.x + c.z + d + e + f);
            """.ReplaceLineEndings("\n"),
            all.KernelBody);
        // The grid-position built-ins arrive raw and are rebased by the prologue for split dispatches;
        // the position within the threadgroup is the same in every part, so it is used directly.
        Assert.Contains(
            """
                constant __GridWindow& __window [[buffer(2), function_constant(__windowed)]],
                uint3 __tid_in [[thread_position_in_grid]],
                uint3 __grid_in [[threads_per_grid]],
                uint3 __gtid [[thread_position_in_threadgroup]],
                uint3 __gid_in [[threadgroup_position_in_grid]])
            """.ReplaceLineEndings("\n"),
            all.MslSource);
        Assert.Equal(
            """
            const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
            const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;
            const uint3 __gid = __windowed ? __gid_in + uint3(__window.groupOffset) : __gid_in;
            """.ReplaceLineEndings("\n").TrimEnd('\n'),
            all.KernelPrologue);

        ShaderDescriptor one = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body("buffer[DispatchSize.X - 1] = factor;")).Verify(Shader);

        Assert.Contains("uint3 __grid_in [[threads_per_grid]])", one.MslSource);
        Assert.Equal("const uint3 __grid = __windowed ? uint3(__window.size) : __grid_in;", one.KernelPrologue);
        Assert.DoesNotContain("__tid", one.MslSource);
        Assert.DoesNotContain("__gtid", one.MslSource);
        Assert.DoesNotContain("__gid", one.MslSource);

        ShaderDescriptor none = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body("buffer[0] = factor;")).Verify(Shader);

        Assert.DoesNotContain("uint3", none.MslSource);
        Assert.DoesNotContain("__window", none.MslSource);

        // Only the position within a threadgroup: nothing to rebase, so no window either.
        ShaderDescriptor local = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body("buffer[GroupIds.X] = factor;")).Verify(Shader);

        Assert.Contains("uint3 __gtid [[thread_position_in_threadgroup]])", local.MslSource);
        Assert.DoesNotContain("__window", local.MslSource);
    }

    [Fact]
    public void MathF_And_Math_MapToMetalFunctions()
    {
        AssertKernel(
            """
            float x = buffer[0];
            int i = int(__tid.x);
            float a = metal::sqrt(x) + metal::abs(x) + metal::sin(x) + metal::cos(x) + metal::tan(x) + metal::exp(x) + metal::log(x) + metal::log2(x) + metal::log10(x);
            float b = metal::pow(x, 2.0f) + metal::atan2(x, 1.0f) + metal::floor(x) + metal::ceil(x) + metal::trunc(x) + metal::rint(x);
            float c = metal::min(x, 1.0f) + metal::max(x, 0.0f) + metal::clamp(x, 0.0f, 1.0f) + metal::fma(x, x, x) + metal::copysign(x, -1.0f);
            int d = int(metal::sign(x)) + int(metal::abs(i)) + metal::min(i, 3) + metal::max(i, 0) + metal::clamp(i, 0, 9);
            bool e = metal::isnan(x) || metal::isinf(x) || !metal::isfinite(x);
            buffer[0] = a + b + c + float(d) + float(e ? 1 : 0) + metal::mix(x, 1.0f, 0.5f);
            """,
            """
            float x = buffer[0];
            int i = ThreadIds.X;
            float a = MathF.Sqrt(x) + MathF.Abs(x) + MathF.Sin(x) + MathF.Cos(x) + MathF.Tan(x) + MathF.Exp(x) + MathF.Log(x) + MathF.Log2(x) + MathF.Log10(x);
            float b = MathF.Pow(x, 2) + MathF.Atan2(x, 1) + MathF.Floor(x) + MathF.Ceiling(x) + MathF.Truncate(x) + MathF.Round(x);
            float c = MathF.Min(x, 1) + Math.Max(x, 0f) + Math.Clamp(x, 0f, 1f) + MathF.FusedMultiplyAdd(x, x, x) + MathF.CopySign(x, -1);
            int d = MathF.Sign(x) + Math.Abs(i) + Math.Min(i, 3) + Math.Max(i, 0) + Math.Clamp(i, 0, 9);
            bool e = float.IsNaN(x) || float.IsInfinity(x) || !float.IsFinite(x);
            buffer[0] = a + b + c + d + (e ? 1 : 0) + float.Lerp(x, 1, 0.5f);
            """);
    }

    [Fact]
    public void Intrinsics_AreDrivenByTheMslIntrinsicAttribute()
    {
        AssertKernel(
            """
            float3 v = float3(1.0f, 2.0f, 3.0f);
            float x = buffer[0];
            float whole;
            float a = metal::dot(v, v) + metal::length(v) + metal::clamp(x, 0.0f, 1.0f);
            float3 n = metal::normalize(v);
            float3 m = metal::mix(v, n, 0.25f);
            float b = metal::mix(x, 1.0f, 0.5f) + metal::fract(x) + metal::modf(x, whole);
            float c = metal::clamp(1.0f, x, 0.0f);
            buffer[0] = a + m.x + b + c + whole;
            """,
            """
            Vec3 v = new Vec3(1, 2, 3);
            float x = buffer[0];
            float whole;
            float a = Fn.Dot(v, v) + Fn.Length(v) + Fn.Clamp(x, 0, 1);
            Vec3 n = Fn.Normalize(v);
            Vec3 m = Fn.Lerp(v, n, 0.25f);
            float b = Fn.Lerp(x, 1f, 0.5f) + x.Fract() + Fn.Modf(x, out whole);
            float c = Fn.Clamp(max: 0, value: 1, min: x);
            buffer[0] = a + m.X + b + c + whole;
            """);
    }

    [Fact]
    public void Vectors_Members_Constructors_Operators_AndConversions()
    {
        AssertKernel(
            """
            float2 a = float2(1.0f, 2.0f);
            float2 b = float2(3.0f);
            float3 c = float3(a, 0.5f);
            float3 d = float3(0);
            float2 e = a + b * 2.0f - 2.0f * a / 4.0f;
            float2 f = -e.yx;
            float2 g = metal::fmod(e, b);
            float2 h = float2(1.5f);
            int2 i = int2(e);
            int2 j = ((i + i) * 2) & (i >> 1);
            int2 k = ~j % i;
            float2 l = float2(k);
            d.xy = l;
            d.z = c[2] + c.zyx.x;
            c[0] = d.x;
            c += d * a.x;
            buffer[0] = c.x + d.y + f.x + g.y + h.x;
            """,
            """
            Vec2 a = new Vec2(1, 2);
            Vec2 b = new(3f);
            Vec3 c = new Vec3(a, 0.5f);
            Vec3 d = new Vec3();
            Vec2 e = a + b * 2 - 2 * a / 4;
            Vec2 f = -e.YX;
            Vec2 g = e % b;
            Vec2 h = 1.5f;
            IVec2 i = (IVec2)e;
            IVec2 j = (i + i) * 2 & i >> 1;
            IVec2 k = ~j % i;
            Vec2 l = (Vec2)k;
            d.XY = l;
            d.Z = c[2] + c.ZYX.X;
            c[0] = d.X;
            c += d * a.X;
            buffer[0] = c.X + d.Y + f.X + g.Y + h.X;
            """);
    }

    [Fact]
    public void Vectors_FromSharpComputeItself_AreHandledTheSameWay()
    {
        // The runtime's own vector types go through exactly the same attribute-driven path.
        AssertKernel(
            """
            float4 color = float4(0.1f, 0.2f, 0.3f, 1.0f);
            int3 id = int3(__tid.xyz);
            color.x = float(id.x) * __c.tint.w;
            buffer[id.y] = color.x + __c.tint.x;
            """,
            """
            Float4 color = new Float4(0.1f, 0.2f, 0.3f, 1);
            Int3 id = ThreadIds.XYZ;
            color.X = id.X * tint.W;
            buffer[id.Y] = color.X + tint.X;
            """,
            "public readonly Float4 tint;");
    }

    [Fact]
    public void BoolVectors_WorkAsLocals_ButMayNotCrossTheBoundary()
    {
        const string Types = """

            [MslType("bool2")]
            public struct Flags2
            {
                public bool X;
                public bool Y;

                public static Flags2 operator &(Flags2 a, Flags2 b) => default;
                public static Flags2 operator !(Flags2 a) => default;
            }

            public static class Compare
            {
                [MslIntrinsic("metal::isnan")] public static Flags2 IsNaN(TestVectors.Vec2 value) => default;
                [MslIntrinsic("metal::any")] public static bool Any(Flags2 value) => value.X || value.Y;
                [MslIntrinsic("metal::select")] public static TestVectors.Vec2 Select(TestVectors.Vec2 a, TestVectors.Vec2 b, Flags2 mask) => a;
            }
            """;

        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(
            """
            Vec2 v = new Vec2(buffer[0], buffer[1]);
            Flags2 bad = Compare.IsNaN(v);
            Flags2 keep = !bad & bad;
            if (Compare.Any(bad) || keep.X)
            {
                v = Compare.Select(v, new Vec2(0), bad);
            }
            buffer[0] = v.X;
            """) + Types).Verify(Shader);

        Assert.Equal(
            """
            float2 v = float2(buffer[0], buffer[1]);
            bool2 bad = metal::isnan(v);
            bool2 keep = !bad & bad;
            if (metal::any(bad) || keep.x)
            {
                v = metal::select(v, float2(0.0f), bad);
            }
            buffer[0] = v.x;
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);

        GeneratorRun captured = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader("""
                public readonly Flags2 flags;

                public void Execute() { }
            """) + Types);

        Assert.Contains("'bool' has no stable layout", captured.AssertOnlyDiagnostic("SCG0006", "Flags2").GetMessage());
    }

    [Fact]
    public void AssignmentUsedAsCondition_GetsExtraParentheses()
    {
        AssertKernel(
            """
            bool more = true;
            int i = 0;
            while ((more = i < 4))
            {
                i++;
            }
            if ((more = i > 2))
            {
                buffer[0] = float(i);
            }
            """,
            """
            bool more = true;
            int i = 0;
            while (more = i < 4) i++;
            if (more = i > 2) buffer[0] = i;
            """);
    }

    [Fact]
    public void Half_ScalarsUseNativeOperators()
    {
        AssertKernel(
            """
            half a = half(buffer[0]);
            half b = a * a + __c.bias;
            b += a;
            bool larger = b > a;
            buffer[0] = larger ? float(b) : float(-a);
            """,
            """
            Half a = (Half)buffer[0];
            Half b = a * a + bias;
            b += a;
            bool larger = b > a;
            buffer[0] = larger ? (float)b : (float)(-a);
            """,
            "public readonly Half bias;");
    }

    [Fact]
    public void DefaultValues_ForScalarsVectorsAndStructs()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(
            TestSources.Vectors,
            TestSources.Body(
                """
                float a = default;
                int b = default(int);
                uint c = default;
                bool d = default;
                Vec3 e = default;
                Pair f = default;
                Pair g = new Pair();
                Pair h = new();
                Half i = default;
                f.Left = a + b + c + (d ? 1 : 0) + e.X + g.Left + h.Right + (float)i;
                buffer[0] = f.Left;
                """) + "public struct Pair { public float Left; public float Right; }").Verify(Shader);

        Assert.Equal(
            """
            float a = 0.0f;
            int b = 0;
            uint c = 0u;
            bool d = false;
            float3 e = float3(0);
            Pair f = Pair{};
            Pair g = Pair{};
            Pair h = Pair{};
            half i = half(0);
            f.Left = a + float(b) + float(c) + float(d ? 1 : 0) + e.x + g.Left + h.Right + float(i);
            buffer[0] = f.Left;
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }
}
