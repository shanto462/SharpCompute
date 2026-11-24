using System;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>What the generated <c>SetArguments</c> does with a shader whose buffers are null.</summary>
public class NullBufferTests
{
    [MetalFact]
    public void DispatchingDefault_NamesTheShaderAndTheField()
    {
        GeneratorRun run = GeneratorHarness.Run("""
            using SharpCompute;

            namespace MyApp;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct MultiplyByTwo : IComputeShader
            {
                public readonly ReadWriteBuffer<float> buffer;
                public readonly float factor;

                public MultiplyByTwo(ReadWriteBuffer<float> buffer, float factor) { this.buffer = buffer; this.factor = factor; }

                public void Execute() => buffer[ThreadIds.X] *= factor;
            }

            public static partial class Outer
            {
                [GeneratedComputeShaderDescriptor]
                public readonly partial struct Copy(ReadOnlyBuffer<float> source, ReadWriteBuffer<float> destination) : IComputeShader
                {
                    public void Execute() => destination[ThreadIds.X] = source[ThreadIds.X] * source.Length;
                }
            }
            """);

        var field = Assert.IsType<ArgumentNullException>(GpuDispatch.ForDefault(run.AssertClean(), "MyApp.MultiplyByTwo"));

        Assert.Equal("MultiplyByTwo.buffer", field.ParamName);
        Assert.StartsWith("The buffer 'MultiplyByTwo.buffer' is null. Create the shader with its constructor (not 'default') and pass a buffer.", field.Message);

        // Primary-constructor parameters and nested types are named the same way.
        var parameter = Assert.IsType<ArgumentNullException>(GpuDispatch.ForDefault(run, "MyApp.Outer+Copy"));

        Assert.Equal("Outer.Copy.source", parameter.ParamName);
    }
}
