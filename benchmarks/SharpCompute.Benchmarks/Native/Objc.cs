using System;
using System.Runtime.InteropServices;

namespace SharpCompute.Benchmarks.Native;

/// <summary>
/// The Objective-C calls the native baselines make themselves: MetalPerformanceShaders has no
/// SharpCompute wrapper, and a control must not go through the code it is a control for. Each
/// <c>objc_msgSend</c> overload is one exact native call shape (arm64 has no variadic fallback, so
/// the managed signature must match the method's C signature); <c>LibraryImport</c> generates the
/// marshalling at build time, so this stays AOT-safe like the runtime's own interop.
/// </summary>
internal static unsafe partial class Objc
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string MsgSend = "objc_msgSend";

    [LibraryImport(LibObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetClass(string name);

    [LibraryImport(LibObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint Sel(string name);

    [LibraryImport(LibObjC, EntryPoint = "objc_autoreleasePoolPush")]
    internal static partial nint PoolPush();

    [LibraryImport(LibObjC, EntryPoint = "objc_autoreleasePoolPop")]
    internal static partial void PoolPop(nint pool);

    // id f(id, SEL) — alloc, commandBuffer
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint Id(nint receiver, nint selector);

    // id f(id, SEL, id, id) — initWithBuffer:descriptor:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint Id(nint receiver, nint selector, nint arg0, nint arg1);

    // id f(id, SEL, NSUInteger, NSUInteger, NSUInteger, MPSDataType) — matrixDescriptorWithRows:columns:rowBytes:dataType:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint Id(nint receiver, nint selector, nuint arg0, nuint arg1, nuint arg2, uint arg3);

    // id f(id, SEL, id, BOOL, BOOL, NSUInteger, NSUInteger, NSUInteger, double, double)
    // — initWithDevice:transposeLeft:transposeRight:resultRows:resultColumns:interiorColumns:alpha:beta:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint Id(nint receiver, nint selector, nint arg0, byte arg1, byte arg2, nuint arg3, nuint arg4, nuint arg5, double arg6, double arg7);

    // void f(id, SEL) — commit, waitUntilCompleted, release
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void Void(nint receiver, nint selector);

    // void f(id, SEL, id, id, id, id) — encodeToCommandBuffer:leftMatrix:rightMatrix:resultMatrix:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void Void(nint receiver, nint selector, nint arg0, nint arg1, nint arg2, nint arg3);

    // NSUInteger f(id, SEL) — status
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nuint UInt(nint receiver, nint selector);

    // double f(id, SEL) — GPUStartTime, GPUEndTime
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial double Double(nint receiver, nint selector);

    /// <summary>An autorelease pool for the scope of a <c>using</c>: command buffers are autoreleased.</summary>
    internal readonly ref struct Pool
    {
        private readonly nint _pool;

        public Pool() => _pool = PoolPush();

        public void Dispose() => PoolPop(_pool);
    }
}

/// <summary>The selectors the native baselines send, registered once.</summary>
internal static class Sels
{
    public static readonly nint Alloc = Objc.Sel("alloc");
    public static readonly nint Release = Objc.Sel("release");
    public static readonly nint CommandBuffer = Objc.Sel("commandBuffer");
    public static readonly nint Commit = Objc.Sel("commit");
    public static readonly nint WaitUntilCompleted = Objc.Sel("waitUntilCompleted");
    public static readonly nint Status = Objc.Sel("status");
    public static readonly nint GpuStartTime = Objc.Sel("GPUStartTime");
    public static readonly nint GpuEndTime = Objc.Sel("GPUEndTime");
    public static readonly nint MatrixDescriptor = Objc.Sel("matrixDescriptorWithRows:columns:rowBytes:dataType:");
    public static readonly nint InitWithBufferDescriptor = Objc.Sel("initWithBuffer:descriptor:");
    public static readonly nint InitMatrixMultiplication =
        Objc.Sel("initWithDevice:transposeLeft:transposeRight:resultRows:resultColumns:interiorColumns:alpha:beta:");
    public static readonly nint EncodeMatrixMultiplication = Objc.Sel("encodeToCommandBuffer:leftMatrix:rightMatrix:resultMatrix:");
}
