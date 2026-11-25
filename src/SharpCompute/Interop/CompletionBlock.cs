using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace SharpCompute.Interop;

/// <summary>
/// The one Objective-C block SharpCompute needs: the <c>void (^)(id&lt;MTLCommandBuffer&gt;)</c> passed to
/// <c>addCompletedHandler:</c>. Built by hand on the documented block ABI (clang's "Block
/// Implementation Specification") so it stays AOT-safe: no runtime code generation, no reflection,
/// static <see cref="UnmanagedCallersOnlyAttribute"/> functions shared by every block.
/// </summary>
/// <remarks>
/// <para>
/// The block is created as a <em>stack</em> block (<c>isa = _NSConcreteStackBlock</c>) in the caller's
/// frame and captures one <see cref="GCHandle"/> to an <see cref="Attachment"/>. Metal
/// <c>Block_copy</c>s the handler it keeps; the copy helper counts the heap copies, and the dispose
/// helper, which libclosure runs when Metal releases a copy, frees the handle once the last copy is
/// gone. The stack original can go away as soon as <c>addCompletedHandler:</c> returns.
/// </para>
/// <para>
/// Exactly one of two things then finishes the batch, decided by an atomic flag in the attachment:
/// the handler (<see cref="CommandBatch.OnCompleted"/>), or the dispose helper. Metal invokes the
/// handler when the command buffer completes, and also, with the command buffer still uncommitted, when
/// one that was never committed is deallocated (measured on macOS 27; <see cref="CommandBatch.OnCompleted"/>
/// tells the two apart by the status). Should Metal release a handler without calling it, the dispose
/// helper reports the batch as dropped instead (<see cref="CommandBatch.OnDropped"/>). A handler that
/// ran is disposed afterwards, and the flag makes that dispose a plain free of the handle. Dropping an
/// uncommitted command buffer is legal in Metal, and a caller of <see cref="ComputeContext.EncodeInto"/>
/// may do it, so the batch must not keep the command buffer alive itself: it does not retain one it
/// did not create, and the handler receives the command buffer as its argument.
/// </para>
/// </remarks>
internal static unsafe partial class CompletionBlock
{
    /// <summary><c>BLOCK_HAS_COPY_DISPOSE</c>: the descriptor carries copy and dispose helpers.</summary>
    private const int BlockHasCopyDispose = 1 << 25;

    /// <summary><c>BLOCK_HAS_SIGNATURE</c>: the descriptor carries an Objective-C type encoding.</summary>
    private const int BlockHasSignature = 1 << 30;

    private static readonly nint s_stackBlockClass = LoadStackBlockClass();
    private static readonly BlockDescriptor* s_descriptor = CreateDescriptor();
    private static int s_liveAttachments;

    /// <summary>Tests only: how many attachments are alive (some copy of their block has not been disposed yet).</summary>
    internal static int LiveAttachments => Volatile.Read(ref s_liveAttachments);

    /// <summary>
    /// <c>[commandBuffer addCompletedHandler:^(id&lt;MTLCommandBuffer&gt; cb) { batch.OnCompleted(cb); }]</c>,
    /// with <see cref="CommandBatch.OnDropped"/> run instead if the command buffer is deallocated
    /// without the handler having run. From here on, the block is what roots <paramref name="batch"/>
    /// (and through it every leased resource) until one of the two has run.
    /// </summary>
    /// <exception cref="InvalidOperationException">Metal did not keep the handler (it always does); nothing is attached then.</exception>
    internal static void Attach(nint commandBuffer, CommandBatch batch)
    {
        Attachment attachment = new(batch);
        GCHandle handle = GCHandle.Alloc(attachment);

        BlockLiteral block = new()
        {
            Isa = s_stackBlockClass,
            Flags = BlockHasCopyDispose | BlockHasSignature,
            Invoke = &Invoke,
            Descriptor = s_descriptor,
            Target = GCHandle.ToIntPtr(handle),
        };

        ObjC.SendVoid(commandBuffer, Selectors.AddCompletedHandler, (nint)(&block));

        // Metal copies an escaping block before addCompletedHandler: returns. Had it not, no dispose
        // would ever free the handle, and no handler would ever release the batch.
        if (Volatile.Read(ref attachment.Copies) == 0)
        {
            handle.Free();
            throw new InvalidOperationException("Metal did not keep the command buffer's completed handler.");
        }
    }

    /// <summary>
    /// Tests only: what happens if Metal releases a handler without ever calling it. The block is built
    /// exactly as <see cref="Attach"/> builds it, copied with <c>_Block_copy</c> as Metal copies it,
    /// then released with <c>_Block_release</c> without being invoked, so libclosure runs the dispose
    /// helper. (Metal itself calls the handler of a command buffer deallocated uncommitted, so this
    /// path cannot be reached through it.)
    /// </summary>
    internal static void CopyAndReleaseWithoutInvokingForTests(CommandBatch batch)
    {
        GCHandle handle = GCHandle.Alloc(new Attachment(batch));

        BlockLiteral block = new()
        {
            Isa = s_stackBlockClass,
            Flags = BlockHasCopyDispose | BlockHasSignature,
            Invoke = &Invoke,
            Descriptor = s_descriptor,
            Target = GCHandle.ToIntPtr(handle),
        };

        nint copy = BlockCopy((nint)(&block));
        BlockRelease(copy);
    }

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_copy")]
    private static partial nint BlockCopy(nint block);

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_release")]
    private static partial void BlockRelease(nint block);

