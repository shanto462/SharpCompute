using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// Code shared between shaders: static methods of any type in the compilation, generic methods, and
/// the members of user structs (methods, computed properties, operators, conversions, constructors).
/// </summary>
public class CodeReuseTests
{
    private const string Shader = "Tests.TestShader";

    private static GeneratorRun RunBody(string body, string extraMembers = "", string trailer = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(body, extraMembers) + "\n" + trailer);

    private static int Count(string text, string part) => Regex.Matches(text, Regex.Escape(part)).Count;

    private static void AssertClose(float[] gpu, float[] cpu)
    {
        Assert.Equal(cpu.Length, gpu.Length);

        for (int i = 0; i < cpu.Length; i++)
        {
            Assert.True(MathF.Abs(gpu[i] - cpu[i]) <= 1e-5f * MathF.Max(1, MathF.Abs(cpu[i])), $"[{i}] GPU {gpu[i]:R} != CPU {cpu[i]:R}");
        }
    }

    // ---- static methods of other types ------------------------------------------------------------

    private const string ShaderMath = """
        public static class ShaderMath
        {
            public const float Scale = 0.5f;
            public static readonly Vec2 Offset = new(1, 2);

            public static float Hash(uint x) => (x * 747796405u) * Scale;

            public static float Hash(Vec2 p) => Fn.Dot(p, Offset);

            public static float Squared(this float x) => x * x;
        }

        public static class Noise
        {
            public static float Hash(uint x)
            {
                x ^= 61u;
                return ShaderMath.Hash(x) + 1;
            }
        }

        public class Outer
        {
            public static class Inner
            {
                public static float Twice(float value) => value * 2;
            }
        }

        public struct Shape
        {
            public float Size;

            public static float Unit() => 1;
        }
        """;

