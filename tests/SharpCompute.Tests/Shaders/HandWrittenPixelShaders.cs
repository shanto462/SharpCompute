using System;
using System.Runtime.InteropServices;

namespace SharpCompute.Tests.Shaders;

// Hand-written stand-ins for what the generator will emit for IComputeShader<Float4> under the
// pixel-shader ABI: the output texture at [[texture(0)]] (bound by ForEach), buffers
// and constants bound by SetArguments exactly as for IComputeShader, `return expr;` rewritten to
// `{ __output.write(expr, __tid.xy); return; }`, and __tid/__grid always present.

/// <summary>
/// <code>
/// // stats[0] counts invocations; thread (0, 0) records DispatchSize.XYZ in stats[1..3].
/// return new Float4((ThreadIds.X &amp; 255) / 255f, (ThreadIds.Y &amp; 255) / 255f, blue, 1f);
/// </code>
/// </summary>
internal readonly struct PixelCoordinates(ReadWriteBuffer<uint> stats, float blue)
    : IComputeShader<Float4>, IComputeShaderDescriptor<PixelCoordinates>
{
    private readonly ReadWriteBuffer<uint> _stats = stats;
    private readonly float _blue = blue;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Constants
    {
        public float Blue;
    }

    public Float4 Execute() => throw new NotSupportedException();

    public static string EntryPoint => "PixelCoordinates_Execute";

    public static string MslSource => Source;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    internal static string Source => Msl.Header + """
        struct __Constants
        {
            float blue;
        };
        static_assert(sizeof(__Constants) == 4, "__Constants size");

        kernel void PixelCoordinates_Execute(
            texture2d<float, access::write> __output [[texture(0)]],
            device atomic_uint* stats [[buffer(0)]],
            constant __Constants& __c [[buffer(1)]],
            uint3 __tid [[thread_position_in_grid]],
            uint3 __grid [[threads_per_grid]])
        {
            atomic_fetch_add_explicit(&stats[0], 1u, memory_order_relaxed);

            if (__tid.x == 0 && __tid.y == 0)
            {
                atomic_store_explicit(&stats[1], __grid.x, memory_order_relaxed);
                atomic_store_explicit(&stats[2], __grid.y, memory_order_relaxed);
                atomic_store_explicit(&stats[3], __grid.z, memory_order_relaxed);
            }

            { __output.write(float4(float(__tid.x & 255u) / 255.0f, float(__tid.y & 255u) / 255.0f, __c.blue, 1.0f), __tid.xy); return; }
        }
        """;

    internal static void Bind(ReadWriteBuffer<uint> stats, float blue, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, stats);
        writer.SetBytes(1, new Constants { Blue = blue });
    }

    public static void SetArguments(in PixelCoordinates shader, ref ShaderArgumentWriter writer) => Bind(shader._stats, shader._blue, ref writer);

    /// <summary>The color <see cref="PixelCoordinates"/> writes at (x, y) for a <paramref name="blue"/> of k/255.</summary>
    internal static Rgba32 Expected(int x, int y, byte blue) => new((byte)(x & 255), (byte)(y & 255), blue, 255);
}

/// <summary><see cref="PixelCoordinates"/> with <c>[ThreadGroupSize(8, 4, 1)]</c>.</summary>
internal readonly struct PixelCoordinates8x4(ReadWriteBuffer<uint> stats, float blue)
    : IComputeShader<Float4>, IComputeShaderDescriptor<PixelCoordinates8x4>
{
    private readonly ReadWriteBuffer<uint> _stats = stats;
    private readonly float _blue = blue;

    public Float4 Execute() => throw new NotSupportedException();

    public static string EntryPoint => "PixelCoordinates_Execute";

    public static string MslSource => PixelCoordinates.Source;

    public static int ThreadGroupSizeX => 8;

    public static int ThreadGroupSizeY => 4;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in PixelCoordinates8x4 shader, ref ShaderArgumentWriter writer) =>
        PixelCoordinates.Bind(shader._stats, shader._blue, ref writer);
}

/// <summary><c>return new Float4(2f, -1f, 0.5f * x, 64f / 255f);</c> — out-of-range values clamp on the unorm write.</summary>
internal readonly struct OutOfRangeColor(float x)
    : IComputeShader<Float4>, IComputeShaderDescriptor<OutOfRangeColor>
{
    private readonly float _x = x;

    public Float4 Execute() => throw new NotSupportedException();

    public static string EntryPoint => "OutOfRangeColor_Execute";

    public static string MslSource => Msl.Header + """
        struct __Constants
        {
            float x;
        };

        kernel void OutOfRangeColor_Execute(
            texture2d<float, access::write> __output [[texture(0)]],
            constant __Constants& __c [[buffer(0)]],
            uint3 __tid [[thread_position_in_grid]],
            uint3 __grid [[threads_per_grid]])
        {
            { __output.write(float4(2.0f, -1.0f, 0.5f * __c.x, 64.0f / 255.0f), __tid.xy); return; }
        }
        """;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in OutOfRangeColor shader, ref ShaderArgumentWriter writer) => writer.SetBytes(0, shader._x);
}
