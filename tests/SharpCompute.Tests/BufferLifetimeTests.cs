using System;
using System.Runtime.CompilerServices;
using System.Threading;
using SharpCompute;

namespace SharpCompute.Tests;

/// <summary>
/// Regression: the copy helpers must keep the buffer rooted until the copy finishes, or a GC on
/// another thread can finalize the handle and free the MTLBuffer under the memcpy.
/// </summary>
public sealed class BufferLifetimeTests
{
    [MetalFact]
    public void CopyHelpers_KeepBufferAliveUnderConcurrentGC()
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
            for (int i = 0; i < 40; i++)
            {
                float[] result = AllocateAndCopy(1 << 20);
                Assert.Equal(1 << 20, result.Length);
                Assert.Equal(7f, result[^1]);
            }
        }
        finally
        {
            cts.Cancel();
            collector.Join();
        }
    }

    // Fully optimized and not inlined so the JIT can treat the buffer as dead after AsSpan().
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static float[] AllocateAndCopy(int length)
    {
        ReadWriteBuffer<float> buffer = GraphicsDevice.GetDefault().AllocateReadWriteBuffer<float>(length);
        buffer.AsSpan().Fill(7f);
        return buffer.ToArray();
    }
}
