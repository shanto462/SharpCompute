using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// Fixed-size local arrays: <c>stackalloc</c>, array locals, collection expressions and
/// <c>[InlineArray]</c> structs, the MSL they become (checked by Metal), helpers that take them,
/// <c>foreach</c> over them, and the diagnostics SCG0036 to SCG0039.
/// </summary>
public class LocalArrayTests
{
    private const string Shader = "Tests.TestShader";

    /// <summary>Helpers and inline array types the tests share; the body is the <c>Execute</c> of <see cref="TestSources.Body"/>.</summary>
    private const string Types = """

        [System.Runtime.CompilerServices.InlineArray(4)]
        public struct Four { private float _e; }

        [System.Runtime.CompilerServices.InlineArray(3)]
        public struct Points { private Vec3 _point; }

        [System.Runtime.CompilerServices.InlineArray(2)]
        public struct Flags { private bool _e; }

        [System.Runtime.CompilerServices.InlineArray(8192)]
        public struct Huge { private float _e; }

        public struct Particle
        {
            public Vec3 Position;
            public int Weight;
        }
        """;

    private static GeneratorRun RunBody(string body, string helpers = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(body, helpers) + Types);

    private static GeneratorRun RunShader(string members) =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(members) + Types);

    private static void AssertStub(GeneratorRun run)
    {
        GeneratedSourceResult source = Assert.Single(run.Sources);

        Assert.Contains("This shader has SharpCompute errors", source.SourceText.ToString());
        Assert.DoesNotContain(run.Output.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
    }

    // ---- the three forms --------------------------------------------------------------------------

    [Fact]
    public void Stackalloc_BecomesAZeroedMslArray_IndexedWrittenCompoundAssignedAndIncremented()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            int i = ThreadIds.X;
            Span<float> taps = stackalloc float[9];
            taps[0] = buffer[i];
            taps[1] += 2;
            taps[2]++;
            --taps[3];
            taps[i % taps.Length] *= factor;
            buffer[i] = taps[0] + taps[1] + taps[2] + taps[3] + taps.Length;
            """).Verify(Shader);

        // Zeroed, as C# zeroes stackalloc memory (without [SkipLocalsInit]) and new T[n].
        Assert.Equal(
            """
            int i = int(__tid.x);
            float taps[9] = {};
            taps[0] = buffer[i];
            taps[1] += 2.0f;
            taps[2]++;
            --taps[3];
            taps[i % 9] *= __c.factor;
            buffer[i] = taps[0] + taps[1] + taps[2] + taps[3] + float(9);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [Fact]
    public void ArrayLocals_AndCollectionExpressions_BecomeTheSameArrays_ReadOnlySpansConst()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            int i = ThreadIds.X;
            float[] zeros = new float[3];
            float[] listed = new float[] { 1, 2, i };
            float[] implicitArray = { 4, 5 };
            float[] collection = [6, 7, factor];
            Span<float> span = [8, 9];
            ReadOnlySpan<float> weights = [0.25f, 0.5f, 0.25f];
            Span<int> counts = stackalloc int[] { 1, 2, 3 };
            Span<uint> bits = stackalloc uint[2] { 1, 2 };
            buffer[i] = zeros[0] + listed[2] + implicitArray[1] + collection[2] + span[1] + weights[i % 3] + counts[2] + bits[1];
            """).Verify(Shader);

        Assert.Equal(
            """
            int i = int(__tid.x);
            float zeros[3] = {};
            float listed[3] = { 1.0f, 2.0f, float(i) };
            float implicitArray[2] = { 4.0f, 5.0f };
            float collection[3] = { 6.0f, 7.0f, __c.factor };
            float span[2] = { 8.0f, 9.0f };
            const float weights[3] = { 0.25f, 0.5f, 0.25f };
            int counts[3] = { 1, 2, 3 };
            uint bits[2] = { 1u, 2u };
            buffer[i] = zeros[0] + listed[2] + implicitArray[1] + collection[2] + span[1] + weights[i % 3] + float(counts[2]) + float(bits[1]);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [Theory]
    [InlineData("stackalloc float[N]", 8)]
    [InlineData("stackalloc float[N * 2 + 1]", 17)]
    [InlineData("stackalloc float[sizeof(int)]", 4)]
    [InlineData("stackalloc float[(int)Size.Big]", 5)]
    [InlineData("new float[3u]", 3)]
    [InlineData("new float[N]", 8)]
    public void Length_CanBeAnyCompileTimeConstant_AndLengthIsThatConstant(string creation, int length)
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(
            TestSources.Vectors,
            TestSources.Shader(
                $$"""
                    private const int N = 8;
                    private enum Size { Big = 5 }
                    public readonly ReadWriteBuffer<float> buffer;

                    public void Execute()
                    {
                        Span<float> values = {{creation}};
                        values[0] = 1;
                        buffer[0] = values.Length + values[0];
                    }
                """)).Verify(Shader);

        Assert.Contains($"float values[{length}] = {{}};", descriptor.MslSource);
        Assert.Contains($"buffer[0] = float({length}) + values[0];", descriptor.MslSource);
    }

    [Fact]
    public void Elements_CanBeVectorsMatricesStructsBoolsAndHalves_InTheirNativeTypes()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            int i = ThreadIds.X;
            Span<Vec3> points = stackalloc Vec3[2];
            Float3x3[] rotations = new Float3x3[2];
            Span<Float4x4> transforms = stackalloc Float4x4[2];
            Span<Particle> particles = stackalloc Particle[2];
            Span<bool> seen = stackalloc bool[4];
            Span<Half> halves = stackalloc Half[2];
            points[1] = new Vec3(1, 2, 3);
            points[0].Y = 4;
            particles[1].Position = points[1];
            particles[1].Weight += 2;
            seen[i % 4] = true;
            buffer[i] = points[0].Y + particles[1].Position.Z + particles[1].Weight + (seen[1] ? 1 : 0) + (float)halves[0]
                + Gpu.Determinant(rotations[1]) + Gpu.Determinant(transforms[0]);
            """).Verify(Shader);

        // Native types, not the packed boundary types: these arrays never leave the thread.
        Assert.Contains("float3 points[2] = {};", descriptor.KernelBody);
        Assert.Contains("float3x3 rotations[2] = {};", descriptor.KernelBody);
        Assert.Contains("float4x4 transforms[2] = {};", descriptor.KernelBody);
        Assert.Contains("Particle particles[2] = {};", descriptor.KernelBody);
        Assert.Contains("bool seen[4] = {};", descriptor.KernelBody);
        Assert.Contains("half halves[2] = {};", descriptor.KernelBody);
        Assert.Contains("points[0].y = 4.0f;", descriptor.KernelBody);
        Assert.Contains("particles[1].Position = points[1];", descriptor.KernelBody);
        Assert.Contains("particles[1].Weight += 2;", descriptor.KernelBody);
        Assert.DoesNotContain("__ms_unpack", descriptor.KernelBody);
    }

    [Fact]
    public void Initializers_KeepEveryElementExpression_InOrder()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            int i = ThreadIds.X;
            Span<Vec3> corners = [new Vec3(0, 0, 1), new Vec3(i, 1, 0), default];
            ReadOnlySpan<int> offsets = [-1, 0, 1, int.MinValue];
            Particle[] particles = [default, default];
            buffer[i] = corners[1].X + offsets[3] + particles[0].Weight;
            """).Verify(Shader);

        Assert.Contains("float3 corners[3] = { float3(0.0f, 0.0f, 1.0f), float3(float(i), 1.0f, 0.0f), float3(0) };", descriptor.KernelBody);
        Assert.Contains("const int offsets[4] = { -1, 0, 1, -2147483647 - 1 };", descriptor.KernelBody);
        Assert.Contains("Particle particles[2] = { Particle{}, Particle{} };", descriptor.KernelBody);
    }

    [Fact]
    public void ArraysInForInitializers_AndInNestedScopes_AreDeclaredThere()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            for (int pass = 0; pass < 2; pass++)
            {
                Span<float> scratch = stackalloc float[2];
                scratch[pass] = pass;
                buffer[pass] = scratch[0] + scratch[1];
            }

            for (float[] a = new float[2], b = [1, 2]; a[0] < 3; a[0]++)
            {
                buffer[2] += b[1];
            }
            """).Verify(Shader);

        Assert.Contains("    float scratch[2] = {};", descriptor.KernelBody);
        Assert.Contains("for (float a[2] = {}, b[2] = { 1.0f, 2.0f }; a[0] < 3.0f; a[0]++)", descriptor.KernelBody);
    }

    // ---- helpers ----------------------------------------------------------------------------------

    [Fact]
    public void Helpers_WriteThroughThePointer_IntoTheCallersArray()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            int i = ThreadIds.X;
            Span<float> taps = stackalloc float[5];
            float[] other = new float[3];
            ReadOnlySpan<float> weights = [1, 2, 1];
            Fill(taps, buffer[i]);
            Fill(other, 2);
            buffer[i] = Dot(weights, other) + Dot(taps, weights) + taps[4];
            """,
            """
            private static void Fill(Span<float> values, float value)
            {
                for (int k = 0; k < values.Length; k++)
                {
                    values[k] = value + k;
                }

                Scale(values);
            }

            private static void Scale(Span<float> values) => values[0] *= values.Length;

            private static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
            {
                float sum = 0;

                for (int k = 0; k < a.Length; k++)
                {
                    sum += a[k] * b[k];
                }

                return sum;
            }
            """).Verify(Shader);

        Assert.Contains("static void Fill(thread float* values, int __len_values, float value)", descriptor.MslSource);
        Assert.Contains("static void Scale(thread float* values, int __len_values)", descriptor.MslSource);
        Assert.Contains("static float Dot(thread const float* a, int __len_a, thread const float* b, int __len_b)", descriptor.MslSource);
        Assert.Contains("for (int k = 0; k < __len_values; k++)", descriptor.FunctionBody("static void Fill("));
        Assert.Contains("::Scale(values, __len_values);", descriptor.FunctionBody("static void Fill("));
        Assert.Equal("values[0] *= float(__len_values);", descriptor.FunctionBody("static void Scale("));
        Assert.Contains("::Fill(taps, 5, buffer[i]);", descriptor.KernelBody);
        Assert.Contains("::Fill(other, 3, 2.0f);", descriptor.KernelBody);
        Assert.Contains("buffer[i] = ::Dot(weights, 3, other, 3) + ::Dot(taps, 5, weights, 3) + taps[4];", descriptor.KernelBody);
    }

    [Fact]
    public void Elements_PassByReference_LikeLocals()
    {
        // The compare-and-swap of a sorting network.
        ShaderDescriptor descriptor = RunBody(
            """
            Span<float> v = stackalloc float[3];
            Four f = default;
            v[0] = buffer[2];
            Order(ref v[0], ref v[2]);
            Order(ref f[0], ref f[1]);
            Network(v);
            buffer[0] = v[0] + f[1];
            """,
            """
            private static void Order(ref float a, ref float b)
            {
                float low = MathF.Min(a, b);
                b = MathF.Max(a, b);
                a = low;
            }

            private static void Network(Span<float> v)
            {
                Order(ref v[0], ref v[1]);
                Order(ref v[1], ref v[2]);
                Order(ref v[0], ref v[1]);
            }
            """).Verify(Shader);

        Assert.Contains("::Order(v[0], v[2]);", descriptor.KernelBody);
        Assert.Contains("::Order(f._e[0], f._e[1]);", descriptor.KernelBody);
        Assert.Contains("::Network(v, 3);", descriptor.KernelBody);
        Assert.Equal(
            """
            ::Order(v[0], v[1]);
            ::Order(v[1], v[2]);
            ::Order(v[0], v[1]);
            """.ReplaceLineEndings("\n"),
            descriptor.FunctionBody("static void Network("));
    }

    [Fact]
    public void LocalFunctions_TakeLocalArraysToo()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            Span<float> v = stackalloc float[2];
            Set(v, 3);
            buffer[0] = v[1];

            static void Set(Span<float> values, float value)
            {
                foreach (ref float x in values)
                {
                    x = value;
                }
            }
            """).Verify(Shader);

        Assert.Contains("static void Execute_Set(thread float* values, int __len_values, float value)", descriptor.MslSource);
        Assert.Contains("::Execute_Set(v, 2, 3.0f);", descriptor.KernelBody);
    }

    [Fact]
    public void ArrayArgument_ConflictsWithAReadOfTheArray_InTheSameExpression()
    {
        // C# runs the helper before it reads a[0]; MSL may read a[0] first.
        GeneratorRun run = RunBody(
            "float[] a = new float[3]; buffer[0] = Fill(a) + a[0];",
            "private static float Fill(Span<float> v) { v[0] = 1; return 2; }");

        Assert.Contains("'a' is modified and used again", run.AssertOnlyDiagnostic("SCG0021", "buffer[0] = Fill(a) + a[0]").GetMessage());

        // A read-only span cannot be written by the helper, so the same shape is fine.
        RunBody(
            "float[] a = new float[3]; buffer[0] = Sum(a) + a[0];",
            "private static float Sum(ReadOnlySpan<float> v) => v[0];").Verify(Shader);
    }

    // ---- foreach ----------------------------------------------------------------------------------

    [Fact]
    public void ForEach_BecomesAnIndexLoop_OverEveryKindOfLocalArray()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            Span<float> taps = stackalloc float[3];
            float[] values = [1, 2, 3];
            Four four = default;
            float sum = 0;

            foreach (ref float t in taps)
            {
                t = 2;
            }

            foreach (float v in values)
            {
                if (v > 2)
                {
                    break;
                }

                sum += v;
            }

            foreach (var f in four)
                sum += f;

            foreach (float outer in taps)
            {
                foreach (float inner in values)
                {
                    if (inner == outer)
                    {
                        continue;
                    }

                    sum += inner * outer;
                }
            }

            buffer[0] = sum + Total(values);
            """,
            """
            private static float Total(ReadOnlySpan<float> values)
            {
                float total = 0;

                foreach (ref readonly float value in values)
                {
                    total += value;
                }

                return total;
            }
            """).Verify(Shader);

        Assert.Equal(
            """
            float taps[3] = {};
            float values[3] = { 1.0f, 2.0f, 3.0f };
            Four four = Four{};
            float sum = 0.0f;
            for (int __i_t = 0; __i_t < 3; __i_t++)
            {
                thread float& t = taps[__i_t];
                t = 2.0f;
            }
            for (int __i_v = 0; __i_v < 3; __i_v++)
            {
                float v = values[__i_v];
                if (v > 2.0f)
                {
                    break;
                }
                sum += v;
            }
            for (int __i_f = 0; __i_f < 4; __i_f++)
            {
                float f = four._e[__i_f];
                sum += f;
            }
            for (int __i_outer = 0; __i_outer < 3; __i_outer++)
            {
                float outer = taps[__i_outer];
                for (int __i_inner = 0; __i_inner < 3; __i_inner++)
                {
                    float inner = values[__i_inner];
                    if (inner == outer)
                    {
                        continue;
                    }
                    sum += inner * outer;
                }
            }
            buffer[0] = sum + ::Total(values, 3);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);

        Assert.Equal(
            """
            float total = 0.0f;
            for (int __i_value = 0; __i_value < __len_values; __i_value++)
            {
                thread const float& value = values[__i_value];
                total += value;
            }
            return total;
            """.ReplaceLineEndings("\n"),
            descriptor.FunctionBody("static float Total("));
    }

    [Fact]
    public void ForEach_OverAnInlineArrayInBoundaryStorage_ReadsTheElements()
    {
        ShaderDescriptor descriptor = RunShader(
            """
                public readonly ReadWriteBuffer<Four> fours;
                public readonly Four weights;

                public void Execute()
                {
                    float sum = 0;

                    foreach (float w in weights)
                    {
                        sum += w;
                    }

                    foreach (float x in fours[ThreadIds.X])
                    {
                        sum += x;
                    }

                    fours[ThreadIds.X][0] = sum;
                }
            """).Verify(Shader);

        Assert.Contains("float w = __c.weights._e[__i_w];", descriptor.KernelBody);
        Assert.Contains("float x = fours[int(__tid.x)]._e[__i_x];", descriptor.KernelBody);
        Assert.Contains("fours[int(__tid.x)]._e[0] = sum;", descriptor.KernelBody);
    }

    // ---- [InlineArray] structs --------------------------------------------------------------------

    [Fact]
    public void InlineArray_IsAStructWithAnArrayMember_GuardedByTheLayoutAsserts()
    {
        ShaderDescriptor descriptor = RunShader(
            """
                public readonly ReadWriteBuffer<float> buffer;
                public readonly ReadWriteBuffer<Points> points;
                public readonly Four weights;

                public void Execute()
                {
                    int i = ThreadIds.X;
                    Four local = weights;
                    local[1] += 2;
                    Points p = points[i];
                    p[2].Y = local[1];
                    points[i] = p;
                    points[i][0] = p[2] * weights[3];
                    buffer[i] = Sum(local) + Make(i)[2];
                }

                private static float Sum(Four f) => f[0] + f[1] + f[2] + f[3];

                private static Four Make(float value)
                {
                    Four f = default;
                    f[2] = value;
                    return f;
                }
            """).Verify(Shader);

        Assert.Contains(
            """
            struct Four
            {
                float _e[4];
            };
            static_assert(sizeof(Four) == 16, "SharpCompute: size of 'Four' does not match its C# layout");
            static_assert(__builtin_offsetof(Four, _e) == 0, "SharpCompute: offset of 'Four._e' does not match its C# layout");
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);

        // Boundary storage, as in any struct: the elements are packed_float3, 12 bytes each.
        Assert.Contains(
            """
            struct Points
            {
                packed_float3 _point[3];
            };
            static_assert(sizeof(Points) == 36, "SharpCompute: size of 'Points' does not match its C# layout");
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);

        Assert.Equal(
            """
            int i = int(__tid.x);
            Four local = __c.weights;
            local._e[1] += 2.0f;
            Points p = points[i];
            p._point[2].y = local._e[1];
            points[i] = p;
            points[i]._point[0] = p._point[2] * __c.weights._e[3];
            buffer[i] = ::Sum(local) + ::Make(float(i))._e[2];
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);

        Assert.Contains("static Four Make(float value)", descriptor.MslSource);
        AssertSizeMatchesClr(descriptor, "Tests.Four", "Four");
        AssertSizeMatchesClr(descriptor, "Tests.Points", "Points");
    }

    [Fact]
    public void InlineArray_AsAStructField_KeepsTheStructOffsets()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            TestSources.Shader(
                """
                    public readonly ReadWriteBuffer<Kernel> kernels;

                    public void Execute()
                    {
                        Kernel k = kernels[ThreadIds.X];
                        k.Taps[k.Count - 1] = k.Scale;
                        kernels[ThreadIds.X].Taps[0] += k.Taps[1];
                        kernels[ThreadIds.X] = k;
                    }
                """) + Types + """

            public struct Kernel
            {
                public int Count;
                public Four Taps;
                public float Scale;
            }
            """);

        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Contains("    Four Taps;", descriptor.MslSource);
        Assert.Contains("k.Taps._e[k.Count - 1] = k.Scale;", descriptor.KernelBody);
        Assert.Contains("kernels[int(__tid.x)].Taps._e[0] += k.Taps._e[1];", descriptor.KernelBody);
        descriptor.AssertLayoutMatchesClr("Kernel", run.LoadType("Tests.Kernel"));
    }

    [Fact]
    public void InlineArrayOfMatrices_IsPacked_UnpackedOnReadAndPackedOnStore()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            TestSources.Shader(
                """
                    public readonly ReadWriteBuffer<Bones> bones;

                    public void Execute()
                    {
                        Bones b = bones[ThreadIds.X];
                        Float4x4 m = b[0];
                        b[1] = m * 2f;
                        b[1] += m;
                        bones[ThreadIds.X] = b;
                    }
                """) + """

            [System.Runtime.CompilerServices.InlineArray(2)]
            public struct Bones { private Float4x4 _m; }
            """);

        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Contains("__ms_packed_float4x4 _m[2];", descriptor.MslSource);
        Assert.Contains("float4x4 m = __ms_unpack(b._m[0]);", descriptor.KernelBody);
        Assert.Contains("b._m[1] = __ms_pack(m * 2.0f);", descriptor.KernelBody);
        Assert.Contains("b._m[1] = __ms_pack(__ms_unpack(b._m[1]) + m);", descriptor.KernelBody);
        AssertSizeMatchesClr(descriptor, "Tests.Bones", "Bones", run);
    }

    [Fact]
    public void InlineArrayOfBools_IsForLocalsOnly_WithoutLayoutAsserts()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            Flags flags = default;
            flags[ThreadIds.X % 2] = true;
            buffer[0] = flags[0] ? 1 : 0;
            """).Verify(Shader);

        Assert.Contains(
            """
            struct Flags
            {
                bool _e[2];
            };
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);
        Assert.DoesNotContain("sizeof(Flags)", descriptor.MslSource);

        Diagnostic capture = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body("buffer[0] = 1;", "public readonly Flags flags;") + Types).Diagnostics.Single();
        Assert.Equal("SCG0006", capture.Id);
        Assert.Contains("its elements of type 'bool' cannot cross the CPU/GPU boundary", capture.GetMessage());
    }

    private static void AssertSizeMatchesClr(ShaderDescriptor descriptor, string typeName, string mslName, GeneratorRun? run = null)
    {
        Type type = (run ?? GeneratorHarness.Run(TestSources.Vectors, TestSources.Body("buffer[0] = 1;") + Types)).LoadType(typeName);
        int size = (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(type).Invoke(null, null)!;

        Assert.Contains($"static_assert(sizeof({mslName}) == {size},", descriptor.MslSource);
    }

    // ---- SCG0036: unsupported local arrays ------------------------------------------------------

    [Theory]
    [InlineData("float[,] grid = new float[2, 2];", "float[,]", "'float[,]' is a multi-dimensional array; use a one-dimensional array and index it as [y * width + x]")]
    [InlineData("float[][] rows = new float[2][];", "float[][]", "'float[][]' is an array of arrays")]
    [InlineData("Span<float> s; s = default;", "s", "it has no initializer; create the array where it is declared")]
    [InlineData("Span<double> s = stackalloc double[2];", "Span<double>", "its element type 'double' is not supported ('double' is not available in Metal Shading Language; use 'float' instead)")]
    [InlineData("float[] a = [1, 2]; float[] b = [..a];", "..a", "its collection expression spreads '..a' into it")]
    [InlineData("Span<float> s = Make();", "Make()", "its initializer 'Make()' is not a stackalloc, an array creation ('new T[N]') or a collection expression ('[a, b, c]')")]
    public void SCG0036_UnsupportedLocalArray(string body, string location, string reason)
    {
        GeneratorRun run = RunBody(body, "private static Span<float> Make() => default;");
        Diagnostic diagnostic = run.Diagnostics.Single(static d => d.Id == "SCG0036");

        Assert.Equal(location, run.TextAt(diagnostic));
        Assert.Contains(reason, diagnostic.GetMessage());
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        AssertStub(run);
    }

    [Fact]
    public void SCG0036_ParameterOfAnArrayOfArrays()
    {
        GeneratorRun run = RunBody("buffer[0] = 1; Use(default!);", "private static void Use(float[][] rows) { }");

        Assert.Contains("'rows' cannot be a local array in a shader: 'float[][]' is an array of arrays", run.AssertOnlyDiagnostic("SCG0036", "float[][]").GetMessage());
    }

    [Fact]
    public void SCG0009_BrokenStructElement_ReportsTheStruct()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            TestSources.Body("Span<Bad> s = stackalloc Bad[2]; buffer[0] = s.Length;") + "\npublic struct Bad { public double Value; }");

        Assert.Contains("'Bad'", run.AssertOnlyDiagnostic("SCG0009", "Span<Bad>").GetMessage());
    }

    // ---- SCG0037: the length --------------------------------------------------------------------

    [Theory]
    [InlineData("int n = ThreadIds.X + 1; Span<float> s = stackalloc float[n];", "n", "'n' is not a compile-time constant")]
    [InlineData("float[] a = new float[(int)factor];", "(int)factor", "'(int)factor' is not a compile-time constant")]
    [InlineData("float[] a = new float[0];", "0", "an empty array has no MSL equivalent")]
    [InlineData("float[] a = [];", "[]", "an empty array has no MSL equivalent")]
    public void SCG0037_LengthIsNotAPositiveConstant(string body, string location, string reason)
    {
        GeneratorRun run = RunBody(body);
        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0037", location);

        Assert.StartsWith("The length of local array ", diagnostic.GetMessage());
        Assert.Contains(reason, diagnostic.GetMessage());
        AssertStub(run);
    }

    // ---- SCG0038: large arrays ------------------------------------------------------------------

    [Fact]
    public void SCG0038_WarnsAbove16KB_ButStillCompiles()
    {
        GeneratorRun run = RunBody(
            """
            Span<float> fits = stackalloc float[4096];
            Span<float> big = stackalloc float[4097];
            Span<Vec3> padded = stackalloc Vec3[1025];
            Huge huge = default;
            fits[0] = 1; big[0] = 1; padded[0].X = 1; huge[0] = 1;
            buffer[0] = fits[0] + big[0] + padded[0].X + huge[0];
            """);

        Diagnostic[] warnings = run.Diagnostics.ToArray();
        Assert.All(warnings, static d => Assert.Equal(("SCG0038", DiagnosticSeverity.Warning), (d.Id, d.Severity)));
        Assert.Equal(["big", "padded", "huge"], warnings.Select(run.TextAt));

        // A float3 takes 16 bytes in a thread's array.
        Assert.StartsWith("Local array 'padded' takes 16,400 bytes per thread, more than 16,384: ", warnings[1].GetMessage());
        Assert.Contains("Local array 'huge' takes 32,768 bytes", warnings[2].GetMessage());

        run.AssertCompiles().Descriptor(Shader).AssertMslCompiles();
    }

    // ---- SCG0039: uses that would alias or escape -----------------------------------------------

    [Theory]
    [InlineData("float[] a = new float[3]; float[] b = a;", "a", "a local array cannot be copied or aliased")]
    [InlineData("float[] a = new float[3]; a = new float[4];", "a", "a local array cannot be assigned after its declaration")]
    [InlineData("float[] a = new float[3]; bool same = a == null;", "a", "it cannot be copied to another variable, compared, returned or stored")]
    [InlineData("float[] a = new float[3]; buffer[0] = (ThreadIds.X > 0 ? a : a)[0];", "ThreadIds.X > 0 ? a : a", "is not a reference")]
    [InlineData("Span<float> s = stackalloc float[4]; Span<float> t = s.Slice(1, 2);", "s.Slice(1, 2)", "the methods of Span<T> and arrays (Slice, CopyTo, Fill, ...) are not available")]
    [InlineData("Span<float> s = stackalloc float[4]; s.Clear();", "s.Clear()", "the methods of Span<T> and arrays")]
    [InlineData("Span<float> s = stackalloc float[4]; Span<float> t = s[1..3];", "s[1..3]", "ranges and indices from the end ('^') of a local array are not supported")]
    [InlineData("Span<float> s = stackalloc float[4]; buffer[0] = s[^1];", "s[^1]", "ranges and indices from the end")]
    [InlineData("Four f = default; buffer[0] = f[^1];", "f[^1]", "ranges and indices from the end ('^') of an inline array are not supported")]
    [InlineData("Four f = default; Span<float> s = f;", "f", "an inline array cannot be converted to a span in a shader")]
    [InlineData("Four f = default; buffer[0] = Sum(f);", "f", "an inline array cannot be converted to a span in a shader")]
    [InlineData("buffer[0] = Sum(stackalloc float[3]);", "stackalloc float[3]", "takes a local array of the caller")]
    [InlineData("buffer[0] = Sum([1, 2]);", "[1, 2]", "takes a local array of the caller")]
    [InlineData("buffer[0] = (new float[2])[0];", "new float[2]", "a local array can only be created as the initializer of a local variable")]
    [InlineData("float[] a = new float[3]; buffer[0] = a.Max();", "a.Max()", "the methods of Span<T> and arrays")]
    public void SCG0039_UnsupportedUse(string body, string location, string reason)
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            "using System.Linq;\n" + TestSources.Body(body, "private static float Sum(ReadOnlySpan<float> v) => v[0];") + Types);
        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0039", location);

        Assert.StartsWith($"'{location}' cannot be used this way in a shader: ", diagnostic.GetMessage());
        Assert.Contains(reason, diagnostic.GetMessage());
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        AssertStub(run);
    }

    [Fact]
    public void SCG0039_HelperReturningALocalArray_IsReportedAtItsSignatureOnly()
    {
        GeneratorRun run = RunBody(
            "buffer[0] = Make()[0];",
            "private static float[] Make() { float[] a = new float[2]; return a; }");

        Assert.Contains(
            "'Make' cannot be used this way in a shader: a helper method cannot return a local array ('float[]')",
            run.AssertOnlyDiagnostic("SCG0039", "float[]").GetMessage());
    }

    [Theory]
    [InlineData("ref Span<float> values")]
    [InlineData("in Span<float> values")]
    [InlineData("out float[] values")]
    public void SCG0039_ArrayParameterByReference(string parameter)
    {
        string body = parameter.StartsWith("out", StringComparison.Ordinal) ? "values = default!; " : string.Empty;
        string call = parameter.StartsWith("out", StringComparison.Ordinal) ? "out float[] a" : (parameter.StartsWith("ref", StringComparison.Ordinal) ? "ref s" : "in s");
        GeneratorRun run = RunBody(
            $"Span<float> s = stackalloc float[2]; Use({call}); buffer[0] = s[0];",
            $"private static void Use({parameter}) {{ {body}}}");

        Assert.Contains("a local array parameter cannot be 'ref', 'out' or 'in'", run.Diagnostics.Single(static d => d.Id == "SCG0039").GetMessage());
        Assert.Equal(parameter, run.TextAt(run.Diagnostics.Single(static d => d.Id == "SCG0039")));
    }

    [Fact]
    public void AtomicOnALocalArrayElement_IsSCG0034()
    {
        // A thread's own array needs no atomics, and MSL has no atomic type in the thread address space.
        GeneratorRun run = RunBody("Span<int> counts = stackalloc int[2]; Gpu.AtomicAdd(ref counts[0], 1); buffer[0] = counts[0];");

        Assert.Equal("SCG0034", Assert.Single(run.Diagnostics).Id);
        Assert.Equal("counts[0]", run.TextAt(run.Diagnostics[0]));
    }

    [Fact]
    public void LocalFunction_CapturingALocalArray_IsSCG0025()
    {
        GeneratorRun run = RunBody(
            """
            float[] a = new float[2];
            buffer[0] = First();

            float First() => a[0];
            """);

        run.AssertOnlyDiagnostic("SCG0025", "First");
    }

    // ---- foreach that stays unsupported (SCG0012) -----------------------------------------------

    [Theory]
    [InlineData("foreach (var v in new float[2]) { }", "a foreach loop over anything but a local array")]
    [InlineData("float[] a = new float[3]; foreach (int x in a) { }", "a foreach loop whose variable is not of the element type ('int' over elements of type 'float')")]
    [InlineData("Four f = default; foreach (ref float x in f) { }", "a 'ref' foreach loop over an inline array")]
    public void SCG0012_ForEachOverAnythingElse(string body, string construct)
    {
        GeneratorRun run = RunBody(body);

        Assert.StartsWith("Unsupported construct in a compute shader: " + construct, run.AssertOnlyDiagnostic("SCG0012", "foreach").GetMessage());
    }
}
