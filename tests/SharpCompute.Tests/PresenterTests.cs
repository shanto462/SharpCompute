using System;
using SharpCompute.Interop;
using SharpCompute.Presentation;

namespace SharpCompute.Tests;

/// <summary>
/// The internal present kernel, run against an offscreen BGRA8Unorm texture instead of a drawable
/// (same pixel format and the same <see cref="Presenter.Encode"/> path, but readable from the CPU).
/// </summary>
public sealed unsafe class PresenterTests
{
    private static readonly Rgba32 Black = new(0, 0, 0, 255);

    [MetalFact]
    public void SameSize_CopiesExactlyAndSwizzlesToBgra()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> source = CreateNoise(device, 37, 23, seed: 1);
        using BgraTarget target = new(device, 37, 23);

        Present(device, source, target, PresentScaling.Fit);

        Rgba32[] pixels = source.ToArray();

        for (int y = 0; y < 23; y++)
        {
            for (int x = 0; x < 37; x++)
            {
                Rgba32 expected = pixels[(y * 37) + x];
                expected.A = 255; // presented frames are opaque
                Assert.Equal(expected, target.Read(x, y));
            }
        }
    }

    [MetalFact]
    public void IntegerUpscale_ReplicatesPixelsExactly()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> source = CreateNoise(device, 7, 5, seed: 2);
        using BgraTarget target = new(device, 21, 15);

        Present(device, source, target, PresentScaling.Fit);

        Rgba32[] pixels = source.ToArray();

        for (int y = 0; y < 15; y++)
        {
            for (int x = 0; x < 21; x++)
            {
                Rgba32 expected = pixels[(y / 3 * 7) + (x / 3)];
                expected.A = 255;
                Assert.Equal(expected, target.Read(x, y));
            }
        }
    }

    [MetalFact]
    public void Fit_LetterboxesWithBlackBars()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> source = CreateNoise(device, 8, 4, seed: 3);
        using BgraTarget target = new(device, 16, 16);

        Present(device, source, target, PresentScaling.Fit);

        // Destination rectangle (0, 4, 16, 8): a pixel-exact 2x scale.
        Rgba32[] pixels = source.ToArray();

        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                Rgba32 expected = Black;

                if (y is >= 4 and < 12)
                {
                    expected = pixels[((y - 4) / 2 * 8) + (x / 2)];
                    expected.A = 255;
                }

                Assert.Equal(expected, target.Read(x, y));
            }
        }
    }

    [MetalFact]
    public void Fit_PillarboxesAndFiltersNonIntegerScales()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> source = device.AllocateReadWriteTexture2D<Rgba32>(10, 10);
        Rgba32 color = new(200, 100, 50, 255);
        source.CopyFrom(Filled(100, color));
        using BgraTarget target = new(device, 25, 15);

        Present(device, source, target, PresentScaling.Fit);

        // Destination (5, 0, 15, 15): a 1.5x bilinear scale of a flat color is that color.
        for (int y = 0; y < 15; y++)
        {
            for (int x = 0; x < 25; x++)
            {
                Rgba32 actual = target.Read(x, y);

                if (x is >= 5 and < 20)
                {
                    AssertClose(color, actual, tolerance: 1);
                }
                else
                {
                    Assert.Equal(Black, actual);
                }
            }
        }
    }

    [MetalFact]
    public void Stretch_FillsTheTargetOnBothAxes()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> source = device.AllocateReadWriteTexture2D<Rgba32>(2, 1);
        Rgba32 red = new(255, 0, 0, 255), blue = new(0, 0, 255, 255);
        source.CopyFrom([red, blue]);
        using BgraTarget target = new(device, 4, 6);

        Present(device, source, target, PresentScaling.Stretch);

        for (int y = 0; y < 6; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Assert.Equal(x < 2 ? red : blue, target.Read(x, y));
            }
        }
    }

    [MetalFact]
    public void Downscale_StaysWithinTheSourceRange()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> source = device.AllocateReadWriteTexture2D<Rgba32>(64, 48);
        Rgba32 color = new(10, 220, 130, 255);
        source.CopyFrom(Filled(64 * 48, color));
        using BgraTarget target = new(device, 16, 12);

        Present(device, source, target, PresentScaling.Fit);

        for (int y = 0; y < 12; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                AssertClose(color, target.Read(x, y), tolerance: 1);
            }
        }
    }

    private static void Present(GraphicsDevice device, ReadWriteTexture2D<Rgba32> source, BgraTarget target, PresentScaling scaling)
    {
        using Presenter presenter = new(device);
        using AutoreleasePool pool = AutoreleasePool.Push();
        using ObjCHandle.Lease queue = device.QueueHandle.Acquire(device);

        nint commandBuffer = ObjC.SendIntPtr(queue.Pointer, Selectors.CommandBuffer);
        Assert.NotEqual(0, commandBuffer);

        presenter.Encode(commandBuffer, source.Handle.DangerousGetHandle(), source.Width, source.Height, target.Texture, target.Width, target.Height, scaling);

        ObjC.SendVoid(commandBuffer, Selectors.Commit);
        ObjC.SendVoid(commandBuffer, Selectors.WaitUntilCompleted);
        Assert.NotEqual(Metal.CommandBufferStatusError, ObjC.SendNUInt(commandBuffer, Selectors.Status));
    }

    private static ReadWriteTexture2D<Rgba32> CreateNoise(GraphicsDevice device, int width, int height, int seed)
    {
        Random random = new(seed);
        Rgba32[] pixels = new Rgba32[width * height];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
        }

        ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(width, height);
        texture.CopyFrom(pixels);
        return texture;
    }

    private static Rgba32[] Filled(int count, Rgba32 color)
    {
        Rgba32[] pixels = new Rgba32[count];
        Array.Fill(pixels, color);
        return pixels;
    }

    private static void AssertClose(Rgba32 expected, Rgba32 actual, int tolerance)
    {
        Assert.True(
            Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance &&
            Math.Abs(expected.B - actual.B) <= tolerance && expected.A == actual.A,
            $"Expected {expected} ± {tolerance}, got {actual}.");
    }

    /// <summary>A linear BGRA8Unorm texture over a shared buffer: the drawable's format, but CPU-readable.</summary>
    private sealed class BgraTarget : IDisposable
    {
        private readonly ObjCHandle _buffer;
        private readonly ObjCHandle _texture;
        private readonly byte* _contents;
        private readonly nuint _bytesPerRow;

        public BgraTarget(GraphicsDevice device, int width, int height)
        {
            Width = width;
            Height = height;

            nuint alignment = device.GetMinimumLinearTextureAlignment(AppKit.PixelFormatBGRA8Unorm);
            _bytesPerRow = ((nuint)width * 4 + alignment - 1) / alignment * alignment;
            _buffer = device.NewSharedBuffer(_bytesPerRow * (nuint)height);
            _contents = (byte*)ObjC.SendIntPtr(_buffer.DangerousGetHandle(), Selectors.Contents);

            using AutoreleasePool pool = AutoreleasePool.Push();
            nint descriptor = ObjC.SendIntPtr(ObjC.SendIntPtr(ObjC.GetClass("MTLTextureDescriptor"), Selectors.Alloc), Selectors.Init);

            try
            {
                ObjC.SendVoid(descriptor, Selectors.SetTextureType, Metal.TextureType2D);
                ObjC.SendVoid(descriptor, Selectors.SetPixelFormat, AppKit.PixelFormatBGRA8Unorm);
                ObjC.SendVoid(descriptor, Selectors.SetWidth, (nuint)width);
                ObjC.SendVoid(descriptor, Selectors.SetHeight, (nuint)height);
                ObjC.SendVoid(descriptor, Selectors.SetUsage, Metal.TextureUsageShaderReadWrite);
                ObjC.SendVoid(descriptor, Selectors.SetStorageMode, Metal.StorageModeShared);

                nint texture = ObjC.SendIntPtr(_buffer.DangerousGetHandle(), Selectors.NewTextureWithDescriptorOffsetBytesPerRow, descriptor, (nuint)0, _bytesPerRow);
                Assert.NotEqual(0, texture);
                _texture = new ObjCHandle(texture);
            }
            finally
            {
                ObjC.Release(descriptor);
            }

            // Poison the target so a pixel the kernel skips can't pass as black.
            new Span<byte>(_contents, checked((int)(_bytesPerRow * (nuint)height))).Fill(0x5A);
        }

        public int Width { get; }

        public int Height { get; }

        public nint Texture => _texture.DangerousGetHandle();

        /// <summary>The pixel at (x, y), converted from the stored B, G, R, A byte order.</summary>
        public Rgba32 Read(int x, int y)
        {
            byte* p = _contents + ((nuint)y * _bytesPerRow) + ((nuint)x * 4);
            return new Rgba32(p[2], p[1], p[0], p[3]);
        }

        public void Dispose()
        {
            _texture.Dispose();
            _buffer.Dispose();
        }
    }
}
