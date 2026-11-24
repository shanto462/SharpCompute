using System;
using System.Runtime.InteropServices;

namespace SharpCompute.Tests.Shaders;

// Hand-written stand-ins for what the source generator will emit. Each one follows the binding ABI
// exactly: buffers at [[buffer(0..n-1)]] in field order, then one packed `__Constants`
// struct at [[buffer(n)]] mirrored by a `Pack = 4` C# struct, with the same static_assert drift
// guards the generator emits. None is marked [GeneratedComputeShaderDescriptor], so the generator
// leaves them alone and these tests pin the runtime half of the ABI on their own.
//
// Execute() is never called by the runtime; each body is documented by the C# it stands for.

internal static class Msl
{
    public const string Header = """
        #include <metal_stdlib>
        using namespace metal;

        """;
}

/// <summary><c>buffer[ThreadIds.X] *= factor;</c></summary>
internal readonly struct MultiplyByTwo(ReadWriteBuffer<float> buffer, float factor)
    : IComputeShader, IComputeShaderDescriptor<MultiplyByTwo>
{
    private readonly ReadWriteBuffer<float> _buffer = buffer;
    private readonly float _factor = factor;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Constants
    {
        public float Factor;
    }

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "MultiplyByTwo_Execute";

    public static string MslSource => Msl.Header + """
        struct __Constants
        {
            float factor;
        };
        static_assert(sizeof(__Constants) == 4, "__Constants size");

        kernel void MultiplyByTwo_Execute(
            device float* buffer [[buffer(0)]],
            constant __Constants& __c [[buffer(1)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            buffer[int(__tid.x)] *= __c.factor;
        }
        """;

    public static int ThreadGroupSizeX => 64;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in MultiplyByTwo shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._buffer);
        writer.SetBytes(1, new Constants { Factor = shader._factor });
    }
}

/// <summary><c>y[i] = a * x[i] + y[i] + offset[i % 3];</c> with a float + packed_float3 constants struct.</summary>
internal readonly struct Saxpy(ReadOnlyBuffer<float> x, ReadWriteBuffer<float> y, float a, Float3 offset)
    : IComputeShader, IComputeShaderDescriptor<Saxpy>
{
    private readonly ReadOnlyBuffer<float> _x = x;
    private readonly ReadWriteBuffer<float> _y = y;
    private readonly float _a = a;
    private readonly Float3 _offset = offset;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct Constants
    {
        public float A;
        public Float3 Offset;
    }

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "Saxpy_Execute";

    public static string MslSource => Msl.Header + """
        struct __Constants
        {
            float a;
            packed_float3 offset;
        };
        static_assert(sizeof(__Constants) == 16, "__Constants size");
        static_assert(__builtin_offsetof(__Constants, offset) == 4, "__Constants.offset offset");

        kernel void Saxpy_Execute(
            const device float* x [[buffer(0)]],
            device float* y [[buffer(1)]],
            constant __Constants& __c [[buffer(2)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            int i = int(__tid.x);
            float3 offset = __c.offset;
            y[i] = __c.a * x[i] + y[i] + offset[i % 3];
        }
        """;

    public static int ThreadGroupSizeX => 64;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in Saxpy shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._x);
        writer.SetBuffer(1, shader._y);
        writer.SetBytes(2, new Constants { A = shader._a, Offset = shader._offset });
    }
}

/// <summary><c>buffer[ThreadIds.Y * width + ThreadIds.X] = (uint)(ThreadIds.Y * width + ThreadIds.X);</c></summary>
internal readonly struct WriteIndex2D(ReadWriteBuffer<uint> buffer, uint width)
    : IComputeShader, IComputeShaderDescriptor<WriteIndex2D>
{
    private readonly ReadWriteBuffer<uint> _buffer = buffer;
    private readonly uint _width = width;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Constants
    {
        public uint Width;
    }

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "WriteIndex2D_Execute";

    public static string MslSource => Msl.Header + """
        struct __Constants
        {
            uint width;
        };
        static_assert(sizeof(__Constants) == 4, "__Constants size");

        kernel void WriteIndex2D_Execute(
            device uint* buffer [[buffer(0)]],
            constant __Constants& __c [[buffer(1)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            uint index = __tid.y * __c.width + __tid.x;
            buffer[index] = index;
        }
        """;

    public static int ThreadGroupSizeX => 8;

    public static int ThreadGroupSizeY => 8;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in WriteIndex2D shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._buffer);
        writer.SetBytes(1, new Constants { Width = shader._width });
    }
}

