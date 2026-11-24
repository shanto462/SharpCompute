using SharpCompute;

namespace SharpCompute.Samples.HelloWorld;

/// <summary>Multiplies every element of a buffer by two.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct MultiplyByTwo(ReadWriteBuffer<float> buffer) : IComputeShader
{
    public void Execute() => buffer[ThreadIds.X] *= 2;
}

/// <summary><c>y = a * x + y</c>.</summary>
[ThreadGroupSize(256)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct Saxpy(ReadOnlyBuffer<float> x, ReadWriteBuffer<float> y, float a) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        y[i] = (a * x[i]) + y[i];
    }
}
