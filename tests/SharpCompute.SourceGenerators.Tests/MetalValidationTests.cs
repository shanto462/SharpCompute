namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// Proves that the real-Metal validation used throughout the suite (<see cref="ShaderDescriptor.AssertMslCompiles"/>)
/// has teeth. These tests are skipped, not passed, on machines without a Metal device.
/// </summary>
public class MetalValidationTests
{
    private const string ValidKernel = """
        #include <metal_stdlib>

        using namespace metal;

        kernel void Entry(device float* buffer [[buffer(0)]], uint3 __tid [[thread_position_in_grid]])
        {
            buffer[__tid.x] *= 2.0f;
        }
        """;

    [MetalFact]
    public void ValidMsl_CompilesWithoutMessages()
    {
        MetalCompiler.Result result = MetalCompiler.Compile(ValidKernel, "Entry");

        Assert.True(result.Success, result.Messages);
        Assert.Equal(string.Empty, result.Warnings);
    }

    [MetalFact]
    public void InvalidMsl_IsRejectedWithTheCompilerOutput()
    {
        MetalCompiler.Result result = MetalCompiler.Compile(ValidKernel.Replace("2.0f", "undefined_symbol"), "Entry");

        Assert.False(result.Success);
        Assert.Contains("undefined_symbol", result.Messages);
    }

    [MetalFact]
    public void MissingEntryPoint_IsRejected()
    {
        Assert.False(MetalCompiler.Compile(ValidKernel, "SomethingElse").Success);
    }

    [MetalFact]
    public void Warnings_AreSurfaced()
    {
        MetalCompiler.Result result = MetalCompiler.Compile(ValidKernel.Replace("buffer[__tid.x] *= 2.0f;", "float unused = 1.0f; buffer[__tid.x] *= 2.0f;"), "Entry");

        // Metal itself stays silent about warnings when the compile succeeds; the helper digs them out.
        Assert.True(result.Success);
        Assert.Contains("warning: unused variable 'unused'", result.Warnings);
    }

    [MetalFact]
    public void FloatRemainder_ReallyIsAnErrorInMsl()
    {
        // The reason `%` on floats must become metal::fmod: native `%` on floats is an MSL compile error.
        MetalCompiler.Result result = MetalCompiler.Compile(ValidKernel.Replace("*= 2.0f", "= buffer[__tid.x] % 2.0f"), "Entry");

        Assert.False(result.Success);
    }

    [MetalFact]
    public void DriftGuard_FailsCompilationWhenALayoutDisagrees()
    {
        GeneratorRun run = GeneratorHarness.Run(
            TestSources.Vectors,
            """
            using SharpCompute;
            using TestVectors;

            public struct Particle { public Vec3 Position; public float Mass; }

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Shader : IComputeShader
            {
                public readonly ReadWriteBuffer<Particle> particles;

                public void Execute() => particles[ThreadIds.X].Mass = 1;
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Shader");
        Assert.Contains("static_assert(sizeof(Particle) == 16,", descriptor.MslSource);

        // Simulate drift: an unpacked float3 is 16 bytes with 16-byte alignment, so the struct grows to 32.
        string drifted = descriptor.MslSource.Replace("packed_float3 Position;", "float3 Position;");
        MetalCompiler.Result result = MetalCompiler.Compile(drifted, descriptor.EntryPoint);

        Assert.False(result.Success);
        Assert.Contains("SharpCompute: size of 'Particle' does not match its C# layout", result.Messages);
        Assert.Contains("SharpCompute: offset of 'Particle.Mass' does not match its C# layout", result.Messages);
    }
}