    // Called by Metal on one of its completion threads (the runtime attaches it on first use).
    // Nothing may escape: an exception crossing an UnmanagedCallersOnly boundary terminates the process.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Invoke(BlockLiteral* block, nint commandBuffer)
    {
        try
        {
            Attachment attachment = (Attachment)GCHandle.FromIntPtr(block->Target).Target!;

            if (attachment.TryClaim())
            {
                attachment.Batch.OnCompleted(commandBuffer);
            }
        }
        catch
        {
            // OnCompleted reports its own failures through the task; anything that still gets here
            // (it should not) must not take the process down on Metal's thread.
        }
    }

    // Block_copy of a block with BLOCK_HAS_COPY_DISPOSE: libclosure has already copied the bytes
    // (Target included) into the destination; this counts the copy, which the dispose helper uncounts.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Copy(BlockLiteral* destination, BlockLiteral* source)
    {
        try
        {
            Attachment attachment = (Attachment)GCHandle.FromIntPtr(destination->Target).Target!;

            if (Interlocked.Increment(ref attachment.Copies) == 1)
            {
                Interlocked.Increment(ref s_liveAttachments);
            }
        }
        catch
        {
            // Unreachable in practice; see Invoke.
        }
    }

    // Run by libclosure when a heap copy's last reference goes: after the handler ran, or because the
    // command buffer was deallocated without having been committed. It runs on whatever thread
    // released the command buffer, which may be the app's own.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Dispose(BlockLiteral* block)
    {
        try
        {
            GCHandle handle = GCHandle.FromIntPtr(block->Target);
            Attachment attachment = (Attachment)handle.Target!;

            if (Interlocked.Decrement(ref attachment.Copies) != 0)
            {
                return;
            }

            // No copy is left that Metal could still invoke: the handle can go, and a handler that has
            // not run by now never will.
            handle.Free();
            Interlocked.Decrement(ref s_liveAttachments);

            if (attachment.TryClaim())
            {
                attachment.Batch.OnDropped();
            }
        }
        catch
        {
            // OnDropped reports its own failures through the task; see Invoke.
        }
    }

    private static nint LoadStackBlockClass()
    {
        // The address of the symbol *is* the isa (the C declaration is `void *_NSConcreteStackBlock[32]`).
        // libSystem re-exports libsystem_blocks, so the lookup resolves through it.
        nint libSystem = NativeLibrary.Load("/usr/lib/libSystem.B.dylib");
        return NativeLibrary.GetExport(libSystem, "_NSConcreteStackBlock");
    }

    private static BlockDescriptor* CreateDescriptor()
    {
        // Process lifetime, never freed: every block shares it and Metal may read it until the last
        // handler has been disposed. "v16@?0@8" = void (block, id): return, block self, one object argument.
        ReadOnlySpan<byte> signature = "v16@?0@8\0"u8;
        byte* signatureCopy = (byte*)NativeMemory.Alloc((nuint)signature.Length);
        signature.CopyTo(new Span<byte>(signatureCopy, signature.Length));

        BlockDescriptor* descriptor = (BlockDescriptor*)NativeMemory.Alloc((nuint)sizeof(BlockDescriptor));
        descriptor->Reserved = 0;
        descriptor->Size = (nuint)sizeof(BlockLiteral);
        descriptor->Copy = &Copy;
        descriptor->Dispose = &Dispose;
        descriptor->Signature = signatureCopy;
        descriptor->Layout = null;
        return descriptor;
    }

    /// <summary>
    /// What a block's <see cref="GCHandle"/> points at: the batch, whether its completion (or its drop)
    /// has been claimed, and how many heap copies of the block exist. One per attach, never reused: a
    /// batch is recycled and attached again while a block from its previous use may still be alive.
    /// </summary>
    private sealed class Attachment(CommandBatch batch)
    {
        public readonly CommandBatch Batch = batch;
        public int Copies;
        private int _claimed;

        /// <summary>True for the first caller only: the one that finishes the batch.</summary>
        public bool TryClaim() => Interlocked.Exchange(ref _claimed, 1) == 0;
    }

    /// <summary>
    /// <c>struct Block_descriptor_1</c>, <c>Block_descriptor_2</c> (the copy and dispose helpers, present
    /// with <c>BLOCK_HAS_COPY_DISPOSE</c>) and <c>Block_descriptor_3</c> (signature and layout, read with
    /// <c>BLOCK_HAS_SIGNATURE</c>; the layout is null: no extended layout), in that order, as clang emits them.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
        public delegate* unmanaged[Cdecl]<BlockLiteral*, BlockLiteral*, void> Copy;
        public delegate* unmanaged[Cdecl]<BlockLiteral*, void> Dispose;
        public byte* Signature;
        public byte* Layout;
    }

    /// <summary><c>struct Block_layout</c> plus one captured variable: the <see cref="GCHandle"/> of the attachment.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public nint Isa;
        public int Flags;
        public int Reserved;
        public delegate* unmanaged[Cdecl]<BlockLiteral*, nint, void> Invoke;
        public BlockDescriptor* Descriptor;
        public nint Target;
    }
}
