using System;
using SharpCompute;

namespace SharpCompute.Tests.Shaders;

// Shaders for the overlapping-binding tests. Their captures are read-write types throughout, so that
// one buffer or texture can be passed as any two of them: whether that is accepted then depends only
// on which of them the shader writes.

/// <summary><c>sum[i] = a[i] + b[i]</c>: reads two captures, writes the third.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct AddReadWrite(ReadWriteBuffer<float> a, ReadWriteBuffer<float> b, ReadWriteBuffer<float> sum) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        sum[i] = a[i] + b[i];
    }
}

/// <summary>
/// <c>destination = left · right</c> for 8×8 matrices at the start of each buffer: a wave matrix's
/// <c>Load</c> reads two captures and its <c>Store</c>, which names no element, writes the third.
/// </summary>
[ThreadGroupSize(32)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct MatrixProductReadWrite(ReadWriteBuffer<float> left, ReadWriteBuffer<float> right, ReadWriteBuffer<float> destination) : IComputeShader
{
    public void Execute()
    {
        WaveMatrix8x8 a = WaveMatrix8x8.Load(left, 0, 8);
        WaveMatrix8x8 b = WaveMatrix8x8.Load(right, 0, 8);
        WaveMatrix8x8 product = WaveMatrix8x8.Multiply(a, b);
        product.Store(destination, 0, 8);
    }
}

/// <summary><c>if (i &lt; extra.Length) extra[i] = data[i] + 1</c>: writes a buffer that may be empty, and then writes nothing.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct CopyIntoMaybeEmpty(ReadWriteBuffer<float> data, ReadWriteBuffer<float> extra) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;

        if (i < extra.Length)
        {
            extra[i] = data[i] + 1f;
        }
    }
}

/// <summary><c>floats[i] = ints[i]</c>: two element types, for two wrappers of one native buffer.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct IntsToFloats(ReadWriteBuffer<int> ints, ReadWriteBuffer<float> floats) : IComputeShader
{
    public void Execute() => floats[ThreadIds.X] = ints[ThreadIds.X];
}

/// <summary>Writes pixel <c>(i, 0)</c> of one texture and stores what it then reads from another in <c>seen[i]</c>.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct WriteOneReadOther(ReadWriteTexture2D<float> written, ReadWriteTexture2D<float> read, ReadWriteBuffer<float> seen) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        written[i, 0] = new Float4(i + 1f, 0f, 0f, 1f);
        seen[i] = read[i, 0].X;
    }
}

/// <summary><see cref="WriteOneReadOther"/> with the read through a <see cref="ReadOnlyTexture2D{T}"/>, as <c>AsReadOnly()</c> gives.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct WriteOneSampleOther(ReadWriteTexture2D<float> written, ReadOnlyTexture2D<float> read, ReadWriteBuffer<float> seen) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        written[i, 0] = new Float4(i + 1f, 0f, 0f, 1f);
        seen[i] = read[i, 0].X;
    }
}

/// <summary><c>sum[i] = a[i, 0] + b[i, 0]</c>: two read-write textures that are only read.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct AddTexels(ReadWriteTexture2D<float> a, ReadWriteTexture2D<float> b, ReadWriteBuffer<float> sum) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        sum[i] = a[i, 0].X + b[i, 0].X;
    }
}

/// <summary><c>sum[i] = a[i, 0] + b[i, 0]</c> with <c>b</c> a read-only texture: a texture and its view, both only read.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct AddTexelAndView(ReadWriteTexture2D<float> a, ReadOnlyTexture2D<float> b, ReadWriteBuffer<float> sum) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        sum[i] = a[i, 0].X + b[i, 0].X;
    }
}

/// <summary>A pixel shader that reads a captured texture: half of <c>source</c> at the pixel.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct HalveSampled(ReadOnlyTexture2D<Rgba32> source) : IComputeShader<Float4>
{
    public Float4 Execute() => source[ThreadIds.XY] * 0.5f;
}

