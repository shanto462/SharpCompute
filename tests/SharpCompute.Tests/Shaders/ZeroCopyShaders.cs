namespace SharpCompute.Tests.Shaders;

/// <summary>Keeps the GPU busy for a while on every element, so a test can act during the dispatch.</summary>
[GeneratedComputeShaderDescriptor]
internal readonly partial struct SpinOnBuffer(ReadWriteBuffer<float> data, int iterations) : IComputeShader
{
    public void Execute()
    {
        float v = data[ThreadIds.X];

        for (int i = 0; i < iterations; i++)
        {
            v = (v * 1.0000001f) + 0.5f;
        }

        data[ThreadIds.X] = v;
    }
}
