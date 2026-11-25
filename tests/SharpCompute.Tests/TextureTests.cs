using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SharpCompute.Tests;

public class TextureTests
{
    [MetalFact]
    public void Allocate_ReportsSizeAndDevice_AndIsZeroFilled()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(37, 19);

        Assert.Same(device, texture.Device);
        Assert.Equal(37, texture.Width);
        Assert.Equal(19, texture.Height);

        Rgba32[] pixels = texture.ToArray();

        Assert.Equal(37 * 19, pixels.Length);
        Assert.All(pixels, static p => Assert.Equal(default, p));
    }

    [MetalTheory]
    [InlineData(1, 1)]
    [InlineData(3, 5)]
    [InlineData(16, 16)]
    [InlineData(17, 9)]
    [InlineData(1000, 563)]
    [InlineData(16384, 1)]
    [InlineData(1, 16384)]
    public void BytesPerRow_IsAlignedAndHoldsARow(int width, int height)
    {
        using ReadWriteTexture2D<Rgba32> texture = GraphicsDevice.GetDefault().AllocateReadWriteTexture2D<Rgba32>(width, height);

        nuint alignment = GraphicsDevice.GetDefault().GetMinimumLinearTextureAlignment(Interop.Metal.PixelFormatRGBA8Unorm);

        Assert.Equal((nuint)0, texture.BytesPerRow % alignment);
        Assert.True(texture.BytesPerRow >= (nuint)(width * 4));
        Assert.True(texture.BytesPerRow < (nuint)(width * 4) + alignment);
    }

    [MetalFact]
    public void NarrowTexture_RowsArePaddedInTheBackingBuffer()
    {
        // 3 pixels = 12 bytes per row, below every linear-texture alignment Apple GPUs report, so
        // this pins that CopyTo/CopyFrom de-stride rather than treat the buffer as tightly packed.
        using ReadWriteTexture2D<Rgba32> texture = GraphicsDevice.GetDefault().AllocateReadWriteTexture2D<Rgba32>(3, 5);

        Assert.True(texture.BytesPerRow > 12, $"bytesPerRow is {texture.BytesPerRow}");
    }

    [MetalTheory]
    [InlineData(1, 1)]
    [InlineData(3, 5)]
    [InlineData(17, 9)]
    [InlineData(64, 64)]
    [InlineData(1000, 563)]
    public void CopyFrom_ThenToArray_RoundTripsEveryPixel(int width, int height)
    {
        Rgba32[] source = RandomPixels(width * height, seed: width * 31 + height);

        using ReadWriteTexture2D<Rgba32> texture = GraphicsDevice.GetDefault().AllocateReadWriteTexture2D<Rgba32>(width, height);
        texture.CopyFrom(source);

        Assert.Equal(source, texture.ToArray());

        Rgba32[] destination = new Rgba32[width * height + 3];
        destination.AsSpan().Fill(new Rgba32(1, 2, 3, 4));
        texture.CopyTo(destination);

        Assert.Equal(source, destination[..^3]);
        Assert.Equal(new Rgba32(1, 2, 3, 4), destination[^1]);
    }

    [MetalFact]
    public void CopyFrom_ShorterSource_FillsRowMajorAndLeavesTheRest()
    {
        using ReadWriteTexture2D<Rgba32> texture = GraphicsDevice.GetDefault().AllocateReadWriteTexture2D<Rgba32>(4, 3);
        Rgba32[] source = RandomPixels(6, seed: 7);

        texture.CopyFrom(source);
        Rgba32[] result = texture.ToArray();

        Assert.Equal(source, result[..6]);
        Assert.All(result[6..], static p => Assert.Equal(default, p));
    }

    [MetalFact]
    public void CopyHelpers_RejectWrongLengths()
    {
        using ReadWriteTexture2D<Rgba32> texture = GraphicsDevice.GetDefault().AllocateReadWriteTexture2D<Rgba32>(4, 3);

        Assert.Throws<ArgumentException>("destination", () => texture.CopyTo(new Rgba32[11]));
        Assert.Throws<ArgumentException>("source", () => texture.CopyFrom(new Rgba32[13]));
    }

    [MetalTheory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -5)]
    [InlineData(16385, 1)]
    [InlineData(1, 16385)]
    public void InvalidSize_Throws(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GraphicsDevice.GetDefault().AllocateReadWriteTexture2D<Rgba32>(width, height));
    }

    [MetalFact]
    public void ElementTypesWithoutAPixelFormat_AreNotSupported()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        // (The supported formats, and the read-only ones, are covered by TextureShaderTests.)
        Assert.Throws<NotSupportedException>(() => device.AllocateReadWriteTexture2D<uint>(4, 4));
        Assert.Throws<NotSupportedException>(() => device.AllocateReadWriteTexture2D<Float2>(4, 4));
        Assert.Throws<NotSupportedException>(() => device.AllocateReadWriteTexture2D<double>(4, 4));
    }

    [MetalFact]
    public void Disposed_EveryAccessorThrowsObjectDisposedException()
    {
        ReadWriteTexture2D<Rgba32> texture = GraphicsDevice.GetDefault().AllocateReadWriteTexture2D<Rgba32>(8, 8);
        texture.Dispose();

        Assert.Throws<ObjectDisposedException>(() => texture.ToArray());
        Assert.Throws<ObjectDisposedException>(() => texture.CopyTo(new Rgba32[64]));
        Assert.Throws<ObjectDisposedException>(() => texture.CopyFrom(new Rgba32[64]));

        // Like Buffer<T>.Length, the size stays readable after disposal.
        Assert.Equal(8, texture.Width);
        Assert.Equal(8, texture.Height);
    }

    [MetalFact]
    public void Dispose_IsIdempotent()
    {
        ReadWriteTexture2D<Rgba32> texture = GraphicsDevice.GetDefault().AllocateReadWriteTexture2D<Rgba32>(8, 8);

        texture.Dispose();
        texture.Dispose();
    }

    [MetalFact]
    public void DisposedDevice_CannotAllocate()
    {
        GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        device.Dispose();

        Assert.Throws<ObjectDisposedException>(() => device.AllocateReadWriteTexture2D<Rgba32>(4, 4));
    }

    [MetalFact]
    public void Texture_OutlivesItsDeviceWrapper()
    {
        // As with buffers, the texture holds its own Metal references; disposing the device wrapper
        // must not invalidate memory the texture still exposes.
        GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(5, 5);
        device.Dispose();

        Rgba32[] source = RandomPixels(25, seed: 3);
        texture.CopyFrom(source);

        Assert.Equal(source, texture.ToArray());
    }

    [MetalFact]
    public void CopyHelpers_KeepTextureAliveUnderConcurrentGC()
    {
        using CancellationTokenSource cts = new();
        Thread collector = new(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }) { IsBackground = true };

        collector.Start();

        try
        {
            for (int i = 0; i < 20; i++)
            {
                Rgba32[] result = AllocateFillAndCopy(1023, 517);
                Assert.Equal(1023 * 517, result.Length);
                Assert.Equal(new Rgba32(9, 8, 7, 6), result[^1]);
            }
        }
        finally
        {
            cts.Cancel();
            collector.Join();
        }
    }

    [MetalFact]
    public void Leaked_TexturesAreReleasedByFinalization()
    {
        for (int i = 0; i < 50; i++)
        {
            AllocateAndDrop();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    internal static Rgba32[] RandomPixels(int count, int seed)
    {
        Random random = new(seed);
        byte[] bytes = new byte[count * 4];
        random.NextBytes(bytes);

        Rgba32[] pixels = new Rgba32[count];

        for (int i = 0; i < count; i++)
        {
            pixels[i] = new Rgba32(bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3]);
        }

        return pixels;
    }

    // Fully optimized and not inlined so the JIT can treat the texture as dead once the copy starts.
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static Rgba32[] AllocateFillAndCopy(int width, int height)
    {
        ReadWriteTexture2D<Rgba32> texture = GraphicsDevice.GetDefault().AllocateReadWriteTexture2D<Rgba32>(width, height);
        Rgba32[] fill = new Rgba32[width * height];
        fill.AsSpan().Fill(new Rgba32(9, 8, 7, 6));
        texture.CopyFrom(fill);
        return texture.ToArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateAndDrop() => GraphicsDevice.GetDefault().AllocateReadWriteTexture2D<Rgba32>(512, 512);
}
