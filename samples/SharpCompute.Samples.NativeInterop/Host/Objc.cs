// Original SharpCompute sample (MIT).
using System;
using System.Runtime.InteropServices;

namespace SharpCompute.Samples.NativeInterop.Host;

/// <summary>
/// The host renderer's own Objective-C bindings: what an app that already talks to Metal has, written
/// here without any binding library. Each <c>objc_msgSend</c> overload is one exact native call shape
/// (arm64 has no variadic fallback, so the managed signature must match the method's C signature).
/// Nothing here comes from SharpCompute: the host would look the same without it.
/// </summary>
internal static unsafe partial class Objc
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string MsgSend = "objc_msgSend";
    private const string MetalFramework = "/System/Library/Frameworks/Metal.framework/Metal";

    static Objc() => NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");

    [LibraryImport(MetalFramework, EntryPoint = "MTLCreateSystemDefaultDevice")]
    internal static partial nint MTLCreateSystemDefaultDevice();

    [LibraryImport(LibObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetClass(string name);

    [LibraryImport(LibObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Sel(string name);

    [LibraryImport(LibObjC, EntryPoint = "objc_autoreleasePoolPush")]
    internal static partial nint PoolPush();

    [LibraryImport(LibObjC, EntryPoint = "objc_autoreleasePoolPop")]
    internal static partial void PoolPop(nint pool);

    // id f(id, SEL)
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint Id(nint receiver, nint selector);

    // id f(id, SEL, id) — newFunctionWithName:, newTextureWithDescriptor:, renderCommandEncoderWithDescriptor:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint Id(nint receiver, nint selector, nint arg0);

    // id f(id, SEL, NSUInteger) — objectAtIndexedSubscript:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint Id(nint receiver, nint selector, nuint arg0);

    // id f(id, SEL, id, NSError**) — newRenderPipelineStateWithDescriptor:error:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint Id(nint receiver, nint selector, nint arg0, nint* error);

    // id f(id, SEL, id, id, NSError**) — newLibraryWithSource:options:error:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint Id(nint receiver, nint selector, nint arg0, nint arg1, nint* error);

    // id f(id, SEL, NSUInteger, NSUInteger) — newBufferWithLength:options:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint Id(nint receiver, nint selector, nuint arg0, nuint arg1);

    // id f(id, SEL, const unichar*, NSUInteger) — initWithCharacters:length:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint Id(nint receiver, nint selector, char* arg0, nuint arg1);

    // NSUInteger f(id, SEL) — status, width, height
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nuint UInt(nint receiver, nint selector);

    // double f(id, SEL) — GPUStartTime, GPUEndTime
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial double Double(nint receiver, nint selector);

    // const char* f(id, SEL) — UTF8String
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial byte* Utf8(nint receiver, nint selector);

    // void f(id, SEL) — release, endEncoding, commit, waitUntilCompleted
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void Void(nint receiver, nint selector);

    // void f(id, SEL, id) — setVertexFunction:, setRenderPipelineState:, setTexture:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void Void(nint receiver, nint selector, nint arg0);

    // void f(id, SEL, NSUInteger) — setPixelFormat:, setLoadAction:, setUsage:, the blend factors
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void Void(nint receiver, nint selector, nuint arg0);

    // void f(id, SEL, BOOL) — setBlendingEnabled:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void Void(nint receiver, nint selector, byte arg0);

    // void f(id, SEL, MTLClearColor) — setClearColor: (four doubles: passed in SIMD registers)
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void Void(nint receiver, nint selector, ClearColor arg0);

    // void f(id, SEL, id, NSUInteger) — setFragmentTexture:atIndex:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void Void(nint receiver, nint selector, nint arg0, nuint arg1);

    // void f(id, SEL, id, NSUInteger, NSUInteger) — setVertexBuffer:offset:atIndex:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void Void(nint receiver, nint selector, nint arg0, nuint arg1, nuint arg2);

    // void f(id, SEL, const void*, NSUInteger, NSUInteger) — setVertexBytes:length:atIndex:, setFragmentBytes:length:atIndex:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void Void(nint receiver, nint selector, void* arg0, nuint arg1, nuint arg2);

    // void f(id, SEL, NSUInteger, NSUInteger, NSUInteger, NSUInteger) — drawPrimitives:vertexStart:vertexCount:instanceCount:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void Void(nint receiver, nint selector, nuint arg0, nuint arg1, nuint arg2, nuint arg3);

    // void f(id, SEL, MTLRegion, NSUInteger, const void*, NSUInteger) — replaceRegion:mipmapLevel:withBytes:bytesPerRow:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void Void(nint receiver, nint selector, Region arg0, nuint arg1, void* arg2, nuint arg3);

    internal static void Release(nint obj)
    {
        if (obj != 0)
        {
            Void(obj, Sels.Release);
        }
    }

    internal static nint Retain(nint obj) => Id(obj, Sels.Retain);

    /// <summary><c>[[cls alloc] init]</c>: a +1 reference.</summary>
    internal static nint New(string className) => Id(Id(GetClass(className), Sels.Alloc), Sels.Init);

    /// <summary>A +1 <c>NSString</c> holding <paramref name="value"/>.</summary>
    internal static nint String(string value)
    {
        fixed (char* chars = value)
        {
            return Id(Id(GetClass("NSString"), Sels.Alloc), Sels.InitWithCharactersLength, chars, (nuint)value.Length);
        }
    }

    /// <summary><c>error.localizedDescription</c>, for an exception message.</summary>
    internal static string Describe(nint error) =>
        error == 0 ? "no error was reported" : Marshal.PtrToStringUTF8((nint)Utf8(Id(error, Sels.LocalizedDescription), Sels.Utf8String)) ?? "unknown error";
}

/// <summary><c>MTLClearColor</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct ClearColor(double red, double green, double blue, double alpha)
{
    public readonly double Red = red;
    public readonly double Green = green;
    public readonly double Blue = blue;
    public readonly double Alpha = alpha;
}

/// <summary><c>MTLRegion</c>: an origin and a size, three <c>NSUInteger</c>s each.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct Region(nuint x, nuint y, nuint width, nuint height)
{
    public readonly nuint X = x;
    public readonly nuint Y = y;
    public readonly nuint Z = 0;
    public readonly nuint Width = width;
    public readonly nuint Height = height;
    public readonly nuint Depth = 1;
}

/// <summary>A scoped <c>@autoreleasepool</c>; dispose it on the thread that pushed it.</summary>
internal readonly ref struct Pool
{
    private readonly nint _pool;

    private Pool(nint pool) => _pool = pool;

    public static Pool Push() => new(Objc.PoolPush());

    public void Dispose() => Objc.PoolPop(_pool);
}

/// <summary>The selectors the host sends, registered once.</summary>
internal static class Sels
{
    public static readonly nint Alloc = Objc.Sel("alloc");
    public static readonly nint Init = Objc.Sel("init");
    public static readonly nint Retain = Objc.Sel("retain");
    public static readonly nint Release = Objc.Sel("release");
    public static readonly nint InitWithCharactersLength = Objc.Sel("initWithCharacters:length:");
    public static readonly nint LocalizedDescription = Objc.Sel("localizedDescription");
    public static readonly nint Utf8String = Objc.Sel("UTF8String");
    public static readonly nint ObjectAtIndexedSubscript = Objc.Sel("objectAtIndexedSubscript:");

    // Device and resources
    public static readonly nint NewCommandQueue = Objc.Sel("newCommandQueue");
    public static readonly nint NewBufferWithLengthOptions = Objc.Sel("newBufferWithLength:options:");
    public static readonly nint NewTextureWithDescriptor = Objc.Sel("newTextureWithDescriptor:");
    public static readonly nint NewLibraryWithSourceOptionsError = Objc.Sel("newLibraryWithSource:options:error:");
    public static readonly nint NewFunctionWithName = Objc.Sel("newFunctionWithName:");
    public static readonly nint NewRenderPipelineStateWithDescriptorError = Objc.Sel("newRenderPipelineStateWithDescriptor:error:");
    public static readonly nint Contents = Objc.Sel("contents");
    public static readonly nint ReplaceRegion = Objc.Sel("replaceRegion:mipmapLevel:withBytes:bytesPerRow:");

    // MTLTextureDescriptor
    public static readonly nint SetTextureType = Objc.Sel("setTextureType:");
    public static readonly nint SetPixelFormat = Objc.Sel("setPixelFormat:");
    public static readonly nint SetWidth = Objc.Sel("setWidth:");
    public static readonly nint SetHeight = Objc.Sel("setHeight:");
    public static readonly nint SetUsage = Objc.Sel("setUsage:");
    public static readonly nint SetStorageMode = Objc.Sel("setStorageMode:");

    // MTLRenderPipelineDescriptor and its color attachment
    public static readonly nint SetVertexFunction = Objc.Sel("setVertexFunction:");
    public static readonly nint SetFragmentFunction = Objc.Sel("setFragmentFunction:");
    public static readonly nint ColorAttachments = Objc.Sel("colorAttachments");
    public static readonly nint SetBlendingEnabled = Objc.Sel("setBlendingEnabled:");
    public static readonly nint SetSourceRGBBlendFactor = Objc.Sel("setSourceRGBBlendFactor:");
    public static readonly nint SetDestinationRGBBlendFactor = Objc.Sel("setDestinationRGBBlendFactor:");
    public static readonly nint SetSourceAlphaBlendFactor = Objc.Sel("setSourceAlphaBlendFactor:");
    public static readonly nint SetDestinationAlphaBlendFactor = Objc.Sel("setDestinationAlphaBlendFactor:");

    // MTLRenderPassDescriptor and its color attachment
    public static readonly nint RenderPassDescriptor = Objc.Sel("renderPassDescriptor");
    public static readonly nint SetTexture = Objc.Sel("setTexture:");
    public static readonly nint SetLoadAction = Objc.Sel("setLoadAction:");
    public static readonly nint SetStoreAction = Objc.Sel("setStoreAction:");
    public static readonly nint SetClearColor = Objc.Sel("setClearColor:");

    // Command buffers and render encoders
    public static readonly nint CommandBuffer = Objc.Sel("commandBuffer");
    public static readonly nint RenderCommandEncoderWithDescriptor = Objc.Sel("renderCommandEncoderWithDescriptor:");
    public static readonly nint SetRenderPipelineState = Objc.Sel("setRenderPipelineState:");
    public static readonly nint SetVertexBufferOffsetAtIndex = Objc.Sel("setVertexBuffer:offset:atIndex:");
    public static readonly nint SetVertexBytesLengthAtIndex = Objc.Sel("setVertexBytes:length:atIndex:");
    public static readonly nint SetFragmentBytesLengthAtIndex = Objc.Sel("setFragmentBytes:length:atIndex:");
    public static readonly nint SetFragmentTextureAtIndex = Objc.Sel("setFragmentTexture:atIndex:");
    public static readonly nint DrawPrimitives = Objc.Sel("drawPrimitives:vertexStart:vertexCount:instanceCount:");
    public static readonly nint EndEncoding = Objc.Sel("endEncoding");
    public static readonly nint Commit = Objc.Sel("commit");
    public static readonly nint WaitUntilCompleted = Objc.Sel("waitUntilCompleted");
    public static readonly nint Status = Objc.Sel("status");
    public static readonly nint Error = Objc.Sel("error");
    public static readonly nint GpuStartTime = Objc.Sel("GPUStartTime");
    public static readonly nint GpuEndTime = Objc.Sel("GPUEndTime");
}

/// <summary>The Metal enumeration values the host uses.</summary>
internal static class Mtl
{
    internal const nuint PixelFormatR8Unorm = 10;
    internal const nuint PixelFormatRGBA8Unorm = 70;
    internal const nuint TextureType2D = 2;
    internal const nuint UsageShaderRead = 0x1;
    internal const nuint UsageShaderWrite = 0x2;
    internal const nuint UsageRenderTarget = 0x4;
    internal const nuint StorageModeShared = 0;
    internal const nuint StorageModePrivate = 2;
    internal const nuint ResourceStorageModeShared = 0;
    internal const nuint LoadActionLoad = 1;
    internal const nuint LoadActionClear = 2;
    internal const nuint StoreActionStore = 1;
    internal const nuint PrimitiveTypeTriangleStrip = 4;
    internal const nuint BlendFactorOne = 1;
    internal const nuint BlendFactorSourceAlpha = 4;
    internal const nuint BlendFactorOneMinusSourceAlpha = 5;
    internal const nuint CommandBufferStatusError = 5;
    internal const nuint LanguageVersion3_2 = (3u << 16) + 2;
}
