using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpCompute.Interop;

namespace SharpCompute;

/// <summary>
/// One <c>id&lt;MTLCommandBuffer&gt;</c> with one open compute encoder, the dispatches recorded into it
/// and everything they need kept alive until the GPU has finished. Shared by
/// <see cref="ComputeContext"/> (many dispatches, one commit) and the <c>ForAsync</c> methods
/// (one dispatch, completion reported through a <see cref="Task"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Ordering.</b> The encoder comes from <c>computeCommandEncoder</c>, whose dispatch type is
/// <c>MTLDispatchTypeSerial</c>: each dispatch finishes, and its writes are visible, before the next
/// one starts. Every SharpCompute resource is shared storage with the default (tracked) hazard mode,
/// so command buffers committed to the same queue are ordered the same way.
/// </para>
/// <para>
/// <b>Lifetime.</b> Unlike the synchronous <c>For</c>, encoding and completion happen in different
/// frames (and, for <see cref="CommitAsync"/>, on different threads), so the scoped
/// <see cref="ObjCHandle.Lease"/> cannot be used. Every handle that is bound is instead leased with
/// <see cref="ObjCHandle.AddLease"/> and recorded here; the leases are dropped when the command buffer
/// completes, or when an uncommitted batch is abandoned. Metal's command buffer (created with retained
/// references) also retains what is bound to it, but the leases make SharpCompute's own guarantee
/// explicit: a buffer, texture, pipeline or queue disposed while a batch that uses it is pending keeps
/// its reference (and its GC memory pressure) until the GPU is done, whatever Metal does internally.
/// The command buffer and the encoder are autoreleased by Metal and are retained here for the same reason.
/// </para>
/// <para>
/// <b>Threading.</b> Not thread-safe: one thread records at a time (<see cref="ComputeContext"/>
/// guards that). Completion may run on a Metal thread.
/// </para>
/// </remarks>
internal sealed unsafe class CommandBatch
{
    private readonly GraphicsDevice _device;
    private readonly List<ObjCHandle> _leases = [];
    private readonly List<Type> _shaderTypes = [];
    private List<nint>? _temporaries;
    private TaskCompletionSource? _completion;
    private nint _commandBuffer;
    private nint _encoder;
    private nint _currentPipeline;
    private int _dispatchCount;
    private int _released;
    private string? _simulatedError;

    /// <summary>Creates the command buffer and its compute encoder on <paramref name="device"/>'s queue.</summary>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    internal CommandBatch(GraphicsDevice device)
    {
        _device = device;

        using AutoreleasePool pool = AutoreleasePool.Push();

        try
        {
            // Held until completion: a device disposed while the batch is pending keeps its queue.
            nint queue = Lease(device.QueueHandle, device);
            nint commandBuffer = ObjC.SendIntPtr(queue, Selectors.CommandBuffer);

            if (commandBuffer == 0)
            {
                throw new InvalidOperationException("Metal failed to create a command buffer.");
            }

            nint encoder = ObjC.SendIntPtr(commandBuffer, Selectors.ComputeCommandEncoder);

            if (encoder == 0)
            {
                throw new InvalidOperationException("Metal failed to create a compute command encoder.");
            }

            // Both are autoreleased; the pool above would drain them when this constructor returns.
            _commandBuffer = ObjC.Retain(commandBuffer);
            _encoder = ObjC.Retain(encoder);
        }
        catch
        {
            ReleaseResources();
            throw;
        }
    }

    // Reached only for a batch that was never committed and was dropped without Abandon (a leaked
    // ComputeContext). Metal aborts the process when an encoder is deallocated without endEncoding,
    // so this ends it before letting go. A committed batch is rooted by the wait or by the GCHandle
    // its completion handler owns, and suppresses finalization once it completes.
    ~CommandBatch() => Abandon();

    /// <summary>Number of dispatches recorded so far.</summary>
    internal int DispatchCount => _dispatchCount;

    /// <summary>
    /// Tests only: report this error description at completion as if Metal had set the command
    /// buffer's status to <c>MTLCommandBufferStatusError</c>. Real GPU faults cannot be provoked
    /// reliably (or politely, on a shared GPU), and everything after the status check is the same.
    /// </summary>
    internal void SimulateGpuError(string description) => _simulatedError = description;

    /// <summary>
    /// Encodes one dispatch. <paramref name="output"/>, when non-null, is bound at texture index 0
    /// before the shader's own arguments (the <c>ForEach</c> contract). If this throws, nothing of the
    /// failed dispatch runs; dispatches recorded earlier are unaffected.
    /// </summary>
    internal void Encode<T>(ComputePipeline pipeline, MTLSize grid, MTLSize threadsPerThreadgroup, ReadWriteTexture2D<Rgba32>? output, in T shader)
        where T : struct, IComputeShaderDescriptor<T>
    {
        Debug.Assert(_encoder != 0, "The batch is no longer recording.");

        using AutoreleasePool pool = AutoreleasePool.Push();

        // Bindings persist across dispatches in one encoder, so the pipeline is only switched when it
        // changes (and leased the first time it becomes current; one lease is enough for the batch).
        nint state = pipeline.State.DangerousGetHandle();

        if (state != _currentPipeline)
        {
            state = Lease(pipeline.State, _device);
            ObjC.SendVoid(_encoder, Selectors.SetComputePipelineState, state);
            _currentPipeline = state;
        }

        if (output is not null)
        {
            nint texture = Lease(output.Handle, output);
            Lease(output.BufferHandle, output);
            ObjC.SendVoid(_encoder, Selectors.SetTextureAtIndex, texture, (nuint)0);
        }

        ShaderArgumentWriter writer = new(_encoder, _device, this);
        T.SetArguments(in shader, ref writer);

        ObjC.SendVoid(_encoder, Selectors.DispatchThreadsThreadsPerThreadgroup, grid, threadsPerThreadgroup);

        _dispatchCount++;

        if (!_shaderTypes.Contains(typeof(T)))
        {
            _shaderTypes.Add(typeof(T));
        }
    }

