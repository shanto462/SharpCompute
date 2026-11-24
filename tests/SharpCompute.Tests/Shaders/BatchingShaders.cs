using System;
using System.Runtime.InteropServices;

namespace SharpCompute.Tests.Shaders;

// Hand-written descriptors for the ComputeContext / ForAsync tests, in the style of HandWrittenShaders.cs.

/// <summary><c>buffer[i] += 1;</c> Repeated in one batch, the result counts the dispatches that ran.</summary>
internal readonly struct IncrementAll(ReadWriteBuffer<int> buffer)
    : IComputeShader, IComputeShaderDescriptor<IncrementAll>
{
    private readonly ReadWriteBuffer<int> _buffer = buffer;

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "IncrementAll_Execute";

    public static string MslSource => Msl.Header + """
        kernel void IncrementAll_Execute(
            device int* buffer [[buffer(0)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            buffer[int(__tid.x)] += 1;
        }
        """;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in IncrementAll shader, ref ShaderArgumentWriter writer) =>
        writer.SetBuffer(0, shader._buffer);
}

/// <summary>
/// <c>destination[i] = source[length - 1 - i] + 1;</c> Every thread reads an element another
/// threadgroup of the previous dispatch wrote, so a chain of these only adds up if each dispatch
/// sees all writes of the one before it.
/// </summary>
internal readonly struct ReverseAddOne(ReadWriteBuffer<int> source, ReadWriteBuffer<int> destination)
    : IComputeShader, IComputeShaderDescriptor<ReverseAddOne>
{
    private readonly ReadWriteBuffer<int> _source = source;
    private readonly ReadWriteBuffer<int> _destination = destination;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Constants
    {
        public int Length;
    }

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "ReverseAddOne_Execute";

    public static string MslSource => Msl.Header + """
        struct __Constants
        {
            int length;
        };
        static_assert(sizeof(__Constants) == 4, "__Constants size");

        kernel void ReverseAddOne_Execute(
            device int* source [[buffer(0)]],
            device int* destination [[buffer(1)]],
            constant __Constants& __c [[buffer(2)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            int i = int(__tid.x);
            destination[i] = source[__c.length - 1 - i] + 1;
        }
        """;

    public static int ThreadGroupSizeX => 64;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in ReverseAddOne shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._source);
        writer.SetBuffer(1, shader._destination);
        writer.SetBytes(2, new Constants { Length = shader._source.Length });
    }
}

/// <summary>
/// Writes gigabytes past the end of <c>buffer</c>, outside any mapping, so the GPU raises an address
/// fault and Metal marks the command buffer as failed. Used only on a private device.
/// </summary>
internal readonly struct FaultingWrite(ReadWriteBuffer<uint> buffer)
    : IComputeShader, IComputeShaderDescriptor<FaultingWrite>
{
    private readonly ReadWriteBuffer<uint> _buffer = buffer;

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "FaultingWrite_Execute";

    public static string MslSource => Msl.Header + """
        kernel void FaultingWrite_Execute(
            device uint* buffer [[buffer(0)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            ulong offset = 0x100000000ul + ulong(__tid.x) * 0x1000000ul;
            buffer[offset] = __tid.x;
        }
        """;

    public static int ThreadGroupSizeX => 64;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in FaultingWrite shader, ref ShaderArgumentWriter writer) =>
        writer.SetBuffer(0, shader._buffer);
}

/// <summary>
/// <see cref="IncrementAll"/>, except that binding its arguments signals <c>entered</c> and then
/// waits for <c>release</c>, which holds a recording call open for as long as a test needs.
/// </summary>
internal readonly struct BlockingIncrement(ReadWriteBuffer<int> buffer, System.Threading.ManualResetEventSlim entered, System.Threading.ManualResetEventSlim release)
    : IComputeShader, IComputeShaderDescriptor<BlockingIncrement>
{
    private readonly ReadWriteBuffer<int> _buffer = buffer;
    private readonly System.Threading.ManualResetEventSlim _entered = entered;
    private readonly System.Threading.ManualResetEventSlim _release = release;

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => IncrementAll.EntryPoint;

    public static string MslSource => IncrementAll.MslSource;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in BlockingIncrement shader, ref ShaderArgumentWriter writer)
    {
        shader._entered.Set();
        shader._release.Wait(TimeSpan.FromSeconds(30));
        writer.SetBuffer(0, shader._buffer);
    }
}
