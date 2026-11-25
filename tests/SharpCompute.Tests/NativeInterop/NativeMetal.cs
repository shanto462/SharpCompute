using System;
using SharpCompute.Interop;

namespace SharpCompute.Tests.NativeInterop;

/// <summary>
/// What "other Metal code" does in the interop tests: creates buffers, textures, queues and command
/// buffers straight through <c>objc_msgSend</c>, outside SharpCompute. Every method returns a +1
/// reference the test releases (<see cref="ObjC.Release"/>).
/// </summary>
internal static unsafe class NativeMetal
{
    // MTLResourceOptions
    internal const nuint StorageShared = 0;
    internal const nuint StorageManaged = 1 << 4;
    internal const nuint StoragePrivate = 2 << 4;
    internal const nuint HazardUntracked = 1 << 8;

    // MTLStorageMode
    internal const nuint ModeShared = 0;
    internal const nuint ModeManaged = 1;
    internal const nuint ModePrivate = 2;

    // MTLTextureUsage
    internal const nuint UsageShaderRead = 0x1;
    internal const nuint UsageShaderWrite = 0x2;
    internal const nuint UsageRenderTarget = 0x4;

    // MTLPixelFormat
    internal const nuint RGBA8Unorm = 70;
    internal const nuint RGBA8UnormSrgb = 71;
    internal const nuint BGRA8Unorm = 80;
    internal const nuint R32Float = 55;
    internal const nuint RG8Unorm = 30;

    // MTLTextureType
    internal const nuint Type2D = 2;
    internal const nuint Type2DArray = 3;
    internal const nuint Type2DMultisample = 4;

    private static readonly nint NewTextureWithDescriptor = ObjC.GetSelector("newTextureWithDescriptor:");
    private static readonly nint SetMipmapLevelCount = ObjC.GetSelector("setMipmapLevelCount:");
    private static readonly nint SetSampleCount = ObjC.GetSelector("setSampleCount:");
    private static readonly nint SetArrayLength = ObjC.GetSelector("setArrayLength:");
    private static readonly nint SetHazardTrackingMode = ObjC.GetSelector("setHazardTrackingMode:");

    /// <summary><c>[device newBufferWithLength:options:]</c>.</summary>
    internal static nint NewBuffer(GraphicsDevice device, nuint byteLength, nuint options = StorageShared)
    {
        nint buffer = ObjC.SendIntPtr(device.NativeHandle, Selectors.NewBufferWithLengthOptions, byteLength, options);
        Assert.NotEqual(0, buffer);
        return buffer;
    }

    /// <summary><c>[device newTextureWithDescriptor:]</c> for a texture described by the arguments.</summary>
    internal static nint NewTexture(
        GraphicsDevice device,
        int width,
        int height,
        nuint pixelFormat = RGBA8Unorm,
        nuint usage = UsageShaderRead | UsageShaderWrite,
        nuint storageMode = ModePrivate,
        nuint textureType = Type2D,
        int mipmapLevels = 1,
        int sampleCount = 1,
        int arrayLength = 1,
        bool untracked = false)
    {
        using AutoreleasePool pool = AutoreleasePool.Push();
        nint descriptor = NewDescriptor(width, height, pixelFormat, usage, storageMode, textureType, mipmapLevels, sampleCount, arrayLength, untracked);

        try
        {
            nint texture = ObjC.SendIntPtr(device.NativeHandle, NewTextureWithDescriptor, descriptor);
            Assert.NotEqual(0, texture);
            return texture;
        }
        finally
        {
            ObjC.Release(descriptor);
        }
    }

