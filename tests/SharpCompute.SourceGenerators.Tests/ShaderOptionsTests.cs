namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// <c>[ShaderOptions]</c>: the generator passes the options on through the descriptor's
/// <c>CompileOptions</c> and changes nothing else. The MSL is the same with and without them; the
/// runtime applies them when it compiles the kernel.
/// </summary>
public class ShaderOptionsTests
{
    private const string Shader = "Tests.TestShader";
    private const string Member = "static global::SharpCompute.ShaderCompileOptions global::SharpCompute.IComputeShaderDescriptor<global::Tests.TestShader>.CompileOptions => ";
    private const string Options = "global::SharpCompute.ShaderCompileOptions.";

    private const string Members = """
            public readonly ReadWriteBuffer<float> buffer;
            public readonly float factor;

            public void Execute()
            {
                buffer[ThreadIds.X] = (buffer[ThreadIds.X] * factor) + 1f;
            }
        """;

    private static GeneratorRun Run(string attributes) => GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(Members, attributes));

    [Theory]
    [InlineData("[ShaderOptions(FastMath = true)]", ShaderCompileOptions.FastMath, Options + "FastMath;")]
    [InlineData("[ShaderOptions(FpContract = false)]", ShaderCompileOptions.NoFpContract, Options + "NoFpContract;")]
    [InlineData(
        "[ShaderOptions(FpContract = false, FastMath = true)]",
        ShaderCompileOptions.FastMath | ShaderCompileOptions.NoFpContract,
        Options + "FastMath | " + Options + "NoFpContract;")]
    [InlineData("[ShaderOptions(FastMath = true, FpContract = true)]", ShaderCompileOptions.FastMath, Options + "FastMath;")]
    [InlineData("[ShaderOptionsAttribute(FastMath = true)]", ShaderCompileOptions.FastMath, Options + "FastMath;")]
    [InlineData("[SharpCompute.ShaderOptions(FastMath = true), ThreadGroupSize(64)]", ShaderCompileOptions.FastMath, Options + "FastMath;")]
    public void TheDescriptor_ReportsTheOptions(string attributes, ShaderCompileOptions expected, string emitted)
    {
        GeneratorRun run = Run(attributes);

        Assert.Equal(expected, run.Verify(Shader).CompileOptions);
        Assert.Contains(Member + emitted, run.Source("TestShader.SharpCompute.g.cs"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[ShaderOptions]")]
    [InlineData("[ShaderOptions()]")]
    [InlineData("[ShaderOptions(FastMath = false)]")]
    [InlineData("[ShaderOptions(FpContract = true)]")]
    [InlineData("[ShaderOptions(FastMath = false, FpContract = true)]")]
    public void DefaultOptions_EmitNoMember(string attributes)
    {
        // CompileOptions is a static virtual with a default of None, so it is only emitted when an option is set.
        GeneratorRun run = Run(attributes);

        Assert.Equal(ShaderCompileOptions.None, run.Verify(Shader).CompileOptions);

        // (The shader's record names the options type too, with the value 0: see ShaderRecordTests.)
        Assert.DoesNotContain(">.CompileOptions", run.Source("TestShader.SharpCompute.g.cs"));
    }

    [Fact]
    public void ConstantExpressions_AreEvaluated()
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(
            Members + "\n    public const bool Fast = true;\n",
            "[ShaderOptions(FastMath = TestShader.Fast, FpContract = !TestShader.Fast)]"));

        Assert.Equal(ShaderCompileOptions.FastMath | ShaderCompileOptions.NoFpContract, run.Verify(Shader).CompileOptions);
    }

    [Fact]
    public void Options_OnAShaderWithThreadgroupMemory_EmitBothSetsOfMembers()
    {
        // The options and the threadgroup-memory facts are independent descriptor members; a shader
        // that has both reports both, and the options still leave its MSL alone.
        const string GroupShared = """
                [GroupShared]
                private static readonly float[] tile = new float[64];

                public readonly ReadWriteBuffer<float> buffer;

                public void Execute()
                {
                    tile[GroupIds.X] = (buffer[ThreadIds.X] * 2f) + 1f;
                    Gpu.GroupMemoryBarrierWithGroupSync();
                    buffer[ThreadIds.X] = tile[63 - GroupIds.X];
                }
            """;

        GeneratorRun with = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(GroupShared, "[ThreadGroupSize(64), ShaderOptions(FastMath = true, FpContract = false)]"));
        GeneratorRun without = GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader(GroupShared, "[ThreadGroupSize(64)]"));
        ShaderDescriptor descriptor = with.Verify(Shader);

        Assert.Equal(ShaderCompileOptions.FastMath | ShaderCompileOptions.NoFpContract, descriptor.CompileOptions);
        Assert.True(descriptor.UsesThreadgroupMemory);
        Assert.Equal(64 * sizeof(float), descriptor.ThreadgroupMemoryLength);
        Assert.Contains(Member + Options + "FastMath | " + Options + "NoFpContract;", with.Source("TestShader.SharpCompute.g.cs"));

        ShaderDescriptor plain = without.Verify(Shader);
        Assert.Equal(ShaderCompileOptions.None, plain.CompileOptions);
        Assert.True(plain.UsesThreadgroupMemory);
        Assert.Equal(plain.MslSource, descriptor.MslSource);
    }

    [Fact]
    public void TheOptions_DoNotChangeTheMsl()
    {
        string plain = Run(string.Empty).Verify(Shader).MslSource;
        string withOptions = Run("[ShaderOptions(FastMath = true, FpContract = false)]").Verify(Shader).MslSource;

        // No pragma, no fast-math marker: the runtime adds the pragma at compile time.
        Assert.Equal(plain, withOptions);
        Assert.DoesNotContain("pragma", withOptions);
    }
}
