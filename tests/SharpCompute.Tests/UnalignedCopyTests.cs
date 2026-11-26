using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SharpCompute.Interop;
using SharpCompute.Tests.NativeInterop;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// <c>Copy</c>, <c>Fill</c> and <c>Clear</c> of byte ranges whose offsets or sizes are not multiples of
/// 4: buffers of bytes, of 16-bit elements and of structs of an odd size. Metal documents its buffer
/// blits to need multiples of 4 on macOS, so those ranges go to a blit only for their aligned middle
/// and to a compute kernel for the rest.
/// </summary>
public unsafe class UnalignedCopyTests
{
    private const byte Untouched = 0xEE;

    // Around a multiple of 4, around the kernel's chunk (ByteKernel.Chunk), and several chunks.
    private static readonly int[] Sizes = [1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 13, 63, 64, 65, 67, 127, 129, 255, 256, 257, 511, 1021];

    private static byte[] Pattern(int length) => Enumerable.Range(0, length).Select(i => (byte)((i * 7) + 1)).ToArray();

    [MetalFact]
    public void Copy_OfBytes_AtEveryAlignmentOfSourceAndDestination()
    {
        // Every pair of offsets modulo 4 (and past a multiple of 4), with sizes that leave 0 to 3
        // bytes at each end, fit inside one chunk of the kernel, or cross chunks.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        byte[] data = Pattern(1100);
        using ReadOnlyBuffer<byte> source = device.AllocateReadOnlyBuffer<byte>(data);
        using ReadWriteBuffer<byte> destination = device.AllocateReadWriteBuffer<byte>(1100);
        byte[] expected = new byte[1100];

        for (int sourceOffset = 0; sourceOffset < 6; sourceOffset++)
        {
            for (int destinationOffset = 0; destinationOffset < 6; destinationOffset++)
            {
                foreach (int size in Sizes)
                {
                    destination.AsSpan().Fill(Untouched);
                    expected.AsSpan().Fill(Untouched);
                    data.AsSpan(sourceOffset, size).CopyTo(expected.AsSpan(destinationOffset));

                    device.Copy(source, sourceOffset, destination, destinationOffset, size);

                    Assert.True(
                        expected.AsSpan().SequenceEqual(destination.AsReadOnlySpan()),
                        $"Copy of {size} bytes from {sourceOffset} to {destinationOffset}: first difference at {FirstDifference(expected, destination.AsReadOnlySpan())}.");
                }
            }
        }

        Assert.Equal(data, source.ToArray());
    }

    [MetalFact]
    public void Copy_OfShorts_AtOddAndEvenIndices()
    {
        // 2-byte elements: an odd index is a byte offset that is 2 past a multiple of 4. Odd to even
        // (and back) is the case with no aligned middle at all.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        short[] data = Enumerable.Range(0, 3000).Select(i => (short)((i * 31) - 5000)).ToArray();
        using ReadWriteBuffer<short> source = device.AllocateReadWriteBuffer<short>(data);
        using ReadWriteBuffer<short> destination = device.AllocateReadWriteBuffer<short>(3000);
        short[] expected = new short[3000];

        for (int sourceOffset = 0; sourceOffset < 4; sourceOffset++)
        {
            for (int destinationOffset = 0; destinationOffset < 4; destinationOffset++)
            {
                foreach (int count in new[] { 1, 2, 3, 4, 5, 31, 32, 33, 127, 128, 129, 2001 })
                {
                    destination.AsSpan().Fill(-1);
                    expected.AsSpan().Fill(-1);
                    data.AsSpan(sourceOffset, count).CopyTo(expected.AsSpan(destinationOffset));

                    device.Copy(source, sourceOffset, destination, destinationOffset, count);

                    Assert.True(
                        expected.AsSpan().SequenceEqual(destination.AsReadOnlySpan()),
                        $"Copy of {count} shorts from {sourceOffset} to {destinationOffset} differs.");
                }
            }
        }
    }

    [MetalTheory]
    [InlineData(1, 2, 1_000_001)] // offsets that differ modulo 4: the kernel copies all of it
    [InlineData(3, 7, 999_999)] // offsets that agree modulo 4: one byte, a blit, two bytes
    [InlineData(0, 0, 1_048_573)] // aligned offsets, an odd size: a blit and one byte
    [InlineData(2, 0, 65_536)]
    public void Copy_OfALargeRange_CopiesEveryByte(int sourceOffset, int destinationOffset, int size)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Length = (1 << 20) + 16;
        byte[] data = new byte[Length];
        new Random(size).NextBytes(data);
        using ReadWriteBuffer<byte> source = device.AllocateReadWriteBuffer<byte>(data);
        using ReadWriteBuffer<byte> destination = device.AllocateReadWriteBuffer<byte>(Length);
        destination.AsSpan().Fill(Untouched);

