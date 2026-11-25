using System.Linq;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>How the shader struct is declared: namespaces, nesting, partial parts, captures, helpers, naming.</summary>
public class DeclarationTests
{
    private const string SimpleMembers = """
            public readonly ReadWriteBuffer<float> buffer;

            public void Execute() => buffer[ThreadIds.X] = 1;
        """;

    [Fact]
    public void GlobalNamespace()
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Fill : IComputeShader
            {
            {{SimpleMembers}}
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Fill");

        Assert.Equal("Fill", descriptor.EntryPoint);
        Assert.Equal("Fill.SharpCompute.g.cs", Assert.Single(run.Sources).HintName);
        Assert.DoesNotContain("\nnamespace ", run.Source("Fill.SharpCompute.g.cs"));
    }

    [Fact]
    public void BlockScopedAndNestedNamespaces()
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            using SharpCompute;

            namespace Company.Product
            {
                namespace Shaders
                {
                    [GeneratedComputeShaderDescriptor]
                    internal readonly partial struct Fill : IComputeShader
                    {
            {{SimpleMembers}}
                    }
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Company.Product.Shaders.Fill");

        Assert.Equal("Company_Product_Shaders_Fill", descriptor.EntryPoint);
        Assert.Equal("Company.Product.Shaders.Fill.SharpCompute.g.cs", Assert.Single(run.Sources).HintName);
        Assert.Contains("namespace Company.Product.Shaders", run.Source("Fill.SharpCompute.g.cs"));
    }

    [Fact]
    public void FileScopedNamespace()
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            using SharpCompute;

            namespace Company.Shaders;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Fill : IComputeShader
            {
            {{SimpleMembers}}
            }
            """);

        Assert.Equal("Company_Shaders_Fill", run.Verify("Company.Shaders.Fill").EntryPoint);
    }

    [Fact]
    public void NestedInClassesStructsRecordsAndInterfaces()
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            using SharpCompute;

            namespace App;

            public static partial class Shaders
            {
                internal partial struct Group
                {
                    private partial record Inner
                    {
                        public partial interface IDeep
                        {
                            [ThreadGroupSize(4, 4, 4)]
                            [GeneratedComputeShaderDescriptor]
                            public readonly partial struct Fill : IComputeShader
                            {
            {{SimpleMembers}}
                            }
                        }
                    }
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("App.Shaders+Group+Inner+IDeep+Fill");

        Assert.Equal("App_Shaders_Group_Inner_IDeep_Fill", descriptor.EntryPoint);
        Assert.Equal((4, 4, 4), (descriptor.ThreadGroupSizeX, descriptor.ThreadGroupSizeY, descriptor.ThreadGroupSizeZ));
        Assert.Equal("App.Shaders.Group.Inner.IDeep.Fill.SharpCompute.g.cs", Assert.Single(run.Sources).HintName);

        string code = run.Source("Fill.SharpCompute.g.cs");
        Assert.Contains("partial class Shaders", code);
        Assert.Contains("partial struct Group", code);
        Assert.Contains("partial record Inner", code);
        Assert.Contains("partial interface IDeep", code);
        Assert.Contains("partial struct Fill : global::SharpCompute.IComputeShaderDescriptor<global::App.Shaders.Group.Inner.IDeep.Fill>", code);
    }

    [Fact]
    public void MultipleShaders_GetOneFileEach()
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            using SharpCompute;

            namespace A { [GeneratedComputeShaderDescriptor] public readonly partial struct Fill : IComputeShader { {{SimpleMembers}} } }
            namespace B { [GeneratedComputeShaderDescriptor] public readonly partial struct Fill : IComputeShader { {{SimpleMembers}} } }
            """);