    [Fact]
    public void StaticMethodsOfOtherTypes_BecomeFunctionsNamedAfterTheirType()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            buffer[0] = ShaderMath.Hash(1u) + Noise.Hash(2u) + ShaderMath.Hash(3u);
            buffer[1] = Outer.Inner.Twice(factor) + Shape.Unit() + ShaderMath.Hash(new Vec2(1, 2)) + factor.Squared();
            """,
            trailer: ShaderMath).Verify(Shader);

        Assert.Equal(
            """
            buffer[0] = ::ShaderMath_Hash(1u) + ::Noise_Hash(2u) + ::ShaderMath_Hash(3u);
            buffer[1] = ::Outer_Inner_Twice(__c.factor) + ::Shape_Unit() + ::ShaderMath_Hash_1(float2(1.0f, 2.0f)) + ::ShaderMath_Squared(__c.factor);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);

        // Two types each have a Hash(uint), and one of them has two overloads: three functions.
        Assert.Contains(
            """
            static float ShaderMath_Hash(uint x);
            static float Noise_Hash(uint x);
            static float Outer_Inner_Twice(float value);
            static float Shape_Unit();
            static float ShaderMath_Hash_1(float2 p);
            static float ShaderMath_Squared(float x);
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);

        // A const of another type is inlined; a static readonly field becomes a constant named after its type.
        Assert.Contains("constant float2 ShaderMath_Offset = float2(1.0f, 2.0f);", descriptor.MslSource);
        Assert.Equal("return float(x * 747796405u) * 0.5f;", descriptor.FunctionBody("static float ShaderMath_Hash(uint x)"));
        Assert.Equal("return metal::dot(p, ::ShaderMath_Offset);", descriptor.FunctionBody("static float ShaderMath_Hash_1(float2 p)"));
        Assert.Equal("x ^= 61u;\nreturn ::ShaderMath_Hash(x) + 1.0f;", descriptor.FunctionBody("static float Noise_Hash(uint x)"));
    }

    [Fact]
    public void SharedHelpers_AreEmittedOnce_InEachShaderThatUsesThem()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            """
            using SharpCompute;
            using TestVectors;

            namespace Tests;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct First(ReadWriteBuffer<float> buffer) : IComputeShader
            {
                public void Execute() => buffer[0] = ShaderMath.Hash(1u) + Noise.Hash(2u) + ShaderMath.Hash(3u);
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Second(ReadWriteBuffer<float> buffer) : IComputeShader
            {
                public void Execute() => buffer[0] = Twice(ShaderMath.Hash(4u));

                private static float Twice(float value) => ShaderMath.Hash((uint)value) * 2;
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Third(ReadWriteBuffer<float> buffer) : IComputeShader
            {
                public void Execute() => buffer[0] = 1;
            }
            """ + ShaderMath);

        string first = run.Verify("Tests.First").MslSource;
        string second = run.Verify("Tests.Second").MslSource;
        string third = run.Verify("Tests.Third").MslSource;

        // Called three times (twice directly, once through Noise.Hash): one function.
        Assert.Equal(1, Count(first, "static float ShaderMath_Hash(uint x)\n{"));
        Assert.Equal(1, Count(first, "static float Noise_Hash(uint x)\n{"));
        Assert.Equal(1, Count(second, "static float ShaderMath_Hash(uint x)\n{"));

        // Only what a shader reaches is emitted: no Noise in the second, nothing in the third.
        Assert.DoesNotContain("Noise_Hash", second);
        Assert.DoesNotContain("ShaderMath", third);
        Assert.DoesNotContain("static float", third);
    }

    [Fact]
    public void SharedHelpers_UseBuiltinsAtomicsBarriersAndSimdGroups_LikeTheShaderDoes()
    {
        // They are static APIs, so they work in any method: the built-ins a helper reads reach it as
        // hidden parameters, and what it uses is reported on the descriptor of the shader that calls it.
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            public static class Kernels
            {
                public static int Cell() => ThreadIds.X + (DispatchSize.X * ThreadIds.Y);

                public static int Count(ReadWriteBuffer<int> bins, int bin) => Gpu.AtomicAdd(ref bins[bin], 1);

                public static void Sync() => Gpu.GroupMemoryBarrierWithGroupSync();

                public static float LaneSum(float value)
                {
                    return Gpu.WaveActiveSum(value);
                }

                public static float Local() => GroupIds.X + Cell();
            }

            [GeneratedComputeShaderDescriptor]
            [ThreadGroupSize(8)]
            public readonly partial struct Uses(ReadWriteBuffer<float> output, ReadWriteBuffer<int> bins) : IComputeShader
            {
                public void Execute()
                {
                    int cell = Kernels.Cell();
                    Kernels.Count(bins, cell & 3);
                    Kernels.Sync();
                    output[cell] = Kernels.LaneSum(1f) + Kernels.Local();
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Uses");

        Assert.Contains(
            """
            static int Kernels_Cell(uint3 __tid, uint3 __grid);
            static int Kernels_Count(device int* __local_bins, int bin);
            static void Kernels_Sync();
            static float Kernels_LaneSum(float value);
            static float Kernels_Local(uint3 __tid, uint3 __grid, uint3 __gtid);
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);
        Assert.Equal(
            "return metal::atomic_fetch_add_explicit((device metal::atomic_int*)&(__local_bins[bin]), 1, metal::memory_order_relaxed);",
            descriptor.FunctionBody("static int Kernels_Count"));
        Assert.Equal(
            """
            int cell = ::Kernels_Cell(__tid, __grid);
            ::Kernels_Count(bins, cell & 3);
            ::Kernels_Sync();
            output[cell] = ::Kernels_LaneSum(1.0f) + ::Kernels_Local(__tid, __grid, __gtid);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);

        Assert.True(descriptor.UsesAtomics);
        Assert.True(descriptor.UsesThreadgroupMemory);
        Assert.Contains("UsesSimdGroups => true", run.Source("Uses.SharpCompute.g.cs"));
    }

    [Fact]
    public void SharedHelpers_TakeBuffersAndTextures_AndKeepTheRulesOfHelpers()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            public static class Sampling
            {
                public static float Sum(ReadOnlyBuffer<float> data, int at) => data[at] + data[at + 1];

                public static Float4 Bilinear(ReadOnlyTexture2D<Rgba32> image, Float2 uv) => image.Sample(Sampler.LinearClamp, uv);

                public static void Split(float value, out float whole, ref int count)
                {
                    whole = Gpu.Floor(value);
                    count++;
                }
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Uses(ReadWriteBuffer<float> output, ReadOnlyBuffer<float> data, ReadOnlyTexture2D<Rgba32> image) : IComputeShader
            {
                public void Execute()
                {
                    int count = 0;
                    Sampling.Split(Sampling.Sum(data, 0), out float whole, ref count);
                    output[0] = whole + count + Sampling.Bilinear(image, new Float2(0.5f, 0.5f)).X;
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Uses");

        // A parameter called like a captured buffer or texture is renamed, as in the shader's own helpers:
        // those names are the kernel's, and a helper that uses the capture receives it under that name.
        Assert.Contains("static float Sampling_Sum(const device float* __local_data, int at);", descriptor.MslSource);
        Assert.Contains("static float4 Sampling_Bilinear(texture2d<float, access::sample> __local_image, float2 uv);", descriptor.MslSource);
        Assert.Contains("static void Sampling_Split(float value, thread float& whole, thread int& count);", descriptor.MslSource);
        Assert.Equal(
            """
            int count = 0;
            float whole;
            ::Sampling_Split(::Sampling_Sum(data, 0), whole, count);
            output[0] = whole + float(count) + ::Sampling_Bilinear(image, float2(0.5f, 0.5f)).x;
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [Fact]
    public void SharedHelpersAndStructMethods_TakeAndUseLocalArrays()
    {
        // Local arrays are a feature of method bodies, so they work in shared code as in the shader:
        // a span parameter of a static helper, and an array local inside a method of a struct.
        GeneratorRun run = GeneratorHarness.Run("""
            using System;
            using SharpCompute;

            public static class Arrays
            {
                public static float Sum(ReadOnlySpan<float> values)
                {
                    float sum = 0f;

                    for (int i = 0; i < values.Length; i++)
                    {
                        sum += values[i];
                    }

                    return sum;
                }
            }

            public struct Kernel3
            {
                public float Left;
                public float Center;
                public float Right;

                public readonly float Total()
                {
                    Span<float> taps = stackalloc float[3];
                    taps[0] = Left;
                    taps[1] = Center;
                    taps[2] = Right;
                    return Arrays.Sum(taps);
                }
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Uses(ReadWriteBuffer<float> output, Kernel3 weights) : IComputeShader
            {
                public void Execute()
                {
                    Span<float> pair = stackalloc float[2];
                    pair[0] = ThreadIds.X;
                    pair[1] = 0.5f;
                    output[ThreadIds.X] = Arrays.Sum(pair) + weights.Total();
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Uses");

        Assert.Contains("static float Arrays_Sum(thread const float* values, int __len_values);", descriptor.MslSource);
        Assert.Contains("static float Kernel3_Total(Kernel3 __this);", descriptor.MslSource);
        Assert.Contains("return ::Arrays_Sum(taps, 3);", descriptor.FunctionBody("static float Kernel3_Total"));
        Assert.EndsWith("output[int(__tid.x)] = ::Arrays_Sum(pair, 2) + ::Kernel3_Total(__c.weights);", descriptor.KernelBody);
    }

    [Theory]
    [InlineData("buffer[0] = HashCode.Combine(1, 2);", "HashCode.Combine(1, 2)", "it is not an [MslIntrinsic] method, a supported System.Math/MathF method, or a method whose source is part of this compilation (it is declared in the referenced assembly 'System.Private.CoreLib', so there is no C# source to translate; share the source file between the projects instead)")]
    [InlineData("buffer[0] = Box<float>.Twice(factor);", "Box<float>.Twice(factor)", "it is declared in the generic type 'Box<T>'; only generic methods are specialized, so move it to a type that is not generic")]
    [InlineData("buffer[0] = Shapes.Area(new Square { Side = 2 });", "shape.Area()", "it is an instance method of 'IShape', which is not a struct; only static methods and the methods of structs can be called from a shader")]
    [InlineData("buffer[0] = Shapes.Missing(1);", "Shapes.Missing(1)", "it has no body in source")]
    public void MSG0016_MethodsThatCannotBecomeFunctions(string statement, string location, string reason)
    {
        GeneratorRun run = RunBody(statement, trailer: """
            public static class Box<T> { public static float Twice(float value) => value * 2; }

            public interface IShape { float Area(); }

            public struct Square : IShape { public float Side; public float Area() => Side * Side; }

            public static partial class Shapes
            {
                public static float Area<T>(T shape) where T : IShape => shape.Area();

                public static partial float Missing(float value);
            }
            """);

        Diagnostic diagnostic = run.Diagnostics.First(static d => d.Id == "SCG0016");

        Assert.Equal(location, run.TextAt(diagnostic));
        Assert.EndsWith("cannot be called from a compute shader: " + reason, diagnostic.GetMessage());
    }

    [Fact]
    public void SharedHelpers_KeepTheRulesOfHelpers_RecursionCapturesAndEvaluationOrder()
    {
        const string Helpers = """
            public static class Ping { public static int Go(int n) => n <= 0 ? 0 : Pong.Go(n - 1); }

            public static class Pong { public static int Go(int n) => Ping.Go(n - 1) + 1; }

            public static class Local
            {
                public static float Shifted(float value, float offset)
                {
                    return Shift(value);

                    float Shift(float x) => x + offset;
                }

                public static int Unordered(int i) => i++ + i;
            }
            """;

        Assert.Equal(
            "Method 'Go' is recursive (Go -> Go -> Go); recursion is not supported in Metal Shading Language",
            RunBody("buffer[0] = Ping.Go(3);", trailer: Helpers).AssertOnlyDiagnostic("SCG0014", "Ping.Go(n - 1)").GetMessage());

        Assert.Contains("captures 'offset'", RunBody("buffer[0] = Local.Shifted(1, 2);", trailer: Helpers).AssertOnlyDiagnostic("SCG0025", "Shift").GetMessage());

        Assert.StartsWith("'i' is modified and used again", RunBody("buffer[0] = Local.Unordered(1);", trailer: Helpers).AssertOnlyDiagnostic("SCG0021", "i++ + i").GetMessage());
    }

    [Theory]
    [InlineData("buffer[0] = Settings.Gain;", "Settings.Gain", "static fields that are not readonly are not available on the GPU")]
    [InlineData("buffer[0] = BitConverter.IsLittleEndian ? 1 : 0;", "BitConverter.IsLittleEndian", "only static readonly fields whose source is part of this compilation can be used")]
    [InlineData("buffer[0] = Settings.Stored;", "Settings.Stored", "static properties are not available on the GPU, unless they are computed properties whose source is part of this compilation")]
    [InlineData("buffer[0] = Environment.TickCount;", "Environment.TickCount", "static properties are not available on the GPU, unless they are computed properties whose source is part of this compilation")]
    public void MSG0017_StaticStateOfOtherTypes(string statement, string location, string reason)
    {
        GeneratorRun run = RunBody(statement, trailer: "public static class Settings { public static float Gain = 2; public static float Stored { get; } = 1; }");

        Assert.Contains(reason, run.AssertOnlyDiagnostic("SCG0017", location).GetMessage());
    }

    [MetalFact]
    public void SharedHelpers_ComputeOnTheGpuWhatTheyComputeOnTheCpu()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            public static class ShaderMath
            {
                public const float Tau = 6.2831853f;
                public static readonly Float3 LumaWeights = new(0.2126f, 0.7152f, 0.0722f);

                public static uint Hash(uint x)
                {
                    x ^= x >> 16;
                    x *= 0x7FEB352Du;
                    x ^= x >> 15;
                    return x;
                }

                public static float Unit(uint x) => (float)(Hash(x) & 0xFFFFu) / 65535f;

                public static float Luminance(Float3 color) => Gpu.Dot(color, LumaWeights);

                public static Float3 Palette(float t) => (new Float3(Gpu.Cos(t * Tau), Gpu.Sin(t * Tau), t) * 0.5f) + 0.5f;

                public static float Cell => ThreadIds.X * 0.25f;
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Shared(ReadWriteBuffer<float> output, float gain) : IComputeShader
            {
                public void Execute()
                {
                    int i = ThreadIds.X;
                    float t = ShaderMath.Unit((uint)i);
                    output[i] = (ShaderMath.Luminance(ShaderMath.Palette(t)) * gain) + ShaderMath.Cell;
                }
            }
            """);

        (float[] gpu, float[] cpu) = GpuDispatch.ForBoth(run.AssertClean(), "Shared", 64, 64, 3f);

        AssertClose(gpu, cpu);
        Assert.Contains(cpu, static v => v != 0);
    }

    // ---- generic methods --------------------------------------------------------------------------

    private const string Generic = """
        public static class Generic
        {
            public static T Pick<T>(bool condition, T a, T b) => condition ? a : b;

            public static void Swap<T>(ref T a, ref T b)
            {
                T held = a;
                a = b;
                b = held;
            }

            public static T Load<T>(ReadWriteBuffer<T> data, int index) where T : unmanaged => data[index];

            public static T Mix<T>(T a, T b, float t) => Fn.Lerp(a, Pick(t > 1, a, b), t);

            public static T Largest<T>(T a, T b, float key)
            {
                return Choose(key > 0, a, b);

                static T Choose(bool first, T x, T y) => first ? x : y;
            }
        }
        """;

    [Fact]
    public void GenericMethods_BecomeOneFunctionPerSetOfTypeArguments()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            float a = buffer[0], b = buffer[1];
            Generic.Swap(ref a, ref b);
            Vec2 p = new Vec2(a, b), q = new Vec2(b, a);
            Generic.Swap(ref p, ref q);
            buffer[2] = Generic.Pick(a > b, a, b) + Generic.Pick(a > b, p, q).X + Generic.Pick(a < b, 1f, 2f);
            buffer[3] = Generic.Load(buffer, 0) + Generic.Mix(a, b, factor) + Generic.Mix(p, q, factor).Y;
            buffer[4] = Generic.Largest(a, b, factor) + Generic.Largest(p, q, factor).X + First(a, b) + First(p, q).Y;
            """,
            "private static T First<T>(T a, T b) => a;",
            Generic).Verify(Shader);

        // Each instantiation is a function of its own, named after its type arguments; a local function
        // of a generic method, which may name `T`, is one per instantiation too.
        Assert.Contains(
            """
            static void Generic_Swap_float(thread float& a, thread float& b);
            static void Generic_Swap_float2(thread float2& a, thread float2& b);
            static float Generic_Pick_float(bool condition, float a, float b);
            static float2 Generic_Pick_float2(bool condition, float2 a, float2 b);
            static float Generic_Load_float(device float* data, int index);
            static float Generic_Mix_float(float a, float b, float t);
            static float2 Generic_Mix_float2(float2 a, float2 b, float t);
            static float Generic_Largest_Choose_float(bool first, float x, float y);
            static float Generic_Largest_float(float a, float b, float key);
            static float2 Generic_Largest_Choose_float2(bool first, float2 x, float2 y);
            static float2 Generic_Largest_float2(float2 a, float2 b, float key);
            static float First_float(float a, float b);
            static float2 First_float2(float2 a, float2 b);
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);

        Assert.Equal("float2 held = a;\na = b;\nb = held;", descriptor.FunctionBody("static void Generic_Swap_float2"));

        // A generic method that calls another one calls the instantiation for its own type argument.
        Assert.Equal("return metal::mix(a, ::Generic_Pick_float2(t > 1.0f, a, b), t);", descriptor.FunctionBody("static float2 Generic_Mix_float2"));
        Assert.Equal("return ::Generic_Largest_Choose_float2(key > 0.0f, a, b);", descriptor.FunctionBody("static float2 Generic_Largest_float2"));
        Assert.Equal(1, Count(descriptor.MslSource, "static float Generic_Pick_float(bool condition, float a, float b)\n{"));
        Assert.Equal(
            """
            float a = buffer[0];
            float b = buffer[1];
            ::Generic_Swap_float(a, b);
            float2 p = float2(a, b);
            float2 q = float2(b, a);
            ::Generic_Swap_float2(p, q);
            buffer[2] = ::Generic_Pick_float(a > b, a, b) + ::Generic_Pick_float2(a > b, p, q).x + ::Generic_Pick_float(a < b, 1.0f, 2.0f);
            buffer[3] = ::Generic_Load_float(buffer, 0) + ::Generic_Mix_float(a, b, __c.factor) + ::Generic_Mix_float2(p, q, __c.factor).y;
            buffer[4] = ::Generic_Largest_float(a, b, __c.factor) + ::Generic_Largest_float2(p, q, __c.factor).x + ::First_float(a, b) + ::First_float2(p, q).y;
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [Fact]
    public void MSG0046_GenericMethodWithATypeArgumentThatHasNoMslType()
    {
        GeneratorRun run = RunBody("buffer[0] = (float)Generic.Pick(factor > 0, 1.0, 2.0);", trailer: Generic);

        Assert.Equal(
            "Generic method 'Pick' cannot be specialized for the type argument 'double': 'double' is not available in Metal Shading Language; use 'float' instead",
            run.AssertOnlyDiagnostic("SCG0046", "Generic.Pick(factor > 0, 1.0, 2.0)").GetMessage());
    }

    [MetalFact]
    public void GenericMethods_ComputeOnTheGpuWhatTheyComputeOnTheCpu()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            public static class Generic
            {
                public static T Pick<T>(bool condition, T a, T b) => condition ? a : b;

                public static void Swap<T>(ref T a, ref T b)
                {
                    T held = a;
                    a = b;
                    b = held;
                }

                public static T Twice<T>(T value, T zero) => Pick(true, value, zero);
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Generics(ReadWriteBuffer<float> output) : IComputeShader
            {
                public void Execute()
                {
                    int i = ThreadIds.X;
                    float low = i, high = i * 2f;
                    Float2 p = new Float2(i, 1), q = new Float2(2, i);
                    Generic.Swap(ref low, ref high);
                    Generic.Swap(ref p, ref q);
                    int which = Generic.Pick(i % 2 == 0, 10, 20);
                    output[i] = low + (high * 0.5f) + Generic.Pick(i % 3 == 0, p, q).X + which + Generic.Twice(p, q).Y;
                }
            }
            """);

        (float[] gpu, float[] cpu) = GpuDispatch.ForBoth(run.AssertClean(), "Generics", 16, 16);

        AssertClose(gpu, cpu);

        // Thread 1: low and high swapped (2 and 1), q picked after the swap of p and q, 20, and p.Y.
        Assert.Equal(2f + 0.5f + 1f + 20f + 1f, cpu[1]);
    }

    // ---- members of user structs ------------------------------------------------------------------

    private const string Ray = """
        public struct Ray
        {
            public Vec3 Origin;
            public Vec3 Direction;
            public float Far = 100;

            public Ray(Vec3 origin, Vec3 direction)
            {
                Origin = origin;
                Direction = direction;
            }

            public Ray(Vec3 origin) : this(origin, new Vec3(0, 0, 1)) => Far = 10;

            public readonly Vec3 At(float t) => Origin + (Direction * t);

            public float Reach
            {
                get { return Far * 2; }
            }

            public float Length => Fn.Length(Direction);

            public float this[int axis] => Origin[axis];

            public void Advance(float t)
            {
                Origin = At(t);
                Far -= t;
            }

            public Ray Moved(float t)
            {
                Ray copy = this;
                copy.Advance(t);
                return copy;
            }

            public static Ray Along(Vec3 direction) => new(new Vec3(0, 0, 0), direction);
        }
        """;

    [Fact]
    public void StructMembers_BecomeFunctionsThatTakeTheStruct()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            Ray ray = new Ray(new Vec3(1, 2, 3), new Vec3(0, 1, 0));
            ray.Advance(factor);
            Ray other = new Ray(ray.At(2)).Moved(1);
            Ray third = Ray.Along(other.Direction);
            buffer[0] = ray.Reach + ray.Length + ray[1] + other.At(1).X + third.Far + new Ray().Far + default(Ray).Far;
            """,
            trailer: Ray).Verify(Shader);

        // A method that does not modify the struct takes a copy; one that does takes a reference.
        Assert.Contains(
            """
            static Ray Ray_new(float3 origin, float3 direction);
            static float3 Ray_At(Ray __this, float t);
            static void Ray_Advance(thread Ray& __this, float t);
            static Ray Ray_Moved(Ray __this, float t);
            static Ray Ray_new_1(float3 origin);
            static Ray Ray_Along(float3 direction);
            static float Ray_Reach(Ray __this);
            static float Ray_Length(Ray __this);
            static float Ray_Item(Ray __this, int axis);
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);

        Assert.Equal("return __this.Origin + (__this.Direction * t);", descriptor.FunctionBody("static float3 Ray_At"));
        Assert.Equal("__this.Origin = ::Ray_At(__this, t);\n__this.Far -= t;", descriptor.FunctionBody("static void Ray_Advance"));

        // `this` as a value is the struct the function received.
        Assert.Equal("Ray copy = __this;\n::Ray_Advance(copy, t);\nreturn copy;", descriptor.FunctionBody("static Ray Ray_Moved"));
        Assert.Equal("return __this.Far * 2.0f;", descriptor.FunctionBody("static float Ray_Reach"));
        Assert.Equal("return metal::length(__this.Direction);", descriptor.FunctionBody("static float Ray_Length"));
        Assert.Equal("return __this.Origin[axis];", descriptor.FunctionBody("static float Ray_Item"));
        Assert.Equal(
            """
            Ray ray = ::Ray_new(float3(1.0f, 2.0f, 3.0f), float3(0.0f, 1.0f, 0.0f));
            ::Ray_Advance(ray, __c.factor);
            Ray other = ::Ray_Moved(::Ray_new_1(::Ray_At(ray, 2.0f)), 1.0f);
            Ray third = ::Ray_Along(other.Direction);
            buffer[0] = ::Ray_Reach(ray) + ::Ray_Length(ray) + ::Ray_Item(ray, 1) + ::Ray_At(other, 1.0f).x + third.Far + Ray{}.Far + Ray{}.Far;
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [Fact]
    public void MutatingStructMethods_AreEmittedPerAddressSpace_AndTakeACopyWhereCSharpCopies()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            public struct Particle
            {
                public Float2 Position;
                public Float2 Velocity;
                public float Age;

                public readonly float Speed => Gpu.Length(Velocity);

                public void Step(float dt)
                {
                    Position += Velocity * dt;
                    Age += dt;
                }

                public void Bounce()
                {
                    Velocity = -Velocity;
                    Step(0.5f);
                }
            }

            public struct Emitter
            {
                public Particle Seed;
                public readonly Particle Fixed;
            }

            [GeneratedComputeShaderDescriptor]
            [ThreadGroupSize(4)]
            public readonly partial struct Spaces(ReadWriteBuffer<Particle> particles, ReadOnlyBuffer<Particle> seeds, ReadWriteBuffer<Emitter> emitters, Particle origin) : IComputeShader
            {
                [GroupShared]
                private static readonly Particle[] shared = new Particle[4];

                private static float Aged(in Particle particle)
                {
                    particle.Step(1);
                    return particle.Age;
                }

                private static void Kick(ref Particle particle) => particle.Bounce();

                public void Execute()
                {
                    int i = ThreadIds.X;
                    Particle local = seeds[i];
                    local.Step(0.1f);
                    particles[i].Step(0.2f);
                    emitters[i].Seed.Bounce();
                    shared[GroupIds.X] = local;
                    shared[GroupIds.X].Step(0.3f);
                    seeds[i].Step(0.4f);
                    origin.Step(0.5f);
                    emitters[i].Fixed.Step(0.6f);
                    Kick(ref local);
                    particles[i].Age += Aged(local) + local.Speed + particles[i].Speed + shared[GroupIds.X].Speed + origin.Speed;
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Spaces");

        // An MSL reference names its address space, so Step exists once for each one it is called in,
        // and once more for the calls C# makes on a copy. Bounce, which calls Step, follows suit.
        Assert.Contains(
            """
            static void Particle_Step(thread Particle& __this, float dt);
            static void Particle_Step_device(device Particle& __this, float dt);
            static void Particle_Bounce_device(device Particle& __this);
            static void Particle_Step_threadgroup(threadgroup Particle& __this, float dt);
            static void Particle_Step_copy(Particle __this, float dt);
            static void Particle_Bounce(thread Particle& __this);
            static void Kick(thread Particle& particle);
            static float Aged(Particle particle);
            static float Particle_Speed(Particle __this);
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);

        Assert.Equal("__this.Velocity = -__this.Velocity;\n::Particle_Step_device(__this, 0.5f);", descriptor.FunctionBody("static void Particle_Bounce_device"));
        Assert.Equal("__this.Velocity = -__this.Velocity;\n::Particle_Step(__this, 0.5f);", descriptor.FunctionBody("static void Particle_Bounce("));

        // An `in` parameter is read-only in C#, which runs Step on a copy of it; so does the MSL.
        Assert.Equal("::Particle_Step_copy(particle, 1.0f);\nreturn particle.Age;", descriptor.FunctionBody("static float Aged"));
        Assert.Equal(
            """
            threadgroup Particle shared[4];
            int i = int(__tid.x);
            Particle local = seeds[i];
            ::Particle_Step(local, 0.1f);
            ::Particle_Step_device(particles[i], 0.2f);
            ::Particle_Bounce_device(emitters[i].Seed);
            shared[int(__gtid.x)] = local;
            ::Particle_Step_threadgroup(shared[int(__gtid.x)], 0.3f);
            ::Particle_Step_copy(seeds[i], 0.4f);
            ::Particle_Step_copy(__c.origin, 0.5f);
            ::Particle_Step_copy(emitters[i].Fixed, 0.6f);
            ::Kick(local);
            particles[i].Age += ::Aged(local) + ::Particle_Speed(local) + ::Particle_Speed(particles[i]) + ::Particle_Speed(shared[int(__gtid.x)]) + ::Particle_Speed(__c.origin);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [MetalFact]
    public void MutatingStructMethods_OnLocalArrayElementsAndForeachVariables_DoWhatCSharpDoes()
    {
        // An element of a stackalloc span or an array local is a variable, and so is the variable of a
        // foreach loop (C# forbids assigning it, but calls a method on it in place; with `ref` it is
        // the element itself). An element of a ReadOnlySpan is read-only: C# calls the method on a
        // copy, and so does the MSL.
        GeneratorRun run = GeneratorHarness.Run("""
            using System;
            using SharpCompute;

            public struct Counter
            {
                public float Count;

                public float Bump(float by)
                {
                    Count += by;
                    return Count;
                }
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Arrays(ReadWriteBuffer<float> output) : IComputeShader
            {
                private static float First(ReadOnlySpan<Counter> counters) => counters[0].Bump(100f) + counters[0].Count;

                public void Execute()
                {
                    Span<Counter> span = stackalloc Counter[2];
                    Counter[] array = new Counter[2];
                    span[1].Bump(ThreadIds.X + 1f);
                    array[0].Bump(2f);
                    float sum = 0f;

                    foreach (Counter counter in span)
                    {
                        counter.Bump(1000f);
                        sum += counter.Count;
                    }

                    foreach (ref Counter counter in span)
                    {
                        counter.Bump(0.5f);
                    }

                    output[ThreadIds.X] = sum + span[0].Count + (span[1].Count * 10f) + (array[0].Count * 100f) + First(span);
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Arrays");

        Assert.Contains("::Counter_Bump(span[1], float(int(__tid.x)) + 1.0f);", descriptor.KernelBody);
        Assert.Contains("::Counter_Bump(array[0], 2.0f);", descriptor.KernelBody);
        Assert.Contains("::Counter_Bump(counter, 1000.0f);", descriptor.KernelBody);
        Assert.Contains("::Counter_Bump(counter, 0.5f);", descriptor.KernelBody);
        Assert.Equal("return ::Counter_Bump_copy(counters[0], 100.0f) + counters[0].Count;", descriptor.FunctionBody("static float First"));

        (float[] gpu, float[] cpu) = GpuDispatch.ForBoth(run, "Arrays", 4, 4);

        AssertClose(gpu, cpu);

        // Thread 2: the loop's copies become 1000 and 1003 (the span keeps 0 and 3), then the `ref`
        // loop makes the span (0.5, 3.5); array[0] = 2; First = 100.5 from the copy + 0.5 still in the span.
        Assert.Equal(2003f + 0.5f + 35f + 200f + 101f, cpu[2]);
    }

    private const string Complex = """
        public struct Complex
        {
            public float Re;
            public float Im;

            public Complex(float re, float im)
            {
                Re = re;
                Im = im;
            }

            public readonly float Norm => (Re * Re) + (Im * Im);

            public static Complex operator +(Complex a, Complex b) => new(a.Re + b.Re, a.Im + b.Im);

            public static Complex operator *(Complex a, Complex b) => new((a.Re * b.Re) - (a.Im * b.Im), (a.Re * b.Im) + (a.Im * b.Re));

            public static Complex operator *(Complex a, float scale) => new(a.Re * scale, a.Im * scale);

            public static Complex operator -(Complex a) => new(-a.Re, -a.Im);

            public static bool operator ==(Complex a, Complex b) => a.Re == b.Re && a.Im == b.Im;

            public static bool operator !=(Complex a, Complex b) => !(a == b);

            public static implicit operator Complex(float re) => new(re, 0);

            public static explicit operator float(Complex c) => c.Re;

            public override bool Equals(object? other) => other is Complex c && c == this;

            public override int GetHashCode() => 0;
        }
        """;

    [Fact]
    public void OperatorsAndConversionsOfUserStructs_BecomeFunctions()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            Complex z = new Complex(buffer[0], buffer[1]);
            Complex c = 2;
            Complex w = (z * z) + c;
            w += z;
            w *= factor;
            w = -w * 3;
            buffer[2] = w == z ? (float)w : w.Norm + (w != c ? 1 : 0);
            """,
            trailer: Complex).Verify(Shader);

        Assert.Contains(
            """
            static Complex Complex_new(float re, float im);
            static Complex Complex_op_Implicit(float re);
            static Complex Complex_op_Addition(Complex a, Complex b);
            static Complex Complex_op_Multiply(Complex a, Complex b);
            static Complex Complex_op_Multiply_1(Complex a, float scale);
            static Complex Complex_op_UnaryNegation(Complex a);
            static bool Complex_op_Equality(Complex a, Complex b);
            static float Complex_op_Explicit(Complex c);
            static float Complex_Norm(Complex __this);
            static bool Complex_op_Inequality(Complex a, Complex b);
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);

        Assert.Equal("return ::Complex_new(a.Re + b.Re, a.Im + b.Im);", descriptor.FunctionBody("static Complex Complex_op_Addition"));
        Assert.Equal("return !(::Complex_op_Equality(a, b));", descriptor.FunctionBody("static bool Complex_op_Inequality"));

        // `w += z` names its target twice; `Complex c = 2` converts the int before the operator sees it.
        Assert.Equal(
            """
            Complex z = ::Complex_new(buffer[0], buffer[1]);
            Complex c = ::Complex_op_Implicit(2.0f);
            Complex w = ::Complex_op_Addition(::Complex_op_Multiply(z, z), c);
            w = ::Complex_op_Addition(w, z);
            w = ::Complex_op_Multiply_1(w, __c.factor);
            w = ::Complex_op_Multiply_1(::Complex_op_UnaryNegation(w), 3.0f);
            buffer[2] = ::Complex_op_Equality(w, z) ? ::Complex_op_Explicit(w) : ::Complex_Norm(w) + float(::Complex_op_Inequality(w, c) ? 1 : 0);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [Fact]
    public void Constructors_BecomeFunctionsThatReturnTheStruct()
    {
        ShaderDescriptor descriptor = RunBody(
            """
            Box a = new Box(new Vec2(1, 2), new Vec2(3, 4));
            Box b = new(2f);
            Box c = new Box(a.Min);
            Box d = new Box(a.Min, 3, buffer[0] > 0);
            Sphere sphere = new Sphere(new Vec3(0, 0, 1), 2);
            Lcg random = new Lcg(7u);
            float first = random.Next();
            float second = random.Next();
            buffer[0] = a.Max.X + b.Margin + c.Max.Y + d.Max.X + sphere.Area + sphere.Radius + first + second + random.Calls + new Scaled(factor).Value;
            """,
            trailer: """
            public struct Box
            {
                public Vec2 Min;
                public Vec2 Max = new(1, 1);

                public float Margin { get; set; } = 0.5f;

                public Box(Vec2 min, Vec2 max)
                {
                    Min = min;
                    Max = max;
                }

                public Box(float size) : this(new Vec2(0, 0), new Vec2(size, size))
                {
                    Margin = size > 4 ? 1 : 0;
                }

                public Box(Vec2 min) => Min = min;

                public Box(Vec2 min, float size, bool grow)
                {
                    Min = min;

                    if (!grow)
                    {
                        return;
                    }

                    Max = new Vec2(size, size);
                }
            }

            public readonly record struct Sphere(Vec3 Center, float Radius)
            {
                public float Area => 12.566371f * Radius * Radius;
            }

            public struct Lcg(uint seed)
            {
                public uint Calls = 0;

                public float Next()
                {
                    seed = (seed * 1664525u) + 1013904223u;
                    Calls++;
                    return (float)(seed >> 8) / 16777216f;
                }
            }

            public struct Scaled(float scale)
            {
                public float Value = scale * 2;
            }
            """).Verify(Shader);

        // Field initializers run before the body, unless the constructor chains to another one.
        Assert.Equal(
            """
            Box __this = Box{};
            __this.Max = float2(1.0f, 1.0f);
            __this.Margin = 0.5f;
            __this.Min = min;
            __this.Max = max;
            return __this;
            """.ReplaceLineEndings("\n"),
            descriptor.FunctionBody("static Box Box_new(float2 min, float2 max)"));
        Assert.Equal(
            """
            Box __this = Box{};
            __this = ::Box_new(float2(0.0f, 0.0f), float2(size, size));
            __this.Margin = float(size > 4.0f ? 1 : 0);
            return __this;
            """.ReplaceLineEndings("\n"),
            descriptor.FunctionBody("static Box Box_new_1(float size)"));
        Assert.Equal(
            "Box __this = Box{};\n__this.Max = float2(1.0f, 1.0f);\n__this.Margin = 0.5f;\n__this.Min = min;\nreturn __this;",
            descriptor.FunctionBody("static Box Box_new_2(float2 min)"));

        // `return;` in a constructor returns what it has built.
        Assert.Contains("if (!grow)\n{\n    return __this;\n}\n__this.Max = float2(size, size);\nreturn __this;", descriptor.FunctionBody("static Box Box_new_3"));

        // A record's positional parameters are its properties; a primary-constructor parameter that a
        // method reads is a field the compiler adds, which the layout includes and the MSL names.
        Assert.Equal("Sphere __this = Sphere{};\n__this.Center = Center;\n__this.Radius = Radius;\nreturn __this;", descriptor.FunctionBody("static Sphere Sphere_new"));
        Assert.Contains("struct Lcg\n{\n    uint seed;\n    uint Calls;\n};", descriptor.MslSource);
        Assert.Equal("Lcg __this = Lcg{};\n__this.seed = seed;\n__this.Calls = 0u;\nreturn __this;", descriptor.FunctionBody("static Lcg Lcg_new"));
        Assert.Equal(
            "__this.seed = (__this.seed * 1664525u) + 1013904223u;\n__this.Calls++;\nreturn float(__this.seed >> 8) / 16777216.0f;",
            descriptor.FunctionBody("static float Lcg_Next(thread Lcg& __this)"));

        // A parameter that only initializes a field is not kept.
        Assert.Contains("struct Scaled\n{\n    float Value;\n};", descriptor.MslSource);
        Assert.Equal("Scaled __this = Scaled{};\n__this.Value = scale * 2.0f;\nreturn __this;", descriptor.FunctionBody("static Scaled Scaled_new"));
        Assert.Equal(
            """
            Box a = ::Box_new(float2(1.0f, 2.0f), float2(3.0f, 4.0f));
            Box b = ::Box_new_1(2.0f);
            Box c = ::Box_new_2(a.Min);
            Box d = ::Box_new_3(a.Min, 3.0f, buffer[0] > 0.0f);
            Sphere sphere = ::Sphere_new(float3(0.0f, 0.0f, 1.0f), 2.0f);
            Lcg random = ::Lcg_new(7u);
            float first = ::Lcg_Next(random);
            float second = ::Lcg_Next(random);
            buffer[0] = a.Max.x + b.Margin + c.Max.y + d.Max.x + ::Sphere_Area(sphere) + sphere.Radius + first + second + float(random.Calls) + ::Scaled_new(__c.factor).Value;
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    // ---- instance members of the shader ------------------------------------------------------------

    [Fact]
    public void InstanceMembersOfTheShader_CalledOnThis_ReceiveTheCapturedState()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            TestSources.Body(
                "buffer[0] = this.Scaled(2) + Half + this.Half + Unit + TestShader.Unit + First(factor, 1f) + Doubled() + this[1];",
                """
                private float Scaled(float value) => value * factor;

                private float Half => factor * 0.5f;

                private static float Unit => 1;

                private T First<T>(T a, T b) => Scaled(1) > 0 ? a : b;

                private float this[int index] => buffer[index] * Half;
                """),
            """
            namespace Tests;

            public readonly partial struct TestShader
            {
                private float Doubled() => this.Scaled(2) * Unit;
            }
            """);

        ShaderDescriptor descriptor = run.Verify(Shader);

        // `this.` changes nothing; a computed property (or indexer) of the shader is a helper like a
        // method, also when it is declared in another part of the struct; a generic one is specialized.
        Assert.Contains(
            """
            static float Scaled(float value, constant __Constants& __c);
            static float Half(constant __Constants& __c);
            static float Unit();
            static float First_float(float a, float b, constant __Constants& __c);
            static float Doubled(constant __Constants& __c);
            static float Item(int index, device float* buffer, constant __Constants& __c);
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);
        Assert.Equal("return ::Scaled(2.0f, __c) * ::Unit();", descriptor.FunctionBody("static float Doubled"));
        Assert.Equal("return buffer[index] * ::Half(__c);", descriptor.FunctionBody("static float Item"));
        Assert.Equal(
            "buffer[0] = ::Scaled(2.0f, __c) + ::Half(__c) + ::Half(__c) + ::Unit() + ::Unit() + ::First_float(__c.factor, 1.0f, __c) + ::Doubled(__c) + ::Item(1, buffer, __c);",
            descriptor.KernelBody);
    }

    // ---- diagnostics for members of user structs ---------------------------------------------------

    private const string Counter = """
        public struct Counter
        {
            public int Count;

            public int Next() => ++Count;

            public readonly int Peek() => Count;

            public int Twice() => Count++ + Count;

            public int Total
            {
                get => Count;
                set => Count = value;
            }

            public int Drawn => Count++;
        }
        """;

    [Fact]
    public void MSG0021_AStructModifiedAndUsedAgainInOneExpression()
    {
        // A method that modifies its struct is a write of the struct, like a `ref` argument.
        GeneratorRun run = RunBody("Counter counter = default; buffer[0] = counter.Next() + counter.Next();", trailer: Counter);

        Assert.StartsWith("'counter' is modified and used again within one expression", run.AssertOnlyDiagnostic("SCG0021", "buffer[0] = counter.Next() + counter.Next()").GetMessage());

        // Inside a member, the struct is `this`.
        run = RunBody("Counter counter = default; buffer[0] = counter.Twice();", trailer: Counter);

        Assert.StartsWith("'this' is modified and used again within one expression", run.AssertOnlyDiagnostic("SCG0021", "Count++ + Count").GetMessage());

        // A getter that modifies its struct is such a method too, and takes the struct by reference.
        run = RunBody("Counter counter = default; buffer[0] = counter.Drawn + counter.Drawn;", trailer: Counter);

        Assert.StartsWith("'counter' is modified and used again", run.AssertOnlyDiagnostic("SCG0021", "buffer[0] = counter.Drawn + counter.Drawn").GetMessage());

        ShaderDescriptor drawn = RunBody("Counter counter = default; int first = counter.Drawn; buffer[0] = first + counter.Drawn;", trailer: Counter).Verify(Shader);

        Assert.Contains("static int Counter_Drawn(thread Counter& __this);", drawn.MslSource);
        Assert.Equal("Counter counter = Counter{};\nint first = ::Counter_Drawn(counter);\nbuffer[0] = float(int(uint(first) + uint(::Counter_Drawn(counter))));", drawn.KernelBody);

        // Methods that only read may be called as often as you like.
        RunBody("Counter counter = default; counter.Next(); buffer[0] = counter.Peek() + counter.Peek() + counter.Count;", trailer: Counter).Verify(Shader);
    }

    [Theory]
    [InlineData("counter.Total = 3;", "counter.Total")]
    [InlineData("counter.Total += 3;", "counter.Total")]
    [InlineData("counter.Total++;", "counter.Total")]
    public void MSG0045_ComputedPropertyAssigned(string statement, string location)
    {
        GeneratorRun run = RunBody("Counter counter = default; " + statement + " buffer[0] = counter.Total;", trailer: Counter);

        Assert.Equal(
            "Property 'Total' of 'Counter' has accessors with bodies, and only its getter is translated; assign the fields it stores to, or do the work in a method",
            run.AssertOnlyDiagnostic("SCG0045", location).GetMessage());
    }

    [Fact]
    public void MSG0034_AtomicOnAFieldOfTheStructAMethodIsCalledOn()
    {
        GeneratorRun run = RunBody("Tally tally = default; buffer[0] = tally.Bump();", trailer: """
            public struct Tally
            {
                public int Count;

                public int Bump() => Gpu.AtomicAdd(ref Count, 1);
            }
            """);

        Assert.Contains(
            "it is a field of the struct this method is called on, and only whole elements of a buffer or of a [GroupShared] array can be atomic",
            run.AssertOnlyDiagnostic("SCG0034", "Count").GetMessage());
    }

    [Fact]
    public void AProblemInSharedCode_IsReportedByEveryShaderThatUsesIt_AtTheSharedCode()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            public static class Broken
            {
                public static float Half(float value) => (float)(value * 0.5);
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct First(ReadWriteBuffer<float> buffer) : IComputeShader
            {
                public void Execute() => buffer[0] = Broken.Half(1);
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Second(ReadWriteBuffer<float> buffer) : IComputeShader
            {
                public void Execute() => buffer[0] = Broken.Half(2);
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Third(ReadWriteBuffer<float> buffer) : IComputeShader
            {
                public void Execute() => buffer[0] = 3;
            }
            """);

        // The diagnostic points into the helper, where the fix is; a shader that does not call it is unaffected.
        Assert.Equal(2, run.Diagnostics.Length);
        Assert.All(run.Diagnostics, d => Assert.Equal("SCG0007", d.Id));
        Assert.All(run.Diagnostics, d => Assert.Equal("value * 0.5", run.TextAt(d)));
        Assert.Contains("metal_stdlib", run.Source("Third.SharpCompute.g.cs"));
        Assert.DoesNotContain("metal_stdlib", run.Source("First.SharpCompute.g.cs"));
    }

    [Fact]
    public void MSG0018_CapturedStructModifiedThroughAMethod()
    {
        const string Source = """
            using SharpCompute;

            public struct Counter
            {
                public int Count;

                public int Next() => ++Count;
            }

            [GeneratedComputeShaderDescriptor]
            public MODIFIER partial struct Counting : IComputeShader
            {
                public MODIFIER ReadWriteBuffer<int> output;
                public MODIFIER Counter counter;

                public void Execute() => output[ThreadIds.X] = counter.Next();
            }
            """;

        // In a readonly shader C# runs Next on a copy of the field, and so does the GPU.
        ShaderDescriptor descriptor = GeneratorHarness.Run(Source.Replace("MODIFIER", "readonly")).Verify("Counting");

        Assert.Equal("output[int(__tid.x)] = ::Counter_Next_copy(__c.counter);", descriptor.KernelBody);
        Assert.Contains("static int Counter_Next_copy(Counter __this)", descriptor.MslSource);

        // In a shader that is not readonly, C# would modify the field itself, which the GPU cannot.
        GeneratorRun run = GeneratorHarness.Run(Source.Replace("MODIFIER ", string.Empty));

        Assert.Equal(
            "Cannot modify 'counter': captured shader state is read-only on the GPU (write to a ReadWriteBuffer<T> or a local instead)",
            run.AssertDiagnostic("SCG0018", "counter.Next()").GetMessage());
    }

    [Theory]
    [InlineData("point = point + point;", "SCG0016", "point + point", "only operators declared by [MslType] vector types, or by a type whose source is part of this compilation, are supported")]
    [InlineData("point = -point;", "SCG0016", "-point", "only operators declared by [MslType] vector types, or by a type whose source is part of this compilation, are supported")]
    [InlineData("point += point;", "SCG0016", "point += point", "only operators declared by [MslType] vector types, or by a type whose source is part of this compilation, are supported")]
    [InlineData("buffer[1] = point.Length();", "SCG0016", "point.Length()", "it is not an [MslIntrinsic] method, a supported System.Math/MathF method, or a method whose source is part of this compilation (it is declared in the referenced assembly '")]
    [InlineData("point = new System.Numerics.Vector2(1, 2);", "SCG0012", "new System.Numerics.Vector2(1, 2)", "a constructor call on the struct 'Vector2', whose source is not part of this compilation (use 'default' and assign the fields instead)")]
    public void MembersOfStructsFromReferencedAssemblies_AreADiagnostic(string statement, string id, string location, string reason)
    {
        // The struct itself is fine (its fields are known from metadata); its members have no source.
        GeneratorRun run = RunBody("System.Numerics.Vector2 point = default; point.X = buffer[0]; " + statement + " buffer[0] = point.X;");

        Assert.Contains(reason, run.AssertOnlyDiagnostic(id, location).GetMessage());
    }

    // ---- GPU against CPU ---------------------------------------------------------------------------

    [MetalFact]
    public void StructMembers_ComputeOnTheGpuWhatTheyComputeOnTheCpu()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            public struct Ray
            {
                public Float3 Origin;
                public Float3 Direction;
                public float Far = 100;

                public Ray(Float3 origin, Float3 direction)
                {
                    Origin = origin;
                    Direction = Gpu.Normalize(direction);
                }

                public Ray(Float3 origin) : this(origin, new Float3(0, 0, 1)) => Far = 10;

                public readonly Float3 At(float t) => Origin + (Direction * t);

                public float Reach => Far * 2;

                public void Advance(float t)
                {
                    Origin = At(t);
                    Far -= t;
                }

                public static Ray operator +(Ray ray, Float3 offset) => new(ray.Origin + offset, ray.Direction);

                public static explicit operator float(Ray ray) => ray.Far;
            }

            public readonly record struct Sphere(Float3 Center, float Radius)
            {
                public float Intersect(Ray ray)
                {
                    Float3 oc = ray.Origin - Center;
                    float b = Gpu.Dot(oc, ray.Direction);
                    float h = (b * b) - (Gpu.Dot(oc, oc) - (Radius * Radius));

                    return h < 0 ? -1f : -b - Gpu.Sqrt(h);
                }
            }

            public struct Lcg(uint seed)
            {
                public float Next()
                {
                    seed = (seed * 1664525u) + 1013904223u;
                    return (float)(seed >> 8) / 16777216f;
                }
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Trace(ReadWriteBuffer<float> output, Sphere sphere, float spread) : IComputeShader
            {
                private float Jitter => spread * 0.5f;

                public void Execute()
                {
                    int i = ThreadIds.X;
                    Lcg random = new Lcg((uint)i);
                    float dx = (random.Next() - 0.5f) * spread;
                    float dy = (random.Next() - 0.5f) * this.Jitter;
                    Ray ray = new Ray(new Float3(dx, dy, -4));
                    ray.Advance(0.5f);
                    ray = ray + new Float3(0, 0, -0.25f);
                    ray += new Float3(0.01f, 0, 0);
                    float t = sphere.Intersect(ray);
                    output[i] = t + ray.At(t).Z + ray.Reach + (float)ray + new Ray(ray.Origin, new Float3(1, 1, 0)).Direction.X;
                }
            }
            """);

        Type sphere = run.AssertClean().LoadType("Sphere");
        object unit = Activator.CreateInstance(sphere, new Float3(0, 0, 1), 1.5f)!;
        (float[] gpu, float[] cpu) = GpuDispatch.ForBoth(run, "Trace", 64, 64, unit, 6f);

        AssertClose(gpu, cpu);

        // Some rays hit the sphere and some miss it (t = -1): both paths ran.
        Assert.Contains(cpu, static v => v > 300);
        Assert.Contains(cpu, static v => v < 298);
    }

    [MetalFact]
    public void InstanceMembersOfTheShader_ComputeOnTheGpuWhatTheyComputeOnTheCpu()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Members(ReadWriteBuffer<float> output, float scale, float offset) : IComputeShader
            {
                private float Slope => scale * 2;

                private static float Unit => 1;

                private float this[int index] => (index * Slope) + offset;

                private T Choose<T>(T a, T b) => offset > 0 ? a : b;

                private float Apply(float x) => this[(int)x] + this.Choose(Unit, -Unit);

                public void Execute() => output[ThreadIds.X] = this.Apply(ThreadIds.X) + Choose(new Float2(3, 4), new Float2(5, 6)).Y;
            }
            """);

        (float[] gpu, float[] cpu) = GpuDispatch.ForBoth(run.AssertClean(), "Members", 8, 8, 1.5f, 0.25f);

        AssertClose(gpu, cpu);
        Assert.Equal([5.25f, 8.25f, 11.25f], cpu[..3]);
    }

    // ---- the README --------------------------------------------------------------------------------

    /// <summary>A read-only buffer of a struct that only exists in the generated assembly.</summary>
    private static ReadOnlyBuffer<T> AllocateReadOnly<T>(GraphicsDevice device, Array data)
        where T : unmanaged => device.AllocateReadOnlyBuffer<T>((T[])data);

    [MetalFact]
    public void TheReadmeExample_TranslatesAndMatchesTheCpu()
    {
        // The code of "Sharing code between shaders" in the README, as it is printed there (plus a
        // shader-shaped output buffer first, which is what GpuDispatch fills in).
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            public static class ShaderMath
            {
                public const float Tau = 6.2831853f;                                  // inlined where it is used
                public static readonly Float3 SunDirection = Gpu.Normalize(new Float3(0.55f, 0.75f, -0.45f)); // an MSL constant

                public static uint Hash(uint x)
                {
                    x ^= x >> 16;
                    x *= 0x7FEB352Du;
                    x ^= x >> 15;
                    return x;
                }

                public static T Pick<T>(bool condition, T whenTrue, T whenFalse) => condition ? whenTrue : whenFalse;
            }

            public struct Ray
            {
                public Float3 Origin;
                public Float3 Direction;

                public Ray(Float3 origin, Float3 direction)
                {
                    Origin = origin;
                    Direction = Gpu.Normalize(direction);
                }

                public readonly Float3 At(float distance) => Origin + (Direction * distance);
            }

            public struct Sphere
            {
                public Float3 Center;
                public float Radius;

                public readonly float Intersect(Ray ray)
                {
                    Float3 offset = ray.Origin - Center;
                    float b = Gpu.Dot(offset, ray.Direction);
                    float discriminant = (b * b) - (Gpu.Dot(offset, offset) - (Radius * Radius));

                    return discriminant < 0f ? -1f : -b - Gpu.Sqrt(discriminant);
                }
            }

            public struct RandomStream(uint seed)
            {
                public float Next()                       // modifies the struct it is called on
                {
                    seed = ShaderMath.Hash(seed + 0x9E3779B9u);
                    return (seed >> 8) * (1f / 16777216f);
                }
            }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Depth(ReadWriteBuffer<float> depth, ReadOnlyBuffer<Sphere> spheres, int width) : IComputeShader
            {
                private float Aspect => (float)width / DispatchSize.Y;          // a computed property of the shader

                public void Execute()
                {
                    RandomStream random = new((uint)((ThreadIds.Y * width) + ThreadIds.X));
                    Float2 point = (ThreadIds.Normalized.XY * 2f) - 1f;
                    Ray ray = new(new Float3(0f, 1f, -5f), new Float3(point.X * Aspect, -point.Y, 2f + (random.Next() * 0.01f)));
                    float nearest = -1f;

                    for (int i = 0; i < spheres.Length; i++)
                    {
                        float distance = spheres[i].Intersect(ray);                // a method, called on a buffer element
                        nearest = ShaderMath.Pick(distance > 0f && (nearest < 0f || distance < nearest), distance, nearest);
                    }

                    depth[(ThreadIds.Y * width) + ThreadIds.X] = nearest < 0f ? 0f : ray.At(nearest).Z;
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Depth");


        // SunDirection is declared but not used by this shader, so it is not in its MSL.
        Assert.DoesNotContain("ShaderMath_SunDirection", descriptor.MslSource);
        Assert.Contains("static uint ShaderMath_Hash(uint x)", descriptor.MslSource);
        Assert.Contains("static float ShaderMath_Pick_float(bool condition, float whenTrue, float whenFalse)", descriptor.MslSource);
        Assert.Contains("static Ray Ray_new(float3 origin, float3 direction)", descriptor.MslSource);
        Assert.Contains("static float3 Ray_At(Ray __this, float distance)", descriptor.MslSource);
        Assert.Contains("static float Sphere_Intersect(Sphere __this, Ray ray)", descriptor.MslSource);
        Assert.Contains("static float RandomStream_Next(thread RandomStream& __this)", descriptor.MslSource);
        Assert.Contains("static float Aspect(constant __Constants& __c, uint3 __grid)", descriptor.MslSource);
        Assert.Contains("float distance = ::Sphere_Intersect(spheres[i], ray);", descriptor.KernelBody);

        // Dispatched over 16 x 16 with two spheres, on the GPU and on the CPU.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Type sphere = run.LoadType("Sphere");
        Array scene = Array.CreateInstance(sphere, 2);
        scene.SetValue(Make(sphere, new Float3(0f, 1f, 0f), 1f), 0);
        scene.SetValue(Make(sphere, new Float3(1.5f, 0.5f, 2f), 0.5f), 1);

        using IDisposable spheres = (IDisposable)typeof(CodeReuseTests)
            .GetMethod(nameof(AllocateReadOnly), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(sphere)
            .Invoke(null, [device, scene])!;

        using ReadWriteBuffer<float> gpu = device.AllocateReadWriteBuffer<float>(256);
        using ReadWriteBuffer<float> cpu = device.AllocateReadWriteBuffer<float>(256);
        Type shader = run.LoadType("Depth");

        foreach ((string method, ReadWriteBuffer<float> output) in new[] { (nameof(GraphicsDevice.For), gpu), (nameof(GraphicsDevice.ForCpu), cpu) })
        {
            typeof(GraphicsDevice).GetMethods()
                .Single(m => m.Name == method && m.GetParameters().Length == 3)
                .MakeGenericMethod(shader)
                .Invoke(device, [16, 16, Activator.CreateInstance(shader, output, spheres, 16)!]);
        }

        AssertClose(gpu.ToArray(), cpu.ToArray());

        // The middle of the picture is the big sphere, about one unit nearer than its center; the corners are empty.
        Assert.InRange(cpu.ToArray()[(8 * 16) + 8], -1.01f, -0.9f);
        Assert.Equal(0f, cpu.ToArray()[0]);

        static object Make(Type type, Float3 center, float radius)
        {
            object value = Activator.CreateInstance(type)!;
            type.GetField("Center")!.SetValue(value, center);
            type.GetField("Radius")!.SetValue(value, radius);
            return value;
        }
    }
}