/// <summary>
/// 3D kernel with no constants: <c>buffer[(z * DispatchSize.Y + y) * DispatchSize.X + x] = x | y &lt;&lt; 10 | z &lt;&lt; 20;</c>
/// </summary>
internal readonly struct EncodePosition3D(ReadWriteBuffer<uint> buffer)
    : IComputeShader, IComputeShaderDescriptor<EncodePosition3D>
{
    private readonly ReadWriteBuffer<uint> _buffer = buffer;

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "EncodePosition3D_Execute";

    public static string MslSource => Msl.Header + """
        kernel void EncodePosition3D_Execute(
            device uint* buffer [[buffer(0)]],
            uint3 __tid [[thread_position_in_grid]],
            uint3 __grid [[threads_per_grid]])
        {
            uint index = (__tid.z * __grid.y + __tid.y) * __grid.x + __tid.x;
            buffer[index] = __tid.x | (__tid.y << 10) | (__tid.z << 20);
        }
        """;

    public static int ThreadGroupSizeX => 4;

    public static int ThreadGroupSizeY => 4;

    public static int ThreadGroupSizeZ => 4;

    public static void SetArguments(in EncodePosition3D shader, ref ShaderArgumentWriter writer) =>
        writer.SetBuffer(0, shader._buffer);
}

/// <summary><c>destination[i] = source[i] * source[i];</c> — a read-only input and a read/write output.</summary>
internal readonly struct Square(ReadOnlyBuffer<int> source, ReadWriteBuffer<int> destination)
    : IComputeShader, IComputeShaderDescriptor<Square>
{
    private readonly ReadOnlyBuffer<int> _source = source;
    private readonly ReadWriteBuffer<int> _destination = destination;

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "Square_Execute";

    public static string MslSource => Msl.Header + """
        kernel void Square_Execute(
            const device int* source [[buffer(0)]],
            device int* destination [[buffer(1)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            int i = int(__tid.x);
            destination[i] = source[i] * source[i];
        }
        """;

    public static int ThreadGroupSizeX => 64;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in Square shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._source);
        writer.SetBuffer(1, shader._destination);
    }
}

/// <summary><c>buffer[i] = buffer[i] * 2 + bias;</c> over <c>packed_float3</c> elements (12-byte stride).</summary>
internal readonly struct ScaleVectors(ReadWriteBuffer<Float3> buffer, Float3 bias)
    : IComputeShader, IComputeShaderDescriptor<ScaleVectors>
{
    private readonly ReadWriteBuffer<Float3> _buffer = buffer;
    private readonly Float3 _bias = bias;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Constants
    {
        public Float3 Bias;
    }

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "ScaleVectors_Execute";

    public static string MslSource => Msl.Header + """
        struct __Constants
        {
            packed_float3 bias;
        };
        static_assert(sizeof(__Constants) == 12, "__Constants size");
        static_assert(sizeof(packed_float3) == 12, "packed_float3 size");

        kernel void ScaleVectors_Execute(
            device packed_float3* buffer [[buffer(0)]],
            constant __Constants& __c [[buffer(1)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            int i = int(__tid.x);
            float3 value = buffer[i];
            buffer[i] = value * 2.0f + __c.bias;
        }
        """;

    public static int ThreadGroupSizeX => 64;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in ScaleVectors shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._buffer);
        writer.SetBytes(1, new Constants { Bias = shader._bias });
    }
}

/// <summary>A threadgroup size supplied as a type argument, so one kernel can be compiled with several.</summary>
internal interface IThreadGroupSize
{
    static abstract int X { get; }

    static abstract int Y { get; }

    static abstract int Z { get; }
}

/// <summary>What a descriptor reports when the shader has no <c>[ThreadGroupSize]</c>: the runtime picks.</summary>
internal readonly struct RuntimePicked : IThreadGroupSize
{
    public static int X => 0;

    public static int Y => 0;

    public static int Z => 0;
}

internal readonly struct Size64x1x1 : IThreadGroupSize
{
    public static int X => 64;

    public static int Y => 1;

    public static int Z => 1;
}

internal readonly struct Size4x2x3 : IThreadGroupSize
{
    public static int X => 4;

    public static int Y => 2;

    public static int Z => 3;
}

