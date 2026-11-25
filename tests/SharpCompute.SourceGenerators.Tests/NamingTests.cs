namespace SharpCompute.SourceGenerators.Tests;

/// <summary>MSL/C++ keyword escaping and name-clash handling.</summary>
public class NamingTests
{
    [Theory]
    [InlineData("value", "value")]
    [InlineData("Position", "Position")]
    [InlineData("class", "__ms_class")]
    [InlineData("float", "__ms_float")]
    [InlineData("kernel", "__ms_kernel")]
    [InlineData("device", "__ms_device")]
    [InlineData("constant", "__ms_constant")]
    [InlineData("template", "__ms_template")]
    [InlineData("float3", "__ms_float3")]
    [InlineData("packed_float3", "__ms_packed_float3")]
    [InlineData("half", "__ms_half")]
    [InlineData("uint", "__ms_uint")]
    [InlineData("float4x4", "__ms_float4x4")]
    [InlineData("INFINITY", "__ms_INFINITY")]
    [InlineData("main", "__ms_main")]
    [InlineData("__c", "__ms___c")]
    [InlineData("__tid", "__ms___tid")]
    [InlineData("__ms_x", "__ms___ms_x")]
    [InlineData("_single", "_single")]
    [InlineData("größe", "gr_u00F6_u00DFe")]
    public void Escape(string identifier, string expected) => Assert.Equal(expected, MslNames.Escape(identifier));

    [Fact]
    public void Escape_IsInjective_ForTheGeneratorsOwnNames()
    {
        // Nothing a user can write maps onto a name the generator reserves for itself.
        foreach (string reserved in new[] { "__c", "__tid", "__grid", "__gtid", "__gid", "__Constants", "__local_x", "__length_x" })
        {
            Assert.NotEqual(reserved, MslNames.Escape(reserved));
            Assert.StartsWith("__ms_", MslNames.Escape(reserved));
        }
    }

