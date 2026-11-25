using System;
using System.Runtime.InteropServices;

namespace SharpCompute.Tests.Shaders;

/// <summary>
/// A kernel whose GPU time is set by an argument: every thread runs <c>rounds</c> rounds of an integer
/// mix on its element. The count is a run-time constant, so the compiler cannot fold the loop away,
/// and each round depends on the one before it.
/// </summary>
internal readonly struct MixRounds(ReadWriteBuffer<uint> buffer, int rounds)
    : IComputeShader, IComputeShaderDescriptor<MixRounds>
{
    private readonly ReadWriteBuffer<uint> _buffer = buffer;
    private readonly int _rounds = rounds;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Constants
    {
        public int Rounds;
    }

    public void Execute() => throw new NotSupportedException();

    public static string EntryPoint => "MixRounds_Execute";

    public static string MslSource => Msl.Header + """
        kernel void MixRounds_Execute(
            device uint* buffer [[buffer(0)]],
            constant int& rounds [[buffer(1)]],
            uint3 __tid [[thread_position_in_grid]])
        {
            uint h = buffer[__tid.x] + __tid.x;

            for (int r = 0; r < rounds; r++)
            {
                h ^= h >> 16;
                h *= 0x85ebca6bu;
                h ^= h >> 13;
                h += uint(r);
            }

            buffer[__tid.x] = h;
        }
        """;

    public static int ThreadGroupSizeX => 256;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in MixRounds shader, ref ShaderArgumentWriter writer)
    {
        writer.SetBuffer(0, shader._buffer);
        writer.SetBytes(1, new Constants { Rounds = shader._rounds });
    }

    /// <summary>What the kernel leaves in element <paramref name="index"/> that held <paramref name="value"/>.</summary>
    public static uint Expected(uint value, int index, int rounds)
    {
        uint h = value + (uint)index;

        for (int r = 0; r < rounds; r++)
        {
            h ^= h >> 16;
            h *= 0x85ebca6bu;
            h ^= h >> 13;
            h += (uint)r;
        }

        return h;
    }
}
