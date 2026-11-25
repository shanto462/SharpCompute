namespace SharpCompute.Tests.Shaders;

// Shaders for PrecompiledShaderTests: each is run from the precompiled library and from source, and
// the results are compared bit for bit. The four MathSweep shaders have one body and differ only in
// their [ShaderOptions], which is what selects the compiler flags of the precompiled kernel.

/// <summary>
/// Eight results per thread from four inputs: sums and products that contraction and reassociation
/// change, and math functions that fast math replaces. Compiled with the defaults.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct MathSweep(ReadOnlyBuffer<float> input, ReadWriteBuffer<float> output) : IComputeShader
{
    public const int Inputs = 4;
    public const int Outputs = 8;

    public void Execute()
    {
        int i = ThreadIds.X;
        float a = input[i * Inputs];
        float b = input[(i * Inputs) + 1];
        float c = input[(i * Inputs) + 2];
        float d = input[(i * Inputs) + 3];

        output[i * Outputs] = (a * b) + c;
        output[(i * Outputs) + 1] = (a + b) - a;
        output[(i * Outputs) + 2] = Gpu.Sin(a) * Gpu.Cos(b);
        output[(i * Outputs) + 3] = Gpu.Pow(Gpu.Abs(a), b);
        output[(i * Outputs) + 4] = Gpu.Exp(c) + Gpu.Log(Gpu.Abs(d));
        output[(i * Outputs) + 5] = a / b * b;
        output[(i * Outputs) + 6] = (a * b) - (c * d);
        output[(i * Outputs) + 7] = Gpu.Sqrt(Gpu.Abs(c)) * (a + (b * 0.1f) + (c * 0.2f) + (d * 0.3f));
    }
}

/// <summary><see cref="MathSweep"/> with fast math.</summary>
[ShaderOptions(FastMath = true)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MathSweepFast(ReadOnlyBuffer<float> input, ReadWriteBuffer<float> output) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        float a = input[i * MathSweep.Inputs];
        float b = input[(i * MathSweep.Inputs) + 1];
        float c = input[(i * MathSweep.Inputs) + 2];
        float d = input[(i * MathSweep.Inputs) + 3];

        output[i * MathSweep.Outputs] = (a * b) + c;
        output[(i * MathSweep.Outputs) + 1] = (a + b) - a;
        output[(i * MathSweep.Outputs) + 2] = Gpu.Sin(a) * Gpu.Cos(b);
        output[(i * MathSweep.Outputs) + 3] = Gpu.Pow(Gpu.Abs(a), b);
        output[(i * MathSweep.Outputs) + 4] = Gpu.Exp(c) + Gpu.Log(Gpu.Abs(d));
        output[(i * MathSweep.Outputs) + 5] = a / b * b;
        output[(i * MathSweep.Outputs) + 6] = (a * b) - (c * d);
        output[(i * MathSweep.Outputs) + 7] = Gpu.Sqrt(Gpu.Abs(c)) * (a + (b * 0.1f) + (c * 0.2f) + (d * 0.3f));
    }
}

/// <summary><see cref="MathSweep"/> with contraction off.</summary>
[ShaderOptions(FpContract = false)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MathSweepNoContract(ReadOnlyBuffer<float> input, ReadWriteBuffer<float> output) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        float a = input[i * MathSweep.Inputs];
        float b = input[(i * MathSweep.Inputs) + 1];
        float c = input[(i * MathSweep.Inputs) + 2];
        float d = input[(i * MathSweep.Inputs) + 3];

        output[i * MathSweep.Outputs] = (a * b) + c;
        output[(i * MathSweep.Outputs) + 1] = (a + b) - a;
        output[(i * MathSweep.Outputs) + 2] = Gpu.Sin(a) * Gpu.Cos(b);
        output[(i * MathSweep.Outputs) + 3] = Gpu.Pow(Gpu.Abs(a), b);
        output[(i * MathSweep.Outputs) + 4] = Gpu.Exp(c) + Gpu.Log(Gpu.Abs(d));
        output[(i * MathSweep.Outputs) + 5] = a / b * b;
        output[(i * MathSweep.Outputs) + 6] = (a * b) - (c * d);
        output[(i * MathSweep.Outputs) + 7] = Gpu.Sqrt(Gpu.Abs(c)) * (a + (b * 0.1f) + (c * 0.2f) + (d * 0.3f));
    }
}

/// <summary><see cref="MathSweep"/> with fast math and contraction off.</summary>
[ShaderOptions(FastMath = true, FpContract = false)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MathSweepFastNoContract(ReadOnlyBuffer<float> input, ReadWriteBuffer<float> output) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        float a = input[i * MathSweep.Inputs];
        float b = input[(i * MathSweep.Inputs) + 1];
        float c = input[(i * MathSweep.Inputs) + 2];
        float d = input[(i * MathSweep.Inputs) + 3];

        output[i * MathSweep.Outputs] = (a * b) + c;
        output[(i * MathSweep.Outputs) + 1] = (a + b) - a;
        output[(i * MathSweep.Outputs) + 2] = Gpu.Sin(a) * Gpu.Cos(b);
        output[(i * MathSweep.Outputs) + 3] = Gpu.Pow(Gpu.Abs(a), b);
        output[(i * MathSweep.Outputs) + 4] = Gpu.Exp(c) + Gpu.Log(Gpu.Abs(d));
        output[(i * MathSweep.Outputs) + 5] = a / b * b;
        output[(i * MathSweep.Outputs) + 6] = (a * b) - (c * d);
        output[(i * MathSweep.Outputs) + 7] = Gpu.Sqrt(Gpu.Abs(c)) * (a + (b * 0.1f) + (c * 0.2f) + (d * 0.3f));
    }
}

/// <summary>
/// A 2D field from the thread's position and the dispatch size, the built-ins a split dispatch
/// rebases with its grid window: the kernel the windowed specialization is checked with.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct WindowedField(ReadWriteBuffer<float> field, float phase) : IComputeShader
{
    public void Execute()
    {
        Float2 uv = ThreadIds.Normalized.XY;
        int index = (ThreadIds.Y * DispatchSize.X) + ThreadIds.X;

        field[index] = (Gpu.Sin((uv.X * 12f) + phase) * Gpu.Cos(uv.Y * 7f)) + (ThreadIds.X * 0.001f) + (DispatchSize.Y * 0.5f);
    }
}

/// <summary>Shaders nothing else dispatches, so each test sees their first use on its own device.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct PrecompiledProbeA(ReadWriteBuffer<int> data) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] = ThreadIds.X + 101;
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct PrecompiledProbeB(ReadWriteBuffer<int> data) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] = ThreadIds.X + 202;
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct PrecompiledProbeC(ReadWriteBuffer<int> data) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] = ThreadIds.X + 303;
}

[ShaderOptions(FastMath = true)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct PrecompiledProbeFast(ReadWriteBuffer<int> data) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] = ThreadIds.X + 404;
}