    /// <summary>
    /// Leases <paramref name="handle"/> until the batch completes and returns the raw <c>id</c>.
    /// Throws <see cref="ObjectDisposedException"/> naming <paramref name="owner"/> if it is already disposed.
    /// </summary>
    internal nint Lease(ObjCHandle handle, object owner)
    {
        // Reserve the slot first so that a successful AddLease is always recorded (and released).
        _leases.EnsureCapacity(_leases.Count + 1);
        nint pointer = handle.AddLease(owner);
        _leases.Add(handle);
        return pointer;
    }

    /// <summary>Takes ownership of a +1 reference (a large-constants buffer) and releases it when the batch completes.</summary>
    internal void Own(nint temporary)
    {
        (_temporaries ??= []).Add(temporary);
    }

    /// <summary>Ends encoding, commits and blocks until the GPU has finished.</summary>
    /// <exception cref="MetalException">The GPU reported an error executing the batch.</exception>
    internal void Commit()
    {
        MetalException? error;

        try
        {
            using AutoreleasePool pool = AutoreleasePool.Push();

            EndEncoding();
            ObjC.SendVoid(_commandBuffer, Selectors.Commit);
            ObjC.SendVoid(_commandBuffer, Selectors.WaitUntilCompleted);
            error = GetError();
        }
        finally
        {
            ReleaseResources();
        }

        if (error is not null)
        {
            throw error;
        }
    }

    /// <summary>
    /// Ends encoding, commits and returns a task that completes (asynchronously, never inline on
    /// Metal's thread) when the GPU has finished, or faults with <see cref="MetalException"/>.
    /// </summary>
    internal Task CommitAsync()
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;

        // The handle roots this batch, and through it every leased resource, until the handler runs.
        GCHandle self = GCHandle.Alloc(this);

        using (AutoreleasePool pool = AutoreleasePool.Push())
        {
            EndEncoding();

            // Neither call can fail once the encoder has ended, so the handle is always freed by the handler.
            CompletionBlock.Attach(_commandBuffer, self);
            ObjC.SendVoid(_commandBuffer, Selectors.Commit);
        }

        return completion.Task;
    }

    /// <summary>Drops an uncommitted batch: ends encoding and releases everything without running it.</summary>
    internal void Abandon() => ReleaseResources();

    /// <summary>The completed handler; runs on a Metal completion thread and must not throw.</summary>
    internal void OnCompleted()
    {
        TaskCompletionSource? completion = _completion;
        MetalException? error = null;

        try
        {
            using AutoreleasePool pool = AutoreleasePool.Push();
            error = GetError();
        }
        catch (Exception e)
        {
            error = new MetalException($"Reading the completion status of {Describe()} failed.", e);
        }
        finally
        {
            try
            {
                ReleaseResources();
            }
            catch (Exception e)
            {
                error ??= new MetalException($"Releasing the resources of {Describe()} failed.", e);
            }
        }

        if (error is null)
        {
            completion?.TrySetResult();
        }
        else
        {
            completion?.TrySetException(error);
        }
    }

    private void EndEncoding()
    {
        if (_encoder == 0)
        {
            return;
        }

        nint encoder = _encoder;
        _encoder = 0;
        ObjC.SendVoid(encoder, Selectors.EndEncoding);
        ObjC.Release(encoder);
    }

    private MetalException? GetError()
    {
        string description;

        if (ObjC.SendNUInt(_commandBuffer, Selectors.Status) == Metal.CommandBufferStatusError)
        {
            // `error` is an autoreleased getter; callers hold a pool.
            description = NSString.DescribeError(ObjC.SendIntPtr(_commandBuffer, Selectors.Error));
        }
        else if (_simulatedError is not null)
        {
            description = _simulatedError;
        }
        else
        {
            return null;
        }

        return new MetalException($"The GPU failed to execute {Describe()}: {description}");
    }

    /// <summary>"shader 'X'" for one dispatch, otherwise the dispatch count and every shader type in the batch.</summary>
    private string Describe()
    {
        if (_dispatchCount == 1 && _shaderTypes.Count == 1)
        {
            return $"shader '{_shaderTypes[0]}'";
        }

        return $"a batch of {_dispatchCount} dispatches (shaders: {string.Join(", ", _shaderTypes.ConvertAll(static t => $"'{t}'"))})";
    }

    private void ReleaseResources()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        GC.SuppressFinalize(this);

        // May run on the finalizer thread or a Metal completion thread, neither of which has a pool.
        using AutoreleasePool pool = AutoreleasePool.Push();

        EndEncoding();
        ObjC.Release(_commandBuffer);
        _commandBuffer = 0;

        if (_temporaries is not null)
        {
            foreach (nint temporary in _temporaries)
            {
                ObjC.Release(temporary);
            }
        }

        foreach (ObjCHandle handle in _leases)
        {
            handle.DangerousRelease();
        }

        _leases.Clear();
    }
}
