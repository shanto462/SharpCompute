using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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
/// completes, when an uncommitted batch is abandoned, or when a caller's command buffer it was encoded
/// into (<see cref="EncodeInto"/>) is deallocated without having been committed. Metal's command buffer (created with retained
/// references) also retains what is bound to it, but the leases make SharpCompute's own guarantee
/// explicit: a buffer, texture, pipeline or queue disposed while a batch that uses it is pending keeps
/// its reference (and its GC memory pressure) until the GPU is done, whatever Metal does internally.
/// A command buffer and the encoders this batch creates are autoreleased by Metal and are retained here
/// for the same reason; a caller's command buffer is not (see <see cref="EncodeInto"/>).
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
    private BindingTable? _bindings;
    private List<nint>? _temporaries;
    private TaskCompletionSource? _completion;
    private nint _queue;
    private nint _commandBuffer;
    private nint _encoder;
    private bool _encoderIsBlit;
    private nint _currentPipeline;
    // Copies and fills as the caller recorded them: one of them can be several blits and dispatches
    // (see RecordCopy), and the dispatches of those are counted apart from the caller's own.
    private int _copyCount;
    private int _helperDispatches;
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
        _copyCount = 0;
        _helperDispatches = 0;
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
    // releases. A committed (or encoded-into) batch is rooted by the wait or by the GCHandle its
    // completion handler's block owns, and suppresses finalization once it completes or is dropped.
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

    /// <summary>Number of dispatches recorded so far (direct and indirect), not counting the kernels a copy or fill may need.</summary>
    internal int DispatchCount => _dispatches.Count - _helperDispatches;

    /// <summary>Number of operations recorded so far: dispatches, copies and fills, each as the caller recorded it.</summary>
    internal int OperationCount => DispatchCount + _copyCount;

    /// <summary>
    /// Tests only: report this error description at completion as if Metal had set the command
    /// buffer's status to <c>MTLCommandBufferStatusError</c>. Real GPU faults cannot be provoked
    /// reliably (or politely, on a shared GPU), and everything after the status check is the same.
    /// </summary>
    internal void SimulateGpuError(string description) => _simulatedError = description;

    /// <summary>Tests only: a task for this batch's completion, as <see cref="CommitAsync"/> and <see cref="EncodeInto"/> make one.</summary>
    internal Task TrackCompletionForTests()
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;
        return completion.Task;
    }

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

            // What this dispatch binds, to refuse bindings that overlap (see BindingTable). The batch
            // records one dispatch at a time, so one table of its own serves them all.
            BindingTable bindings = _bindings ??= new BindingTable();
            bindings.Clear();

            ShaderArgumentWriter writer = new(0, _device, this, bindings);

            if (output is not null)
            {
                writer.NoteOutput(output);
            }

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
    /// Records a copy of <paramref name="size"/> bytes between two buffers. Offsets and
    /// <paramref name="size"/> are in bytes and already validated (the ranges do not overlap); both
    /// buffers are leased now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A blit (<c>copyFromBuffer:sourceOffset:toBuffer:destinationOffset:size:</c>) when both offsets
    /// and the size are multiples of 4, which is what Metal documents the blit to need on macOS, and
    /// which every copy of 4-byte (or 8-, 12-, 16-byte...) elements is.
    /// </para>
    /// <para>
    /// Otherwise the bytes the blit may not be given are copied by <see cref="CopyBytesKernel"/>, in
    /// the same command buffer and so in the same place in the recorded order. When the two offsets
    /// are the same distance from a multiple of 4, that is at most 3 bytes at each end: the aligned
    /// middle is still one blit. When they are not (2-byte elements copied from an odd index to an even
    /// one), no part of the range is aligned on both sides and the kernel copies all of it.
    /// The three parts touch different bytes, so their order among themselves does not matter; the
    /// blit goes first, which costs no more encoder switches than any other order.
    /// </para>
    /// </remarks>
    internal void RecordCopy(
        ObjCHandle source, object sourceOwner, nuint sourceOffset, ObjCHandle destination, object destinationOwner, nuint destinationOffset, nuint size)
    {
        nint from = Lease(source, sourceOwner);
        nint to = Lease(destination, destinationOwner);

        if (((sourceOffset | destinationOffset | size) & BlitAlignmentMask) == 0)
        {
            RecordBlit(new BlitOperation(BlitKind.CopyBuffer, from, sourceOffset, to, destinationOffset, size));
            _copyCount++;
            return;
        }

        // Compiled on the first copy that needs it; nothing is recorded if that fails.
        ComputePipeline kernel = _device.CopyBytesPipeline;
        Checkpoint checkpoint = new(this);

        try
        {
            SplitAtAlignment(sourceOffset, size, out nuint head, out nuint middle);

            if (((sourceOffset ^ destinationOffset) & BlitAlignmentMask) != 0 || middle == 0)
            {
                RecordByteKernel(kernel, from, sourceOffset, to, destinationOffset, size, value: 0);
            }
            else
            {
                nuint tail = size - head - middle;

                RecordBlit(new BlitOperation(BlitKind.CopyBuffer, from, sourceOffset + head, to, destinationOffset + head, middle));

                if (head != 0)
                {
                    RecordByteKernel(kernel, from, sourceOffset, to, destinationOffset, head, value: 0);
                }

                if (tail != 0)
                {
                    RecordByteKernel(kernel, from, sourceOffset + head + middle, to, destinationOffset + head + middle, tail, value: 0);
                }
            }
        }
        catch
        {
            checkpoint.Restore(this);
            throw;
        }

        _copyCount++;
    }

    /// <summary>Records a fill of <paramref name="size"/> bytes from <paramref name="offset"/> with <paramref name="value"/>; the buffer is leased now.</summary>
    /// <remarks>
    /// A blit (<c>fillBuffer:range:value:</c>) when the offset and the size are multiples of 4, as
    /// Metal documents it to need on macOS. Otherwise the aligned middle of the range is that blit
    /// and the up to 3 bytes at each end are set by <see cref="FillBytesKernel"/>; see <see cref="RecordCopy(ObjCHandle, object, nuint, ObjCHandle, object, nuint, nuint)"/>.
    /// </remarks>
    internal void RecordFill(ObjCHandle buffer, object owner, nuint offset, nuint size, byte value)
    {
        nint target = Lease(buffer, owner);

        if (((offset | size) & BlitAlignmentMask) == 0)
        {
            RecordBlit(new BlitOperation(BlitKind.FillBuffer, 0, 0, target, offset, size, Value: value));
            _copyCount++;
            return;
        }

        ComputePipeline kernel = _device.FillBytesPipeline;
        Checkpoint checkpoint = new(this);

        try
        {
            SplitAtAlignment(offset, size, out nuint head, out nuint middle);

            if (middle == 0)
            {
                RecordByteKernel(kernel, source: 0, sourceOffset: 0, target, offset, size, value);
            }
            else
            {
                nuint tail = size - head - middle;

                RecordBlit(new BlitOperation(BlitKind.FillBuffer, 0, 0, target, offset + head, middle, Value: value));

                if (head != 0)
                {
                    RecordByteKernel(kernel, source: 0, sourceOffset: 0, target, offset, head, value);
                }

                if (tail != 0)
                {
                    RecordByteKernel(kernel, source: 0, sourceOffset: 0, target, offset + head + middle, tail, value);
                }
            }
        }
        catch
        {
            checkpoint.Restore(this);
            throw;
        }

        _copyCount++;
    }

    /// <summary>What Metal documents a buffer blit's offsets and size to be multiples of on macOS, as a mask.</summary>
    private const nuint BlitAlignmentMask = 3;

    /// <summary>
    /// Divides <paramref name="size"/> bytes at <paramref name="offset"/> into the bytes before the
    /// first multiple of 4 (<paramref name="head"/>, 0 to 3), the whole 4-byte units after them
    /// (<paramref name="middle"/>) and the rest (0 to 3 bytes).
    /// </summary>
    private static void SplitAtAlignment(nuint offset, nuint size, out nuint head, out nuint middle)
    {
        head = Math.Min((4 - (offset & BlitAlignmentMask)) & BlitAlignmentMask, size);
        middle = (size - head) & ~BlitAlignmentMask;
    }

    /// <summary>
    /// Records one dispatch of a byte kernel over <paramref name="size"/> bytes: its pipeline, the
    /// buffers bound at the bytes the range starts at (<paramref name="source"/> is 0 for a fill, which
    /// has none), its constants and a grid of one thread per <see cref="ByteKernel.Chunk"/> bytes.
    /// The buffers are already leased by the caller.
    /// </summary>
    private void RecordByteKernel(ComputePipeline kernel, nint source, nuint sourceOffset, nint destination, nuint destinationOffset, nuint size, byte value)
    {
        nint state = kernel.State.DangerousGetHandle();

        if (state != _currentPipeline)
        {
            state = Lease(kernel.State, _device);
            _commands.Add(new Command(CommandKind.Pipeline, state, 0));
            _currentPipeline = state;
        }

        int index = 0;

        if (source != 0)
        {
            _commands.Add(new Command(CommandKind.Buffer, source, index++, sourceOffset));
        }

        _commands.Add(new Command(CommandKind.Buffer, destination, index++, destinationOffset));

        ByteKernel.Parameters parameters = new(size, ByteKernel.Chunk, value);
        RecordBytes(&parameters, sizeof(ByteKernel.Parameters), index);

        nuint threads = (size + ByteKernel.Chunk - 1) / ByteKernel.Chunk;
        nuint threadsPerThreadgroup = Math.Min(Math.Min(threads, 256), kernel.MaxTotalThreadsPerThreadgroup);

        _dispatches.Add(new DispatchSizes(new MTLSize(threads, 1, 1), new MTLSize(threadsPerThreadgroup, 1, 1)));
        _helperDispatches++;
        _commands.Add(new Command(CommandKind.Dispatch, 0, _dispatches.Count - 1));
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
        _copyCount++;
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
        _copyCount++;
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
        _copyCount++;
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
            error = GetError(_commandBuffer);
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

        try
        {
            using AutoreleasePool pool = AutoreleasePool.Push();

            EncodeRecorded();

            // From Attach on, the handler's block roots this batch, and through it every leased
            // resource, until the command buffer has completed (see CompletionBlock).
            CompletionBlock.Attach(_commandBuffer, this);
            ObjC.SendVoid(_commandBuffer, Selectors.Commit);
        }
        catch
        {
            // Nothing was committed: drop the batch, so a failure here cannot leak the leases. If the
            // handler was attached, releasing the command buffer (the last thing ReleaseResources does)
            // disposes it, and the batch is reported as dropped, which nobody awaits.
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
    /// <para>
    /// The command buffer is the caller's and is not retained: the caller may release it uncommitted,
    /// which Metal allows, and then it must be deallocated. Its completed handler then never runs, and
    /// the block's dispose helper reports the batch as dropped (<see cref="OnDropped"/>) instead, which
    /// releases everything and faults the task. Either way the batch is released exactly once.
    /// </para>
    /// <para>
    /// The handler is attached before anything is encoded. If encoding then fails partway, the commands
    /// already encoded stay in the command buffer (nothing can take them out again), so what they bound
    /// must stay leased until it has run or been dropped.
    /// </para>
    /// </remarks>
    internal Task EncodeInto(nint commandBuffer)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;

        try
        {
            using AutoreleasePool pool = AutoreleasePool.Push();
            CompletionBlock.Attach(commandBuffer, this);
        }
        catch
        {
            ReleaseResources();
            throw;
        }

        // From here on the handler (or the drop) owns the batch's release, whether or not the encoding
        // succeeds. The caller holds the command buffer for the length of this call; afterwards this
        // batch keeps no pointer to it.
        try
        {
            using AutoreleasePool pool = AutoreleasePool.Push();
            _commandBuffer = commandBuffer;
            EncodeCommands();
        }
        finally
        {
            _commandBuffer = 0;
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

    /// <summary>
    /// The completed handler of <paramref name="commandBuffer"/>; runs on a Metal completion thread and
    /// must not throw. Metal also calls it, from the command buffer's deallocation and on the thread
    /// that released it, when a command buffer that was never committed is deallocated (measured on
    /// macOS 27 on an Apple M4 Pro): the status then says the work never ran, and the batch is
    /// reported as dropped (<see cref="OnDropped"/>) instead.
    /// </summary>
    internal void OnCompleted(nint commandBuffer)
    {
        nuint status;

        try
        {
            status = ObjC.SendNUInt(commandBuffer, Selectors.Status);
        }
        catch
        {
            status = Metal.CommandBufferStatusCompleted;
        }

        if (status < Metal.CommandBufferStatusCompleted)
        {
            OnDropped();
            return;
        }

        TaskCompletionSource? completion = _completion;
        MetalException? error = null;

        try
        {
            using AutoreleasePool pool = AutoreleasePool.Push();
            error = GetError(commandBuffer);
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

    /// <summary>The message of the exception a dropped batch's task faults with.</summary>
    internal const string DroppedMessage = "The command buffer was released without being committed";

    /// <summary>
    /// The command buffer this batch was encoded into was deallocated without having been committed,
    /// so its work never ran: releases everything the batch pinned and faults the task with
    /// <see cref="InvalidOperationException"/>. Reached from the completed handler, which Metal calls
    /// with the uncommitted status as the command buffer is deallocated, or, if a Metal version did
    /// not call it, from the handler block's dispose helper (see <see cref="CompletionBlock"/>). Runs
    /// on the thread that released the command buffer and must not throw.
    /// </summary>
    internal void OnDropped()
    {
        TaskCompletionSource? completion = _completion;
        Exception error = new InvalidOperationException($"{DroppedMessage}, so {Describe()} encoded into it never ran.");

        try
        {
            ReleaseResources();
        }
        catch (Exception e)
        {
            error = new AggregateException(error, e);
        }

        // As in OnCompleted: only locals from here on, and the task completes whatever happens.
        try
        {
            Recycle();
        }
        catch
        {
            // The owner's callback must not throw; the task still has to complete.
        }

        completion?.TrySetException(error);
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

    /// <summary>The error of a completed <paramref name="commandBuffer"/>, or null when it ran without one.</summary>
    private MetalException? GetError(nint commandBuffer)
    {
        string description;

        if (ObjC.SendNUInt(commandBuffer, Selectors.Status) == Metal.CommandBufferStatusError)
        {
            // `error` is an autoreleased getter; callers hold a pool.
            description = NSString.DescribeError(ObjC.SendIntPtr(commandBuffer, Selectors.Error));
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
        // As the caller recorded them: a copy is one copy, however many blits and kernels it became.
        int dispatches = DispatchCount;

        if (dispatches == 1 && _shaderTypes.Count == 1 && _copyCount == 0)
        {
            return $"shader '{_shaderTypes[0]}'";
        }

        if (dispatches == 0)
        {
            return _copyCount == 1 ? "a GPU copy or fill" : $"a batch of {_copyCount} GPU copies and fills";
        }

        string copies = _copyCount == 0 ? string.Empty : $" and {_copyCount} copies or fills";
        return $"a batch of {dispatches} dispatches{copies} (shaders: {string.Join(", ", _shaderTypes.ConvertAll(static t => $"'{t}'"))})";
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

        // Set only while this batch holds its own reference: one it created and committed itself
        // (never the caller's, see EncodeInto). Released last: if its completed handler never ran,
        // that release disposes the handler, which reports the batch as dropped, and by then
        // everything else must already be released.
        nint commandBuffer = _commandBuffer;
        _commandBuffer = 0;
        _queue = 0;
        _commands.Clear();

        try
        {
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
        finally
        {
            ObjC.Release(commandBuffer);
        }
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

    /// <summary>
    /// What a recording that takes several steps puts back when one of them fails, so that nothing of
    /// it stays recorded. Leases taken since then stay with the batch, which only delays their release.
    /// </summary>
    private readonly struct Checkpoint(CommandBatch batch)
    {
        private readonly int _commands = batch._commands.Count;
        private readonly int _dispatches = batch._dispatches.Count;
        private readonly int _blits = batch._blits.Count;
        private readonly int _constantsLength = batch._constantsLength;
        private readonly int _helperDispatches = batch._helperDispatches;
        private readonly nint _currentPipeline = batch._currentPipeline;

        public void Restore(CommandBatch batch)
        {
            batch._commands.RemoveRange(_commands, batch._commands.Count - _commands);
            batch._dispatches.RemoveRange(_dispatches, batch._dispatches.Count - _dispatches);
            batch._blits.RemoveRange(_blits, batch._blits.Count - _blits);
            batch._constantsLength = _constantsLength;
            batch._helperDispatches = _helperDispatches;
            batch._currentPipeline = _currentPipeline;
        }
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
