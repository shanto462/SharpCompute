using System;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCompute;

/// <summary>
/// Records several dispatches into one Metal command buffer and runs them with a single commit and
/// a single wait, instead of one round trip per <c>For</c>. Created by
/// <see cref="GraphicsDevice.CreateComputeContext"/>.
/// </summary>
/// <example>
/// <code>
/// using (ComputeContext context = device.CreateComputeContext())
/// {
///     context.For(width, height, new BlurHorizontal(source, temp, weights, width, radius));
///     context.For(width, height, new BlurVertical(temp, output, weights, width, height, radius));
/// } // both passes run here, with one commit and one wait
/// </code>
/// </example>
/// <remarks>
/// <para>
/// <b>Recording.</b> <c>For</c> and <c>ForEach</c> only encode: nothing runs on the GPU until
/// <see cref="Execute"/>, <see cref="ExecuteAsync"/>, <see cref="Dispose"/> or <see cref="DisposeAsync"/>.
/// All dispatches share one compute encoder; the pipeline is switched per dispatch when the shader changes.
/// Shader constants are copied when the dispatch is recorded, so changing the shader struct (or
/// reusing it with other values) afterwards does not affect recorded work.
/// </para>
/// <para>
/// <b>Ordering.</b> Dispatches run in the order they were recorded, and each one sees every write of
/// the dispatches before it (Metal's serial dispatch type), so dependent passes such as the two halves
/// of a separable blur need no barrier. Separate executions of a context, and work committed to the
/// device by other means, run in the order they were submitted; recorded work is submitted when the
/// context executes, not when <c>For</c> is called.
/// </para>
/// <para>
/// <b>Lifetime.</b> Every buffer, texture and pipeline a recorded dispatch binds stays alive until the
/// GPU has finished with it, even if it (or the device) is disposed in between. Do not read or write
/// the contents of a bound resource from the CPU until the context has executed.
/// </para>
/// <para>
/// <b>Reuse.</b> A context can execute any number of times: each execution submits what was recorded
/// since the previous one, and the next <c>For</c> starts a new command buffer. Disposing executes
/// what is still pending (and rethrows a GPU error); a context that is never disposed and never
/// executed drops its recorded work without running it.
/// </para>
/// <para>
/// <b>Threading.</b> A context is not thread-safe: use it from one thread at a time (concurrent calls
/// are detected and throw <see cref="InvalidOperationException"/>). Any number of contexts can be used
/// on different threads at once. Each context with pending work holds one of the queue's 64 command
/// buffers until it executes, so a thread should not keep dozens of contexts pending at once.
/// </para>
/// </remarks>
public sealed class ComputeContext : IDisposable, IAsyncDisposable
{
    private CommandBatch? _batch;
    private int _busy;
    private bool _disposed;

    internal ComputeContext(GraphicsDevice device) => Device = device;

    /// <summary>The device this context dispatches on.</summary>
    public GraphicsDevice Device { get; }

    /// <summary>The number of dispatches recorded since the context last executed.</summary>
    public int PendingCount => Volatile.Read(ref _batch)?.DispatchCount ?? 0;

