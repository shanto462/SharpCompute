using System.Runtime.InteropServices;

namespace SharpCompute.Tests.Shaders;

// Shaders for the texture tests.
//
// The first group is hand-written and generic over the pixel type, so that one shader covers every
// texture format: a generated shader cannot be generic, and the MSL is the same for every format
// anyway (a texture2d<float>, whatever the pixel format). Each MSL body is what the generator emits
// for the C# next to it, and Execute() is real, because ForCpu runs it. They bind through
// ShaderArgumentWriter.SetTexture exactly as generated code does.
//
// The second group is generated, and covers the generator and the runtime together.

/// <summary><c>output[y * Width + x] = source[x, y]</c> for a sampled (read-only) texture.</summary>
internal readonly struct ReadTexels<TPixel>(ReadOnlyTexture2D<TPixel> source, ReadWriteBuffer<Float4> output)
    : IComputeShader, IComputeShaderDescriptor<ReadTexels<TPixel>>
    where TPixel : unmanaged
{
    private readonly ReadOnlyTexture2D<TPixel> _source = source;
    private readonly ReadWriteBuffer<Float4> _output = output;

    public void Execute() => _output[(ThreadIds.Y * _source.Width) + ThreadIds.X] = _source[ThreadIds.XY];

    public static string EntryPoint => "ReadTexels";

    public static string MslSource => Msl.Header + """
        kernel void ReadTexels(
            texture2d<float, access::sample> source [[texture(0)]],
            device packed_float4* output [[buffer(0)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            output[int(__tid.y) * int(source.get_width()) + int(__tid.x)] = source.read(uint2(int2(__tid.xy)));
        }
        """;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in ReadTexels<TPixel> shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._output);
        writer.SetTexture(0, shader._source);
    }
}

/// <summary><c>output[y * Width + x] = target[x, y]</c> for a read-write texture (<c>access::read_write</c>).</summary>
internal readonly struct LoadTexels<TPixel>(ReadWriteTexture2D<TPixel> target, ReadWriteBuffer<Float4> output)
    : IComputeShader, IComputeShaderDescriptor<LoadTexels<TPixel>>
    where TPixel : unmanaged
{
    private readonly ReadWriteTexture2D<TPixel> _target = target;
    private readonly ReadWriteBuffer<Float4> _output = output;

    public void Execute() => _output[(ThreadIds.Y * _target.Width) + ThreadIds.X] = _target[ThreadIds.X, ThreadIds.Y];

    public static string EntryPoint => "LoadTexels";

    public static string MslSource => Msl.Header + """
        kernel void LoadTexels(
            texture2d<float, access::read_write> target [[texture(0)]],
            device packed_float4* output [[buffer(0)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            output[int(__tid.y) * int(target.get_width()) + int(__tid.x)] = target.read(uint2(int(__tid.x), int(__tid.y)));
        }
        """;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in LoadTexels<TPixel> shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._output);
        writer.SetTexture(0, shader._target);
    }
}

/// <summary><c>target[x, y] = colors[y * Width + x]</c>.</summary>
internal readonly struct StoreTexels<TPixel>(ReadWriteTexture2D<TPixel> target, ReadOnlyBuffer<Float4> colors)
    : IComputeShader, IComputeShaderDescriptor<StoreTexels<TPixel>>
    where TPixel : unmanaged
{
    private readonly ReadWriteTexture2D<TPixel> _target = target;
    private readonly ReadOnlyBuffer<Float4> _colors = colors;

    public void Execute() => _target[ThreadIds.XY] = _colors[(ThreadIds.Y * _target.Width) + ThreadIds.X];

    public static string EntryPoint => "StoreTexels";

    public static string MslSource => Msl.Header + """
        kernel void StoreTexels(
            texture2d<float, access::read_write> target [[texture(0)]],
            const device packed_float4* colors [[buffer(0)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            target.write(colors[int(__tid.y) * int(target.get_width()) + int(__tid.x)], uint2(int2(__tid.xy)));
        }
        """;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in StoreTexels<TPixel> shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._colors);
        writer.SetTexture(0, shader._target);
    }
}

