using System;

namespace SharpCompute.Build.Tests;

// The shaders this assembly's own metadata is read for. The generated ones are what the tool is for;
// the hand-written ones are the cases it has to leave alone or survive.

[GeneratedComputeShaderDescriptor]
public readonly partial struct Fill(ReadWriteBuffer<float> data, float value) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] = value;
}

[ShaderOptions(FastMath = true)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct FillFast(ReadWriteBuffer<float> data, float value) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] = Gpu.Sin(value);
}

[ShaderOptions(FpContract = false)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MulAddExact(ReadWriteBuffer<float> data, float a, float b) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] = (data[ThreadIds.X] * a) + b;
}

[ShaderOptions(FastMath = true, FpContract = false)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MulAddFastExact(ReadWriteBuffer<float> data, float a, float b) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] = (data[ThreadIds.X] * a) + b;
}

/// <summary>A pixel shader that reads its position, so its kernel declares the windowed function constant.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Gradient(float time) : IComputeShader<Float4>
{
    public Float4 Execute()
    {
        Float2 uv = ThreadIds.Normalized.XY;
        return new Float4(uv.X, uv.Y, Gpu.Sin(time), 1f);
    }
}

public static partial class Containing
{
    /// <summary>Private and nested: found through metadata like any other.</summary>
    [GeneratedComputeShaderDescriptor]
    private readonly partial struct Nested(ReadWriteBuffer<int> data) : IComputeShader
    {
        public void Execute() => data[ThreadIds.X] = 7;
    }

    internal static string NestedEntryPoint => EntryPoint<Nested>();

    internal static string NestedSource => Source<Nested>();

    private static string EntryPoint<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.EntryPoint;

    private static string Source<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.MslSource;
}

/// <summary>A hand-written descriptor: it has no record, so the tool does not see it at all.</summary>
internal readonly struct HandWritten : IComputeShader, IComputeShaderDescriptor<HandWritten>
{
    public static string EntryPoint => "HandWritten_Kernel";

    public static string MslSource => "#include <metal_stdlib>\nkernel void HandWritten_Kernel() { }\n";

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in HandWritten shader, ref ShaderArgumentWriter writer)
    {
    }

    public void Execute() => throw new NotSupportedException();
}

/// <summary>
/// A record on a descriptor whose MSL is put together at run time. The tool cannot know the text, so
/// it has to skip the shader and say so. (The getter's body is as long as one that returns a literal.)
/// </summary>
[GeneratedShaderRecord("ComputedSource_Kernel", ShaderCompileOptions.None)]
internal readonly struct ComputedSource : IComputeShader, IComputeShaderDescriptor<ComputedSource>
{
    private static readonly string s_source = "#include <metal_stdlib>\n" + "kernel void ComputedSource_Kernel() { }\n";

    static string IComputeShaderDescriptor<ComputedSource>.EntryPoint => "ComputedSource_Kernel";

    static string IComputeShaderDescriptor<ComputedSource>.MslSource => s_source;

    static int IComputeShaderDescriptor<ComputedSource>.ThreadGroupSizeX => 0;

    static int IComputeShaderDescriptor<ComputedSource>.ThreadGroupSizeY => 0;

    static int IComputeShaderDescriptor<ComputedSource>.ThreadGroupSizeZ => 0;

    static void IComputeShaderDescriptor<ComputedSource>.SetArguments(in ComputedSource shader, ref ShaderArgumentWriter writer)
    {
    }

    public void Execute() => throw new NotSupportedException();
}

/// <summary>
/// A record on a descriptor that implements the interface with public members instead of the explicit
/// ones the generator emits. The compiler records which method implements a static interface member
/// either way, and that is what the tool goes by, so this one is read like a generated shader.
/// </summary>
[GeneratedShaderRecord("ImplicitMembers_Kernel", ShaderCompileOptions.None)]
internal readonly struct ImplicitMembers : IComputeShader, IComputeShaderDescriptor<ImplicitMembers>
{
    public static string EntryPoint => "ImplicitMembers_Kernel";

    public static string MslSource => "#include <metal_stdlib>\nkernel void ImplicitMembers_Kernel() { }\n";

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in ImplicitMembers shader, ref ShaderArgumentWriter writer)
    {
    }

    public void Execute() => throw new NotSupportedException();
}

/// <summary>
/// A record whose MSL the Metal compiler rejects: what a shader the generator translated badly would
/// look like to the tool. It is left out of the library and the rest is still built.
/// </summary>
[GeneratedShaderRecord("Rejected_Kernel", ShaderCompileOptions.None)]
internal readonly struct Rejected : IComputeShader, IComputeShaderDescriptor<Rejected>
{
    static string IComputeShaderDescriptor<Rejected>.EntryPoint => "Rejected_Kernel";

    static string IComputeShaderDescriptor<Rejected>.MslSource =>
        """
        #include <metal_stdlib>
        kernel void Rejected_Kernel() { this_is_not_declared(); }
        """;

    static int IComputeShaderDescriptor<Rejected>.ThreadGroupSizeX => 0;

    static int IComputeShaderDescriptor<Rejected>.ThreadGroupSizeY => 0;

    static int IComputeShaderDescriptor<Rejected>.ThreadGroupSizeZ => 0;

    static void IComputeShaderDescriptor<Rejected>.SetArguments(in Rejected shader, ref ShaderArgumentWriter writer)
    {
    }

    public void Execute() => throw new NotSupportedException();
}

/// <summary>Reads a shader's descriptor, which is only reachable through the type parameter.</summary>
internal static class Descriptor
{
    public static string EntryPoint<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.EntryPoint;

    public static string Source<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.MslSource;

    public static int Options<T>()
        where T : struct, IComputeShaderDescriptor<T> => (int)T.CompileOptions;
}
