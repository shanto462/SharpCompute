// The application BuildTargetTests builds: it dispatches one shader of its own and one of the
// library it references, and prints where each pipeline came from.
using System;
using Fixture.Library;
using SharpCompute;

if (!GraphicsDevice.IsSupported)
{
    Console.WriteLine("no Metal device");
    return 2;
}

GraphicsDevice device = GraphicsDevice.GetDefault();
using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(64);

device.For(64, new Fixture.Fill(buffer, 3f));
device.For(64, new Scale(buffer, 2f));
device.For(64, new Fixture.AddFast(buffer, 1f));

Console.WriteLine($"Fill={device.GetPipelineOrigin<Fixture.Fill>()} Scale={device.GetPipelineOrigin<Scale>()} AddFast={device.GetPipelineOrigin<Fixture.AddFast>()} value={buffer[63]}");
return 0;

namespace Fixture
{
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct Fill(ReadWriteBuffer<float> data, float value) : IComputeShader
    {
        public void Execute() => data[ThreadIds.X] = value;
    }

    [ShaderOptions(FastMath = true)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct AddFast(ReadWriteBuffer<float> data, float amount) : IComputeShader
    {
        public void Execute() => data[ThreadIds.X] += amount;
    }
}
