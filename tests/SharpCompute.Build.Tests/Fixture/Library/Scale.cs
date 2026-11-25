using SharpCompute;

namespace Fixture.Library;

[GeneratedComputeShaderDescriptor]
public readonly partial struct Scale(ReadWriteBuffer<float> data, float factor) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] *= factor;
}
