using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SharpCompute.Benchmarks.Native;

/// <summary>
/// <c>C = A · B</c> for n × n single-precision matrices with Apple's own kernel,
/// <c>MPSMatrixMultiplication</c>: the native reference a generated SGEMM is measured against. It
/// multiplies the same three buffers the SharpCompute kernels use (through their
/// <see cref="Buffer{T}.NativeHandle"/>), in a command buffer of the device's own queue, so the only
/// thing that differs from a SharpCompute row is who wrote the kernel.
/// </summary>
/// <remarks>
/// Not thread-safe. The matrices and the kernel object are created once, outside the timed region,
/// as a pipeline is; a sample then encodes, commits and waits.
/// </remarks>
public sealed class MpsMatrixMultiply : IDisposable
{
    private const string Framework = "/System/Library/Frameworks/MetalPerformanceShaders.framework/MetalPerformanceShaders";

    // MPSDataTypeFloatBit | 32
    private const uint DataTypeFloat32 = 0x10000000 | 32;

    // MTLCommandBufferStatusError
    private const nuint StatusError = 5;

    private readonly nint _queue;
    private readonly nint _left;
    private readonly nint _right;
    private readonly nint _result;
    private readonly nint _kernel;

    public MpsMatrixMultiply(GraphicsDevice device, int n, ReadOnlyBuffer<float> a, ReadOnlyBuffer<float> b, ReadWriteBuffer<float> c)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(a.Length, n * n);
        ArgumentOutOfRangeException.ThrowIfLessThan(b.Length, n * n);
        ArgumentOutOfRangeException.ThrowIfLessThan(c.Length, n * n);

        // dlopen: the classes below do not exist until the framework is loaded.
        NativeLibrary.Load(Framework);

        using Objc.Pool pool = new();
        _queue = device.NativeCommandQueueHandle;

        // Rows are tightly packed, as the SharpCompute kernels index them (row * n + column).
        nint descriptor = Objc.Id(Class("MPSMatrixDescriptor"), Sels.MatrixDescriptor, (nuint)n, (nuint)n, (nuint)n * sizeof(float), DataTypeFloat32);
        _left = NewMatrix(a.NativeHandle, descriptor);
        _right = NewMatrix(b.NativeHandle, descriptor);
        _result = NewMatrix(c.NativeHandle, descriptor);

        _kernel = Objc.Id(
            Objc.Id(Class("MPSMatrixMultiplication"), Sels.Alloc), Sels.InitMatrixMultiplication,
            device.NativeHandle, 0, 0, (nuint)n, (nuint)n, (nuint)n, 1.0, 0.0);

        if (_kernel == 0)
        {
            throw new InvalidOperationException("MPSMatrixMultiplication could not be created.");
        }
    }

    /// <summary>
    /// Encodes one multiplication into a command buffer of its own, commits it and waits, as
    /// <c>device.For</c> does for a SharpCompute kernel. Returns the command buffer's GPU execution
    /// time (<c>GPUEndTime − GPUStartTime</c>, what <see cref="GraphicsDevice.LastGpuTime"/> reports
    /// for a dispatch) and the wall time of the whole call, both in seconds.
    /// </summary>
    public (double GpuSeconds, double WallSeconds) Run()
    {
        using Objc.Pool pool = new();
        long start = Stopwatch.GetTimestamp();
        nint commandBuffer = Objc.Id(_queue, Sels.CommandBuffer);

        Objc.Void(_kernel, Sels.EncodeMatrixMultiplication, commandBuffer, _left, _right, _result);
        Objc.Void(commandBuffer, Sels.Commit);
        Objc.Void(commandBuffer, Sels.WaitUntilCompleted);
        double wall = Stopwatch.GetElapsedTime(start).TotalSeconds;

        if (Objc.UInt(commandBuffer, Sels.Status) == StatusError)
        {
            throw new InvalidOperationException("The GPU failed to execute MPSMatrixMultiplication.");
        }

        return (Objc.Double(commandBuffer, Sels.GpuEndTime) - Objc.Double(commandBuffer, Sels.GpuStartTime), wall);
    }

    public void Dispose()
    {
        Objc.Void(_kernel, Sels.Release);
        Objc.Void(_result, Sels.Release);
        Objc.Void(_right, Sels.Release);
        Objc.Void(_left, Sels.Release);
    }

    private static nint NewMatrix(nint buffer, nint descriptor)
    {
        nint matrix = Objc.Id(Objc.Id(Class("MPSMatrix"), Sels.Alloc), Sels.InitWithBufferDescriptor, buffer, descriptor);
        return matrix != 0 ? matrix : throw new InvalidOperationException("MPSMatrix could not be created.");
    }

    private static nint Class(string name)
    {
        nint value = Objc.GetClass(name);
        return value != 0 ? value : throw new InvalidOperationException($"The Objective-C class {name} was not found.");
    }
}