/// <summary>2048 threads per group: more than any Apple GPU's <c>maxTotalThreadsPerThreadgroup</c> (1024).</summary>
internal readonly struct Size2048x1x1 : IThreadGroupSize
{
    public static int X => 2048;

    public static int Y => 1;

    public static int Z => 1;
}

/// <summary>Malformed: only "all zero" means runtime-picked.</summary>
internal readonly struct Size0x4x1 : IThreadGroupSize
{
    public static int X => 0;

    public static int Y => 4;

    public static int Z => 1;
}

/// <summary>
/// Records each thread's <c>GroupIds.XYZ</c> (position inside its threadgroup) as three uints per
/// thread. The largest value seen per axis is the threadgroup size minus one, which makes the size
/// the runtime actually dispatched with observable.
/// </summary>
internal readonly struct RecordGroupIds<TSize>(ReadWriteBuffer<uint> buffer)
    : IComputeShader, IComputeShaderDescriptor<RecordGroupIds<TSize>>
    where TSize : struct, IThreadGroupSize
{
    private readonly ReadWriteBuffer<uint> _buffer = buffer;

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "RecordGroupIds_Execute";

    public static string MslSource => Msl.Header + """
        kernel void RecordGroupIds_Execute(
            device uint* buffer [[buffer(0)]],
            uint3 __tid [[thread_position_in_grid]],
            uint3 __grid [[threads_per_grid]],
            uint3 __gtid [[thread_position_in_threadgroup]])
        {
            uint index = ((__tid.z * __grid.y + __tid.y) * __grid.x + __tid.x) * 3;
            buffer[index + 0] = __gtid.x;
            buffer[index + 1] = __gtid.y;
            buffer[index + 2] = __gtid.z;
        }
        """;

    public static int ThreadGroupSizeX => TSize.X;

    public static int ThreadGroupSizeY => TSize.Y;

    public static int ThreadGroupSizeZ => TSize.Z;

    public static void SetArguments(in RecordGroupIds<TSize> shader, ref ShaderArgumentWriter writer) =>
        writer.SetBuffer(0, shader._buffer);
}

/// <summary>8196 bytes of constants: over the 4 KB <c>setBytes</c> limit, so the runtime must use a temporary buffer.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal unsafe struct LargeConstants
{
    public const int ValueCount = 2048;

    public fixed float Values[ValueCount];
    public uint Bias;
}

/// <summary><c>buffer[i] = values[i] + bias;</c> where <c>values</c> is a 2048-float constant array.</summary>
internal readonly struct ReadLargeConstants(ReadWriteBuffer<float> buffer, LargeConstants constants)
    : IComputeShader, IComputeShaderDescriptor<ReadLargeConstants>
{
    private readonly ReadWriteBuffer<float> _buffer = buffer;
    private readonly LargeConstants _constants = constants;

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "ReadLargeConstants_Execute";

    public static string MslSource => Msl.Header + """
        struct __Constants
        {
            float values[2048];
            uint bias;
        };
        static_assert(sizeof(__Constants) == 8196, "__Constants size");
        static_assert(__builtin_offsetof(__Constants, bias) == 8192, "__Constants.bias offset");

        kernel void ReadLargeConstants_Execute(
            device float* buffer [[buffer(0)]],
            constant __Constants& __c [[buffer(1)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            int i = int(__tid.x);
            buffer[i] = __c.values[i] + float(__c.bias);
        }
        """;

    public static int ThreadGroupSizeX => 64;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in ReadLargeConstants shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._buffer);
        writer.SetBytes(1, in shader._constants);
    }
}

/// <summary>
/// Classifies each input with tests that only hold under IEEE semantics:
/// bit 0 = <c>x != x</c>, bit 1 = <c>isnan(x)</c>, bit 2 = <c>(x - x) != (x - x)</c>.
/// Fast math lets the compiler assume no NaN/Inf and fold all three to false.
/// </summary>
internal readonly struct ClassifyNaN(ReadOnlyBuffer<float> input, ReadWriteBuffer<uint> output)
    : IComputeShader, IComputeShaderDescriptor<ClassifyNaN>
{
    private readonly ReadOnlyBuffer<float> _input = input;
    private readonly ReadWriteBuffer<uint> _output = output;

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "ClassifyNaN_Execute";

    public static string MslSource => Msl.Header + """
        kernel void ClassifyNaN_Execute(
            const device float* input [[buffer(0)]],
            device uint* output [[buffer(1)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            int i = int(__tid.x);
            float x = input[i];
            float difference = x - x;
            uint flags = 0;
            if (x != x) { flags |= 1u; }
            if (metal::isnan(x)) { flags |= 2u; }
            if (difference != difference) { flags |= 4u; }
            output[i] = flags;
        }
        """;

    public static int ThreadGroupSizeX => 64;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in ClassifyNaN shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._input);
        writer.SetBuffer(1, shader._output);
    }
}

