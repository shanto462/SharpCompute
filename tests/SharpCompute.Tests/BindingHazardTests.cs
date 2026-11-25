using System;
using System.Linq;
using System.Runtime.InteropServices;
using SharpCompute.Interop;
using SharpCompute.Tests.NativeInterop;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// Bindings of one dispatch that reach the same memory: refused with an <see cref="ArgumentException"/>
/// when the shader writes through one of them (on the GPU the reads through the other would be stale),
/// on every dispatch path, and accepted when it only reads them or they do not overlap.
/// </summary>
public unsafe class BindingHazardTests
{
    private const int Length = 64;

    private static float[] Ramp(int length = Length, float scale = 1f) => Enumerable.Range(0, length).Select(i => (i + 1) * scale).ToArray();

    /// <summary>Asserts that every way of dispatching <paramref name="shader"/> refuses it, naming the two bindings.</summary>
    private static void AssertRefusedEverywhere<T>(GraphicsDevice device, in T shader, string parameter, params string[] messageParts)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T>
    {
        T copy = shader;

        void Check(string path, Action dispatch)
        {
            ArgumentException error = Assert.Throws<ArgumentException>(dispatch);
            Assert.True(parameter == error.ParamName, $"{path}: parameter '{error.ParamName}'.");

            foreach (string part in messageParts)
            {
                Assert.True(error.Message.Contains(part, StringComparison.Ordinal), $"{path}: '{part}' is not in: {error.Message}");
            }
        }

        Check("For", () => device.For(Length, copy));
        Check("ForCpu", () => device.ForCpu(Length, copy));
        Check("ForSplit", () => device.ForSplit(Length, copy, 0.5));
        Check("ForAsync", () => device.ForAsync(Length, copy).GetAwaiter().GetResult());

        using ComputeContext context = device.CreateComputeContext();
        Check("ComputeContext.For", () => context.For(Length, copy));

        // Nothing of the refused dispatch stays recorded.
        Assert.Equal(0, context.PendingCount);
        context.Execute();
    }

    // ---- buffers ----------------------------------------------------------------------------------

    [MetalFact]
    public void TheSameBuffer_ReadThroughTwoCaptures_IsAllowed_OnEveryPath()
    {
        // Neither capture is written, so it does not matter that they are one buffer: x + x.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        float[] expected = Ramp(scale: 2f);
        using ReadWriteBuffer<float> x = device.AllocateReadWriteBuffer<float>(Ramp());
        using ReadWriteBuffer<float> sum = device.AllocateReadWriteBuffer<float>(Length);
        AddReadWrite shader = new(x, x, sum);

        device.For(Length, shader);
        Assert.Equal(expected, sum.ToArray());

        sum.AsSpan().Clear();
        device.ForCpu(Length, shader);
        Assert.Equal(expected, sum.ToArray());

        sum.AsSpan().Clear();
        device.ForSplit(Length, shader, 0.5);
        Assert.Equal(expected, sum.ToArray());

        sum.AsSpan().Clear();
        device.ForAsync(Length, shader).GetAwaiter().GetResult();
        Assert.Equal(expected, sum.ToArray());

        sum.AsSpan().Clear();

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.For(Length, shader);
        }