        device.Copy(source, sourceOffset, destination, destinationOffset, size);

        byte[] expected = new byte[Length];
        expected.AsSpan().Fill(Untouched);
        data.AsSpan(sourceOffset, size).CopyTo(expected.AsSpan(destinationOffset));
        Assert.True(expected.AsSpan().SequenceEqual(destination.AsReadOnlySpan()), $"First difference at {FirstDifference(expected, destination.AsReadOnlySpan())}.");
    }

    [MetalFact]
    public void Copy_OfAWholeBuffer_WhoseByteLengthIsNotAMultipleOfFour()
    {
        // The MTLBuffer itself is 1021 bytes: there is no room to round the copy up.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        byte[] data = Pattern(1021);
        using ReadWriteBuffer<byte> source = device.AllocateReadWriteBuffer<byte>(data);
        using ReadWriteBuffer<byte> destination = device.AllocateReadWriteBuffer<byte>(1021);

        device.Copy(source, destination);
        Assert.Equal(data, destination.ToArray());

        device.Clear(destination);
        Assert.All(destination.ToArray(), value => Assert.Equal(0, value));

        // Views: the last three bytes to the first three, and a 5-byte view filled.
        device.Copy(source.Slice(1018), destination.Slice(0, 3));
        device.Fill(destination.Slice(1016), 0x5A);

        byte[] expected = new byte[1021];
        data.AsSpan(1018).CopyTo(expected);
        expected.AsSpan(1016).Fill(0x5A);
        Assert.Equal(expected, destination.ToArray());
    }

    [MetalFact]
    public void Copy_WithinOneBuffer_AtOddOffsets()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        byte[] data = Pattern(600);
        using ReadWriteBuffer<byte> buffer = device.AllocateReadWriteBuffer<byte>(data);

        // Disjoint ranges of one buffer, touching: [1, 300) -> [300, 599).
        device.Copy(buffer, 1, buffer, 300, 299);

        byte[] expected = (byte[])data.Clone();
        data.AsSpan(1, 299).CopyTo(expected.AsSpan(300));
        Assert.Equal(expected, buffer.ToArray());

        // Overlapping ranges are refused as before, whatever their alignment.
        Assert.Throws<ArgumentException>(() => device.Copy(buffer, 1, buffer, 3, 5));
        Assert.Equal(expected, buffer.ToArray());
    }

    [MetalFact]
    public void Fill_AtEveryAlignment_SetsExactlyThatRange()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<byte> buffer = device.AllocateReadWriteBuffer<byte>(1100);
        byte[] expected = new byte[1100];

        for (int offset = 0; offset < 9; offset++)
        {
            foreach (int size in Sizes)
            {
                buffer.AsSpan().Fill(Untouched);
                expected.AsSpan().Fill(Untouched);
                expected.AsSpan(offset, size).Fill(0x3C);

                device.Fill(buffer.Slice(offset, size), 0x3C);

                Assert.True(
                    expected.AsSpan().SequenceEqual(buffer.AsReadOnlySpan()),
                    $"Fill of {size} bytes at {offset}: first difference at {FirstDifference(expected, buffer.AsReadOnlySpan())}.");

                device.Clear(buffer.Slice(offset, size));
                expected.AsSpan(offset, size).Clear();
                Assert.True(expected.AsSpan().SequenceEqual(buffer.AsReadOnlySpan()), $"Clear of {size} bytes at {offset} differs.");
            }
        }
    }

    [MetalFact]
    public void Fill_OfShortsAndHalves_AtOddIndices()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<ushort> shorts = device.AllocateReadWriteBuffer<ushort>(Enumerable.Repeat((ushort)0x1234, 1001).ToArray());
        using ReadWriteBuffer<Half> halves = device.AllocateReadWriteBuffer<Half>(Enumerable.Repeat((Half)2f, 9).ToArray());

        device.Fill(shorts.Slice(1, 999), 0xFF);
        device.Clear(halves.Slice(3, 5));

        ushort[] expected = Enumerable.Repeat((ushort)0xFFFF, 1001).ToArray();
        expected[0] = expected[1000] = 0x1234;
        Assert.Equal(expected, shorts.ToArray());
        Assert.Equal(new[] { 2f, 2f, 2f, 0f, 0f, 0f, 0f, 0f, 2f }, halves.ToArray().Select(h => (float)h));
    }

    /// <summary>Three bytes: no offset but every fourth element is a multiple of 4.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Rgb24(byte r, byte g, byte b)
    {
        public byte R = r;
        public byte G = g;
        public byte B = b;
    }

    [MetalFact]
    public void CopyAndFill_OfThreeByteElements()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Assert.Equal(3, sizeof(Rgb24));
        Rgb24[] data = Enumerable.Range(0, 500).Select(i => new Rgb24((byte)i, (byte)(i >> 1), (byte)(i * 3))).ToArray();
        using ReadWriteBuffer<Rgb24> source = device.AllocateReadWriteBuffer<Rgb24>(data);
        using ReadWriteBuffer<Rgb24> destination = device.AllocateReadWriteBuffer<Rgb24>(500);

        for (int sourceOffset = 0; sourceOffset < 5; sourceOffset++)
        {
            for (int destinationOffset = 0; destinationOffset < 5; destinationOffset++)
            {
                foreach (int count in new[] { 1, 2, 3, 4, 5, 171, 400 })
                {
                    device.Clear(destination);
                    device.Copy(source, sourceOffset, destination, destinationOffset, count);

                    Rgb24[] expected = new Rgb24[500];
                    data.AsSpan(sourceOffset, count).CopyTo(expected.AsSpan(destinationOffset));
                    Assert.True(
                        MemoryMarshal.AsBytes(expected.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(destination.AsReadOnlySpan())),
                        $"Copy of {count} three-byte elements from {sourceOffset} to {destinationOffset} differs.");
                }
            }
        }

        // The last copy put elements 4..403 at 4..403; a fill of elements 1 and 2 leaves 0 and 3 alone.
        device.Fill(destination.Slice(1, 2), 9);

        Rgb24[] result = destination.ToArray();
        Assert.Equal(default, result[0]);
        Assert.Equal(new Rgb24(9, 9, 9), result[1]);
        Assert.Equal(new Rgb24(9, 9, 9), result[2]);
        Assert.Equal(default, result[3]);
        Assert.Equal(data[4], result[4]);
        Assert.Equal(data[403], result[403]);
        Assert.Equal(default, result[404]);
    }

    [MetalFact]
    public void InAContext_UnalignedCopiesAndFills_RunInRecordedOrder_AsOneOperationEach()
    {
        // image -> invert (dispatch) -> copy an odd range out -> fill part of the image -> invert again.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        byte[] data = Pattern(999);
        using ReadWriteBuffer<byte> image = device.AllocateReadWriteBuffer<byte>(data);
        using ReadWriteBuffer<byte> snapshot = device.AllocateReadWriteBuffer<byte>(999);
        using ComputeContext context = device.CreateComputeContext();

        context.For(999, new InvertBytes(image, 16));
        context.Copy(image, 1, snapshot, 2, 995);
        context.Fill(image.Slice(5, 990), 0xFF);
        context.For(999, new InvertBytes(image, 16));
        context.Copy(image, 0, snapshot, 997, 2);
        context.Clear(snapshot.Slice(0, 1));

        // A copy or fill is one pending operation, however many blits and kernels it takes.
        Assert.Equal(6, context.PendingCount);
        context.Execute();
        Assert.Equal(0, context.PendingCount);

        byte[] inverted = data.Select(value => (byte)(255 - value)).ToArray();
        byte[] expectedSnapshot = new byte[999];
        inverted.AsSpan(1, 995).CopyTo(expectedSnapshot.AsSpan(2));

        byte[] expectedImage = (byte[])inverted.Clone();
        expectedImage.AsSpan(5, 990).Fill(0xFF);
        expectedImage = expectedImage.Select(value => (byte)(255 - value)).ToArray();
        expectedImage.AsSpan(0, 2).CopyTo(expectedSnapshot.AsSpan(997));

        Assert.Equal(expectedImage, image.ToArray());
        Assert.Equal(expectedSnapshot, snapshot.ToArray());

        // The context records again afterwards, with the kernel's pipeline and an ordinary one alternating.
        for (int i = 0; i < 3; i++)
        {
            context.Copy(image, 3, snapshot, 0, 7);
            context.For(999, new InvertBytes(image, 16));
            context.Fill(snapshot.Slice(9, 3), 1);
        }

        context.Execute();
        Assert.Equal(new byte[] { 1, 1, 1 }, snapshot.ToArray()[9..12]);
    }

    [MetalFact]
    public void GpuOnlyBuffers_CopyAndFillAtOddOffsets()
    {
        // Private storage: the edges cannot be patched from the CPU, which is why a kernel does them.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlBuffer = NativeMetal.NewBuffer(device, 1024, NativeMetal.StoragePrivate);

        try
        {
            byte[] data = Pattern(1024);
            using ReadWriteBuffer<byte> hidden = device.WrapNativeReadWriteBuffer<byte>(mtlBuffer);
            using ReadWriteBuffer<byte> staging = device.AllocateReadWriteBuffer<byte>(data);
            using ReadWriteBuffer<byte> result = device.AllocateReadWriteBuffer<byte>(1024);
            Assert.False(hidden.IsCpuAccessible);

            device.Clear(hidden);
            device.Copy(staging, 3, hidden, 6, 1001);
            device.Fill(hidden.Slice(101, 7), 0x77);
            device.Copy(hidden, 1, hidden.Slice(1010), 0, 9);
            device.Copy(hidden, result);

            byte[] expected = new byte[1024];
            data.AsSpan(3, 1001).CopyTo(expected.AsSpan(6));
            expected.AsSpan(101, 7).Fill(0x77);
            expected.AsSpan(1, 9).CopyTo(expected.AsSpan(1010));
            Assert.Equal(expected, result.ToArray());
        }
        finally
        {
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void MappedFile_IsCopiedFromAtAnOddOffset()
    {
        // The source's pages are read-only to this process; the kernel only reads them.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        string path = Path.GetTempFileName();

        try
        {
            byte[] data = Pattern(5003);
            File.WriteAllBytes(path, data);

            using ReadOnlyBuffer<byte> mapped = device.MapReadOnlyBuffer<byte>(path);
            using ReadWriteBuffer<byte> destination = device.AllocateReadWriteBuffer<byte>(5003);

            device.Copy(mapped, 7, destination, 2, 4990);

            byte[] expected = new byte[5003];
            data.AsSpan(7, 4990).CopyTo(expected.AsSpan(2));
            Assert.Equal(expected, destination.ToArray());

            Assert.Throws<NotSupportedException>(() => device.Fill(mapped.Slice(1, 3), 0));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [MetalFact]
    public void OnlyRangesTheBlitCannotTake_UseTheKernels()
    {
        // A device of its own, so that what other tests compiled does not count.
        using GraphicsDevice device = GraphicsDevice.CreateStandInForAnotherDevice();
        using ReadWriteBuffer<byte> bytes = device.AllocateReadWriteBuffer<byte>(Pattern(64));
        using ReadWriteBuffer<byte> target = device.AllocateReadWriteBuffer<byte>(64);
        using ReadWriteBuffer<int> ints = device.AllocateReadWriteBuffer<int>(Enumerable.Range(0, 64).ToArray());
        using ReadWriteBuffer<int> intTarget = device.AllocateReadWriteBuffer<int>(64);
        using ReadWriteBuffer<short> shorts = device.AllocateReadWriteBuffer<short>(64);

        // Multiples of 4 bytes, of any element size: the blit alone.
        device.Copy(ints, 3, intTarget, 5, 17);
        device.Fill(intTarget.Slice(1, 9), 1);
        device.Copy(bytes, 4, target, 8, 12);
        device.Fill(target.Slice(16, 8), 2);
        device.Copy(shorts, 2, shorts, 10, 6);
        device.Clear(shorts.Slice(4, 2));

        Assert.False(device.IsPipelineCached<CopyBytesKernel>());
        Assert.False(device.IsPipelineCached<FillBytesKernel>());
        Assert.Equal(0, device.PipelineCompileCount);

        device.Copy(bytes, 4, target, 8, 13);
        Assert.True(device.IsPipelineCached<CopyBytesKernel>());
        Assert.False(device.IsPipelineCached<FillBytesKernel>());

        device.Fill(target.Slice(17, 8), 2);
        Assert.True(device.IsPipelineCached<FillBytesKernel>());

        // Compiled once each, however many unaligned ranges follow.
        device.Copy(shorts, 1, shorts, 20, 3);
        device.Clear(shorts.Slice(5, 1));
        Assert.Equal(2, device.PipelineCompileCount);
    }

    [MetalFact]
    public void AFailedUnalignedCopy_IsReportedAsACopy()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<byte> source = device.AllocateReadWriteBuffer<byte>(Pattern(64));
        using ReadWriteBuffer<byte> destination = device.AllocateReadWriteBuffer<byte>(64);
        using ComputeContext context = device.CreateComputeContext();

        // One byte, a blit and two bytes: still "a GPU copy or fill" to whoever reads the error.
        context.Copy(source, 3, destination, 7, 27);
        context.SimulateGpuError("Injected");
        Assert.Contains("a GPU copy or fill:", Assert.Throws<MetalException>(context.Execute).Message);

        context.Copy(source, 3, destination, 7, 27);
        context.Fill(destination.Slice(41, 5), 1);
        context.SimulateGpuError("Injected");
        Assert.Contains("a batch of 2 GPU copies and fills:", Assert.Throws<MetalException>(context.Execute).Message);
    }

    private static int FirstDifference(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) => expected.CommonPrefixLength(actual);
}
