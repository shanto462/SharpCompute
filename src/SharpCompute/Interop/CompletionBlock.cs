using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpCompute.Interop;

/// <summary>
/// The one Objective-C block SharpCompute needs: the <c>void (^)(id&lt;MTLCommandBuffer&gt;)</c> passed to
/// <c>addCompletedHandler:</c>. Built by hand on the documented block ABI (clang's "Block
/// Implementation Specification") so it stays AOT-safe: no runtime code generation, no reflection,
/// one static <see cref="UnmanagedCallersOnlyAttribute"/> invoke function shared by every block.
/// </summary>
/// <remarks>
/// <para>
/// The block is created as a <em>stack</em> block (<c>isa = _NSConcreteStackBlock</c>) in the caller's
/// frame. Metal must <c>Block_copy</c> any handler it keeps (the convention for escaping blocks), and
/// copying a stack block without copy/dispose helpers is a plain <c>malloc</c> + <c>memcpy</c> of
/// <see cref="BlockDescriptor.Size"/> bytes. The copy carries the captured <see cref="GCHandle"/>
/// value; the stack original can go away as soon as <c>addCompletedHandler:</c> returns.
/// </para>
/// <para>
/// Metal invokes a completed handler exactly once, so the <see cref="GCHandle"/> is freed inside the
/// invoke function rather than by a dispose helper: the heap copy is then just bytes that Metal frees
/// with <c>Block_release</c>. The handle must therefore only be attached to a command buffer that is
/// committed (a never-committed buffer never calls its handlers, which would leak the handle).
/// </para>
/// </remarks>
internal static unsafe class CompletionBlock
{
    /// <summary><c>BLOCK_HAS_SIGNATURE</c>: the descriptor carries an Objective-C type encoding.</summary>
    private const int BlockHasSignature = 1 << 30;

    private static readonly nint s_stackBlockClass = LoadStackBlockClass();
    private static readonly BlockDescriptor* s_descriptor = CreateDescriptor();

    /// <summary>
    /// <c>[commandBuffer addCompletedHandler:^(id&lt;MTLCommandBuffer&gt;) { target.OnCompleted(); }]</c>.
    /// Ownership of <paramref name="target"/> passes to the block, which frees it when it runs.
    /// </summary>
    internal static void Attach(nint commandBuffer, GCHandle target)
    {
        BlockLiteral block = new()
        {
            Isa = s_stackBlockClass,
            Flags = BlockHasSignature,
            Invoke = &Invoke,
            Descriptor = s_descriptor,
            Target = GCHandle.ToIntPtr(target),
        };

        ObjC.SendVoid(commandBuffer, Selectors.AddCompletedHandler, (nint)(&block));
    }

    // Called by Metal on one of its completion threads (the runtime attaches it on first use).
    // Nothing may escape: an exception crossing an UnmanagedCallersOnly boundary terminates the process.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Invoke(BlockLiteral* block, nint commandBuffer)
    {
        GCHandle handle = GCHandle.FromIntPtr(block->Target);
        CommandBatch? batch = handle.Target as CommandBatch;
        handle.Free();

        batch?.OnCompleted();
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
        // handler has run. "v16@?0@8" = void (block, id): return, block self, one object argument.
        ReadOnlySpan<byte> signature = "v16@?0@8\0"u8;
        byte* signatureCopy = (byte*)NativeMemory.Alloc((nuint)signature.Length);
        signature.CopyTo(new Span<byte>(signatureCopy, signature.Length));

        BlockDescriptor* descriptor = (BlockDescriptor*)NativeMemory.Alloc((nuint)sizeof(BlockDescriptor));
        descriptor->Reserved = 0;
        descriptor->Size = (nuint)sizeof(BlockLiteral);
        descriptor->Signature = signatureCopy;
        return descriptor;
    }

    /// <summary><c>struct Block_descriptor_1</c> followed by the signature field (no copy/dispose helpers).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
        public byte* Signature;
    }

    /// <summary><c>struct Block_layout</c> plus one captured variable: the <see cref="GCHandle"/> of the batch.</summary>
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