/// <summary>
/// <c>output[i] = source.Sample(sampler, coordinates[i])</c>, where <c>sampler</c> selects one of the six
/// <see cref="Sampler"/> properties in declaration order (0 = PointClamp … 5 = LinearMirror).
/// </summary>
internal readonly struct SampleTexels<TPixel>(ReadOnlyTexture2D<TPixel> source, ReadOnlyBuffer<Float2> coordinates, ReadWriteBuffer<Float4> output, int sampler)
    : IComputeShader, IComputeShaderDescriptor<SampleTexels<TPixel>>
    where TPixel : unmanaged
{
    private readonly ReadOnlyTexture2D<TPixel> _source = source;
    private readonly ReadOnlyBuffer<Float2> _coordinates = coordinates;
    private readonly ReadWriteBuffer<Float4> _output = output;
    private readonly int _sampler = sampler;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Constants
    {
        public int Sampler;
    }

    public void Execute()
    {
        int i = ThreadIds.X;
        Float2 uv = _coordinates[i];

        switch (_sampler)
        {
            case 0:
                _output[i] = _source.Sample(Sampler.PointClamp, uv);
                break;
            case 1:
                _output[i] = _source.Sample(Sampler.PointRepeat, uv);
                break;
            case 2:
                _output[i] = _source.Sample(Sampler.PointMirror, uv);
                break;
            case 3:
                _output[i] = _source.Sample(Sampler.LinearClamp, uv);
                break;
            case 4:
                _output[i] = _source.Sample(Sampler.LinearRepeat, uv);
                break;
            default:
                _output[i] = _source.Sample(Sampler.LinearMirror, uv);
                break;
        }
    }

    public static string EntryPoint => "SampleTexels";

    public static string MslSource => Msl.Header + """
        constexpr sampler __ms_sampler_point_clamp(coord::normalized, filter::nearest, address::clamp_to_edge);
        constexpr sampler __ms_sampler_point_repeat(coord::normalized, filter::nearest, address::repeat);
        constexpr sampler __ms_sampler_point_mirror(coord::normalized, filter::nearest, address::mirrored_repeat);
        constexpr sampler __ms_sampler_linear_clamp(coord::normalized, filter::linear, address::clamp_to_edge);
        constexpr sampler __ms_sampler_linear_repeat(coord::normalized, filter::linear, address::repeat);
        constexpr sampler __ms_sampler_linear_mirror(coord::normalized, filter::linear, address::mirrored_repeat);

        struct __Constants
        {
            int sampler;
        };
        static_assert(sizeof(__Constants) == 4, "__Constants size");

        kernel void SampleTexels(
            texture2d<float, access::sample> source [[texture(0)]],
            const device packed_float2* coordinates [[buffer(0)]],
            device packed_float4* output [[buffer(1)]],
            constant __Constants& __c [[buffer(2)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            int i = int(__tid.x);
            float2 uv = coordinates[i];
            switch (__c.sampler)
            {
                case 0:
                {
                    output[i] = source.sample(__ms_sampler_point_clamp, uv, metal::level(0));
                    break;
                }
                case 1:
                {
                    output[i] = source.sample(__ms_sampler_point_repeat, uv, metal::level(0));
                    break;
                }
                case 2:
                {
                    output[i] = source.sample(__ms_sampler_point_mirror, uv, metal::level(0));
                    break;
                }
                case 3:
                {
                    output[i] = source.sample(__ms_sampler_linear_clamp, uv, metal::level(0));
                    break;
                }
                case 4:
                {
                    output[i] = source.sample(__ms_sampler_linear_repeat, uv, metal::level(0));
                    break;
                }
                default:
                {
                    output[i] = source.sample(__ms_sampler_linear_mirror, uv, metal::level(0));
                    break;
                }
            }
        }
        """;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in SampleTexels<TPixel> shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._coordinates);
        writer.SetBuffer(1, shader._output);
        writer.SetTexture(0, shader._source);
        writer.SetBytes(2, new Constants { Sampler = shader._sampler });
    }
}

/// <summary>Binds one texture at an arbitrary texture index, to test <see cref="ShaderArgumentWriter.SetTexture"/>'s checks.</summary>
internal readonly struct BindTextureAtIndex(ReadOnlyTexture2D<float> texture, int index)
    : IComputeShader, IComputeShaderDescriptor<BindTextureAtIndex>
{
    private readonly ReadOnlyTexture2D<float> _texture = texture;
    private readonly int _index = index;

    public void Execute()
    {
    }

    public static string EntryPoint => "BindTextureAtIndex";

    public static string MslSource => Msl.Header + """
        kernel void BindTextureAtIndex(uint3 __tid [[thread_position_in_grid]])
        {
        }
        """;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in BindTextureAtIndex shader, ref ShaderArgumentWriter writer) =>
        writer.SetTexture(shader._index, shader._texture);
}

// ---- generated ------------------------------------------------------------------------------------