        Assert.Equal(expected, sum.ToArray());
    }

    [MetalFact]
    public void TheSameBuffer_WrittenThroughOneCaptureAndReadThroughAnother_IsRefused_OnEveryPath()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        float[] data = Ramp();
        using ReadWriteBuffer<float> x = device.AllocateReadWriteBuffer<float>(data);
        using ReadWriteBuffer<float> y = device.AllocateReadWriteBuffer<float>(data);

        // sum (index 2) is x, which is also a (index 0): the later binding reports the earlier one.
        AssertRefusedEverywhere(device, new AddReadWrite(x, y, x), "buffer", "The buffer bound at index 2 overlaps the buffer bound at index 0", "the shader writes one of them");
        AssertRefusedEverywhere(device, new AddReadWrite(y, x, x), "buffer", "index 2 overlaps the buffer bound at index 1");

        // Nothing ran, on the GPU or the CPU.
        Assert.Equal(data, x.ToArray());
        Assert.Equal(data, y.ToArray());

        // The buffers are still usable (the refused dispatches left no lease behind), and the same
        // shader with a buffer of its own for the sum runs.
        using ReadWriteBuffer<float> sum = device.AllocateReadWriteBuffer<float>(Length);
        device.For(Length, new AddReadWrite(x, y, sum));
        Assert.Equal(Ramp(scale: 2f), sum.ToArray());
    }

    [MetalTheory]

    // (start, length) of the view that is read and of the view that is written, in one buffer of 64.
    [InlineData(0, 32, 32, 32, false)] // touching halves
    [InlineData(0, 16, 48, 16, false)]
    [InlineData(40, 8, 0, 40, false)]
    [InlineData(0, 33, 32, 32, true)] // one element in common
    [InlineData(0, 32, 31, 32, true)]
    [InlineData(0, 64, 20, 4, true)] // one inside the other
    [InlineData(20, 4, 0, 64, true)]
    [InlineData(10, 20, 10, 20, true)] // the same range, as two views
    [InlineData(63, 1, 63, 1, true)]
    [InlineData(10, 0, 0, 64, false)] // an empty view covers nothing
    [InlineData(0, 64, 30, 0, false)]
    [InlineData(64, 0, 0, 64, false)]
    public void OverlappingViews_AreRefusedWhenOneIsWritten_AndDisjointOnesAreNot(int readStart, int readLength, int writtenStart, int writtenLength, bool overlaps)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(Ramp());
        using ReadWriteBuffer<float> other = device.AllocateReadWriteBuffer<float>(Ramp());
        ReadWriteBuffer<float> read = buffer.Slice(readStart, readLength);
        ReadWriteBuffer<float> written = buffer.Slice(writtenStart, writtenLength);
        int count = Math.Min(readLength, writtenLength);

        if (overlaps)
        {
            Assert.Throws<ArgumentException>(() => device.For(count, new AddReadWrite(read, other, written)));
            Assert.Throws<ArgumentException>(() => device.ForCpu(count, new AddReadWrite(read, other, written)));

            // A view of a view, and the view against the whole buffer it came from.
            Assert.Throws<ArgumentException>(() => device.For(count, new AddReadWrite(read.Slice(0), other, written.Slice(0, writtenLength))));
            Assert.Throws<ArgumentException>(() => device.For(count, new AddReadWrite(buffer, other, written)));
            Assert.Equal(Ramp(), buffer.ToArray());

            // Read through both, the overlap is fine.
            device.For(count, new AddReadWrite(read, written, other));
            Assert.Equal((readStart + 1f) + (writtenStart + 1f), other[0]);
            return;
        }

        float[] expected = Ramp();

        for (int i = 0; i < count; i++)
        {
            expected[writtenStart + i] = expected[readStart + i] + (i + 1f);
        }

        device.For(count, new AddReadWrite(read, other, written));
        Assert.Equal(expected, buffer.ToArray());
    }

    [MetalFact]
    public void PingPong_BetweenTwoBuffers_IsAllowed()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> a = device.AllocateReadWriteBuffer<float>(Ramp());
        using ReadWriteBuffer<float> b = device.AllocateReadWriteBuffer<float>(Length);
        using ReadWriteBuffer<float> one = device.AllocateReadWriteBuffer<float>(Enumerable.Repeat(1f, Length).ToArray());

        using (ComputeContext context = device.CreateComputeContext())
        {
            for (int pass = 0; pass < 4; pass++)
            {
                context.For(Length, new AddReadWrite(a, one, b)); // ping
                context.For(Length, new AddReadWrite(b, one, a)); // pong
            }
        }

        Assert.Equal(Ramp().Select(v => v + 8f), a.ToArray());
    }

    [MetalFact]
    public void TwoWrappersOfOneNativeBuffer_AreTheSameMemory()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlBuffer = NativeMetal.NewBuffer(device, Length * sizeof(float));
        nint otherBuffer = NativeMetal.NewBuffer(device, Length * sizeof(float));

        try
        {
            using ReadWriteBuffer<float> first = device.WrapNativeReadWriteBuffer<float>(mtlBuffer);
            using ReadWriteBuffer<float> second = device.WrapNativeReadWriteBuffer<float>(mtlBuffer);
            using ReadOnlyBuffer<float> readOnly = device.WrapNativeReadOnlyBuffer<float>(mtlBuffer);
            using ReadWriteBuffer<int> ints = device.WrapNativeReadWriteBuffer<int>(mtlBuffer);
            using ReadWriteBuffer<float> elsewhere = device.WrapNativeReadWriteBuffer<float>(otherBuffer);
            using ReadWriteBuffer<float> sum = device.AllocateReadWriteBuffer<float>(Length);
            first.CopyFrom(Ramp());

            Assert.Throws<ArgumentException>(() => device.For(Length, new AddReadWrite(first, elsewhere, second)));
            Assert.Throws<ArgumentException>(() => device.ForCpu(Length, new AddReadWrite(first, elsewhere, second)));
            Assert.Throws<ArgumentException>(() => device.For(Length, new AddReadWrite(first.Slice(0, 8), elsewhere, second.Slice(7, 8))));

            // A read-only wrapper is still the memory the read-write one writes, whatever the element type.
            Assert.Throws<ArgumentException>(() => device.For(Length, new EndToEnd.Saxpy(readOnly, first, 2f, default)));
            Assert.Throws<ArgumentException>(() => device.For(Length, new IntsToFloats(ints, first)));
            Assert.Equal(Ramp(), first.ToArray());

            // Read through both wrappers, and written to a buffer that is other memory.
            device.For(Length, new AddReadWrite(first, second, sum));
            Assert.Equal(Ramp(scale: 2f), sum.ToArray());
            device.For(Length, new IntsToFloats(ints, elsewhere));
            device.For(Length, new AddReadWrite(first.Slice(0, 8), elsewhere, second.Slice(8, 8)));
        }
        finally
        {
            ObjC.Release(otherBuffer);
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void GpuOnlyBuffers_AreComparedByBufferAndRange()
    {
        // Private storage has no CPU address to compare; the MTLBuffer and the byte range stand in.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlBuffer = NativeMetal.NewBuffer(device, Length * sizeof(float), NativeMetal.StoragePrivate);
        nint otherBuffer = NativeMetal.NewBuffer(device, Length * sizeof(float), NativeMetal.StoragePrivate);

        try
        {
            using ReadWriteBuffer<float> first = device.WrapNativeReadWriteBuffer<float>(mtlBuffer);
            using ReadWriteBuffer<float> second = device.WrapNativeReadWriteBuffer<float>(mtlBuffer);
            using ReadWriteBuffer<float> elsewhere = device.WrapNativeReadWriteBuffer<float>(otherBuffer);
            using ReadWriteBuffer<float> shared = device.AllocateReadWriteBuffer<float>(Ramp());
            using ReadWriteBuffer<float> result = device.AllocateReadWriteBuffer<float>(Length);
            Assert.False(first.IsCpuAccessible);

            Assert.Throws<ArgumentException>(() => device.For(Length, new AddReadWrite(first, shared, second)));
            Assert.Throws<ArgumentException>(() => device.For(Length, new AddReadWrite(first, shared, first)));
            Assert.Throws<ArgumentException>(() => device.For(16, new AddReadWrite(first.Slice(0, 16), shared, second.Slice(15, 16))));

            // Two private buffers, disjoint ranges of one, and a private one next to shared ones.
            device.Copy(shared, first);
            device.For(Length, new AddReadWrite(first, shared, elsewhere));
            device.For(16, new AddReadWrite(first.Slice(0, 16), shared, second.Slice(16, 16)));
            device.For(Length, new AddReadWrite(first, second, result));

            float[] expected = Ramp();
            float[] doubled = Ramp(scale: 2f);
            Array.Copy(doubled, 0, expected, 16, 16);
            Assert.Equal(expected.Select(v => v * 2f), result.ToArray());

            device.Copy(elsewhere, result);
            Assert.Equal(doubled, result.ToArray());
        }
        finally
        {
            ObjC.Release(otherBuffer);
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void WrappedMemory_ThatOverlapsAnotherBinding_IsRefused()
    {
        // Two MTLBuffers over the same pages: different objects, the same memory.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int page = GraphicsDevice.PageSize;
        int perPage = page / sizeof(float);
        float* memory = (float*)NativeMemory.AlignedAlloc((nuint)(3 * page), (nuint)page);

        try
        {
            new Span<float>(memory, 3 * perPage).Clear();

            using ReadWriteBuffer<float> firstTwo = device.WrapReadWriteBuffer<float>((nint)memory, 2 * perPage);
            using ReadWriteBuffer<float> lastTwo = device.WrapReadWriteBuffer<float>((nint)(memory + perPage), 2 * perPage);
            using ReadWriteBuffer<float> lastOne = device.WrapReadWriteBuffer<float>((nint)(memory + (2 * perPage)), perPage);
            using ReadOnlyBuffer<float> readOnlyFirstTwo = device.WrapReadOnlyBuffer<float>((nint)memory, 2 * perPage);
            using ReadWriteBuffer<float> other = device.AllocateReadWriteBuffer<float>(Ramp(perPage));
            Assert.NotEqual(firstTwo.NativeHandle, lastTwo.NativeHandle);

            // The second page belongs to both.
            Assert.Throws<ArgumentException>(() => device.For(perPage, new AddReadWrite(firstTwo, other, lastTwo)));
            Assert.Throws<ArgumentException>(() => device.ForCpu(perPage, new AddReadWrite(firstTwo, other, lastTwo)));
            Assert.Throws<ArgumentException>(() => device.For(perPage, new EndToEnd.Saxpy(readOnlyFirstTwo, lastTwo, 2f, default)));
            Assert.Throws<ArgumentException>(() => device.For(perPage, new AddReadWrite(firstTwo.Slice(perPage), other, lastTwo.Slice(0, perPage))));

            // Their parts that do not meet, and wrappers of different pages.
            device.For(perPage, new AddReadWrite(firstTwo.Slice(0, perPage), other, lastTwo.Slice(0, perPage)));
            Assert.Equal(perPage, memory[(2 * perPage) - 1]);

            device.For(perPage, new AddReadWrite(firstTwo, other, lastOne));
            Assert.Equal(perPage, memory[(3 * perPage) - 1]);
        }
        finally
        {
            NativeMemory.AlignedFree(memory);
        }
    }

    [MetalFact]
    public void HandWrittenDescriptors_SayWhatIsWritten_OrReadWriteBuffersCountAsWritten()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> x = device.AllocateReadWriteBuffer<float>(Length);
        using ReadWriteBuffer<float> y = device.AllocateReadWriteBuffer<float>(Length);
        using ReadOnlyBuffer<float> frozen = device.AllocateReadOnlyBuffer<float>(Ramp());

        // SetBuffer(index, buffer): a ReadWriteBuffer may be written, so it cannot be bound twice.
        ArgumentException error = Assert.Throws<ArgumentException>(() => device.For(1, new BindBuffers([x, y, x])));
        Assert.Contains("index 2 overlaps the buffer bound at index 0", error.Message);
        Assert.Throws<ArgumentException>(() => device.ForCpu(1, new BindBuffers([x, x.Slice(4, 4)])));

        // ... a ReadOnlyBuffer never is, so it can.
        device.For(1, new BindBuffers([frozen, y, frozen, frozen.Slice(3, 9)]));
        device.For(1, new BindBuffers([x, y, x.Slice(0, 0)]));

        // SetBuffer(index, buffer, written): what the descriptor says goes, for either kind of buffer.
        device.For(1, new BindBuffers([x, y, x], written: false));
        device.ForCpu(1, new BindBuffers([x, x.Slice(4, 4)], written: false));
        Assert.Throws<ArgumentException>(() => device.For(1, new BindBuffers([x, y, x], written: true)));
        Assert.Throws<ArgumentException>(() => device.For(1, new BindBuffers([frozen, frozen], written: true)));
    }

    [MetalFact]
    public void ManyBindings_AreAllCompared()
    {
        // More bindings than the table starts with room for: the last one is checked against the first.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadWriteBuffer<float>[] buffers = Enumerable.Range(0, 31).Select(_ => device.AllocateReadWriteBuffer<float>(4)).ToArray();

        try
        {
            device.For(1, new BindBuffers(buffers));
            device.ForCpu(1, new BindBuffers(buffers));

            Buffer<float>[] aliased = [.. buffers[..30], buffers[0]];
            Assert.Contains("index 30 overlaps the buffer bound at index 0", Assert.Throws<ArgumentException>(() => device.For(1, new BindBuffers(aliased))).Message);

            aliased = [.. buffers[..30], buffers[17].Slice(2)];
            Assert.Contains("index 30 overlaps the buffer bound at index 17", Assert.Throws<ArgumentException>(() => device.ForCpu(1, new BindBuffers(aliased))).Message);

            // A refused dispatch does not disturb the next one on this thread.
            device.For(1, new BindBuffers(buffers));
        }
        finally
        {
            foreach (ReadWriteBuffer<float> buffer in buffers)
            {
                buffer.Dispose();
            }
        }
    }

    [MetalFact]
    public void RecordingADispatch_AllocatesNothingForTheCheck()
    {
        // The table of bindings is reused by the thread: a context that records the same dispatches
        // every frame, with buffers that share memory read-only, stays allocation-free.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteBuffer<float> x = device.AllocateReadWriteBuffer<float>(Ramp());
        using ReadWriteBuffer<float> sum = device.AllocateReadWriteBuffer<float>(Length);
        using ComputeContext context = device.CreateComputeContext();
        ReadWriteBuffer<float> half = x.Slice(0, Length / 2);

        void Frame()
        {
            for (int pass = 0; pass < 10; pass++)
            {
                context.For(Length, new AddReadWrite(x, x, sum));
                context.For(Length / 2, new AddReadWrite(half, x, sum));
            }

            context.Execute();
        }

        for (int frame = 0; frame < 5; frame++)
        {
            Frame();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int frame = 0; frame < 50; frame++)
        {
            Frame();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"Recording and executing 50 frames allocated {allocated} bytes.");
    }

    // ---- textures ---------------------------------------------------------------------------------

    [MetalFact]
    public void TheSameTexture_WrittenThroughOneCaptureAndReadThroughAnother_IsRefused_OnEveryPath()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<float> texture = device.AllocateReadWriteTexture2D<float>(Length, 1);
        using ReadWriteBuffer<float> seen = device.AllocateReadWriteBuffer<float>(Length);

        AssertRefusedEverywhere(
            device,
            new WriteOneReadOther(texture, texture, seen),
            "texture",
            "The texture bound at texture index 1 is the same texture as the texture bound at texture index 0",
            "the shader writes it");

        Assert.All(texture.ToArray(), value => Assert.Equal(0f, value));
        Assert.All(seen.ToArray(), value => Assert.Equal(0f, value));
    }

    [MetalFact]
    public void ATextureAndItsReadOnlyView_InADispatchThatWritesIt_AreRefused_OnEveryPath()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<float> texture = device.AllocateReadWriteTexture2D<float>(Length, 1);
        using ReadOnlyTexture2D<float> view = texture.AsReadOnly();
        using ReadWriteBuffer<float> seen = device.AllocateReadWriteBuffer<float>(Length);

        AssertRefusedEverywhere(
            device,
            new WriteOneSampleOther(texture, view, seen),
            "texture",
            "is the same texture as the texture bound at texture index 0",
            "AsReadOnly()");

        Assert.All(texture.ToArray(), value => Assert.Equal(0f, value));
    }

    [MetalFact]
    public void TwoDifferentTextures_WriteOneAndReadTheOther()
    {
        // What the refused dispatches have to be written as: a texture of its own for each role.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<float> written = device.AllocateReadWriteTexture2D<float>(Length, 1);
        using ReadWriteTexture2D<float> read = device.AllocateReadWriteTexture2D<float>(Ramp(scale: 10f), Length, 1);
        using ReadOnlyTexture2D<float> view = read.AsReadOnly();
        using ReadWriteBuffer<float> seen = device.AllocateReadWriteBuffer<float>(Length);

        device.For(Length, new WriteOneReadOther(written, read, seen));
        Assert.Equal(Ramp(scale: 10f), seen.ToArray());
        Assert.Equal(Ramp(), written.ToArray());

        seen.AsSpan().Clear();
        device.For(Length, new WriteOneSampleOther(written, view, seen));
        Assert.Equal(Ramp(scale: 10f), seen.ToArray());

        // Ping-pong: each texture is written in one dispatch and read, through its view, in the next.
        using ReadOnlyTexture2D<float> writtenView = written.AsReadOnly();

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.For(Length, new WriteOneSampleOther(read, writtenView, seen));
            context.For(Length, new WriteOneSampleOther(written, view, seen));
        }

        Assert.Equal(Ramp(), seen.ToArray());
    }

    [MetalFact]
    public void TheSameTexture_OnlyRead_IsAllowed()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<float> texture = device.AllocateReadWriteTexture2D<float>(Ramp(), Length, 1);
        using ReadOnlyTexture2D<float> view = texture.AsReadOnly();
        using ReadOnlyTexture2D<float> secondView = texture.AsReadOnly();
        using ReadWriteBuffer<float> sum = device.AllocateReadWriteBuffer<float>(Length);
        float[] expected = Ramp(scale: 2f);

        // Captured twice as a read-write texture, with its view, and as two views.
        device.For(Length, new AddTexels(texture, texture, sum));
        Assert.Equal(expected, sum.ToArray());

        sum.AsSpan().Clear();
        device.ForCpu(Length, new AddTexels(texture, texture, sum));
        Assert.Equal(expected, sum.ToArray());

        sum.AsSpan().Clear();
        device.For(Length, new AddTexelAndView(texture, view, sum));
        Assert.Equal(expected, sum.ToArray());

        sum.AsSpan().Clear();
        device.For(Length, new BindTextures(view, secondView));

        using (ComputeContext context = device.CreateComputeContext())
        {
            context.For(Length, new AddTexelAndView(texture, secondView, sum));
        }

        Assert.Equal(expected, sum.ToArray());
    }

    [MetalFact]
    public void HandWrittenDescriptors_SayWhatIsWritten_OrReadWriteTexturesCountAsWritten()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<float> texture = device.AllocateReadWriteTexture2D<float>(4, 4);
        using ReadWriteTexture2D<float> other = device.AllocateReadWriteTexture2D<float>(4, 4);
        using ReadOnlyTexture2D<float> view = texture.AsReadOnly();

        // SetTexture(index, texture): a ReadWriteTexture2D may be written.
        Assert.Throws<ArgumentException>(() => device.For(1, new BindTextures(texture, texture)));
        Assert.Throws<ArgumentException>(() => device.For(1, new BindTextures(view, texture)));
        Assert.Throws<ArgumentException>(() => device.ForCpu(1, new BindTextures(texture, view)));
        device.For(1, new BindTextures(texture, other));
        device.For(1, new BindTextures(view, view));

        // SetTexture(index, texture, written).
        device.For(1, new BindTextures(texture, texture, written: false));
        device.ForCpu(1, new BindTextures(texture, view, written: false));
        Assert.Throws<ArgumentException>(() => device.For(1, new BindTextures(view, view, written: true)));
    }

    [MetalFact]
    public void TwoWrappersOfOneNativeTexture_AreTheSameTexture()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlTexture = NativeMetal.NewTexture(device, Length, 1, NativeMetal.R32Float, storageMode: NativeMetal.ModeShared);

        try
        {
            using ReadWriteTexture2D<float> first = device.WrapNativeReadWriteTexture2D<float>(mtlTexture);
            using ReadWriteTexture2D<float> second = device.WrapNativeReadWriteTexture2D<float>(mtlTexture);
            using ReadOnlyTexture2D<float> readOnly = device.WrapNativeReadOnlyTexture2D<float>(mtlTexture);
            using ReadWriteBuffer<float> seen = device.AllocateReadWriteBuffer<float>(Length);

            Assert.Throws<ArgumentException>(() => device.For(Length, new WriteOneReadOther(first, second, seen)));
            Assert.Throws<ArgumentException>(() => device.For(Length, new WriteOneSampleOther(first, readOnly, seen)));

            first.CopyFrom(Ramp());
            device.For(Length, new AddTexels(first, second, seen));
            Assert.Equal(Ramp(scale: 2f), seen.ToArray());
        }
        finally
        {
            ObjC.Release(mtlTexture);
        }
    }

    // ---- the output of a pixel shader --------------------------------------------------------------

    [MetalFact]
    public void APixelShader_ThatCapturesItsOwnOutput_IsRefused_OnEveryPath()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Rgba32[] pixels = Enumerable.Range(0, 16 * 8).Select(i => new Rgba32((byte)(i * 2), 100, 200, 254)).ToArray();
        using ReadWriteTexture2D<Rgba32> frame = device.AllocateReadWriteTexture2D<Rgba32>(pixels, 16, 8);
        using ReadOnlyTexture2D<Rgba32> view = frame.AsReadOnly();
        using ComputeContext context = device.CreateComputeContext();

        void Check(Action dispatch)
        {
            ArgumentException error = Assert.Throws<ArgumentException>(dispatch);
            Assert.Equal("texture", error.ParamName);
            Assert.Contains("The texture bound at texture index 1 is the same texture as the output texture of the pixel shader", error.Message);
        }

        // Through its read-only view, and as the read-write texture itself (which the shader only reads).
        Check(() => device.ForEach(frame, new HalveSampled(view)));
        Check(() => device.ForEachCpu(frame, new HalveSampled(view)));
        Check(() => device.ForEachSplit(frame, new HalveSampled(view), 0.5));
        Check(() => device.ForEachAsync(frame, new HalveSampled(view)).GetAwaiter().GetResult());
        Check(() => context.ForEach(frame, new HalveSampled(view)));
        Check(() => device.ForEach(frame, new HalveLoaded(frame)));
        Check(() => device.ForEachCpu(frame, new HalveLoaded(frame)));
        Check(() => context.ForEach(frame, new HalveLoaded(frame)));

        Assert.Equal(0, context.PendingCount);
        Assert.Equal(pixels, frame.ToArray());

        // Into another texture it is an ordinary pixel shader.
        using ReadWriteTexture2D<Rgba32> target = device.AllocateReadWriteTexture2D<Rgba32>(16, 8);
        device.ForEach(target, new HalveSampled(view));
        Rgba32[] halved = target.ToArray();
        device.ForEachCpu(target, new HalveLoaded(frame));
        Assert.Equal(halved, target.ToArray());
        Assert.Equal(new Rgba32(2, 50, 100, 127), halved[2]);
    }

    // ---- a texture and the buffer that stores its pixels --------------------------------------------

    [MetalFact]
    public void ATextureOverABuffer_ThatIsBoundToo_IsRefusedWhenEitherIsWritten()
    {
        // Other Metal code made a texture that is a view of its buffer; both are wrapped.
        const int width = 16, height = 4;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nuint alignment = device.GetMinimumLinearTextureAlignment(NativeMetal.RGBA8Unorm);
        nuint bytesPerRow = ((nuint)(width * 4) + alignment - 1) / alignment * alignment;

        // The texture's rows, then as many bytes again that are not the texture's.
        nuint textureBytes = bytesPerRow * height;
        nint mtlBuffer = NativeMetal.NewBuffer(device, textureBytes * 2);
        nint mtlTexture = NativeMetal.NewLinearTexture(mtlBuffer, width, height, 0, bytesPerRow);

        try
        {
            using ReadWriteTexture2D<Rgba32> texture = device.WrapNativeReadWriteTexture2D<Rgba32>(mtlTexture);
            using ReadOnlyTexture2D<Rgba32> sampled = device.WrapNativeReadOnlyTexture2D<Rgba32>(mtlTexture);
            using ReadWriteBuffer<float> buffer = device.WrapNativeReadWriteBuffer<float>(mtlBuffer);
            using ReadOnlyBuffer<float> readOnlyBuffer = device.WrapNativeReadOnlyBuffer<float>(mtlBuffer);
            using ReadWriteBuffer<float> sum = device.AllocateReadWriteBuffer<float>(width);
            Assert.True(texture.IsCpuAccessible);

            int elementsOfTexture = (int)(textureBytes / sizeof(float));

            // The shader writes the buffer and reads the texture, or the other way around.
            ArgumentException error = Assert.Throws<ArgumentException>(() => device.For(width, new TexelsToBuffer(sampled, buffer)));
            Assert.Equal("texture", error.ParamName);
            Assert.Contains("The texture bound at texture index 0 stores its pixels in memory that the buffer bound at index 0 also covers", error.Message);

            Assert.Throws<ArgumentException>(() => device.ForCpu(width, new TexelsToBuffer(sampled, buffer)));
            Assert.Throws<ArgumentException>(() => device.For(width, new BufferToTexels(texture, readOnlyBuffer)));
            Assert.Throws<ArgumentException>(() => device.ForCpu(width, new BufferToTexels(texture, readOnlyBuffer)));

            // One byte of the last pixel is enough; the padding after the last row is not the texture's.
            int lastPixelElement = (int)((((nuint)(height - 1) * bytesPerRow) + ((nuint)(width - 1) * 4)) / sizeof(float));
            Assert.Throws<ArgumentException>(() => device.For(1, new TexelsToBuffer(sampled, buffer.Slice(lastPixelElement, 1))));
            device.For(1, new TexelsToBuffer(sampled, buffer.Slice(lastPixelElement + 1, 1)));

            // The part of the buffer after the texture is other memory.
            device.For(width, new TexelsToBuffer(sampled, buffer.Slice(elementsOfTexture, width)));
            Assert.Equal(1f, buffer[elementsOfTexture]);
            device.For(width, new BufferToTexels(texture, readOnlyBuffer.Slice(elementsOfTexture, width)));
            Assert.Equal(new Rgba32(255, 0, 0, 255), texture.ToArray()[0]);

            // Both only read: the overlap does not matter.
            device.For(width, new AddTexelAndElement(sampled, readOnlyBuffer, sum));
            device.ForCpu(width, new AddTexelAndElement(sampled, readOnlyBuffer, sum));
        }
        finally
        {
            ObjC.Release(mtlTexture);
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void TwoTexturesOverTheSameBufferMemory_AreRefusedWhenOneIsWritten()
    {
        // Different MTLTexture objects, each a view of the same rows of one buffer; a third one is
        // a view of the rows after them.
        const int width = 16, height = 4;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nuint alignment = device.GetMinimumLinearTextureAlignment(NativeMetal.RGBA8Unorm);
        nuint bytesPerRow = ((nuint)(width * 4) + alignment - 1) / alignment * alignment;
        nuint textureBytes = bytesPerRow * height;
        nint mtlBuffer = NativeMetal.NewBuffer(device, textureBytes * 2);
        nint firstTexture = NativeMetal.NewLinearTexture(mtlBuffer, width, height, 0, bytesPerRow);
        nint secondTexture = NativeMetal.NewLinearTexture(mtlBuffer, width, height, 0, bytesPerRow);
        nint shiftedTexture = NativeMetal.NewLinearTexture(mtlBuffer, width, height - 1, bytesPerRow, bytesPerRow);
        nint laterTexture = NativeMetal.NewLinearTexture(mtlBuffer, width, height, textureBytes, bytesPerRow);

        try
        {
            using ReadWriteTexture2D<Rgba32> first = device.WrapNativeReadWriteTexture2D<Rgba32>(firstTexture);
            using ReadWriteTexture2D<Rgba32> second = device.WrapNativeReadWriteTexture2D<Rgba32>(secondTexture);
            using ReadWriteTexture2D<Rgba32> shifted = device.WrapNativeReadWriteTexture2D<Rgba32>(shiftedTexture);
            using ReadWriteTexture2D<Rgba32> later = device.WrapNativeReadWriteTexture2D<Rgba32>(laterTexture);
            Assert.NotEqual(first.NativeHandle, second.NativeHandle);

            ArgumentException error = Assert.Throws<ArgumentException>(() => device.For(width, height, new CopyTexels(first, second)));
            Assert.Contains("are views of overlapping memory of one buffer", error.Message);
            Assert.Throws<ArgumentException>(() => device.ForCpu(width, height, new CopyTexels(first, second)));

            // Rows 1 to 3 of the first are rows 0 to 2 of the shifted one.
            Assert.Throws<ArgumentException>(() => device.For(width, height - 1, new CopyTexels(shifted, first)));

            // The rows after the first texture are other memory.
            first.CopyFrom(Enumerable.Repeat(new Rgba32(1, 2, 3, 4), width * height).ToArray());
            device.For(width, height, new CopyTexels(later, first));
            Assert.All(later.ToArray(), pixel => Assert.Equal(new Rgba32(1, 2, 3, 4), pixel));
        }
        finally
        {
            ObjC.Release(laterTexture);
            ObjC.Release(shiftedTexture);
            ObjC.Release(secondTexture);
            ObjC.Release(firstTexture);
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void ATextureAndAnUnrelatedBuffer_AreNotConfused()
    {
        // A texture SharpCompute allocated owns its buffer, which nothing else can name.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(16, 1);
        using ReadOnlyTexture2D<Rgba32> view = texture.AsReadOnly();
        using ReadWriteBuffer<float> data = device.AllocateReadWriteBuffer<float>(16);
        using ReadOnlyBuffer<float> source = device.AllocateReadOnlyBuffer<float>(Enumerable.Repeat(1f, 16).ToArray());

        device.For(16, new BufferToTexels(texture, source));
        device.For(16, new TexelsToBuffer(view, data));
        Assert.All(data.ToArray(), value => Assert.Equal(2f, value));
    }
}
