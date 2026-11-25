using System;
using System.Linq;
using System.Reflection;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// The record the generator puts on every shader it translates
/// (<see cref="GeneratedShaderRecordAttribute"/>), which is how the build step that precompiles an
/// assembly's shaders finds them in its metadata: the entry point and the compile options, on the
/// shader struct, next to the descriptor that returns the MSL.
/// </summary>
public class ShaderRecordTests
{
    private const string Shader = "Tests.TestShader";

    private const string Members = """
            public readonly ReadWriteBuffer<float> buffer;
            public readonly float factor;

            public void Execute()
            {
                buffer[ThreadIds.X] = (buffer[ThreadIds.X] * factor) + 1f;
            }
        """;

    private static GeneratorRun Run(string attributes = "") => GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(Members, attributes));

    [Fact]
    public void TheRecord_IsEmittedOnTheShaderStruct_AheadOfItsDeclaration()
    {
        GeneratorRun run = Run();
        run.Verify(Shader);

        // Pinned to the character: this line is the contract with SharpCompute.Build, which reads it
        // back from the compiled assembly as a string and an int.
        Assert.Contains(
            """
                [global::SharpCompute.GeneratedShaderRecord("Tests_TestShader", (global::SharpCompute.ShaderCompileOptions)0)]
                partial struct TestShader : global::SharpCompute.IComputeShaderDescriptor<global::Tests.TestShader>
            """.ReplaceLineEndings("\n"),
            run.Source("TestShader.SharpCompute.g.cs").ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("[ShaderOptions]", 0)]
    [InlineData("[ShaderOptions(FastMath = true)]", 1)]
    [InlineData("[ShaderOptions(FpContract = false)]", 2)]
    [InlineData("[ShaderOptions(FastMath = true, FpContract = false)]", 3)]
    public void TheRecord_CarriesTheCompileOptions_AsTheDescriptorReportsThem(string attributes, int options)
    {
        GeneratorRun run = Run(attributes);
        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Contains(
            $"[global::SharpCompute.GeneratedShaderRecord(\"Tests_TestShader\", (global::SharpCompute.ShaderCompileOptions){options})]",
            run.Source("TestShader.SharpCompute.g.cs"));

        GeneratedShaderRecordAttribute record = Record(descriptor.Type);

        Assert.Equal(descriptor.EntryPoint, record.EntryPoint);
        Assert.Equal(descriptor.CompileOptions, record.CompileOptions);
        Assert.Equal((ShaderCompileOptions)options, record.CompileOptions);
    }

    [Fact]
    public void TheOptionsEnum_IsStoredAsAnInt()
    {
        // The build tool decodes the attribute's second argument as four bytes without resolving the
        // enum type, which is right for as long as the enum is declared over int.
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(ShaderCompileOptions)));

        ConstructorInfo constructor = Assert.Single(typeof(GeneratedShaderRecordAttribute).GetConstructors());
        Assert.Equal([typeof(string), typeof(ShaderCompileOptions)], constructor.GetParameters().Select(static parameter => parameter.ParameterType));
    }

    [Fact]
    public void TheRecord_IsOnTheShader_NotOnTheTypesThatContainIt()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            namespace App;

            public static partial class Shaders
            {
                internal partial struct Group
                {
                    [ShaderOptions(FastMath = true)]
                    [GeneratedComputeShaderDescriptor]
                    private readonly partial struct Fill : IComputeShader
                    {
                        public readonly ReadWriteBuffer<float> buffer;

                        public void Execute() => buffer[ThreadIds.X] = 1;
                    }
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("App.Shaders+Group+Fill");
        string code = run.Source("Fill.SharpCompute.g.cs");

        // Exactly one, directly above the shader: a private nested shader is recorded like any other.
        Assert.Equal(1, code.Split("GeneratedShaderRecord").Length - 1);
        Assert.Matches(@"\[global::SharpCompute\.GeneratedShaderRecord\(""App_Shaders_Group_Fill"", \(global::SharpCompute\.ShaderCompileOptions\)1\)\]\s+partial struct Fill ", code);

        Assert.Equal("App_Shaders_Group_Fill", Record(descriptor.Type).EntryPoint);
        Assert.Empty(descriptor.Type.DeclaringType!.GetCustomAttributes(typeof(GeneratedShaderRecordAttribute), inherit: false));
    }

    [Fact]
    public void TheRecord_CarriesTheEntryPointAsItIsInTheMsl_EscapedAndHashed()
    {
        // An underscore in a name makes the generator append a hash to the kernel's name, and a C++
        // keyword is escaped: the record has to carry the result, the name the MSL really uses.
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            namespace My_App;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct @class : IComputeShader
            {
                public readonly ReadWriteBuffer<float> buffer;

                public void Execute() => buffer[ThreadIds.X] = 1;
            }
            """);

        ShaderDescriptor descriptor = run.Verify("My_App.class");

        Assert.NotEqual("My_App_class", descriptor.EntryPoint);
        Assert.Contains("kernel void " + descriptor.EntryPoint + "(", descriptor.MslSource);
        Assert.Equal(descriptor.EntryPoint, Record(descriptor.Type).EntryPoint);
    }

    [Fact]
    public void AShaderWithErrors_GetsNoRecord()
    {
        // Its descriptor is a stub with no MSL, so there is nothing to precompile.
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Body("decimal m = 1.5m;"));

        Assert.NotEmpty(run.Diagnostics);
        string code = Assert.Single(run.Sources).SourceText.ToString();

        Assert.Contains("This shader has SharpCompute errors", code);
        Assert.DoesNotContain("GeneratedShaderRecord", code);
    }

    [Fact]
    public void EveryShader_GetsItsOwnRecord()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            namespace App;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct First : IComputeShader
            {
                public readonly ReadWriteBuffer<float> buffer;

                public void Execute() => buffer[ThreadIds.X] = 1;
            }

            [ShaderOptions(FpContract = false)]
            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Second : IComputeShader
            {
                public readonly ReadWriteBuffer<float> buffer;

                public void Execute() => buffer[ThreadIds.X] = 2;
            }
            """);

        run.AssertClean();

        Assert.Equal(("App_First", ShaderCompileOptions.None), Entry(Record(run.LoadType("App.First"))));
        Assert.Equal(("App_Second", ShaderCompileOptions.NoFpContract), Entry(Record(run.LoadType("App.Second"))));

        static (string, ShaderCompileOptions) Entry(GeneratedShaderRecordAttribute record) => (record.EntryPoint, record.CompileOptions);
    }

    private static GeneratedShaderRecordAttribute Record(Type shader) =>
        Assert.Single(shader.GetCustomAttributes(typeof(GeneratedShaderRecordAttribute), inherit: false).Cast<GeneratedShaderRecordAttribute>());
}