/// <summary>The generated counterpart of <see cref="SampleTexels{TPixel}"/> for one format, to check the two agree.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SampleFloat4(ReadOnlyTexture2D<Float4> source, ReadOnlyBuffer<Float2> coordinates, ReadWriteBuffer<Float4> output, int sampler) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        Float2 uv = coordinates[i];

        switch (sampler)
        {
            case 0:
                output[i] = source.Sample(Sampler.PointClamp, uv);
                break;
            case 1:
                output[i] = source.Sample(Sampler.PointRepeat, uv);
                break;
            case 2:
                output[i] = source.Sample(Sampler.PointMirror, uv);
                break;
            case 3:
                output[i] = source.Sample(Sampler.LinearClamp, uv);
                break;
            case 4:
                output[i] = source.Sample(Sampler.LinearRepeat, uv);
                break;
            default:
                output[i] = source.Sample(Sampler.LinearMirror, uv);
                break;
        }
    }
}

/// <summary>
/// Copies a texture into a buffer through a helper that takes the texture as a parameter and one that
/// reads the captured texture, over a 2D grid (so it can be split between the CPU and the GPU).
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct TextureToBuffer(ReadOnlyTexture2D<Rgba32> source, ReadWriteBuffer<Float4> output) : IComputeShader
{
    public void Execute()
    {
        Int2 xy = ThreadIds.XY;
        output[(xy.Y * source.Width) + xy.X] = (Texel(source, xy) + Captured(xy.X, xy.Y)) * 0.5f;
    }

    private static Float4 Texel(ReadOnlyTexture2D<Rgba32> image, Int2 at) => image[at];

    private Float4 Captured(int x, int y) => source[x, y];
}

/// <summary>
/// Writes a value that depends on the pixel's position to every pixel of an R8 texture, over a 2D grid
/// (so it can be split between the CPU and the GPU): <c>(7x + 13y) mod 256</c>, as a byte.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct WritePositionR8(ReadWriteTexture2D<R8> target) : IComputeShader
{
    public void Execute()
    {
        int value = ((ThreadIds.X * 7) + (ThreadIds.Y * 13)) & 255;
        target[ThreadIds.XY] = new Float4(value / 255f, 0f, 0f, 1f);
    }
}

/// <summary><see cref="WritePositionR8"/> for an Rgba32 texture, with a different function of the value in each channel.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct WritePositionRgba32(ReadWriteTexture2D<Rgba32> target) : IComputeShader
{
    public void Execute()
    {
        int value = ((ThreadIds.X * 7) + (ThreadIds.Y * 13)) & 255;
        target[ThreadIds.XY] = new Float4(value / 255f, (255 - value) / 255f, (value ^ 85) / 255f, 1f);
    }
}

/// <summary><see cref="WritePositionR8"/> for a float texture: <c>7x + 13y</c>, which no two pixels of a row or a column share.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct WritePositionSingle(ReadWriteTexture2D<float> target) : IComputeShader
{
    public void Execute() => target[ThreadIds.XY] = new Float4((ThreadIds.X * 7) + (ThreadIds.Y * 13), 0f, 0f, 1f);
}

/// <summary>
/// Writes a pixel of each texture and reads it straight back, in the kernel itself: every thread must
/// see its own write. <paramref name="seen"/> gets what was read, three entries per thread
/// (R8, Rgba32, float).
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct WriteThenRead(
    ReadWriteTexture2D<R8> r8, ReadWriteTexture2D<Rgba32> rgba, ReadWriteTexture2D<float> single, ReadWriteBuffer<Float4> seen) : IComputeShader
{
    public void Execute()
    {
        Int2 xy = ThreadIds.XY;
        int index = ((xy.Y * r8.Width) + xy.X) * 3;
        int value = 1 + (((xy.X * 7) + (xy.Y * 13)) % 255);

        r8[xy] = new Float4(value / 255f, 0f, 0f, 1f);
        rgba[xy] = new Float4(value / 255f, (255 - value) / 255f, (value ^ 85) / 255f, 1f);
        single[xy] = new Float4(value * 0.5f, 0f, 0f, 1f);

        seen[index] = r8[xy];
        seen[index + 1] = rgba[xy];
        seen[index + 2] = single[xy];
    }
}

