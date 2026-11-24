using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpCompute.Interop;

namespace SharpCompute;

/// <summary>
/// A list of recorded dispatches that is encoded into one <c>id&lt;MTLCommandBuffer&gt;</c> with one
/// compute encoder when it is committed, plus everything the dispatches need kept alive until the GPU
/// has finished. Shared by
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
/// <b>Deferred encoding.</b> Recording validates every dispatch, leases what it binds and copies its
/// constants right away, but the command buffer and encoder are only created when the batch is
/// committed. A queue has a fixed number of command buffers (64 by default) and creating one more
/// blocks until one completes, so a pending batch that held a command buffer would hold a slot of
/// that device-wide pool: 64 pending (or leaked) contexts would block every later dispatch on the
/// device forever. Recorded work holds no Metal command buffer at all.
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
    // A dispatch records 3 to 5 commands; starting at 32 avoids regrowing the list in the common case.
    private readonly List<Command> _commands = new(32);
    private byte[] _constants = [];
    private int _constantsLength;
    private List<nint>? _temporaries;
    private TaskCompletionSource? _completion;
    private nint _queue;
    private nint _commandBuffer;
    private nint _encoder;
    private nint _currentPipeline;
    private int _dispatchCount;
    private int _released;
    private string? _simulatedError;

    /// <summary>Starts an empty batch on <paramref name="device"/>'s queue. No Metal object is created until it is committed.</summary>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    internal CommandBatch(GraphicsDevice device)
    {
        _device = device;

        // Held until completion: a device disposed while the batch is pending keeps its queue.
        _queue = Lease(device.QueueHandle, device);
    }

    // Reached only for a batch that was never committed and was dropped without Abandon (a leaked
    // ComputeContext): it owns no command buffer or encoder, only leases and temporaries, which this
    // releases. A committed batch is rooted by the wait or by the GCHandle its completion handler
    // owns, and suppresses finalization once it completes.
    ~CommandBatch()
    {
        try
        {
            Abandon();
        }
        catch (AggregateException)
        {
            // Only a release callback that broke its must-not-throw contract gets here, and there is
            // nobody to report it to on the finalizer thread; the leases were all released regardless.
        }
    }

    /// <summary>Number of dispatches recorded so far.</summary>
    internal int DispatchCount => _dispatchCount;

    /// <summary>
    /// Tests only: report this error description at completion as if Metal had set the command
    /// buffer's status to <c>MTLCommandBufferStatusError</c>. Real GPU faults cannot be provoked
    /// reliably (or politely, on a shared GPU), and everything after the status check is the same.
    /// </summary>
    internal void SimulateGpuError(string description) => _simulatedError = description;

    /// <summary>
    /// Records one dispatch. <paramref name="output"/>, when non-null, is bound at texture index 0
    /// before the shader's own arguments (the <c>ForEach</c> contract). Everything is validated and
    /// leased, and the constants are copied, now; if this throws, nothing of the failed dispatch is
    /// recorded and dispatches recorded earlier are unaffected.
    /// </summary>
    internal void Encode<T>(ComputePipeline pipeline, MTLSize grid, MTLSize threadsPerThreadgroup, ReadWriteTexture2D<Rgba32>? output, in T shader)
        where T : struct, IComputeShaderDescriptor<T>
    {
        Debug.Assert(_commandBuffer == 0 && Volatile.Read(ref _released) == 0, "The batch is no longer recording.");

        int commandCount = _commands.Count;
        int constantsLength = _constantsLength;
        nint currentPipeline = _currentPipeline;

        try
        {
            // Bindings persist across dispatches in one encoder, so the pipeline is only switched when it
            // changes (and leased the first time it becomes current; one lease is enough for the batch).
            nint state = pipeline.State.DangerousGetHandle();

            if (state != _currentPipeline)
            {
                state = Lease(pipeline.State, _device);
                _commands.Add(new Command(CommandKind.Pipeline, state, 0));
                _currentPipeline = state;
            }

            if (output is not null)
            {
                nint texture = Lease(output.Handle, output);
                Lease(output.BufferHandle, output);
                _commands.Add(new Command(CommandKind.Texture, texture, 0));
            }

            ShaderArgumentWriter writer = new(0, _device, this);
            T.SetArguments(in shader, ref writer);

            _commands.Add(new Command(CommandKind.Dispatch, 0, 0) { Grid = grid, ThreadsPerThreadgroup = threadsPerThreadgroup });
        }
        catch
        {
            // Drop the partial dispatch's commands. Its leases stay recorded and are released with the
            // batch, which is harmless: they only delay a release until the batch is done.
            _commands.RemoveRange(commandCount, _commands.Count - commandCount);
            _constantsLength = constantsLength;
            _currentPipeline = currentPipeline;
            throw;
        }

        _dispatchCount++;

        if (!_shaderTypes.Contains(typeof(T)))
        {
            _shaderTypes.Add(typeof(T));
        }
    }

    /// <summary>Records <c>setBuffer:offset:0 atIndex:</c> for an already leased buffer.</summary>
    internal void RecordBuffer(nint buffer, int index) => _commands.Add(new Command(CommandKind.Buffer, buffer, index));

    /// <summary>Records <c>setBytes:length:atIndex:</c> with a copy of the bytes, taken now.</summary>
    internal void RecordBytes(void* bytes, int length, int index)
    {
        if (_constantsLength + length > _constants.Length)
        {
            Array.Resize(ref _constants, Math.Max(_constantsLength + length, Math.Max(256, _constants.Length * 2)));
        }

        new ReadOnlySpan<byte>(bytes, length).CopyTo(_constants.AsSpan(_constantsLength));
        _commands.Add(new Command(CommandKind.Bytes, _constantsLength, index) { Length = length });
        _constantsLength += length;
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
        // Reserve the slot first so that recording it cannot fail after we own the reference.
        List<nint> temporaries = _temporaries ??= [];
        temporaries.EnsureCapacity(temporaries.Count + 1);
        temporaries.Add(temporary);
    }

    /// <summary>Ends encoding, commits and blocks until the GPU has finished.</summary>
    /// <exception cref="MetalException">The GPU reported an error executing the batch.</exception>
    internal void Commit()
    {
        MetalException? error;

        try
        {
            using AutoreleasePool pool = AutoreleasePool.Push();

            EncodeRecorded();
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
        GCHandle self = default;

        try
        {
            using AutoreleasePool pool = AutoreleasePool.Push();

            EncodeRecorded();

            // The handle roots this batch, and through it every leased resource, until the handler
            // runs; from Attach on, the handler owns it and frees it.
            self = GCHandle.Alloc(this);
            CompletionBlock.Attach(_commandBuffer, self);
            self = default;
            ObjC.SendVoid(_commandBuffer, Selectors.Commit);
        }
        catch
        {
            // Nothing was committed: free what Attach did not take over and drop the batch, so a
            // failure here cannot leak the leases or root the batch forever.
            if (self.IsAllocated)
            {
                self.Free();
            }

            ReleaseResources();
            throw;
        }

        return completion.Task;
    }

    /// <summary>Drops an uncommitted batch: releases everything without running it.</summary>
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

    /// <summary>
    /// Creates the command buffer and compute encoder, replays the recorded commands into it and ends
    /// encoding. On failure the encoder is still ended (Metal aborts if it is deallocated otherwise);
    /// the caller releases the batch.
    /// </summary>
    private void EncodeRecorded()
    {
        nint commandBuffer = ObjC.SendIntPtr(_queue, Selectors.CommandBuffer);

        if (commandBuffer == 0)
        {
            throw new InvalidOperationException("Metal failed to create a command buffer.");
        }

        // Autoreleased; retained so they outlive the caller's pool until the batch is released.
        _commandBuffer = ObjC.Retain(commandBuffer);
        nint encoder = ObjC.SendIntPtr(commandBuffer, Selectors.ComputeCommandEncoder);

        if (encoder == 0)
        {
            throw new InvalidOperationException("Metal failed to create a compute command encoder.");
        }

        _encoder = ObjC.Retain(encoder);

        try
        {
            fixed (byte* constants = _constants)
            {
                foreach (Command command in _commands)
                {
                    switch (command.Kind)
                    {
                        case CommandKind.Pipeline:
                            ObjC.SendVoid(encoder, Selectors.SetComputePipelineState, command.Pointer);
                            break;
                        case CommandKind.Texture:
                            ObjC.SendVoid(encoder, Selectors.SetTextureAtIndex, command.Pointer, (nuint)0);
                            break;
                        case CommandKind.Buffer:
                            ObjC.SendVoid(encoder, Selectors.SetBufferOffsetAtIndex, command.Pointer, (nuint)0, (nuint)command.Index);
                            break;
                        case CommandKind.Bytes:
                            ObjC.SendVoid(encoder, Selectors.SetBytesLengthAtIndex, constants + command.Pointer, (nuint)command.Length, (nuint)command.Index);
                            break;
                        case CommandKind.Dispatch:
                            ObjC.SendVoid(encoder, Selectors.DispatchThreadsThreadsPerThreadgroup, command.Grid, command.ThreadsPerThreadgroup);
                            break;
                    }
                }
            }
        }
        finally
        {
            EndEncoding();
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
        _commands.Clear();

        if (_temporaries is not null)
        {
            foreach (nint temporary in _temporaries)
            {
                ObjC.Release(temporary);
            }
        }

        // The last release of a wrapped buffer runs the caller's release callback, possibly on Metal's
        // completion thread; one that throws must not stop the other leases from being released.
        GraphicsDevice.ReleaseLeases(_leases);
    }

    private enum CommandKind : byte
    {
        Pipeline,
        Texture,
        Buffer,
        Bytes,
        Dispatch,
    }

    /// <summary>
    /// One recorded encoder call. <see cref="Pointer"/> is an <c>id</c> (pipeline, texture, buffer)
    /// or, for <see cref="CommandKind.Bytes"/>, the offset of the copied constants.
    /// </summary>
    private readonly struct Command(CommandKind kind, nint pointer, int index)
    {
        public CommandKind Kind { get; } = kind;

        public nint Pointer { get; } = pointer;

        public int Index { get; } = index;

        public int Length { get; init; }

        public MTLSize Grid { get; init; }

        public MTLSize ThreadsPerThreadgroup { get; init; }
    }
}
