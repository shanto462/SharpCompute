using System;
using System.Threading;
using System.Threading.Tasks;
using SharpCompute.Interop;

namespace SharpCompute;

/// <summary>
/// Records several dispatches, and GPU copies and fills between them, into one Metal command buffer
/// and runs them with a single commit and a single wait, instead of one round trip per <c>For</c>.
/// Created by <see cref="GraphicsDevice.CreateComputeContext"/>.
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
/// <b>Recording.</b> <c>For</c>, <c>ForEach</c>, <c>ForIndirect</c>, <c>Copy</c>, <c>Fill</c> and
/// <c>Clear</c> only record: nothing runs on the GPU until
/// <see cref="Execute"/>, <see cref="ExecuteAsync"/>, <see cref="EncodeInto"/>, <see cref="Dispose"/> or <see cref="DisposeAsync"/>.
/// Consecutive dispatches share one compute encoder; the pipeline is switched per dispatch when the shader changes.
/// Shader constants are copied when the dispatch is recorded, so changing the shader struct (or
/// reusing it with other values) afterwards does not affect recorded work.
/// </para>
/// <para>
/// <b>Ordering.</b> Operations run in the order they were recorded, and each one sees every write of
/// the operations before it, so dependent passes such as the two halves of a separable blur need no
/// barrier. That holds across kinds: a copy recorded between two dispatches copies what the first one
/// wrote, and the second one reads the copy. (Dispatches use Metal's serial dispatch type; a copy or
/// fill runs in a blit encoder between two compute encoders of the same command buffer, which Metal
/// executes in order.) Separate executions of a context, and work committed to the
/// device by other means, run in the order they were submitted; recorded work is submitted when the
/// context executes, not when it is recorded.
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

    /// <summary>
    /// The number of operations recorded since the last execution: every dispatch (direct or indirect,
    /// and each <c>WriteDispatchArguments</c>, which is a one-thread dispatch), copy and fill that is
    /// waiting for <see cref="Execute"/>. 0 right after an execution, and for a context that only
    /// recorded operations with nothing to do (an empty grid, a copy of 0 elements).
    /// </summary>
    public int PendingCount => Volatile.Read(ref _batch)?.OperationCount ?? 0;

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

    /// <summary>
    /// Records an indirect dispatch of <paramref name="shader"/>: its threadgroup counts are read from
    /// <paramref name="arguments"/>[0] on the GPU when the dispatch runs.
    /// </summary>
    /// <inheritdoc cref="ForIndirect{T}(Buffer{DispatchArguments}, int, in T)" path="/remarks"/>
    /// <inheritdoc cref="ForIndirect{T}(Buffer{DispatchArguments}, int, in T)" path="/exception"/>
    public void ForIndirect<T>(Buffer<DispatchArguments> arguments, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => ForIndirect(arguments, 0, in shader);

    /// <summary>
    /// Records an indirect dispatch of <paramref name="shader"/>: its threadgroup counts are read from
    /// <paramref name="arguments"/>[<paramref name="argumentsIndex"/>] on the GPU when the dispatch runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Because the counts are read when the dispatch runs, an operation recorded earlier in the same
    /// context can produce them, and one execution then does the whole chain on the GPU:
    /// </para>
    /// <code>
    /// context.Clear(counter);
    /// context.For(n, new CompactAbove(source, survivors, counter, cutoff)); // counts with Gpu.AtomicAdd
    /// context.WriteDispatchArguments&lt;Shade&gt;(counter, arguments);            // count -&gt; threadgroups
    /// context.ForIndirect(arguments, new Shade(survivors, counter, output)); // runs over the survivors only
    /// </code>
    /// <para>
    /// The grid is a whole number of threadgroups, so the shader must check its index against the real
    /// count itself; the threadgroup size is the shader's <see cref="ThreadGroupSizeAttribute"/>, or
    /// 256 × 1 × 1 without one. See
    /// <see cref="GraphicsDevice.ForIndirect{T}(Buffer{DispatchArguments}, int, in T)"/> for both.
    /// </para>
    /// <para>If this throws, nothing of this dispatch is recorded and earlier operations are unaffected.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="argumentsIndex"/> is not an element of <paramref name="arguments"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="arguments"/> or a bound resource belongs to a different device.</exception>
    /// <exception cref="ObjectDisposedException">The context, the device, <paramref name="arguments"/> or a bound resource has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The context is being used concurrently from another thread.</exception>
    /// <exception cref="ShaderCompilationException">The shader failed to compile.</exception>
    public void ForIndirect<T>(Buffer<DispatchArguments> arguments, int argumentsIndex, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T>
    {
        Enter();

        try
        {
            nuint offset = Device.ValidateIndirectArguments(arguments, argumentsIndex);
            Device.EncodeIndirect(GetOrBeginBatch(), arguments, offset, in shader);
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

    /// <summary>
    /// Records the conversion of the element count in <paramref name="count"/>[0] into the threadgroup
    /// counts of a one-dimensional indirect dispatch of <typeparamref name="T"/>, written to
    /// <paramref name="arguments"/>[0].
    /// </summary>
    /// <inheritdoc cref="WriteDispatchArguments{T}(Buffer{int}, int, Buffer{DispatchArguments}, int)" path="/remarks"/>
    /// <inheritdoc cref="WriteDispatchArguments{T}(Buffer{int}, int, Buffer{DispatchArguments}, int)" path="/exception"/>
    public void WriteDispatchArguments<T>(Buffer<int> count, Buffer<DispatchArguments> arguments)
        where T : struct, IComputeShaderDescriptor<T> => RecordDispatchArguments<T, int>(count, 0, arguments, 0);

    /// <summary>
    /// Records the conversion of the element count in <paramref name="count"/>[<paramref name="countIndex"/>]
    /// into the threadgroup counts of a one-dimensional indirect dispatch of <typeparamref name="T"/>,
    /// written to <paramref name="arguments"/>[<paramref name="argumentsIndex"/>].
    /// </summary>
    /// <typeparam name="T">The shader that will be dispatched with the arguments; its threadgroup size is what the count is divided by.</typeparam>
    /// <remarks>
    /// A one-thread dispatch (it counts in <see cref="PendingCount"/>) that reads the count on the GPU
    /// in recorded order, so the count can come from a dispatch recorded just before it. See
    /// <see cref="GraphicsDevice.WriteDispatchArguments{T}(Buffer{int}, int, Buffer{DispatchArguments}, int)"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">An index is not an element of its buffer.</exception>
    /// <exception cref="ArgumentException">A buffer belongs to a different device.</exception>
    /// <exception cref="ObjectDisposedException">The context, the device or a buffer has been disposed.</exception>
    /// <exception cref="NotSupportedException"><paramref name="arguments"/> maps read-only memory.</exception>
    /// <exception cref="InvalidOperationException">
    /// The threadgroup size of <typeparamref name="T"/> is not one-dimensional, or the context is being used concurrently from another thread.
    /// </exception>
    /// <exception cref="ShaderCompilationException"><typeparamref name="T"/> failed to compile.</exception>
    public void WriteDispatchArguments<T>(Buffer<int> count, int countIndex, Buffer<DispatchArguments> arguments, int argumentsIndex)
        where T : struct, IComputeShaderDescriptor<T> => RecordDispatchArguments<T, int>(count, countIndex, arguments, argumentsIndex);

    /// <inheritdoc cref="WriteDispatchArguments{T}(Buffer{int}, Buffer{DispatchArguments})"/>
    public void WriteDispatchArguments<T>(Buffer<uint> count, Buffer<DispatchArguments> arguments)
        where T : struct, IComputeShaderDescriptor<T> => RecordDispatchArguments<T, uint>(count, 0, arguments, 0);

    /// <inheritdoc cref="WriteDispatchArguments{T}(Buffer{int}, int, Buffer{DispatchArguments}, int)"/>
    public void WriteDispatchArguments<T>(Buffer<uint> count, int countIndex, Buffer<DispatchArguments> arguments, int argumentsIndex)
        where T : struct, IComputeShaderDescriptor<T> => RecordDispatchArguments<T, uint>(count, countIndex, arguments, argumentsIndex);

    /// <summary>Records a GPU copy of the whole of <paramref name="source"/> to the start of <paramref name="destination"/>.</summary>
    /// <inheritdoc cref="Copy{T}(Buffer{T}, int, Buffer{T}, int, int)" path="/remarks"/>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is shorter than <paramref name="source"/>, a buffer belongs to a
    /// different device, or the two ranges overlap (they are the same buffer or overlapping views of one).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The context, the device or a buffer has been disposed.</exception>
    /// <exception cref="NotSupportedException"><paramref name="destination"/> maps read-only memory.</exception>
    /// <exception cref="InvalidOperationException">The context is being used concurrently from another thread.</exception>
    public void Copy<T>(Buffer<T> source, Buffer<T> destination)
        where T : unmanaged
    {
        Enter();

        try
        {
            if (Device.ValidateCopy(source, destination))
            {
                GraphicsDevice.RecordCopy(GetOrBeginBatch(), source, 0, destination, 0, source.Length);
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

    /// <summary>
    /// Records a GPU copy of <paramref name="count"/> elements of <paramref name="source"/> from
    /// <paramref name="sourceOffset"/> to <paramref name="destination"/> at <paramref name="destinationOffset"/>.
    /// </summary>
    /// <remarks>
    /// The copy runs in recorded order with the context's dispatches: it copies what the operations
    /// recorded before it wrote, and those recorded after it see the copy. Offsets and
    /// <paramref name="count"/> are in elements, relative to each buffer (so to a view's start). The two
    /// ranges must not overlap; they may be disjoint ranges of one buffer. Both buffers stay alive until
    /// the GPU has finished, even if they are disposed in between. A copy of 0 elements records nothing.
    /// If this throws, nothing is recorded and earlier operations are unaffected.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">An offset or <paramref name="count"/> is negative, or a range is not inside its buffer.</exception>
    /// <exception cref="ArgumentException">A buffer belongs to a different device, or the two ranges overlap.</exception>
    /// <exception cref="ObjectDisposedException">The context, the device or a buffer has been disposed.</exception>
    /// <exception cref="NotSupportedException"><paramref name="destination"/> maps read-only memory.</exception>
    /// <exception cref="InvalidOperationException">The context is being used concurrently from another thread.</exception>
    public void Copy<T>(Buffer<T> source, int sourceOffset, Buffer<T> destination, int destinationOffset, int count)
        where T : unmanaged
    {
        Enter();

        try
        {
            if (Device.ValidateCopy(source, sourceOffset, destination, destinationOffset, count))
            {
                GraphicsDevice.RecordCopy(GetOrBeginBatch(), source, sourceOffset, destination, destinationOffset, count);
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

    /// <summary>
    /// Records a GPU copy of every pixel of <paramref name="source"/> to <paramref name="destination"/>,
    /// a texture of the same size; see <see cref="GraphicsDevice.Copy{T}(Texture2D{T}, Texture2D{T})"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The textures differ in size, belong to a different device, or are the same texture.</exception>
    /// <exception cref="ObjectDisposedException">The context, the device or a texture has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The context is being used concurrently from another thread.</exception>
    public void Copy<T>(Texture2D<T> source, Texture2D<T> destination)
        where T : unmanaged
    {
        Enter();

        try
        {
            Device.ValidateCopy(source, destination);
            GetOrBeginBatch().RecordCopy(source, destination);
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

    /// <summary>
    /// Records a GPU copy of the first <c>Width × Height</c> elements of <paramref name="source"/>, as
    /// tightly packed row-major pixels, into <paramref name="destination"/>; see
    /// <see cref="GraphicsDevice.Copy{T}(Buffer{T}, Texture2D{T})"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="source"/> holds fewer than <c>Width × Height</c> elements, or a resource belongs to a different device.</exception>
    /// <exception cref="ObjectDisposedException">The context, the device, the buffer or the texture has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The context is being used concurrently from another thread.</exception>
    public void Copy<T>(Buffer<T> source, Texture2D<T> destination)
        where T : unmanaged
    {
        Enter();

        try
        {
            Device.ValidateCopy(source, destination);
            GetOrBeginBatch().RecordCopy(source.Handle, source, source.ByteOffset, destination);
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

    /// <summary>
    /// Records a GPU copy of every pixel of <paramref name="source"/> to the start of
    /// <paramref name="destination"/>, as tightly packed row-major pixels; see
    /// <see cref="GraphicsDevice.Copy{T}(Texture2D{T}, Buffer{T})"/>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="destination"/> holds fewer than <c>Width × Height</c> elements, or a resource belongs to a different device.</exception>
    /// <exception cref="ObjectDisposedException">The context, the device, the texture or the buffer has been disposed.</exception>
    /// <exception cref="NotSupportedException"><paramref name="destination"/> maps read-only memory.</exception>
    /// <exception cref="InvalidOperationException">The context is being used concurrently from another thread.</exception>
    public void Copy<T>(Texture2D<T> source, Buffer<T> destination)
        where T : unmanaged
    {
        Enter();

        try
        {
            Device.ValidateCopy(source, destination);
            GetOrBeginBatch().RecordCopy(source, destination.Handle, destination, destination.ByteOffset);
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

    /// <summary>
    /// Records a GPU fill that sets every <b>byte</b> of <paramref name="buffer"/> to
    /// <paramref name="value"/>, in recorded order with the context's dispatches.
    /// </summary>
    /// <remarks>
    /// The value is a byte pattern, not an element; see <see cref="GraphicsDevice.Fill{T}"/>. A view is
    /// filled over its own range only, and an empty buffer records nothing. The buffer stays alive until
    /// the GPU has finished, even if it is disposed in between.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="buffer"/> belongs to a different device.</exception>
    /// <exception cref="ObjectDisposedException">The context, the device or the buffer has been disposed.</exception>
    /// <exception cref="NotSupportedException"><paramref name="buffer"/> maps read-only memory.</exception>
    /// <exception cref="InvalidOperationException">The context is being used concurrently from another thread.</exception>
    public void Fill<T>(Buffer<T> buffer, byte value)
        where T : unmanaged
    {
        Enter();

        try
        {
            if (Device.ValidateFill(buffer))
            {
                GraphicsDevice.RecordFill(GetOrBeginBatch(), buffer, value);
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

    /// <summary>
    /// Records a GPU zero-fill of <paramref name="buffer"/>: <c>Fill(buffer, 0)</c>. The way to reset a
    /// counter or an accumulator between the passes of one execution.
    /// </summary>
    /// <inheritdoc cref="Fill{T}" path="/exception"/>
    public void Clear<T>(Buffer<T> buffer)
        where T : unmanaged => Fill(buffer, 0);

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

    /// <summary>
    /// Encodes everything recorded so far into <paramref name="mtlCommandBuffer"/>, a command buffer
    /// the caller created and will commit, without committing it: the recorded dispatches, copies and
    /// fills then run in order with the caller's own passes in that command buffer, in one submission.
    /// </summary>
    /// <param name="mtlCommandBuffer">An <c>id&lt;MTLCommandBuffer&gt;</c> of this context's device that has not been committed.</param>
    /// <returns>
    /// A task that completes when the command buffer has completed (continuations run asynchronously),
    /// and faults with <see cref="MetalException"/> when it failed. An already completed task when
    /// nothing is pending, in which case nothing is encoded.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Order.</b> The work is encoded right away, after whatever the caller encoded into the command
    /// buffer before this call and before whatever it encodes after it, and runs in that order with
    /// Metal's guarantees for tracked resources (the only kind SharpCompute wraps): a render pass
    /// encoded after this call sees what the recorded dispatches wrote. The caller must have ended its
    /// own encoder before the call (<c>endEncoding</c>): Metal terminates the process when a second
    /// encoder is opened while one is still encoding, and that cannot be detected beforehand. The
    /// context can record again as soon as this returns, and encoding into the same command buffer
    /// more than once appends each batch after the previous one.
    /// </para>
    /// <para>
    /// <b>Lifetime.</b> Everything the recorded work uses stays leased until the command buffer
    /// completes, as with <see cref="ExecuteAsync"/>, even if the caller created it with unretained
    /// references. The caller must commit the command buffer: one that is never committed never
    /// completes, so what was leased for it is never released. The same holds if this method throws
    /// once encoding has begun (Metal failing to create an encoder): what was already encoded stays
    /// in the command buffer, and stays leased until it completes.
    /// </para>
    /// <para>
    /// Results are not visible on the CPU until the command buffer has completed: wait for the task,
    /// or for the command buffer (<c>waitUntilCompleted</c>), before reading them.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="mtlCommandBuffer"/> is 0, not an <c>id&lt;MTLCommandBuffer&gt;</c>, or belongs to another <c>MTLDevice</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The command buffer has already been committed (its <c>status</c> is past <c>MTLCommandBufferStatusEnqueued</c>),
    /// so nothing can be encoded into it, or the context is being used concurrently from another thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The context or its device has been disposed.</exception>
    public Task EncodeInto(nint mtlCommandBuffer)
    {
        Enter();

        try
        {
            ValidateCommandBuffer(mtlCommandBuffer);
            return TakeBatch()?.EncodeInto(mtlCommandBuffer) ?? Task.CompletedTask;
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

    /// <summary>The preconditions of <see cref="EncodeInto"/>, each of which Metal would otherwise answer by terminating the process.</summary>
    private void ValidateCommandBuffer(nint commandBuffer)
    {
        const string parameter = "mtlCommandBuffer";
        ObjectDisposedException.ThrowIf(Device.IsDisposed, Device);

        using AutoreleasePool pool = AutoreleasePool.Push();

        GraphicsDevice.ThrowIfNot(commandBuffer, MetalProtocols.CommandBuffer, "id<MTLCommandBuffer>", parameter);
        Device.ThrowIfOtherDevice(commandBuffer, "command buffer", parameter);

        nuint status = ObjC.SendNUInt(commandBuffer, Selectors.Status);

        if (status > Metal.CommandBufferStatusEnqueued)
        {
            throw new InvalidOperationException(
                $"The command buffer has already been committed (MTLCommandBufferStatus {status}), so nothing more can be encoded into it. " +
                "Call EncodeInto before committing it.");
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

    private void RecordDispatchArguments<T, TCount>(Buffer<TCount> count, int countIndex, Buffer<DispatchArguments> arguments, int argumentsIndex)
        where T : struct, IComputeShaderDescriptor<T>
        where TCount : unmanaged
    {
        Enter();

        try
        {
            ObjectDisposedException.ThrowIf(Device.IsDisposed, Device);
            Device.EncodeDispatchArguments<T, TCount>(GetOrBeginBatch(), count, countIndex, arguments, argumentsIndex);
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

    /// <summary>Tests only: true when a completed batch is kept for the next recording.</summary>
    internal bool HasSpareBatch => Volatile.Read(ref _spare) is not null;

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

    // When the first operation of a batch fails to record, drop the empty batch right away, returning
    // the leases it took (the device's queue) instead of keeping them until the next execution.
    private void DropEmptyBatch()
    {
        if (_batch is { OperationCount: 0 } empty)
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