    /// <summary>Records a dispatch of <paramref name="x"/> threads.</summary>
    /// <inheritdoc cref="For{T}(int, int, int, in T)" path="/exception"/>
    public void For<T>(int x, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => Record(x, 1, 1, in shader);

    /// <summary>Records a dispatch of an <paramref name="x"/> × <paramref name="y"/> grid.</summary>
    /// <inheritdoc cref="For{T}(int, int, int, in T)" path="/exception"/>
    public void For<T>(int x, int y, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => Record(x, y, 1, in shader);

    /// <summary>Records a dispatch of an <paramref name="x"/> × <paramref name="y"/> × <paramref name="z"/> grid.</summary>
    /// <remarks>An empty grid records nothing. If this throws, nothing of this dispatch is recorded and earlier ones are unaffected.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is negative.</exception>
    /// <exception cref="ArgumentException">A bound resource belongs to a different device.</exception>
    /// <exception cref="ObjectDisposedException">The context, the device or a bound resource has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The context is being used concurrently from another thread.</exception>
    /// <exception cref="ShaderCompilationException">The shader failed to compile.</exception>
    public void For<T>(int x, int y, int z, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => Record(x, y, z, in shader);

    /// <summary>
    /// Records a pixel shader run once per pixel of <paramref name="texture"/>, bound at
    /// <c>[[texture(0)]]</c> as in <see cref="GraphicsDevice.ForEach{T}"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="texture"/> was allocated from a different device.</exception>
    /// <exception cref="ObjectDisposedException">The context, the device or <paramref name="texture"/> has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The context is being used concurrently from another thread.</exception>
    public void ForEach<T>(ReadWriteTexture2D<Rgba32> texture, in T shader)
        where T : struct, IComputeShader<Float4>, IComputeShaderDescriptor<T>
    {
        Enter();

        try
        {
            Device.ValidateOutputTexture(texture);
            Device.EncodePixelShader(GetOrBeginBatch(), texture, in shader);
        }
        catch
        {
            DropEmptyBatch();
            throw;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Runs everything recorded so far and waits for the GPU. No-op when nothing is pending.</summary>
    /// <exception cref="MetalException">The GPU failed to execute the batch; the message names its shaders.</exception>
    /// <exception cref="ObjectDisposedException">The context has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The context is being used concurrently from another thread.</exception>
    public void Execute()
    {
        Enter();

        try
        {
            TakeBatch()?.Commit();
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>
    /// Submits everything recorded so far and returns a task that completes when the GPU has finished
    /// (continuations run asynchronously). The context can record the next batch immediately.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The context has been disposed. (Thrown.)</exception>
    /// <exception cref="InvalidOperationException">The context is being used concurrently from another thread. (Thrown.)</exception>
    /// <exception cref="MetalException">The GPU failed to execute the batch. (Stored in the returned task.)</exception>
    public Task ExecuteAsync()
    {
        Enter();

        try
        {
            return TakeBatch()?.CommitAsync() ?? Task.CompletedTask;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Runs whatever is still pending, waits for it, then disposes the context.</summary>
    /// <exception cref="MetalException">The GPU failed to execute the pending batch. The context is disposed regardless.</exception>
    public void Dispose()
    {
        if (!TryEnterForDispose())
        {
            return;
        }

        try
        {
            TakeBatch()?.Commit();
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Submits whatever is still pending, disposes the context and completes when the GPU has finished.</summary>
    public ValueTask DisposeAsync()
    {
        if (!TryEnterForDispose())
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            return TakeBatch() is { } batch ? new ValueTask(batch.CommitAsync()) : ValueTask.CompletedTask;
        }
        finally
        {
            Exit();
        }
    }

    private void Record<T>(int x, int y, int z, in T shader)
        where T : struct, IComputeShaderDescriptor<T>
    {
        Enter();

        try
        {
            if (Device.ValidateGrid(x, y, z))
            {
                Device.EncodeGrid(GetOrBeginBatch(), x, y, z, in shader);
            }
        }
        catch
        {
            DropEmptyBatch();
            throw;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Tests only: the pending batch completes as if the GPU had failed it (see <see cref="CommandBatch.SimulateGpuError"/>).</summary>
    internal void SimulateGpuError(string description) =>
        (_batch ?? throw new InvalidOperationException("Nothing is pending.")).SimulateGpuError(description);

    private CommandBatch GetOrBeginBatch() => _batch ??= new CommandBatch(Device);

    private CommandBatch? TakeBatch()
    {
        CommandBatch? batch = _batch;
        _batch = null;
        return batch;
    }

    // When the first dispatch of a batch fails to encode, give the command buffer back to the queue
    // instead of holding one of its 64 slots for a batch with nothing in it.
    private void DropEmptyBatch()
    {
        if (_batch is { DispatchCount: 0 } empty)
        {
            _batch = null;
            empty.Abandon();
        }
    }

    private void Enter()
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            throw ConcurrentUse();
        }

        if (_disposed)
        {
            Volatile.Write(ref _busy, 0);
            throw new ObjectDisposedException(nameof(ComputeContext));
        }
    }

    private bool TryEnterForDispose()
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            throw ConcurrentUse();
        }

        if (_disposed)
        {
            Volatile.Write(ref _busy, 0);
            return false;
        }

        _disposed = true;
        return true;
    }

    private void Exit() => Volatile.Write(ref _busy, 0);

    private static InvalidOperationException ConcurrentUse() =>
        new("A ComputeContext is not thread-safe and is already in use on another thread; use one context per thread.");
}