/// <summary><c>output[i] = input[i] + amount;</c> — used only by the concurrency tests, so its first compile is raced.</summary>
internal readonly struct AddAmount(ReadOnlyBuffer<int> input, ReadWriteBuffer<int> output, int amount)
    : IComputeShader, IComputeShaderDescriptor<AddAmount>
{
    private readonly ReadOnlyBuffer<int> _input = input;
    private readonly ReadWriteBuffer<int> _output = output;
    private readonly int _amount = amount;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Constants
    {
        public int Amount;
    }

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "AddAmount_Execute";

    public static string MslSource => Msl.Header + """
        struct __Constants
        {
            int amount;
        };
        static_assert(sizeof(__Constants) == 4, "__Constants size");

        kernel void AddAmount_Execute(
            const device int* input [[buffer(0)]],
            device int* output [[buffer(1)]],
            constant __Constants& __c [[buffer(2)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            int i = int(__tid.x);
            output[i] = input[i] + __c.amount;
        }
        """;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in AddAmount shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._input);
        writer.SetBuffer(1, shader._output);
        writer.SetBytes(2, new Constants { Amount = shader._amount });
    }
}

/// <summary>MSL that does not compile.</summary>
internal readonly struct BrokenSyntax : IComputeShader, IComputeShaderDescriptor<BrokenSyntax>
{
    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "BrokenSyntax_Execute";

    public static string MslSource => Msl.Header + """
        kernel void BrokenSyntax_Execute(uint3 __tid [[thread_position_in_grid]])
        {
            float value = undeclared_identifier_xyz * 2.0f;
        }
        """;

    public static int ThreadGroupSizeX => 64;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in BrokenSyntax shader, ref ShaderArgumentWriter writer)
    {
    }
}

/// <summary>A failed drift guard: the MSL struct is 8 bytes but asserts the 4 its C# mirror would have.</summary>
internal readonly struct BrokenLayoutGuard : IComputeShader, IComputeShaderDescriptor<BrokenLayoutGuard>
{
    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "BrokenLayoutGuard_Execute";

    public static string MslSource => Msl.Header + """
        struct __Constants
        {
            float a;
            float b;
        };
        static_assert(sizeof(__Constants) == 4, "__Constants size drifted from the C# mirror");

        kernel void BrokenLayoutGuard_Execute(
            constant __Constants& __c [[buffer(0)]],
            uint3 __tid [[thread_position_in_grid]])
        {
        }
        """;

    public static int ThreadGroupSizeX => 64;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in BrokenLayoutGuard shader, ref ShaderArgumentWriter writer)
    {
    }
}

/// <summary>Valid MSL whose kernel has a different name than <see cref="EntryPoint"/> reports.</summary>
internal readonly struct MissingEntryPoint : IComputeShader, IComputeShaderDescriptor<MissingEntryPoint>
{
    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "MissingEntryPoint_Execute";

    public static string MslSource => Msl.Header + """
        kernel void SomeOtherName(uint3 __tid [[thread_position_in_grid]])
        {
        }
        """;

    public static int ThreadGroupSizeX => 64;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in MissingEntryPoint shader, ref ShaderArgumentWriter writer)
    {
    }
}

/// <summary>A descriptor that hands the writer whatever index it is told to, for argument validation tests.</summary>
internal readonly struct BindAtIndex(ReadWriteBuffer<float> buffer, int bufferIndex, int bytesIndex)
    : IComputeShader, IComputeShaderDescriptor<BindAtIndex>
{
    private readonly ReadWriteBuffer<float> _buffer = buffer;
    private readonly int _bufferIndex = bufferIndex;
    private readonly int _bytesIndex = bytesIndex;

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "BindAtIndex_Execute";

    public static string MslSource => Msl.Header + """
        kernel void BindAtIndex_Execute(uint3 __tid [[thread_position_in_grid]])
        {
        }
        """;

    public static int ThreadGroupSizeX => 64;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in BindAtIndex shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(shader._bufferIndex, shader._buffer);
        writer.SetBytes(shader._bytesIndex, 1.0f);
    }
}
