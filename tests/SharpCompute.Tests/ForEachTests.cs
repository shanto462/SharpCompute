using System;
using System.Threading.Tasks;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>Pixel-shader dispatch through hand-written descriptors; conformance tests for the "Pixel shaders" ABI.</summary>
public class ForEachTests
{
    private const byte Blue = 51;

    [MetalTheory]
    [InlineData(1, 1)]
    [InlineData(16, 16)]
    [InlineData(17, 5)]
    [InlineData(1, 300)]
    [InlineData(300, 1)]
    [InlineData(1000, 563)]
    [InlineData(1280, 720)]
    [InlineData(4099, 3)]
    public void ForEach_WritesEveryPixelExactlyOnce(int width, int height)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        using ReadWriteBuffer<uint> stats = device.AllocateReadWriteBuffer<uint>(4);

        device.ForEach(texture, new PixelCoordinates(stats, Blue / 255f));

        // Exactly Width × Height invocations (no over-dispatch past a non-multiple of 16), on a
        // Width × Height × 1 grid.
        Assert.Equal([(uint)(width * height), (uint)width, (uint)height, 1u], stats.ToArray());
        AssertPixelCoordinates(texture);
    }

    [MetalFact]
    public void ForEach_HonoursAnExplicitThreadGroupSize()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(1000, 563);
        using ReadWriteBuffer<uint> stats = device.AllocateReadWriteBuffer<uint>(4);

        device.ForEach(texture, new PixelCoordinates8x4(stats, Blue / 255f));

        Assert.Equal([1000u * 563u, 1000u, 563u, 1u], stats.ToArray());
        AssertPixelCoordinates(texture);
    }

    [MetalFact]
    public void ForEach_OverwritesPreviousContents()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(33, 17);
        using ReadWriteBuffer<uint> stats = device.AllocateReadWriteBuffer<uint>(4);

        texture.CopyFrom(TextureTests.RandomPixels(33 * 17, seed: 1));
        device.ForEach(texture, new PixelCoordinates(stats, Blue / 255f));

        AssertPixelCoordinates(texture);
    }

    [MetalTheory]
    [InlineData(4f, 255)]
    [InlineData(-4f, 0)]
    public void ForEach_ClampsOutOfRangeColorsOnTheUnormWrite(float x, byte expectedBlue)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(7, 3);

        device.ForEach(texture, new OutOfRangeColor(x));

        Assert.All(texture.ToArray(), p => Assert.Equal(new Rgba32(255, 0, expectedBlue, 64), p));
    }

    [MetalFact]
    public void ForEach_TextureFromAnotherDevice_Throws()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using GraphicsDevice other = GraphicsDevice.CreateStandInForAnotherDevice();
        using ReadWriteTexture2D<Rgba32> foreign = other.AllocateReadWriteTexture2D<Rgba32>(8, 8);
        using ReadWriteBuffer<uint> stats = device.AllocateReadWriteBuffer<uint>(4);

        Assert.Throws<ArgumentException>("texture", () => device.ForEach(foreign, new PixelCoordinates(stats, 0f)));

        // Nothing ran, and both devices still work.
        Assert.Equal(new uint[4], stats.ToArray());

        using ReadWriteTexture2D<Rgba32> local = device.AllocateReadWriteTexture2D<Rgba32>(8, 8);
        device.ForEach(local, new PixelCoordinates(stats, Blue / 255f));
        AssertPixelCoordinates(local);

        using ReadWriteBuffer<uint> otherStats = other.AllocateReadWriteBuffer<uint>(4);
        other.ForEach(foreign, new PixelCoordinates(otherStats, Blue / 255f));
        AssertPixelCoordinates(foreign);
    }

    [MetalFact]
    public void ForEach_BufferFromAnotherDevice_ThrowsAndLeavesTheDeviceUsable()
    {
        // The texture is valid but SetArguments fails after it was bound: the encoder must still be
        // ended (Metal aborts the process otherwise) and the command buffer dropped.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using GraphicsDevice other = GraphicsDevice.CreateStandInForAnotherDevice();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(8, 8);
        using ReadWriteBuffer<uint> foreignStats = other.AllocateReadWriteBuffer<uint>(4);

        Assert.Throws<ArgumentException>("buffer", () => device.ForEach(texture, new PixelCoordinates(foreignStats, Blue / 255f)));
        Assert.All(texture.ToArray(), static p => Assert.Equal(default, p));

        using ReadWriteBuffer<uint> stats = device.AllocateReadWriteBuffer<uint>(4);
        device.ForEach(texture, new PixelCoordinates(stats, Blue / 255f));
        AssertPixelCoordinates(texture);
    }

    [MetalFact]
    public void ForEach_DisposedTexture_Throws()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(8, 8);
        using ReadWriteBuffer<uint> stats = device.AllocateReadWriteBuffer<uint>(4);
        texture.Dispose();

        Assert.Throws<ObjectDisposedException>(() => device.ForEach(texture, new PixelCoordinates(stats, 0f)));
    }

    [MetalFact]
    public void ForEach_NullTexture_Throws()
    {
        Assert.Throws<ArgumentNullException>("texture", () => GraphicsDevice.GetDefault().ForEach(null!, new OutOfRangeColor(0f)));
    }

    [MetalFact]
    public void ForEach_OnADisposedDevice_Throws()
    {
        GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
        ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(8, 8);
        device.Dispose();

        Assert.Throws<ObjectDisposedException>(() => device.ForEach(texture, new OutOfRangeColor(0f)));
        texture.Dispose();
    }

    [MetalFact]
    public async Task ForEach_IsThreadSafeAcrossTextures()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        await Parallel.ForAsync(0, 16, async (i, _) =>
        {
            await Task.Yield();

            int width = 100 + i * 13, height = 50 + i * 7;
            using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
            using ReadWriteBuffer<uint> stats = device.AllocateReadWriteBuffer<uint>(4);

            for (int n = 0; n < 5; n++)
            {
                device.ForEach(texture, new PixelCoordinates(stats, Blue / 255f));
            }

            Assert.Equal((uint)(5 * width * height), stats[0]);
            AssertPixelCoordinates(texture);
        });
    }

    private static void AssertPixelCoordinates(ReadWriteTexture2D<Rgba32> texture)
    {
        Rgba32[] pixels = texture.ToArray();

        for (int y = 0; y < texture.Height; y++)
        {
            for (int x = 0; x < texture.Width; x++)
            {
                Rgba32 expected = PixelCoordinates.Expected(x, y, Blue);
                Rgba32 actual = pixels[y * texture.Width + x];

                if (!expected.Equals(actual))
                {
                    Assert.Fail($"Pixel ({x}, {y}) of a {texture.Width}×{texture.Height} texture is {actual}, expected {expected}.");
                }
            }
        }
    }
}