    [Fact]
    public void KeywordsAreEscaped_InFieldsLocalsParametersHelpersAndStructs()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            TestSources.Shader("""
                    public readonly ReadWriteBuffer<float> @float;
                    public readonly ReadOnlyBuffer<Template> kernel;
                    public readonly float @class;
                    public readonly float device;
                    public readonly float __c;
                    public readonly Template @template;

                    private static float @struct(float @namespace, float constant) => @namespace * constant;

                    private static float half(float @uint) => ThreadIds.X * @uint;

                    public void Execute()
                    {
                        float @int = @class;
                        float thread = device + __c;
                        Template @typedef = kernel[0];
                        for (int @register = 0; @register < 2; @register++)
                        {
                            @int += @register;
                        }
                        @float[ThreadIds.X] = @struct(@int, thread) + @typedef.auto + half(2f) + @template.@using + kernel.Length;
                    }
                """) + """

            public struct Template { public float auto; public float @using; }
            """);

        ShaderDescriptor descriptor = run.Verify("Tests.TestShader");

        Assert.Equal(
            """
            float __ms_int = __c.__ms_class;
            float __ms_thread = __c.__ms_device + __c.__ms___c;
            Template __ms_typedef = __ms_kernel[0];
            for (int __ms_register = 0; __ms_register < 2; __ms_register = int(uint(__ms_register) + 1u))
            {
                __ms_int += float(__ms_register);
            }
            __ms_float[int(__tid.x)] = ::__ms_struct(__ms_int, __ms_thread) + __ms_typedef.__ms_auto + ::__ms_half(2.0f, __tid) + __c.__ms_template.__ms_using + float(int(__c.__length_kernel));
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);

        Assert.Contains("struct Template\n{\n    float __ms_auto;\n    float __ms_using;\n};", descriptor.MslSource);
        Assert.Contains("struct __Constants\n{\n    float __ms_class;\n    float __ms_device;\n    float __ms___c;\n    Template __ms_template;\n    uint __length_kernel;\n};", descriptor.MslSource);
        Assert.Contains("static float __ms_struct(float __ms_namespace, float __ms_constant);", descriptor.MslSource);
        Assert.Contains("static float __ms_half(float __ms_uint, uint3 __tid);", descriptor.MslSource);
        Assert.Contains("device float* __ms_float [[buffer(0)]],", descriptor.MslSource);
        Assert.Contains("const device Template* __ms_kernel [[buffer(1)]],", descriptor.MslSource);

        // The C# side keeps using the real member names (verbatim where C# needs it).
        string code = run.Source("TestShader.SharpCompute.g.cs");
        Assert.Contains("writer.SetBuffer(0, shader.@float ?? throw new global::System.ArgumentNullException(\"TestShader.float\",", code);
        Assert.Contains("writer.SetBuffer(1, shader.kernel ?? throw new global::System.ArgumentNullException(\"TestShader.kernel\",", code);
        Assert.Contains("__constants.__ms_class = shader.@class;", code);
        Assert.Contains("__constants.__ms___c = shader.__c;", code);
        Assert.Contains("__constants.__length_kernel = (uint)shader.kernel.Length;", code);

        descriptor.AssertLayoutMatchesClr("__Constants", descriptor.ConstantsType!);
    }

    [Fact]
    public void HelpersNamedLikeMetalFunctions_AreCalledThroughTheGlobalNamespace()
    {
        // `using namespace metal;` makes an unqualified `clamp(...)` ambiguous; `::clamp(...)` is not.
        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(
            "buffer[0] = clamp(buffer[0], 0, 1) + sqrt(4) + MathF.Sqrt(2) + popcount(3) + dot(1, 2);",
            """
            private static float clamp(float x, float lo, float hi) => x < lo ? lo : x > hi ? hi : x;

            private static float sqrt(float x) => x * 0.5f;

            private static int popcount(int x) => x + 1;

            private static float dot(float a, float b) => a * b;
            """)).Verify("Tests.TestShader");

        Assert.Equal(
            "buffer[0] = ::clamp(buffer[0], 0.0f, 1.0f) + ::sqrt(4.0f) + metal::sqrt(2.0f) + float(::popcount(3)) + ::dot(1.0f, 2.0f);",
            descriptor.KernelBody);
    }

    [Fact]
    public void LocalsThatShadowBuffersOrHelpers_AreRenamedOrHarmless()
    {
        // In C#, a local may share its name with a field (accessed through `this.`) or a method.
        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(
            """
            float buffer = this.buffer[0];
            float factor = this.factor * 2;
            float Twice = TestShader.Twice(buffer);
            this.buffer[1] = buffer + factor + Twice + TestShader.Twice(1);
            """,
            "private static float Twice(float x) => x * 2;")).Verify("Tests.TestShader");

        Assert.Equal(
            """
            float __local_buffer = buffer[0];
            float factor = __c.factor * 2.0f;
            float Twice = ::Twice(__local_buffer);
            buffer[1] = __local_buffer + factor + Twice + ::Twice(1.0f);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
    }

    [Fact]
    public void LocalsThatShadowAStructType_AreRenamed()
    {
        ShaderDescriptor descriptor = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body(
            """
            Pair Pair = default;
            Pair.Left = factor;
            Pair other = Pair;
            buffer[0] = Total(other);
            """,
            "private static float Total(Pair Pair) => Pair.Left + Pair.Right;") + "public struct Pair { public float Left; public float Right; }").Verify("Tests.TestShader");

        Assert.Equal(
            """
            Pair __local_Pair = Pair{};
            __local_Pair.Left = __c.factor;
            Pair other = __local_Pair;
            buffer[0] = ::Total(other);
            """.ReplaceLineEndings("\n"),
            descriptor.KernelBody);
        Assert.Contains("static float Total(Pair __local_Pair)", descriptor.MslSource);
    }
}
