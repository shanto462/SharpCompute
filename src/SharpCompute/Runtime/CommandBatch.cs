using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpCompute.Interop;

namespace SharpCompute;

/// <summary>
/// A list of recorded operations (dispatches, and GPU copies and fills) that is encoded into one
/// <c>id&lt;MTLCommandBuffer&gt;</c> when it is committed, plus everything the operations need kept
/// alive until the GPU has finished. Shared by
/// <see cref="ComputeContext"/> (many operations, one commit), the <c>ForAsync</c> methods
/// (one dispatch, completion reported through a <see cref="Task"/>) and the synchronous
/// <c>Copy</c>, <c>Fill</c> and <c>Clear</c> of <see cref="GraphicsDevice"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ordering.</b> Dispatches are encoded with <c>computeCommandEncoder</c>, whose dispatch type is
/// <c>MTLDispatchTypeSerial</c>: each dispatch finishes, and its writes are visible, before the next
/// one starts. Every SharpCompute resource uses the default (tracked) hazard mode (wrapped native
/// resources are checked for it), so command buffers committed to the same queue are ordered the same
/// way, and so is the work <see cref="EncodeInto"/> puts between a caller's own passes.
/// </para>
/// <para>
/// <b>Encoders.</b> A command buffer has one open encoder at a time, and copies and fills need a blit
/// encoder. Encoding therefore walks the recorded operations in order and switches encoder whenever
/// the kind of operation changes: a run of dispatches shares one compute encoder, a run of copies and
/// fills shares one blit encoder. Encoders of one command buffer execute in the order they were
/// created and Metal tracks the resources they share, so recorded order is execution order across
/// the switch as well. Nothing carries over from one encoder to the next, which is why the dispatch
/// after a copy records its pipeline again (every other binding is recorded per dispatch anyway).
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
    // A dispatch records 3 to 5 commands. A context batch starts at 32 so a few dispatches never
    // regrow the list; a single async dispatch needs only a handful.
    private readonly List<Command> _commands;
    // The two sizes of each dispatch, kept apart so that the common commands stay 24 bytes.
    private readonly List<DispatchSizes> _dispatches;
    // The copies and fills, kept apart for the same reason; most batches have none.
    private readonly List<BlitOperation> _blits = [];
    private readonly Action<CommandBatch>? _recycle;
    private byte[] _constants = [];
    private int _constantsLength;
    private List<nint>? _temporaries;
    private TaskCompletionSource? _completion;
    private nint _queue;
    private nint _commandBuffer;
    private nint _encoder;
    private bool _encoderIsBlit;
    private nint _currentPipeline;
    private int _released;
    private string? _simulatedError;

    /// <summary>Starts an empty batch on <paramref name="device"/>'s queue. No Metal object is created until it is committed.</summary>
    /// <exception cref="ObjectDisposedException">The device has been disposed.</exception>
    /// <param name="device">The device whose queue runs the batch.</param>
    /// <param name="commandCapacity">Initial room for recorded commands (3 to 5 per dispatch).</param>
    /// <param name="recycle">
    /// Called with the batch once it has run (or was abandoned) and released everything it held, so its
    /// owner can <see cref="Reset"/> and record into it again instead of allocating new lists. It runs
    /// on the completing thread, possibly a Metal thread, and must not throw.
    /// </param>
    internal CommandBatch(GraphicsDevice device, int commandCapacity = 32, Action<CommandBatch>? recycle = null)
    {
        _device = device;
        _commands = new List<Command>(commandCapacity);
        _dispatches = new List<DispatchSizes>(Math.Max(1, commandCapacity / 4));
        _recycle = recycle;

        // Held until completion: a device disposed while the batch is pending keeps its queue.
        _queue = Lease(device.QueueHandle, device);
    }

    /// <summary>
    /// Makes a released batch record again, keeping the capacity of its lists and constants buffer.
    /// Only valid after the batch was handed to the recycle callback.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The device has been disposed; the batch stays released.</exception>
    internal void Reset()
    {
        Debug.Assert(Volatile.Read(ref _released) == 1 && _commandBuffer == 0, "Only a released batch can be reset.");

        _queue = 0;
        _constantsLength = 0;
        _currentPipeline = 0;
        _shaderTypes.Clear();
        _dispatches.Clear();
        _blits.Clear();
        _completion = null;
        _simulatedError = null;

        // Leases the queue before the batch counts as live again, so a disposed device leaves it released.
        _queue = Lease(_device.QueueHandle, _device);
        Volatile.Write(ref _released, 0);
        GC.ReRegisterForFinalize(this);
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

    /// <summary>Number of dispatches recorded so far (direct and indirect).</summary>
    internal int DispatchCount => _dispatches.Count;

    /// <summary>Number of operations recorded so far: dispatches, copies and fills.</summary>
    internal int OperationCount => _dispatches.Count + _blits.Count;

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
    /// <remarks>
    /// With <paramref name="indirect"/>, the dispatch takes its threadgroup counts from that buffer at
    /// <paramref name="indirectOffset"/> bytes when it runs (<c>dispatchThreadgroupsWithIndirectBuffer</c>)
    /// and <paramref name="grid"/> is ignored; the buffer is leased under <paramref name="indirectOwner"/>.
    /// </remarks>
    internal void Encode<T>(
        ComputePipeline pipeline,
        MTLSize grid,
        MTLSize threadsPerThreadgroup,
        ReadWriteTexture2D<Rgba32>? output,
        in T shader,
        ObjCHandle? indirect = null,
        object? indirectOwner = null,
        nuint indirectOffset = 0)
        where T : struct, IComputeShaderDescriptor<T>
    {
        Debug.Assert(_commandBuffer == 0 && Volatile.Read(ref _released) == 0, "The batch is no longer recording.");

        int commandCount = _commands.Count;
        int dispatchCount = _dispatches.Count;
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

            if (indirect is null)
            {
                _dispatches.Add(new DispatchSizes(grid, threadsPerThreadgroup));
                _commands.Add(new Command(CommandKind.Dispatch, 0, _dispatches.Count - 1));
            }
            else
            {
                nint arguments = Lease(indirect, indirectOwner!);
                _dispatches.Add(new DispatchSizes(default, threadsPerThreadgroup));
                _commands.Add(new Command(CommandKind.DispatchIndirect, arguments, _dispatches.Count - 1, indirectOffset));
            }
        }
        catch
        {
            // Drop the partial dispatch's commands. Its leases stay recorded and are released with the
            // batch, which is harmless: they only delay a release until the batch is done.
            _commands.RemoveRange(commandCount, _commands.Count - commandCount);
            _dispatches.RemoveRange(dispatchCount, _dispatches.Count - dispatchCount);
            _constantsLength = constantsLength;
            _currentPipeline = currentPipeline;
            throw;
        }

        if (!_shaderTypes.Contains(typeof(T)))
        {
            _shaderTypes.Add(typeof(T));
        }
    }

    /// <summary>Records <c>setBuffer:offset:atIndex:</c> for an already leased buffer; <paramref name="offset"/> is in bytes (a view's start).</summary>
    internal void RecordBuffer(nint buffer, int index, nuint offset) => _commands.Add(new Command(CommandKind.Buffer, buffer, index, offset));

    /// <summary>Records <c>setTexture:atIndex:</c> for an already leased texture.</summary>
    internal void RecordTexture(nint texture, int index) => _commands.Add(new Command(CommandKind.Texture, texture, index));

    /// <summary>Records <c>setBytes:length:atIndex:</c> with a copy of the bytes, taken now.</summary>
    internal void RecordBytes(void* bytes, int length, int index)
    {
        if (_constantsLength + length > _constants.Length)
        {
            Array.Resize(ref _constants, Math.Max(_constantsLength + length, Math.Max(256, _constants.Length * 2)));
        }

        new ReadOnlySpan<byte>(bytes, length).CopyTo(_constants.AsSpan(_constantsLength));
        _commands.Add(new Command(CommandKind.Bytes, _constantsLength, index, (nuint)length));
        _constantsLength += length;
    }

    /// <summary>
    /// Records <c>copyFromBuffer:sourceOffset:toBuffer:destinationOffset:size:</c>. Offsets and
    /// <paramref name="size"/> are in bytes and already validated; both buffers are leased now.
    /// </summary>
    internal void RecordCopy(
        ObjCHandle source, object sourceOwner, nuint sourceOffset, ObjCHandle destination, object destinationOwner, nuint destinationOffset, nuint size)
    {
        nint from = Lease(source, sourceOwner);
        nint to = Lease(destination, destinationOwner);
        RecordBlit(new BlitOperation(BlitKind.CopyBuffer, from, sourceOffset, to, destinationOffset, size));
    }

    /// <summary>Records <c>fillBuffer:range:value:</c> over <paramref name="size"/> bytes from <paramref name="offset"/>; the buffer is leased now.</summary>
    internal void RecordFill(ObjCHandle buffer, object owner, nuint offset, nuint size, byte value)
    {
        nint target = Lease(buffer, owner);
        RecordBlit(new BlitOperation(BlitKind.FillBuffer, 0, 0, target, offset, size, Value: value));
    }

    /// <summary>
    /// Records a copy of mipmap level 0 between two textures of the same size and format.
    /// Both textures and the buffers behind them are leased now.
    /// </summary>
    internal void RecordCopy<T>(Texture2D<T> source, Texture2D<T> destination)
        where T : unmanaged
    {
        nint from = LeaseTexture(source);
        nint to = LeaseTexture(destination);
        RecordBlit(new BlitOperation(BlitKind.CopyTexture, from, 0, to, 0, 0));
    }

    /// <summary>
    /// Records a copy of tightly packed, row-major pixels at <paramref name="sourceOffset"/> bytes of a
    /// buffer into the whole of <paramref name="destination"/>
    /// (<c>copyFromBuffer:...toTexture:...</c>); Metal applies the texture's row padding.
    /// </summary>
    internal void RecordCopy<T>(ObjCHandle source, object sourceOwner, nuint sourceOffset, Texture2D<T> destination)
        where T : unmanaged
    {
        nint from = Lease(source, sourceOwner);
        nint to = LeaseTexture(destination);
        RecordBlit(new BlitOperation(
            BlitKind.CopyBufferToTexture, from, sourceOffset, to, 0, (nuint)destination.Width * (nuint)Unsafe.SizeOf<T>(), destination.Width, destination.Height));
    }

    /// <summary>
    /// Records a copy of the whole of <paramref name="source"/> into a buffer at
    /// <paramref name="destinationOffset"/> bytes, as tightly packed, row-major pixels
    /// (<c>copyFromTexture:...toBuffer:...</c>).
    /// </summary>
    internal void RecordCopy<T>(Texture2D<T> source, ObjCHandle destination, object destinationOwner, nuint destinationOffset)
        where T : unmanaged
    {
        nint from = LeaseTexture(source);
        nint to = Lease(destination, destinationOwner);
        RecordBlit(new BlitOperation(
            BlitKind.CopyTextureToBuffer, from, 0, to, destinationOffset, (nuint)source.Width * (nuint)Unsafe.SizeOf<T>(), source.Width, source.Height));
    }

    /// <summary>Leases a texture and the buffer that stores its pixels, as binding it does, and returns the <c>id&lt;MTLTexture&gt;</c>.</summary>
    private nint LeaseTexture<T>(Texture2D<T> texture)
        where T : unmanaged
    {
        nint pointer = Lease(texture.Handle, texture);
        Lease(texture.BufferHandle, texture);
        return pointer;
    }

    private void RecordBlit(in BlitOperation operation)
    {
        Debug.Assert(_commandBuffer == 0 && Volatile.Read(ref _released) == 0, "The batch is no longer recording.");

        _blits.Add(operation);

        try
        {
            _commands.Add(new Command(CommandKind.Blit, 0, _blits.Count - 1));
        }
        catch
        {
            _blits.RemoveAt(_blits.Count - 1);
            throw;
        }

        // The blit runs in its own encoder, and the compute encoder opened after it starts with
        // nothing bound: the next dispatch must set its pipeline even if it is the same one.
        _currentPipeline = 0;
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
            ReleaseAndRecycle();
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

    /// <summary>
    /// Encodes the batch into a command buffer someone else created and will commit (see
    /// <see cref="ComputeContext.EncodeInto"/>), and returns a task that completes when that command
    /// buffer has completed and the batch has released everything. The caller has checked that the
    /// command buffer belongs to the device and can still be encoded into.
    /// </summary>
    /// <remarks>
    /// The completed handler is attached before anything is encoded. If encoding then fails partway,
    /// the commands already encoded stay in the command buffer (nothing can take them out again), so
    /// what they bound must stay leased until it has run: the handler releases the batch either way.
    /// </remarks>
    internal Task EncodeInto(nint commandBuffer)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;
        GCHandle self = default;

        try
        {
            using AutoreleasePool pool = AutoreleasePool.Push();

            // Retained like one this batch creates: released with the batch, after the handler ran.
            _commandBuffer = ObjC.Retain(commandBuffer);

            self = GCHandle.Alloc(this);
            CompletionBlock.Attach(_commandBuffer, self);
            self = default;
        }
        catch
        {
            if (self.IsAllocated)
            {
                self.Free();
            }

            ReleaseResources();
            throw;
        }

        // From here on the handler owns the batch's release, whether or not the encoding succeeds.
        using (AutoreleasePool pool = AutoreleasePool.Push())
        {
            EncodeCommands();
        }

        return completion.Task;
    }

    /// <summary>Drops an uncommitted batch: releases everything without running it.</summary>
    internal void Abandon() => ReleaseResources();

    /// <summary>Drops an uncommitted batch like <see cref="Abandon"/>, then hands it back to its owner for reuse.</summary>
    internal void AbandonAndRecycle() => ReleaseAndRecycle();

    /// <summary>
    /// True when the batch has grown past what is worth keeping for reuse (an unusually large batch):
    /// its owner drops it instead, so one big frame does not pin that memory for the context's life.
    /// </summary>
    internal bool IsOversized => _commands.Capacity > MaxRetainedCommands || _constants.Length > MaxRetainedConstantBytes;

    // About 100 KB of commands (a few hundred dispatches) and 64 KB of constants.
    private const int MaxRetainedCommands = 4096;
    private const int MaxRetainedConstantBytes = 64 * 1024;

    /// <summary>
    /// Releases the batch and hands it to its owner. A release callback that throws still lets the
    /// batch be recycled: everything was released before the exception surfaces.
    /// </summary>
    private void ReleaseAndRecycle()
    {
        try
        {
            ReleaseResources();
        }
        finally
        {
            Recycle();
        }
    }

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

        // From here on only locals are used: once recycled, the owner may reset the batch at any time.
        // The task must complete whatever the callback does, or every awaiter would hang.
        try
        {
            Recycle();
        }
        catch (Exception e)
        {
            error ??= new MetalException("Recycling a completed batch failed.", e);
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
    /// Creates the command buffer, replays the recorded commands into it (opening a compute or a blit
    /// encoder as the commands require, see the class remarks) and ends encoding. On failure the open
    /// encoder is still ended (Metal aborts if it is deallocated otherwise); the caller releases the batch.
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
        EncodeCommands();
    }

    /// <summary>Replays the recorded commands into <see cref="_commandBuffer"/> and ends the last encoder, also on failure.</summary>
    private void EncodeCommands()
    {
        try
        {
            fixed (byte* constants = _constants)
            {
                foreach (Command command in _commands)
                {
                    nint encoder = OpenEncoder(blit: command.Kind == CommandKind.Blit);

                    switch (command.Kind)
                    {
                        case CommandKind.Pipeline:
                            ObjC.SendVoid(encoder, Selectors.SetComputePipelineState, command.Pointer);
                            break;
                        case CommandKind.Texture:
                            ObjC.SendVoid(encoder, Selectors.SetTextureAtIndex, command.Pointer, (nuint)command.Index);
                            break;
                        case CommandKind.Buffer:
                            ObjC.SendVoid(encoder, Selectors.SetBufferOffsetAtIndex, command.Pointer, command.Extra, (nuint)command.Index);
                            break;
                        case CommandKind.Bytes:
                            ObjC.SendVoid(encoder, Selectors.SetBytesLengthAtIndex, constants + command.Pointer, command.Extra, (nuint)command.Index);
                            break;
                        case CommandKind.Dispatch:
                            DispatchSizes sizes = _dispatches[command.Index];
                            ObjC.SendVoid(encoder, Selectors.DispatchThreadsThreadsPerThreadgroup, sizes.Grid, sizes.ThreadsPerThreadgroup);
                            break;
                        case CommandKind.DispatchIndirect:
                            ObjC.SendVoid(
                                encoder, Selectors.DispatchThreadgroupsWithIndirectBuffer, command.Pointer, command.Extra, _dispatches[command.Index].ThreadsPerThreadgroup);
                            break;
                        case CommandKind.Blit:
                            EncodeBlit(encoder, _blits[command.Index]);
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

    /// <summary>
    /// The encoder for the next command: the open one when it is of the right kind, otherwise a new one
    /// after ending the open one. Autoreleased by Metal and retained here, like the command buffer.
    /// </summary>
    private nint OpenEncoder(bool blit)
    {
        if (_encoder != 0 && _encoderIsBlit == blit)
        {
            return _encoder;
        }

        EndEncoding();

        nint encoder = ObjC.SendIntPtr(_commandBuffer, blit ? Selectors.BlitCommandEncoder : Selectors.ComputeCommandEncoder);

        if (encoder == 0)
        {
            throw new InvalidOperationException($"Metal failed to create a {(blit ? "blit" : "compute")} command encoder.");
        }

        _encoder = ObjC.Retain(encoder);
        _encoderIsBlit = blit;
        return encoder;
    }

    private static void EncodeBlit(nint encoder, in BlitOperation blit)
    {
        switch (blit.Kind)
        {
            case BlitKind.CopyBuffer:
                ObjC.SendVoid(encoder, Selectors.CopyFromBufferToBuffer, blit.Source, blit.SourceOffset, blit.Destination, blit.DestinationOffset, blit.Size);
                break;
            case BlitKind.FillBuffer:
                ObjC.SendVoid(encoder, Selectors.FillBufferRangeValue, blit.Destination, new NSRange(blit.DestinationOffset, blit.Size), blit.Value);
                break;
            case BlitKind.CopyTexture:
                // Level 0 and slice 0 only, the whole of what a Texture2D is: a wrapped native texture
                // can have mipmaps, which copyFromTexture:toTexture: would copy as well (and require of both).
                ObjC.SendVoid(encoder, Selectors.CopyFromTextureToTextureLevels, blit.Source, 0, 0, blit.Destination, 0, 0, 1, 1);
                break;
            case BlitKind.CopyBufferToTexture:
            {
                // Size is the packed row: the pixels are tightly packed in the buffer, whatever padding the texture's rows have.
                MTLSize size = new((nuint)blit.Width, (nuint)blit.Height, 1);
                ObjC.SendVoid(
                    encoder, Selectors.CopyFromBufferToTexture, blit.Source, blit.SourceOffset, blit.Size, blit.Size * (nuint)blit.Height, size, blit.Destination, 0, 0, default(MTLOrigin));
                break;
            }

            case BlitKind.CopyTextureToBuffer:
            {
                MTLSize size = new((nuint)blit.Width, (nuint)blit.Height, 1);
                ObjC.SendVoid(
                    encoder, Selectors.CopyFromTextureToBuffer, blit.Source, 0, 0, default(MTLOrigin), size, blit.Destination, blit.DestinationOffset, blit.Size, blit.Size * (nuint)blit.Height);
                break;
            }
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

    /// <summary>"shader 'X'" for one dispatch, otherwise the operation counts and every shader type in the batch.</summary>
    private string Describe()
    {
        if (_dispatches.Count == 1 && _shaderTypes.Count == 1 && _blits.Count == 0)
        {
            return $"shader '{_shaderTypes[0]}'";
        }

        if (_dispatches.Count == 0)
        {
            return _blits.Count == 1 ? "a GPU copy or fill" : $"a batch of {_blits.Count} GPU copies and fills";
        }

        string blits = _blits.Count == 0 ? string.Empty : $" and {_blits.Count} copies or fills";
        return $"a batch of {_dispatches.Count} dispatches{blits} (shaders: {string.Join(", ", _shaderTypes.ConvertAll(static t => $"'{t}'"))})";
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
        _queue = 0;
        _commands.Clear();

        if (_temporaries is not null)
        {
            foreach (nint temporary in _temporaries)
            {
                ObjC.Release(temporary);
            }

            _temporaries.Clear();
        }

        // The last release of a wrapped buffer runs the caller's release callback, possibly on Metal's
        // completion thread; one that throws must not stop the other leases from being released.
        GraphicsDevice.ReleaseLeases(_leases);
    }

    /// <summary>Hands a released batch to its owner (the recycle callback), if it has one.</summary>
    private void Recycle()
    {
        if (Volatile.Read(ref _released) == 1)
        {
            _recycle?.Invoke(this);
        }
    }

    internal enum CommandKind : byte
    {
        Pipeline,
        Texture,
        Buffer,
        Bytes,
        Dispatch,
        DispatchIndirect,
        Blit,
    }

    /// <summary>
    /// One recorded encoder call, 24 bytes (two words, an int and the kind; see
    /// <c>CommandBatch_CommandsStay24Bytes</c>). <see cref="Pointer"/> is an <c>id</c> (pipeline,
    /// texture, buffer, indirect arguments buffer) or, for <see cref="CommandKind.Bytes"/>, the offset of
    /// the copied constants. <see cref="Extra"/> is the byte offset a buffer is bound at (a view's start,
    /// which can exceed 4 GB), the byte offset of the arguments of an indirect dispatch, or the byte
    /// count of <see cref="CommandKind.Bytes"/>. <see cref="Index"/> is the argument index, or the
    /// position of a dispatch's sizes in the dispatch list, or of a copy or fill in the blit list.
    /// </summary>
    internal readonly struct Command(CommandKind kind, nint pointer, int index, nuint extra = 0)
    {
        public nint Pointer { get; } = pointer;

        public nuint Extra { get; } = extra;

        public int Index { get; } = index;

        public CommandKind Kind { get; } = kind;
    }

    /// <summary>The sizes of one dispatch. An indirect dispatch has no <see cref="Grid"/>: its counts come from a buffer.</summary>
    private readonly record struct DispatchSizes(MTLSize Grid, MTLSize ThreadsPerThreadgroup);

    private enum BlitKind : byte
    {
        CopyBuffer,
        FillBuffer,
        CopyTexture,
        CopyBufferToTexture,
        CopyTextureToBuffer,
    }

    /// <summary>
    /// One recorded copy or fill. Offsets and <see cref="Size"/> are in bytes; for the buffer↔texture
    /// copies <see cref="Size"/> is the length of one tightly packed row and <see cref="Width"/> and
    /// <see cref="Height"/> the texture's size. <see cref="Source"/> is unused by a fill.
    /// </summary>
    private readonly record struct BlitOperation(
        BlitKind Kind, nint Source, nuint SourceOffset, nint Destination, nuint DestinationOffset, nuint Size, int Width = 0, int Height = 0, byte Value = 0);
}