        run.AssertClean();
        Assert.Equal(new[] { "A.Fill.SharpCompute.g.cs", "B.Fill.SharpCompute.g.cs" }, run.Sources.Select(static s => s.HintName).OrderBy(static n => n));
        Assert.Equal("A_Fill", run.Descriptor("A.Fill").AssertMslCompiles().EntryPoint);
        Assert.Equal("B_Fill", run.Descriptor("B.Fill").AssertMslCompiles().EntryPoint);
    }

    [Fact]
    public void EntryPoint_StaysUniqueWhenNamesContainUnderscores()
    {
        // `A.B_C` and `A_B.C` would both flatten to `A_B_C`; a hash of the exact name disambiguates.
        GeneratorRun run = GeneratorHarness.Run($$"""
            using SharpCompute;

            namespace A { [GeneratedComputeShaderDescriptor] public readonly partial struct B_C : IComputeShader { {{SimpleMembers}} } }
            namespace A_B { [GeneratedComputeShaderDescriptor] public readonly partial struct C : IComputeShader { {{SimpleMembers}} } }
            """);

        run.AssertClean();
        string first = run.Descriptor("A.B_C").AssertMslCompiles().EntryPoint;
        string second = run.Descriptor("A_B.C").AssertMslCompiles().EntryPoint;

        Assert.StartsWith("A_B_C_", first);
        Assert.StartsWith("A_B_C_", second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void PrimaryConstructorParameters_AreCapturedThroughAnInstanceMethod()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            namespace App;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Saxpy(ReadWriteBuffer<float> y, ReadOnlyBuffer<float> x, float a) : IComputeShader
            {
                public readonly float bias;

                public void Execute()
                {
                    int i = ThreadIds.X;

                    if (i < x.Length)
                    {
                        y[i] = a * x[i] + y[i] + bias;
                    }
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("App.Saxpy");

        // Parameters bind first (they are declared first), then fields.
        Assert.Contains(
            """
            kernel void App_Saxpy(
                device float* y [[buffer(0)]],
                const device float* x [[buffer(1)]],
                constant __Constants& __c [[buffer(2)]],
                constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
                uint3 __tid_in [[thread_position_in_grid]])
            {
                const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
                int i = int(__tid.x);
                if (i < int(__c.__length_x))
                {
                    y[i] = __c.a * x[i] + y[i] + __c.bias;
                }
            }
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);

        Assert.Contains(
            """
                    static void global::SharpCompute.IComputeShaderDescriptor<global::App.Saxpy>.SetArguments(in global::App.Saxpy shader, ref global::SharpCompute.ShaderArgumentWriter writer)
                    {
                        shader.__SharpCompute_SetArguments(ref writer);
                    }

                    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
                    private readonly void __SharpCompute_SetArguments(ref global::SharpCompute.ShaderArgumentWriter __writer)
                    {
                        __writer.SetBuffer(0, y ?? throw new global::System.ArgumentNullException("Saxpy.y", "The buffer 'Saxpy.y' is null. Create the shader with its constructor (not 'default') and pass a buffer."));
                        __writer.SetBuffer(1, x ?? throw new global::System.ArgumentNullException("Saxpy.x", "The buffer 'Saxpy.x' is null. Create the shader with its constructor (not 'default') and pass a buffer."));
                        __Constants __constants = default;
                        __constants.a = a;
                        __constants.bias = this.bias;
                        __constants.__length_x = (uint)x.Length;
                        __writer.SetBytes(2, in __constants);
                    }
            """.ReplaceLineEndings("\n"),
            run.Source("Saxpy.SharpCompute.g.cs"));

        descriptor.AssertLayoutMatchesClr("__Constants", descriptor.ConstantsType!);
    }

    [Fact]
    public void PartialStructAcrossFiles_FindsHelpersInOtherParts()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            namespace App;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Split : IComputeShader
            {
                public readonly ReadWriteBuffer<float> buffer;
                public readonly float factor;

                public void Execute() => buffer[ThreadIds.X] = Scale(buffer[ThreadIds.X]) * factor;
            }
            """,
            """
            namespace App;

            public readonly partial struct Split
            {
                private static float Scale(float value) => value * 2;
            }
            """);

        ShaderDescriptor descriptor = run.Verify("App.Split");

        Assert.Equal("buffer[int(__tid.x)] = ::Scale(buffer[int(__tid.x)]) * __c.factor;", descriptor.KernelBody);
        Assert.Equal("return value * 2.0f;", descriptor.FunctionBody("static float Scale("));
    }

    [Fact]
    public void NoCaptures_OmitsTheConstantsBlock()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Empty : IComputeShader
            {
                public void Execute()
                {
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Empty");

        Assert.Equal(
            """
            #include <metal_stdlib>

            using namespace metal;

            kernel void Empty()
            {
            }
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource.TrimEnd('\n'));
        Assert.Null(descriptor.ConstantsType);
        Assert.DoesNotContain("SetBytes", run.Source("Empty.SharpCompute.g.cs"));
        Assert.DoesNotContain("SetBuffer", run.Source("Empty.SharpCompute.g.cs"));
    }

    [Fact]
    public void BuffersOnly_BindsNoConstants()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Copy : IComputeShader
            {
                public readonly ReadOnlyBuffer<Float4> source;
                public readonly ReadWriteBuffer<Float4> destination;

                public void Execute() => destination[ThreadIds.X] = source[ThreadIds.X];
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Copy");

        Assert.Contains("const device packed_float4* source [[buffer(0)]],", descriptor.MslSource);
        Assert.Contains("device packed_float4* destination [[buffer(1)]],", descriptor.MslSource);
        Assert.Contains("static_assert(sizeof(packed_float4) == 16,", descriptor.MslSource);
        Assert.DoesNotContain("__Constants", descriptor.MslSource);
        Assert.DoesNotContain("__Constants", run.Source("Copy.SharpCompute.g.cs"));
    }

    [Fact]
    public void ThreadGroupSize_DefaultsForOmittedDimensions()
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            using SharpCompute;

            [ThreadGroupSize(32)]
            [GeneratedComputeShaderDescriptor]
            public readonly partial struct OneD : IComputeShader { {{SimpleMembers}} }

            [ThreadGroupSize(32, 32)]
            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Largest : IComputeShader { {{SimpleMembers}} }

            [ThreadGroupSize(z: 2, x: 8)]
            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Named : IComputeShader { {{SimpleMembers}} }
            """);

        run.AssertClean();

        ShaderDescriptor oneD = run.Descriptor("OneD");
        Assert.Equal((32, 1, 1), (oneD.ThreadGroupSizeX, oneD.ThreadGroupSizeY, oneD.ThreadGroupSizeZ));

        // 32 * 32 * 1 = 1024 is exactly the limit.
        ShaderDescriptor largest = run.Descriptor("Largest");
        Assert.Equal((32, 32, 1), (largest.ThreadGroupSizeX, largest.ThreadGroupSizeY, largest.ThreadGroupSizeZ));

        ShaderDescriptor named = run.Descriptor("Named");
        Assert.Equal((8, 1, 2), (named.ThreadGroupSizeX, named.ThreadGroupSizeY, named.ThreadGroupSizeZ));
    }

    [Fact]
    public void Helpers_AreForwardDeclared_AndReceiveTheBuiltinsTheyUse()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(
            "buffer[Index(1)] = Weight(buffer, Index(0), factor) + Offset();",
            """
            private static int Index(int offset) => Flat() + offset;

            private static int Flat() => ThreadIds.Y * DispatchSize.X + ThreadIds.X;

            private static float Weight(ReadWriteBuffer<float> values, int index, float scale = 2)
            {
                float sum = 0;

                for (int i = 0; i < 3; i++)
                {
                    sum += values[index + i] * scale;
                }

                return sum;
            }

            private static float Offset() => 0.5f;

            private static float Unused(float x) => x;
            """)).Verify("Tests.TestShader");

        Assert.Contains(
            """
            static int Flat(uint3 __tid, uint3 __grid);
            static int Index(int offset, uint3 __tid, uint3 __grid);
            static float Weight(device float* values, int index, float scale);
            static float Offset();

            static int Flat(uint3 __tid, uint3 __grid)
            {
                return int(__tid.y * __grid.x + __tid.x);
            }

            static int Index(int offset, uint3 __tid, uint3 __grid)
            {
                return int(uint(::Flat(__tid, __grid)) + uint(offset));
            }

            static float Weight(device float* values, int index, float scale)
            {
                float sum = 0.0f;
                for (int i = 0; i < 3; i = int(uint(i) + 1u))
                {
                    sum += values[int(uint(index) + uint(i))] * scale;
                }
                return sum;
            }

            static float Offset()
            {
                return 0.5f;
            }
            """.ReplaceLineEndings("\n"),
            descriptor.MslSource);

        Assert.Equal("buffer[::Index(1, __tid, __grid)] = ::Weight(buffer, ::Index(0, __tid, __grid), __c.factor) + ::Offset();", descriptor.KernelBody);

        // Helpers that are never called are not emitted.
        Assert.DoesNotContain("Unused", descriptor.MslSource);
    }

    [Fact]
    public void Helpers_SupportOverloadsNamedAndOptionalArguments_AndStructParameters()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(
            """
            Pair pair = Make(right: 2, left: 1);
            buffer[0] = Sum(pair) + Sum(1f, 2f) + Sum(new Vec3(1, 2, 3)) + Scale(3);
            """,
            """
            private static Pair Make(float left, float right)
            {
                Pair result = default;
                result.Left = left;
                result.Right = right;
                return result;
            }

            private static float Sum(Pair pair) => pair.Left + pair.Right;

            private static float Sum(float a, float b) => a + b;

            private static float Sum(Vec3 v) => v.X + v.Y + v.Z;

            private static float Scale(float value, float by = 10, bool negate = false) => negate ? -value * by : value * by;
            """) + "public struct Pair { public float Left; public float Right; }").Verify("Tests.TestShader");

        Assert.Equal(
            """
            Pair pair = ::Make(1.0f, 2.0f);
            buffer[0] = ::Sum(pair) + ::Sum_1(1.0f, 2.0f) + ::Sum_2(float3(1.0f, 2.0f, 3.0f)) + ::Scale(3.0f, 10.0f, false);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
        Assert.Contains("static Pair Make(float left, float right);", descriptor.MslSource);
        Assert.Contains("static float Sum(Pair pair);", descriptor.MslSource);
        Assert.Contains("static float Sum_1(float a, float b);", descriptor.MslSource);
        Assert.Contains("static float Sum_2(float3 v);", descriptor.MslSource);
        Assert.Contains("static float Scale(float value, float by, bool negate);", descriptor.MslSource);
    }

    [Fact]
    public void Helpers_TakeInParametersByValue()
    {
        // `in` is a read-only reference in C#; in MSL the argument is simply copied.
        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(
            """
            Pair pair = default;
            pair.Left = factor;
            Vec3 v = new Vec3(1, 2, 3);
            buffer[0] = Total(pair) + Total(in pair) + Scaled(in v, 2) + Scaled(new Vec3(4, 5, 6), factor) + First(buffer[1]);
            """,
            """
            private static float Total(in Pair pair) => pair.Left + pair.Right;

            private static float Scaled(in Vec3 value, in float scale) => (value.X + value.Y + value.Z) * scale;

            private static float First(in float element) => element;
            """) + "public struct Pair { public float Left; public float Right; }").Verify("Tests.TestShader");

        Assert.Contains("static float Total(Pair pair);", descriptor.MslSource);
        Assert.Contains("static float Scaled(float3 value, float scale);", descriptor.MslSource);
        Assert.Contains("static float First(float element);", descriptor.MslSource);
        Assert.Contains(
            "buffer[0] = ::Total(pair) + ::Total(pair) + ::Scaled(v, 2.0f) + ::Scaled(float3(4.0f, 5.0f, 6.0f), __c.factor) + ::First(buffer[1]);",
            descriptor.KernelBody);
    }

    [Fact]
    public void UserStructs_FromOtherNamespacesWithTheSameName_GetDistinctMslNames()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(
            """
            using SharpCompute;

            namespace First { public struct Item { public float Value; } }
            namespace Second { public struct Item { public int Count; public First.Item Nested; } }

            namespace App
            {
                [GeneratedComputeShaderDescriptor]
                public readonly partial struct Shader : IComputeShader
                {
                    public readonly ReadWriteBuffer<First.Item> first;
                    public readonly ReadOnlyBuffer<Second.Item> second;

                    public void Execute() => first[ThreadIds.X].Value = second[ThreadIds.X].Count + second[ThreadIds.X].Nested.Value;
                }
            }
            """).Verify("App.Shader");

        Assert.Contains("struct Item\n{\n    float Value;\n};", descriptor.MslSource);
        Assert.Contains("struct Item_1\n{\n    int Count;\n    Item Nested;\n};", descriptor.MslSource);
        Assert.Contains("device Item* first [[buffer(0)]],", descriptor.MslSource);
        Assert.Contains("const device Item_1* second [[buffer(1)]],", descriptor.MslSource);
        Assert.Equal("first[int(__tid.x)].Value = float(second[int(__tid.x)].Count) + second[int(__tid.x)].Nested.Value;", descriptor.KernelBody);
    }

    [Fact]
    public void UserStructs_DefinedInAnotherAssembly_AreLaidOutFromMetadata()
    {
        // `Float3` and friends come from SharpCompute.dll, so their [StructLayout] is read from metadata.
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            public struct Ray { public Float3 Origin; public float TMin; public Float3 Direction; public float TMax; }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Trace : IComputeShader
            {
                public readonly ReadWriteBuffer<Ray> rays;
                public readonly Int3 grid;
                public readonly UInt2 flags;

                public void Execute() => rays[ThreadIds.X].TMax = rays[ThreadIds.X].Origin.X + grid.Z + flags.Y;
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Trace");

        Assert.Contains("struct Ray\n{\n    packed_float3 Origin;\n    float TMin;\n    packed_float3 Direction;\n    float TMax;\n};", descriptor.MslSource);
        Assert.Contains("static_assert(sizeof(Ray) == 32,", descriptor.MslSource);
        Assert.Contains("struct __Constants\n{\n    packed_int3 grid;\n    packed_uint2 flags;\n};", descriptor.MslSource);
        descriptor.AssertLayoutMatchesClr("Ray", run.LoadType("Ray"));
        descriptor.AssertLayoutMatchesClr("__Constants", descriptor.ConstantsType!);
    }

    [Fact]
    public void NotReadonly_OnlyWarns_AndStillGenerates()
    {
        GeneratorRun run = GeneratorHarness.Run($$"""
            using SharpCompute;

            [GeneratedComputeShaderDescriptor]
            public partial struct Fill : IComputeShader
            {
            {{SimpleMembers}}
            }
            """);

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0004", "Fill");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        run.AssertCompiles().Descriptor("Fill").AssertMslCompiles();
    }
}
