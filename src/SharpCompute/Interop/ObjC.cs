using System;
using System.Runtime.InteropServices;

namespace SharpCompute.Interop;

/// <summary>
/// Hand-rolled Objective-C runtime bindings. Every <c>objc_msgSend</c> overload below is one exact
/// native call shape: the arm64 ABI has no variadic-compatible fallback, so the managed signature
/// must match the method's real C signature (receiver, selector, then the declared arguments).
/// </summary>
internal static unsafe partial class ObjC
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string MsgSend = "objc_msgSend";
    private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";

    // libobjc alone does not register NSString/NSError; make sure Foundation is in the process
    // before any class lookup, independent of what the host happens to have loaded already.
    static ObjC() => NativeLibrary.Load(Foundation);

    [LibraryImport(LibObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_getClass(string name);

    [LibraryImport(LibObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint sel_registerName(string name);

    [LibraryImport(LibObjC, EntryPoint = "objc_autoreleasePoolPush")]
    internal static partial nint AutoreleasePoolPush();

    [LibraryImport(LibObjC, EntryPoint = "objc_autoreleasePoolPop")]
    internal static partial void AutoreleasePoolPop(nint pool);

    /// <summary>Looks up a class by name; throws when it is not registered with the runtime.</summary>
    internal static nint GetClass(string name)
    {
        nint cls = objc_getClass(name);

        if (cls == 0)
        {
            throw new InvalidOperationException($"Objective-C class '{name}' was not found.");
        }

        return cls;
    }

    /// <summary>Registers (or looks up) a selector. Selectors are process-global and never freed.</summary>
    internal static nint GetSelector(string name) => sel_registerName(name);

    // id f(id, SEL) — property getters and no-argument factories.
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint SendIntPtr(nint receiver, nint selector);

    // id f(id, SEL, id) — newFunctionWithName:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint SendIntPtr(nint receiver, nint selector, nint arg0);

    // id f(id, SEL, id, NSError**) — newComputePipelineStateWithFunction:error:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint SendIntPtr(nint receiver, nint selector, nint arg0, nint* error);

    // id f(id, SEL, id, id, NSError**) — newLibraryWithSource:options:error:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint SendIntPtr(nint receiver, nint selector, nint arg0, nint arg1, nint* error);

    // id f(id, SEL, NSUInteger, NSUInteger) — newBufferWithLength:options:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint SendIntPtr(nint receiver, nint selector, nuint arg0, nuint arg1);

    // id f(id, SEL, id, NSUInteger, NSUInteger) — newTextureWithDescriptor:offset:bytesPerRow:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint SendIntPtr(nint receiver, nint selector, nint arg0, nuint arg1, nuint arg2);

    // id f(id, SEL, const void*, NSUInteger) — initWithCharacters:length:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint SendIntPtr(nint receiver, nint selector, void* arg0, nuint arg1);

    // id f(id, SEL, const void*, NSUInteger, NSUInteger) — newBufferWithBytes:length:options:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint SendIntPtr(nint receiver, nint selector, void* arg0, nuint arg1, nuint arg2);

    // id f(id, SEL, void*, NSUInteger, NSUInteger, id) — newBufferWithBytesNoCopy:length:options:deallocator:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint SendIntPtr(nint receiver, nint selector, void* arg0, nuint arg1, nuint arg2, nint arg3);

    // NSUInteger f(id, SEL) — length, status, maxTotalThreadsPerThreadgroup, maxBufferLength.
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nuint SendNUInt(nint receiver, nint selector);

    // NSUInteger f(id, SEL, NSUInteger) — minimumLinearTextureAlignmentForPixelFormat:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nuint SendNUInt(nint receiver, nint selector, nuint arg0);

    // void f(id, SEL) — release, endEncoding, commit, waitUntilCompleted.
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void SendVoid(nint receiver, nint selector);

    // void f(id, SEL, id) — setComputePipelineState:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void SendVoid(nint receiver, nint selector, nint arg0);

    // void f(id, SEL, NSUInteger) — setLanguageVersion:, and the MTLTextureDescriptor setters
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void SendVoid(nint receiver, nint selector, nuint arg0);

    // void f(id, SEL, id, NSUInteger) — setTexture:atIndex:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void SendVoid(nint receiver, nint selector, nint arg0, nuint arg1);

    // void f(id, SEL, id, NSUInteger, NSUInteger) — setBuffer:offset:atIndex:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void SendVoid(nint receiver, nint selector, nint arg0, nuint arg1, nuint arg2);

    // void f(id, SEL, const void*, NSUInteger, NSUInteger) — setBytes:length:atIndex:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void SendVoid(nint receiver, nint selector, void* arg0, nuint arg1, nuint arg2);

    // void f(id, SEL, unichar*, NSRange) — getCharacters:range: (NSRange is 16 bytes: two registers).
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void SendVoid(nint receiver, nint selector, void* arg0, NSRange arg1);

    // void f(id, SEL, MTLSize, MTLSize) — dispatchThreads:threadsPerThreadgroup:
    // MTLSize is 24 bytes, so AAPCS64 passes each by reference to a caller-owned copy; declaring the
    // parameters by value lets the marshaller apply that rule instead of us hard-coding it.
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void SendVoid(nint receiver, nint selector, MTLSize arg0, MTLSize arg1);

    /// <summary><c>[obj retain]</c>; nil-safe.</summary>
    internal static nint Retain(nint obj) => SendIntPtr(obj, Selectors.Retain);

    /// <summary><c>[obj release]</c>; nil-safe.</summary>
    internal static void Release(nint obj) => SendVoid(obj, Selectors.Release);
}

/// <summary>Foundation <c>NSRange</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct NSRange(nuint location, nuint length)
{
    public readonly nuint Location = location;
    public readonly nuint Length = length;
}

/// <summary>
/// Scoped <c>@autoreleasepool</c>. Pools are per-thread and strictly nested, so this must be
/// disposed on the thread that created it (a <c>using</c> in a synchronous method).
/// </summary>
internal readonly ref struct AutoreleasePool
{
    private readonly nint _pool;

    private AutoreleasePool(nint pool) => _pool = pool;

    public static AutoreleasePool Push() => new(ObjC.AutoreleasePoolPush());

    public void Dispose()
    {
        if (_pool != 0)
        {
            ObjC.AutoreleasePoolPop(_pool);
        }
    }
}
