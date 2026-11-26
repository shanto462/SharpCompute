using System;
using System.Runtime.InteropServices;

namespace SharpCompute;

// The kernels behind Copy and Fill for byte ranges a blit cannot be given.
//
// Metal's buffer blits (copyFromBuffer:sourceOffset:toBuffer:destinationOffset:size: and
// fillBuffer:range:value:) are documented to need byte offsets and sizes that are multiples of 4 on
// macOS. A buffer of bytes, of 16-bit elements or of a struct whose size is not a multiple of 4 has
// ranges that are not. Apple silicon was measured to accept them anyway, but nothing promises it, so
// those bytes are moved by a kernel instead: binding a buffer of bytes at any offset, and reading and
// writing single bytes, are both within what Metal documents. CommandBatch decides which bytes go
// where (RecordCopy, RecordFill) and records the kernels' bindings itself; like
// DispatchArgumentsKernel they are hand-written, because the runtime has no generator.

/// <summary>
/// Copies <c>count</c> bytes from one buffer to another, both bound at the byte the copy starts at.
/// Every thread copies one chunk, so the grid is <c>ceil(count / chunk)</c> threads wide.
/// </summary>
/// <remarks>
/// The two ranges never overlap (<c>Copy</c> refuses that), also when they are in one buffer, which
/// is what lets the kernel take them as two arguments: Metal compiles a kernel as if its buffer
/// arguments were different memory.
/// </remarks>
internal readonly struct CopyBytesKernel : IComputeShaderDescriptor<CopyBytesKernel>
{
    public static string EntryPoint => "sharpcompute_copy_bytes";

    public static string MslSource => ByteKernel.Source;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in CopyBytesKernel shader, ref ShaderArgumentWriter writer) => throw ByteKernel.NotDispatchable();
}

/// <summary>Sets <c>count</c> bytes of a buffer, bound at the first of them, to one value; chunked like <see cref="CopyBytesKernel"/>.</summary>
internal readonly struct FillBytesKernel : IComputeShaderDescriptor<FillBytesKernel>
{
    public static string EntryPoint => "sharpcompute_fill_bytes";

    public static string MslSource => ByteKernel.Source;

    public static int ThreadGroupSizeX => 0;

    public static int ThreadGroupSizeY => 0;

    public static int ThreadGroupSizeZ => 0;

    public static void SetArguments(in FillBytesKernel shader, ref ShaderArgumentWriter writer) => throw ByteKernel.NotDispatchable();
}

/// <summary>What <see cref="CopyBytesKernel"/> and <see cref="FillBytesKernel"/> share: the source, the chunk size and the constants.</summary>
internal static class ByteKernel
{
    /// <summary>
    /// The bytes one thread handles. Of 64, 256, 1024 and 4096, 64 was the fastest or within noise
    /// of it for ranges the kernel copies whole (1 MB, 16 MB and 64 MB on an Apple M4 Pro): 1.2 ms
    /// for 16 MB, against 0.4 ms for a blit of the same bytes. The few bytes of an unaligned edge are
    /// one thread whatever the chunk.
    /// </summary>
    internal const uint Chunk = 64;

    // The count is 64-bit: a view of a large buffer can cover more than 4 GB.
    internal const string Source = """
        #include <metal_stdlib>

        using namespace metal;

        struct Parameters
        {
            ulong count;
            uint chunk;
            uint value;
        };

        kernel void sharpcompute_copy_bytes(
            const device uchar* source [[buffer(0)]],
            device uchar* destination [[buffer(1)]],
            constant Parameters& parameters [[buffer(2)]],
            uint position [[thread_position_in_grid]])
        {
            ulong begin = ulong(position) * ulong(parameters.chunk);
            ulong end = begin + ulong(parameters.chunk);
            end = end < parameters.count ? end : parameters.count;

            for (ulong i = begin; i < end; i++)
            {
                destination[i] = source[i];
            }
        }

        kernel void sharpcompute_fill_bytes(
            device uchar* destination [[buffer(0)]],
            constant Parameters& parameters [[buffer(1)]],
            uint position [[thread_position_in_grid]])
        {
            ulong begin = ulong(position) * ulong(parameters.chunk);
            ulong end = begin + ulong(parameters.chunk);
            end = end < parameters.count ? end : parameters.count;

            for (ulong i = begin; i < end; i++)
            {
                destination[i] = uchar(parameters.value);
            }
        }
        """;

    internal static NotSupportedException NotDispatchable() =>
        new("This kernel is recorded by Copy and Fill, which bind its arguments themselves; it cannot be dispatched as a shader.");

    /// <summary>The C# mirror of the MSL <c>Parameters</c>: 16 bytes, the 64-bit count first.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct Parameters(ulong count, uint chunk, uint value)
    {
        public readonly ulong Count = count;
        public readonly uint Chunk = chunk;
        public readonly uint Value = value;
    }
}
