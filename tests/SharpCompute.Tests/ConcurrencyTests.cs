using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

public class ConcurrencyTests
{
    private const int ThreadCount = 8;
    private const int Iterations = 150;
    private const int ElementCount = 4096;

    [MetalFact]
    public void ConcurrentDispatches_FromManyThreads_AllProduceCorrectResults()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] input = Enumerable.Range(0, ElementCount).ToArray();

        // Shared by every thread and only ever read by the GPU.
        using ReadOnlyBuffer<int> shared = device.AllocateReadOnlyBuffer<int>(input);

        // AddAmount is used nowhere else, so the first iteration of every thread races to compile it.
        RunOnThreads(ThreadCount, thread =>
        {
            using ReadWriteBuffer<int> output = device.AllocateReadWriteBuffer<int>(ElementCount);
            using ReadWriteBuffer<float> accumulator = device.AllocateReadWriteBuffer<float>(ElementCount);
            accumulator.AsSpan().Fill(1f);

            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                int amount = thread * 100_000 + iteration;

                device.For(ElementCount, new AddAmount(shared, output, amount));

                ReadOnlySpan<int> result = output.AsSpan();

                for (int i = 0; i < result.Length; i++)
                {
                    if (result[i] != i + amount)
                    {
                        Assert.Fail($"Thread {thread}, iteration {iteration}: element {i} was {result[i]}, expected {i + amount}.");
                    }
                }

                // A second shader type interleaved on the same thread, toggling a value in place.
                device.For(ElementCount, new MultiplyByTwo(accumulator, iteration % 2 == 0 ? 2f : 0.5f));
            }

            Assert.All(accumulator.ToArray(), value => Assert.Equal(1f, value));
        });

        Assert.Equal(input, shared.ToArray());
    }

    [MetalFact]
    public void ConcurrentDispatches_WithLargeConstants_EachSeeTheirOwnSnapshot()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        RunOnThreads(ThreadCount, thread =>
        {
            using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(LargeConstants.ValueCount);

            for (int iteration = 0; iteration < 50; iteration++)
            {
                uint bias = (uint)(thread * 1000 + iteration);
                device.For(buffer.Length, new ReadLargeConstants(buffer, CreateConstants(bias)));

                Assert.Equal(bias, buffer[0]);
                Assert.Equal(LargeConstants.ValueCount - 1 + bias, buffer[LargeConstants.ValueCount - 1]);
            }
        });
    }

    [MetalFact]
    public void ConcurrentAllocationAndDisposal_IsSafe()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        RunOnThreads(ThreadCount, thread =>
        {
            for (int iteration = 0; iteration < 200; iteration++)
            {
                using ReadWriteBuffer<int> buffer = device.AllocateReadWriteBuffer<int>(1 + (iteration % 64));
                buffer[0] = thread;

                Assert.Equal(thread, buffer[0]);
            }
        });
    }

    [MetalFact]
    public void DisposingADeviceWhileOtherThreadsDispatch_NeverCrashes()
    {
        for (int round = 0; round < 10; round++)
        {
            GraphicsDevice device = GraphicsDevice.CreateSystemDefault();
            using ManualResetEventSlim running = new();

            Thread disposer = new(() =>
            {
                running.Wait();
                Thread.Sleep(2);
                device.Dispose();
            });
            disposer.Start();

            // Each thread dispatches until the device goes away underneath it. The only acceptable
            // outcomes are a correct result or ObjectDisposedException.
            RunOnThreads(4, _ =>
            {
                try
                {
                    using ReadWriteBuffer<float> buffer = device.AllocateReadWriteBuffer<float>(256);

                    while (true)
                    {
                        buffer.AsSpan().Fill(3f);
                        running.Set();
                        device.For(buffer.Length, new MultiplyByTwo(buffer, 2f));

                        Assert.All(buffer.ToArray(), value => Assert.Equal(6f, value));
                    }
                }
                catch (ObjectDisposedException)
                {
                }
                finally
                {
                    running.Set();
                }
            });

            disposer.Join();
        }
    }

    private static unsafe LargeConstants CreateConstants(uint bias)
    {
        LargeConstants constants = default;

        for (int i = 0; i < LargeConstants.ValueCount; i++)
        {
            constants.Values[i] = i;
        }

        constants.Bias = bias;
        return constants;
    }

    /// <summary>
    /// Runs <paramref name="body"/> on dedicated threads released together by a barrier. Plain
    /// threads rather than the pool: they start with no autorelease pool, which is the case the
    /// runtime has to handle by itself.
    /// </summary>
    private static void RunOnThreads(int count, Action<int> body)
    {
        using Barrier barrier = new(count);
        ConcurrentQueue<Exception> failures = new();

        Thread[] threads = Enumerable.Range(0, count).Select(index => new Thread(() =>
        {
            try
            {
                barrier.SignalAndWait();
                body(index);
            }
            catch (Exception e)
            {
                failures.Enqueue(e);
            }
        })).ToArray();

        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        if (!failures.IsEmpty)
        {
            throw new AggregateException(failures);
        }
    }
}
