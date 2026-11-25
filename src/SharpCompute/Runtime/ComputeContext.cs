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
/// since the previous one, and the next <c>For</c> starts recording a new batch. Disposing executes
/// what is still pending (and rethrows a GPU error); a context that is never disposed and never
/// executed drops its recorded work without running it. Once a batch has run, the context keeps its
/// command lists and constants storage for the next one, so a renderer that holds one context and
/// executes it every frame records without allocating after the first few frames. (A batch submitted
/// with <see cref="ExecuteAsync"/> is reused once the GPU has finished it; until then the next
/// recording uses a new one.) The kept storage stays at the size of the largest batch so far, up to
/// a few hundred dispatches; a larger batch is dropped after it runs instead of kept.
/// </para>
/// <para>
/// <b>Threading.</b> A context is not thread-safe: use it from one thread at a time (concurrent calls
/// are detected and throw <see cref="InvalidOperationException"/>). Any number of contexts can be used
/// on different threads at once. Recorded work holds no Metal command buffer: one is created, encoded
/// and committed only when the context executes, so any number of contexts can be pending (or leaked)
/// without blocking other dispatches on the device.
/// </para>
/// </remarks>
public sealed class ComputeContext : IDisposable, IAsyncDisposable
{
    private readonly Action<CommandBatch> _recycle;
    private CommandBatch? _batch;
    // A batch that has run and released everything, kept for the next recording. Written by the
    // completing thread (possibly Metal's), taken by the recording thread.
    private CommandBatch? _spare;
    private int _busy;
    private bool _disposed;

    internal ComputeContext(GraphicsDevice device)
    {
        Device = device;
        _recycle = Recycle;
    }

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

    private CommandBatch GetOrBeginBatch()
    {
        if (_batch is { } batch)
        {
            return batch;
        }

        if (Interlocked.Exchange(ref _spare, null) is { } spare)
        {
            // Throws ObjectDisposedException for a disposed device, like the constructor; the spare is dropped.
            spare.Reset();
            return _batch = spare;
        }

        return _batch = new CommandBatch(Device, recycle: _recycle);
    }

    /// <summary>
    /// The recycle callback of this context's batches; may run on a Metal thread. Keeps at most one
    /// spare, never an oversized one, and none once the context is disposed.
    /// </summary>
    private void Recycle(CommandBatch batch)
    {
        if (batch.IsOversized || Volatile.Read(ref _disposed))
        {
            return;
        }

        // Dispose may have run between the check and the store; take the spare back out if so.
        if (Interlocked.CompareExchange(ref _spare, batch, null) is null && Volatile.Read(ref _disposed))
        {
            Interlocked.CompareExchange(ref _spare, null, batch);
        }
    }

    private CommandBatch? TakeBatch()
    {
        CommandBatch? batch = _batch;
        _batch = null;
        return batch;
    }

    // When the first dispatch of a batch fails to record, drop the empty batch right away, returning
    // the leases it took (the device's queue) instead of keeping them until the next execution.
    private void DropEmptyBatch()
    {
        if (_batch is { DispatchCount: 0 } empty)
        {
            _batch = null;
            empty.AbandonAndRecycle();
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

        Volatile.Write(ref _disposed, true);
        Volatile.Write(ref _spare, null);
        return true;
    }

    private void Exit() => Volatile.Write(ref _busy, 0);

    private static InvalidOperationException ConcurrentUse() =>
        new("A ComputeContext is not thread-safe and is already in use on another thread; use one context per thread.");
}