    /// <summary>
    /// A linear texture over <paramref name="buffer"/> (<c>newTextureWithDescriptor:offset:bytesPerRow:</c>),
    /// as an engine that streams pixels through a buffer makes one.
    /// </summary>
    internal static nint NewLinearTexture(nint buffer, int width, int height, nuint offset, nuint bytesPerRow, nuint storageMode = ModeShared)
    {
        using AutoreleasePool pool = AutoreleasePool.Push();
        nint descriptor = NewDescriptor(width, height, RGBA8Unorm, UsageShaderRead | UsageShaderWrite, storageMode, Type2D, 1, 1, 1, untracked: false);

        try
        {
            nint texture = ObjC.SendIntPtr(buffer, Selectors.NewTextureWithDescriptorOffsetBytesPerRow, descriptor, offset, bytesPerRow);
            Assert.NotEqual(0, texture);
            return texture;
        }
        finally
        {
            ObjC.Release(descriptor);
        }
    }

    /// <summary>A retained command buffer of <paramref name="queue"/>.</summary>
    internal static nint NewCommandBuffer(nint queue)
    {
        using AutoreleasePool pool = AutoreleasePool.Push();
        nint commandBuffer = ObjC.SendIntPtr(queue, Selectors.CommandBuffer);
        Assert.NotEqual(0, commandBuffer);
        return ObjC.Retain(commandBuffer);
    }

    /// <summary>Encodes, as the host app would, a blit that sets every byte of <paramref name="buffer"/> to <paramref name="value"/>.</summary>
    internal static void EncodeFill(nint commandBuffer, nint buffer, nuint byteLength, byte value)
    {
        using AutoreleasePool pool = AutoreleasePool.Push();
        nint encoder = ObjC.SendIntPtr(commandBuffer, Selectors.BlitCommandEncoder);
        ObjC.SendVoid(encoder, Selectors.FillBufferRangeValue, buffer, new NSRange(0, byteLength), value);
        ObjC.SendVoid(encoder, Selectors.EndEncoding);
    }

    /// <summary>Encodes, as the host app would, a blit copy of <paramref name="byteLength"/> bytes between two buffers.</summary>
    internal static void EncodeCopy(nint commandBuffer, nint source, nint destination, nuint byteLength)
    {
        using AutoreleasePool pool = AutoreleasePool.Push();
        nint encoder = ObjC.SendIntPtr(commandBuffer, Selectors.BlitCommandEncoder);
        ObjC.SendVoid(encoder, Selectors.CopyFromBufferToBuffer, source, 0, destination, 0, byteLength);
        ObjC.SendVoid(encoder, Selectors.EndEncoding);
    }

    internal static void CommitAndWait(nint commandBuffer)
    {
        ObjC.SendVoid(commandBuffer, Selectors.Commit);
        ObjC.SendVoid(commandBuffer, Selectors.WaitUntilCompleted);
    }

    internal static nuint RetainCount(nint obj) => ComputeContextTests.RetainCount(obj);

    private static nint NewDescriptor(
        int width, int height, nuint pixelFormat, nuint usage, nuint storageMode, nuint textureType, int mipmapLevels, int sampleCount, int arrayLength, bool untracked)
    {
        nint descriptor = ObjC.SendIntPtr(ObjC.SendIntPtr(ObjC.GetClass("MTLTextureDescriptor"), Selectors.Alloc), Selectors.Init);
        ObjC.SendVoid(descriptor, Selectors.SetTextureType, textureType);
        ObjC.SendVoid(descriptor, Selectors.SetPixelFormat, pixelFormat);
        ObjC.SendVoid(descriptor, Selectors.SetWidth, (nuint)width);
        ObjC.SendVoid(descriptor, Selectors.SetHeight, (nuint)height);
        ObjC.SendVoid(descriptor, Selectors.SetUsage, usage);
        ObjC.SendVoid(descriptor, Selectors.SetStorageMode, storageMode);
        ObjC.SendVoid(descriptor, SetMipmapLevelCount, (nuint)mipmapLevels);
        ObjC.SendVoid(descriptor, SetSampleCount, (nuint)sampleCount);
        ObjC.SendVoid(descriptor, SetArrayLength, (nuint)arrayLength);

        if (untracked)
        {
            ObjC.SendVoid(descriptor, SetHazardTrackingMode, (nuint)1);
        }

        return descriptor;
    }
}