/// <summary><see cref="HalveSampled"/> with the texture captured as a read-write one (and only read).</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct HalveLoaded(ReadWriteTexture2D<Rgba32> source) : IComputeShader<Float4>
{
    public Float4 Execute() => source[ThreadIds.XY] * 0.5f;
}

/// <summary><c>data[i] = texture[i, 0] + 1</c>: reads a texture, writes a buffer.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct TexelsToBuffer(ReadOnlyTexture2D<Rgba32> texture, ReadWriteBuffer<float> data) : IComputeShader
{
    public void Execute() => data[ThreadIds.X] = texture[ThreadIds.X, 0].X + 1f;
}

/// <summary><c>texture[i, 0] = data[i] + 1</c>: reads a buffer, writes a texture.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BufferToTexels(ReadWriteTexture2D<Rgba32> texture, ReadOnlyBuffer<float> data) : IComputeShader
{
    public void Execute() => texture[ThreadIds.X, 0] = new Float4(data[ThreadIds.X] + 1f, 0f, 0f, 1f);
}

/// <summary><c>sum[i] = texture[i, 0] + data[i]</c>: a texture and a buffer that are both only read.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct AddTexelAndElement(ReadOnlyTexture2D<Rgba32> texture, ReadOnlyBuffer<float> data, ReadWriteBuffer<float> sum) : IComputeShader
{
    public void Execute() => sum[ThreadIds.X] = texture[ThreadIds.X, 0].X + data[ThreadIds.X];
}

/// <summary><c>written[x, y] = read[x, y]</c> between two RGBA8 textures, the format other Metal code's linear textures have in the tests.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct CopyTexels(ReadWriteTexture2D<Rgba32> written, ReadWriteTexture2D<Rgba32> read) : IComputeShader
{
    public void Execute() => written[ThreadIds.XY] = read[ThreadIds.XY];
}

/// <summary>
/// Binds any number of buffers to a kernel that touches none of them, to test the bindings
/// themselves: with the two-argument <c>SetBuffer</c> (a read-write buffer counts as written), or the
/// three-argument one saying <paramref name="written"/> for each.
/// </summary>
internal readonly struct BindBuffers(Buffer<float>[] buffers, bool? written = null)
    : IComputeShader, IComputeShaderDescriptor<BindBuffers>
{
    private readonly Buffer<float>[] _buffers = buffers;
    private readonly bool? _written = written;

    public void Execute()
    {
    }

    public static string EntryPoint => "BindBuffers";

    public static string MslSource => Msl.Header + """
        kernel void BindBuffers(uint3 __tid [[thread_position_in_grid]])
        {
        }
        """;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in BindBuffers shader, ref ShaderArgumentWriter writer)
    {
        for (int i = 0; i < shader._buffers.Length; i++)
        {
            if (shader._written is { } written)
            {
                writer.SetBuffer(i, shader._buffers[i], written);
            }
            else
            {
                writer.SetBuffer(i, shader._buffers[i]);
            }
        }
    }
}

/// <summary><see cref="BindBuffers"/> for two textures: the two-argument <c>SetTexture</c>, or the three-argument one.</summary>
internal readonly struct BindTextures(Texture2D<float> first, Texture2D<float> second, bool? written = null)
    : IComputeShader, IComputeShaderDescriptor<BindTextures>
{
    private readonly Texture2D<float> _first = first;
    private readonly Texture2D<float> _second = second;
    private readonly bool? _written = written;

    public void Execute()
    {
    }

    public static string EntryPoint => "BindTextures";

    public static string MslSource => Msl.Header + """
        kernel void BindTextures(uint3 __tid [[thread_position_in_grid]])
        {
        }
        """;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in BindTextures shader, ref ShaderArgumentWriter writer)
    {
        if (shader._written is { } written)
        {
            writer.SetTexture(0, shader._first, written);
            writer.SetTexture(1, shader._second, written);
        }
        else
        {
            writer.SetTexture(0, shader._first);
            writer.SetTexture(1, shader._second);
        }
    }
}
