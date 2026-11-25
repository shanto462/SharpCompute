using System;
using SharpCompute.Interop;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests.NativeInterop;

public unsafe class NativeTextureTests
{
    private const int Width = 13;
    private const int Height = 7;

    [MetalFact]
    public void PrivateTexture_IsGpuOnly_ButShadersAndGpuCopiesWork()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlTexture = NativeMetal.NewTexture(device, Width, Height, storageMode: NativeMetal.ModePrivate);

        try
        {
            using ReadWriteTexture2D<Rgba32> texture = device.WrapNativeReadWriteTexture2D<Rgba32>(mtlTexture);
            ReadOnlyTexture2D<Rgba32> view = texture.AsReadOnly();
            Rgba32[] pixels = new Rgba32[Width * Height];

            Assert.Equal(Width, texture.Width);
            Assert.Equal(Height, texture.Height);
            Assert.Equal(mtlTexture, texture.NativeHandle);
            Assert.Equal(mtlTexture, view.NativeHandle);
            Assert.False(texture.IsCpuAccessible);
            Assert.False(view.IsCpuAccessible);

            Assert.Contains("private (GPU-only) storage", Assert.Throws<NotSupportedException>(() => texture[0, 0]).Message);
            Assert.Throws<NotSupportedException>(() => texture[0, 0] = default);
            Assert.Throws<NotSupportedException>(() => view[1, 1]);
            Assert.Throws<NotSupportedException>(() => view.Sample(Sampler.LinearClamp, new Float2(0.5f, 0.5f)));
            Assert.Throws<NotSupportedException>(() => texture.CopyTo(pixels));
            Assert.Throws<NotSupportedException>(() => texture.CopyFrom(pixels));
            Assert.Throws<NotSupportedException>(() => texture.ToArray());
            Assert.Throws<NotSupportedException>(() => device.ForCpu(Width, Height, new WritePositionRgba32(texture)));

            // The GPU writes it, and GPU copies read it back into memory the CPU can see.
            device.For(Width, Height, new WritePositionRgba32(texture));

            using ReadWriteTexture2D<Rgba32> copy = device.AllocateReadWriteTexture2D<Rgba32>(Width, Height);
            using ReadWriteBuffer<Rgba32> buffer = device.AllocateReadWriteBuffer<Rgba32>(Width * Height);
            device.Copy(texture, copy);
            device.Copy(view, buffer);

            Rgba32[] expected = ExpectedPositions();
            Assert.Equal(expected, copy.ToArray());
            Assert.Equal(expected, buffer.ToArray());

            // And back in: buffer -> private texture -> shared texture.
            copy.CopyFrom(new Rgba32[Width * Height]);
            device.Copy(buffer, texture);
            device.Copy(texture, copy);
            Assert.Equal(expected, copy.ToArray());
        }
        finally
        {
            ObjC.Release(mtlTexture);
        }
    }

    [MetalTheory]
    [InlineData(0)] // MTLStorageModeShared
    [InlineData(1)] // MTLStorageModeManaged
    public void SharedTexture_CopiesThroughMetal_ButIsNotAddressable(int storageMode)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlTexture = NativeMetal.NewTexture(device, Width, Height, storageMode: (nuint)storageMode);

        try
        {
            using ReadWriteTexture2D<Rgba32> texture = device.WrapNativeReadWriteTexture2D<Rgba32>(mtlTexture);

            Assert.False(texture.IsCpuAccessible);
            NotSupportedException exception = Assert.Throws<NotSupportedException>(() => texture[0, 0]);
            Assert.Contains("not a linear view of a buffer", exception.Message);
            Assert.Throws<NotSupportedException>(() => device.ForCpu(Width, Height, new WritePositionRgba32(texture)));

            // getBytes / replaceRegion: a CPU round trip, then the GPU sees what the CPU wrote.
            Rgba32[] source = TextureTests.RandomPixels(Width * Height, seed: 11);
            texture.CopyFrom(source);
            Assert.Equal(source, texture.ToArray());

            using ReadWriteTexture2D<Rgba32> copy = device.AllocateReadWriteTexture2D<Rgba32>(Width, Height);
            device.Copy(texture, copy);
            Assert.Equal(source, copy.ToArray());

            // A short source covers whole rows and then the start of the next one; the rest is kept.
            Rgba32[] partial = new Rgba32[(2 * Width) + 3];
            texture.CopyFrom(partial);
            Rgba32[] after = texture.ToArray();
            Assert.Equal(partial, after.AsSpan(0, partial.Length).ToArray());
            Assert.Equal(source.AsSpan(partial.Length).ToArray(), after.AsSpan(partial.Length).ToArray());

            // And what the GPU writes, the CPU copies out.
            device.For(Width, Height, new WritePositionRgba32(texture));
            Rgba32[] read = new Rgba32[(Width * Height) + 5];
            texture.CopyTo(read);
            Assert.Equal(ExpectedPositions(), read.AsSpan(0, Width * Height).ToArray());
            Assert.Throws<ArgumentException>(() => texture.CopyTo(new Rgba32[(Width * Height) - 1]));
            Assert.Throws<ArgumentException>(() => texture.CopyFrom(new Rgba32[(Width * Height) + 1]));
        }
        finally
        {
            ObjC.Release(mtlTexture);
        }
    }

    [MetalFact]
    public void LinearTextureOverASharedBuffer_IsCpuAccessible_InPlace()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nuint alignment = device.GetMinimumLinearTextureAlignment(NativeMetal.RGBA8Unorm);
        nuint bytesPerRow = ((nuint)(Width * 4) + alignment - 1) / alignment * alignment;
        nuint offset = alignment;
        nint mtlBuffer = NativeMetal.NewBuffer(device, offset + (bytesPerRow * Height));
        nint mtlTexture = NativeMetal.NewLinearTexture(mtlBuffer, Width, Height, offset, bytesPerRow);

        try
        {
            byte* contents = (byte*)ObjC.SendIntPtr(mtlBuffer, Selectors.Contents);
            nuint before = NativeMetal.RetainCount(mtlBuffer);

            using (ReadWriteTexture2D<Rgba32> texture = device.WrapNativeReadWriteTexture2D<Rgba32>(mtlTexture))
            {
                Assert.True(texture.IsCpuAccessible);
                Assert.True(NativeMetal.RetainCount(mtlBuffer) > before);

                // The indexers address the app's buffer, at its offset and row pitch.
                texture[2, 3] = new Float4(1f, 0f, 0f, 1f);
                Rgba32* pixel = (Rgba32*)(contents + offset + (3 * bytesPerRow)) + 2;
                Assert.Equal(new Rgba32(255, 0, 0, 255), *pixel);

                device.ForCpu(Width, Height, new WritePositionRgba32(texture));
                Assert.Equal(ExpectedPositions(), texture.ToArray());

                texture.CopyFrom(new Rgba32[Width * Height]);
                device.For(Width, Height, new WritePositionRgba32(texture));
                Assert.Equal(ExpectedPositions(), texture.ToArray());
            }

            ComputeContextTests.WaitForRetainCount(mtlBuffer, before);
        }
        finally
        {
            ObjC.Release(mtlTexture);
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalFact]
    public void LinearTextureOverAPrivateBuffer_IsGpuOnly()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nuint alignment = device.GetMinimumLinearTextureAlignment(NativeMetal.RGBA8Unorm);
        nuint bytesPerRow = ((nuint)(Width * 4) + alignment - 1) / alignment * alignment;
        nint mtlBuffer = NativeMetal.NewBuffer(device, bytesPerRow * Height, NativeMetal.StoragePrivate);
        nint mtlTexture = NativeMetal.NewLinearTexture(mtlBuffer, Width, Height, 0, bytesPerRow, NativeMetal.ModePrivate);

        try
        {
            using ReadOnlyTexture2D<Rgba32> texture = device.WrapNativeReadOnlyTexture2D<Rgba32>(mtlTexture);

            Assert.False(texture.IsCpuAccessible);
            Assert.Throws<NotSupportedException>(() => texture[0, 0]);
            Assert.Throws<NotSupportedException>(() => texture.ToArray());
        }
        finally
        {
            ObjC.Release(mtlTexture);
            ObjC.Release(mtlBuffer);
        }
    }

    [MetalTheory]
    [InlineData(80, "BGRA8Unorm")]
    [InlineData(71, "RGBA8Unorm_sRGB (Rgba32Srgb)")]
    [InlineData(55, "R32Float (float)")]
    [InlineData(30, "RG8Unorm (Rg16)")]
    [InlineData(115, "RGBA16Float (Rgba16Float)")]
    public void PixelFormatThatDoesNotMatchTheElementType_IsRejected(int pixelFormat, string described)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlTexture = NativeMetal.NewTexture(device, 4, 4, (nuint)pixelFormat, NativeMetal.UsageShaderRead);

        try
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(() => device.WrapNativeReadOnlyTexture2D<Rgba32>(mtlTexture));

            Assert.Equal("mtlTexture", exception.ParamName);
            Assert.Contains(described, exception.Message);
            Assert.Contains("needs RGBA8Unorm", exception.Message);
            Assert.Equal(1u, NativeMetal.RetainCount(mtlTexture));
        }
        finally
        {
            ObjC.Release(mtlTexture);
        }
    }

    [MetalFact]
    public void EachSupportedFormat_WrapsAsItsElementType()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        AssertWraps<Rgba32>(device, 70, readWrite: true);
        AssertWraps<Rgba32Srgb>(device, 71, readWrite: false);
        AssertWraps<R8>(device, 10, readWrite: true);
        AssertWraps<Rg16>(device, 30, readWrite: false);
        AssertWraps<Half>(device, 25, readWrite: true);
        AssertWraps<Rgba16Float>(device, 115, readWrite: true);
        AssertWraps<float>(device, 55, readWrite: true);
        AssertWraps<Float4>(device, 125, readWrite: true);

        static void AssertWraps<T>(GraphicsDevice device, nuint pixelFormat, bool readWrite)
            where T : unmanaged
        {
            nint mtlTexture = NativeMetal.NewTexture(device, 3, 2, pixelFormat);

            try
            {
                using (ReadOnlyTexture2D<T> texture = device.WrapNativeReadOnlyTexture2D<T>(mtlTexture))
                {
                    Assert.Equal(3, texture.Width);
                }

                if (readWrite)
                {
                    using ReadWriteTexture2D<T> texture = device.WrapNativeReadWriteTexture2D<T>(mtlTexture);
                    Assert.Equal(2, texture.Height);
                }
                else
                {
                    // Apple GPUs cannot read and write these in one shader, as for allocated textures.
                    Assert.Throws<NotSupportedException>(() => device.WrapNativeReadWriteTexture2D<T>(mtlTexture));
                }
            }
            finally
            {
                ObjC.Release(mtlTexture);
            }
        }
    }

    [MetalFact]
    public void UsageFlags_AreValidated()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint readOnly = NativeMetal.NewTexture(device, 4, 4, usage: NativeMetal.UsageShaderRead);
        nint renderOnly = NativeMetal.NewTexture(device, 4, 4, usage: NativeMetal.UsageRenderTarget);
        nint writeOnly = NativeMetal.NewTexture(device, 4, 4, usage: NativeMetal.UsageShaderWrite);

        try
        {
            // ShaderRead is enough for a read-only texture, but a read-write one needs ShaderWrite too.
            using (ReadOnlyTexture2D<Rgba32> texture = device.WrapNativeReadOnlyTexture2D<Rgba32>(readOnly))
            {
                Assert.Equal(4, texture.Width);
            }

            Assert.Contains("MTLTextureUsageShaderWrite", Assert.Throws<ArgumentException>(() => device.WrapNativeReadWriteTexture2D<Rgba32>(readOnly)).Message);
            Assert.Contains("MTLTextureUsageShaderRead", Assert.Throws<ArgumentException>(() => device.WrapNativeReadOnlyTexture2D<Rgba32>(renderOnly)).Message);
            Assert.Throws<ArgumentException>(() => device.WrapNativeReadWriteTexture2D<Rgba32>(writeOnly));
            Assert.Throws<ArgumentException>(() => device.WrapNativeReadOnlyTexture2D<Rgba32>(writeOnly));
        }
        finally
        {
            ObjC.Release(readOnly);
            ObjC.Release(renderOnly);
            ObjC.Release(writeOnly);
        }
    }

    [MetalFact]
    public void TexturesThatAreNotSingleSample2D_AreRejected()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint array = NativeMetal.NewTexture(device, 4, 4, textureType: NativeMetal.Type2DArray, arrayLength: 2);
        nint multisample = NativeMetal.NewTexture(
            device, 4, 4, usage: NativeMetal.UsageRenderTarget | NativeMetal.UsageShaderRead, textureType: NativeMetal.Type2DMultisample, sampleCount: 4);
        nint untracked = NativeMetal.NewTexture(device, 4, 4, untracked: true);
        nint buffer = NativeMetal.NewBuffer(device, 64);

        try
        {
            Assert.Contains("MTLTextureType2DArray", Assert.Throws<ArgumentException>(() => device.WrapNativeReadOnlyTexture2D<Rgba32>(array)).Message);
            Assert.Contains("MTLTextureType2DMultisample", Assert.Throws<ArgumentException>(() => device.WrapNativeReadOnlyTexture2D<Rgba32>(multisample)).Message);
            Assert.Contains("Untracked", Assert.Throws<ArgumentException>(() => device.WrapNativeReadWriteTexture2D<Rgba32>(untracked)).Message);
            Assert.Contains("id<MTLTexture>", Assert.Throws<ArgumentException>(() => device.WrapNativeReadOnlyTexture2D<Rgba32>(buffer)).Message);
            Assert.Equal("mtlTexture", Assert.Throws<ArgumentException>(() => device.WrapNativeReadOnlyTexture2D<Rgba32>(0)).ParamName);
            Assert.Throws<NotSupportedException>(() => device.WrapNativeReadOnlyTexture2D<int>(array));

            foreach (nint rejected in (ReadOnlySpan<nint>)[array, multisample, untracked, buffer])
            {
                Assert.Equal(1u, NativeMetal.RetainCount(rejected));
            }
        }
        finally
        {
            ObjC.Release(array);
            ObjC.Release(multisample);
            ObjC.Release(untracked);
            ObjC.Release(buffer);
        }
    }

    [MetalFact]
    public void MipmappedTexture_UsesLevel0Only()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlTexture = NativeMetal.NewTexture(device, 16, 8, storageMode: NativeMetal.ModeShared, mipmapLevels: 5);

        try
        {
            using ReadWriteTexture2D<Rgba32> texture = device.WrapNativeReadWriteTexture2D<Rgba32>(mtlTexture);
            using ReadWriteTexture2D<Rgba32> plain = device.AllocateReadWriteTexture2D<Rgba32>(16, 8);

            Assert.Equal(16, texture.Width);
            Assert.Equal(8, texture.Height);

            device.For(16, 8, new WritePositionRgba32(texture));
            device.Copy(texture, plain);

            Rgba32[] level0 = plain.ToArray();
            Assert.Equal(new Rgba32(7, 248, 7 ^ 85, 255), level0[1]);
            Assert.Equal(level0, texture.ToArray());

            // A GPU copy into the mipmapped texture writes level 0 alone.
            plain.CopyFrom(new Rgba32[16 * 8]);
            device.Copy(plain, texture);
            Assert.All(texture.ToArray(), pixel => Assert.Equal(default, pixel));
        }
        finally
        {
            ObjC.Release(mtlTexture);
        }
    }

    [MetalFact]
    public void Wrap_RetainsTheTexture_AndKeepsItAliveWhilePending()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlTexture = NativeMetal.NewTexture(device, Width, Height);

        try
        {
            ReadWriteTexture2D<Rgba32> texture = device.WrapNativeReadWriteTexture2D<Rgba32>(mtlTexture);
            Assert.Equal(2u, NativeMetal.RetainCount(mtlTexture));

            using ReadWriteBuffer<Rgba32> readback = device.AllocateReadWriteBuffer<Rgba32>(Width * Height);
            ComputeContext context = device.CreateComputeContext();
            context.For(Width, Height, new WritePositionRgba32(texture));
            context.Copy(texture, readback);

            texture.Dispose();
            Assert.Throws<ObjectDisposedException>(() => texture.NativeHandle);
            Assert.True(NativeMetal.RetainCount(mtlTexture) > 1);

            context.Dispose();

            Assert.Equal(ExpectedPositions(), readback.ToArray());
            ComputeContextTests.WaitForRetainCount(mtlTexture, 1);
        }
        finally
        {
            ObjC.Release(mtlTexture);
        }
    }

    [MetalFact]
    public void TwoWrappersOfOneTexture_CannotBeCopiedOntoEachOther()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        nint mtlTexture = NativeMetal.NewTexture(device, 4, 4);

        try
        {
            using ReadWriteTexture2D<Rgba32> first = device.WrapNativeReadWriteTexture2D<Rgba32>(mtlTexture);
            using ReadOnlyTexture2D<Rgba32> second = device.WrapNativeReadOnlyTexture2D<Rgba32>(mtlTexture);

            Assert.Contains("same texture", Assert.Throws<ArgumentException>(() => device.Copy(second, first)).Message);
        }
        finally
        {
            ObjC.Release(mtlTexture);
        }
    }

    [MetalFact]
    public void NativeHandle_RoundTrips_ToTheSamePixels()
    {
        // A texture SharpCompute allocates is a linear view of a shared buffer, so wrapping its handle
        // gives a CPU-accessible texture over the very same memory.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> ours = device.AllocateReadWriteTexture2D<Rgba32>(Width, Height);

        Assert.Equal((nuint)Width, ObjC.SendNUInt(ours.NativeHandle, Selectors.Width));
        Assert.Equal(ours.NativeHandle, ours.AsReadOnly().NativeHandle);

        using (ReadOnlyTexture2D<Rgba32> again = device.WrapNativeReadOnlyTexture2D<Rgba32>(ours.NativeHandle))
        {
            Assert.True(again.IsCpuAccessible);

            ours[4, 5] = new Float4(0f, 1f, 0f, 1f);
            Assert.Equal(new Float4(0f, 1f, 0f, 1f), again[4, 5]);

            device.For(Width, Height, new WritePositionRgba32(ours));
            Assert.Equal(ExpectedPositions(), again.ToArray());
        }

        Assert.Equal(ExpectedPositions(), ours.ToArray());
    }

    /// <summary>What <see cref="WritePositionRgba32"/> writes into a <see cref="Width"/> × <see cref="Height"/> texture.</summary>
    private static Rgba32[] ExpectedPositions()
    {
        Rgba32[] pixels = new Rgba32[Width * Height];

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int value = ((x * 7) + (y * 13)) & 255;
                pixels[(y * Width) + x] = new Rgba32((byte)value, (byte)(255 - value), (byte)(value ^ 85), 255);
            }
        }

        return pixels;
    }
}