/// <summary>
/// <see cref="WriteThenRead"/> with the writes and the reads in helpers: instance helpers that use the
/// captured textures, and static ones that take the texture as a parameter.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct WriteThenReadInHelpers(
    ReadWriteTexture2D<R8> r8, ReadWriteTexture2D<Rgba32> rgba, ReadWriteTexture2D<float> single, ReadWriteBuffer<Float4> seen) : IComputeShader
{
    public void Execute()
    {
        Int2 xy = ThreadIds.XY;
        int index = ((xy.Y * r8.Width) + xy.X) * 3;
        int value = 1 + (((xy.X * 7) + (xy.Y * 13)) % 255);

        Store(xy, value);
        Put(single, xy, value * 0.5f);

        seen[index] = r8[xy];
        seen[index + 1] = Load(xy);
        seen[index + 2] = Get(single, xy);
    }

    private void Store(Int2 xy, int value)
    {
        r8[xy] = new Float4(value / 255f, 0f, 0f, 1f);
        rgba[xy] = new Float4(value / 255f, (255 - value) / 255f, (value ^ 85) / 255f, 1f);
    }

    private Float4 Load(Int2 xy) => rgba[xy];

    private static void Put(ReadWriteTexture2D<float> canvas, Int2 at, float value) => canvas[at] = new Float4(value, 0f, 0f, 1f);

    private static Float4 Get(ReadWriteTexture2D<float> canvas, Int2 at) => canvas[at];
}

/// <summary>
/// Adds to a pixel of each texture <see cref="Turns"/> times, reading it back each time: the read
/// comes before the write in the source and after it from the second turn on. The textures start
/// at zero; a thread that did not see its own writes would end one step in instead of four.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct AccumulateInPlace(
    ReadWriteTexture2D<R8> r8, ReadWriteTexture2D<Rgba32> rgba, ReadWriteTexture2D<float> single, ReadWriteBuffer<Float4> seen) : IComputeShader
{
    public const int Turns = 4;

    public void Execute()
    {
        Int2 xy = ThreadIds.XY;
        int index = ((xy.Y * r8.Width) + xy.X) * 3;
        int step = 1 + (((xy.X * 7) + (xy.Y * 13)) & 31);

        for (int turn = 0; turn < Turns; turn++)
        {
            Float4 a = r8[xy];
            Float4 b = rgba[xy];
            Float4 c = single[xy];

            r8[xy] = new Float4(a.X + (step / 255f), 0f, 0f, 1f);
            rgba[xy] = new Float4(b.X + (step / 255f), b.Y + ((63 - step) / 255f), b.Z + ((step ^ 21) / 255f), 1f);
            single[xy] = new Float4(c.X + (step * 0.25f), 0f, 0f, 1f);
        }

        seen[index] = r8[xy];
        seen[index + 1] = rgba[xy];
        seen[index + 2] = single[xy];
    }
}

/// <summary>
/// One Jacobi relaxation step: every pixel of <paramref name="target"/> becomes the mean of its four
/// neighbours in <paramref name="source"/> (clamped at the border). Run back and forth between two textures.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DiffuseStep(ReadWriteTexture2D<float> source, ReadWriteTexture2D<float> target) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        int left = Gpu.Max(x - 1, 0);
        int right = Gpu.Min(x + 1, source.Width - 1);
        int up = Gpu.Max(y - 1, 0);
        int down = Gpu.Min(y + 1, source.Height - 1);

        float sum = source[left, y].X + source[right, y].X + source[x, up].X + source[x, down].X;
        target[x, y] = new Float4(sum * 0.25f, 0f, 0f, 1f);
    }
}

/// <summary>Scales <paramref name="source"/> to the size of <paramref name="target"/> with bilinear filtering.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Magnify(ReadOnlyTexture2D<float> source, ReadWriteTexture2D<Rgba32> target) : IComputeShader
{
    public void Execute()
    {
        Float2 uv = (new Float2(ThreadIds.X, ThreadIds.Y) + 0.5f) / new Float2(target.Width, target.Height);
        float value = source.Sample(Sampler.LinearClamp, uv).X;

        target[ThreadIds.XY] = new Float4(value, 1f - value, 0.5f, 1f);
    }
}

/// <summary>A pixel shader with two input textures, which bind after the output at texture indices 1 and 2.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct Composite(ReadOnlyTexture2D<Rgba32Srgb> image, ReadOnlyTexture2D<R8> mask, float gain) : IComputeShader<Float4>
{
    public Float4 Execute()
    {
        Float2 uv = (new Float2(ThreadIds.X, ThreadIds.Y) + 0.5f) / new Float2(DispatchSize.X, DispatchSize.Y);
        Float4 color = image.Sample(Sampler.LinearRepeat, uv * 2f);
        float coverage = mask.Sample(Sampler.PointMirror, uv * 3f).X;

        return new Float4(color.X * coverage * gain, color.Y * coverage * gain, color.Z * coverage * gain, 1f);
    }
}
