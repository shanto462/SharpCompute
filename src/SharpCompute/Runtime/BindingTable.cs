using System;
using System.Runtime.CompilerServices;

namespace SharpCompute;

/// <summary>
/// The buffers and textures one dispatch has bound so far, kept for the length of its
/// <c>SetArguments</c> to refuse two bindings that reach the same memory when the shader writes
/// through either of them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is an error.</b> A shader names each capture as if it were memory of its own, and Metal
/// compiles it that way. The Metal compiler marks every buffer argument of a kernel as not aliasing
/// any other, so a value read through one argument is kept across a write through another: on an
/// Apple M4 Pro, <c>a[i] = b[i] + 1; a[i] = b[i] + 1; o[i] = b[i];</c> with one buffer bound as both
/// <c>a</c> and <c>b</c> leaves 1 in it and 0 in <c>o</c>, where C# (and <c>ForCpu</c>) give 2 and 2.
/// A pixel written through one texture argument is not seen by a read through another argument bound
/// to the same texture either (the fence the generator emits is for reads of the capture that was
/// written), nor by a read of the buffer that stores the texture's pixels. None of that is a race
/// between threads: a single thread reads stale data. So the dispatch is refused when it is
/// recorded, on the GPU and on the CPU alike, instead of returning other results than the C# does.
/// </para>
/// <para>
/// <b>What is compared.</b> Buffers and linear textures by the CPU address range their elements or
/// pixels occupy, so the same buffer bound twice, overlapping views (<c>Slice</c>) of one buffer, two
/// buffers that wrap the same or overlapping memory, and a texture whose pixels live in a buffer that
/// is bound too are all found, whichever <c>MTLBuffer</c> objects are involved. A buffer the CPU cannot
/// access (private storage) has no address and is compared by its <c>MTLBuffer</c> and byte range.
/// Textures are also compared by <c>MTLTexture</c>, which is what a texture and its
/// <see cref="ReadWriteTexture2D{T}.AsReadOnly"/> view, or two wrappers of one native texture, share.
/// </para>
/// <para>
/// <b>What is allowed.</b> Any overlap among bindings the shader only reads, and bindings that do not
/// overlap: two different buffers swapped between dispatches (ping-pong), disjoint views of one
/// buffer, an empty buffer or view next to anything.
/// </para>
/// <para>
/// <b>What is not seen.</b> Two different <c>MTLTexture</c> objects that share storage without a
/// CPU-visible buffer behind them (a texture view another library made of a wrapped texture, a linear
/// texture over a private buffer), and the buffer an indirect dispatch takes its threadgroup counts from.
/// </para>
/// <para>
/// <b>Cost.</b> One entry per binding, written in place. Bindings are only compared from the first
/// written one on: until then no two can be in conflict, so the read-only bindings of a shader that
/// are bound first (and all of them, for a shader that writes nothing) cost a store each. A
/// <see cref="CommandBatch"/> keeps a table for the dispatches it records; the synchronous and CPU
/// paths take one from, and hand it back to, a per-thread slot. Either way a dispatch allocates
/// nothing for it in the steady state.
/// </para>
/// <para>
/// <b>Threading.</b> Not thread-safe; a table belongs to the one dispatch being recorded: on the
/// calling thread between <see cref="Rent"/> and <see cref="Return"/>, or under its batch, which
/// records one dispatch at a time.
/// </para>
/// </remarks>
internal sealed unsafe class BindingTable
{
    // Most shaders bind fewer; the limit is 31 buffers and 31 textures.
    private const int InitialCapacity = 8;

    [ThreadStatic]
    private static BindingTable? t_spare;

    private Entry[] _entries = new Entry[InitialCapacity];
    private int _count;

    // True once a binding of this dispatch is written. Until then no two bindings can be in
    // conflict, so a shader's read-only bindings are recorded without being compared at all.
    private bool _anyWritten;

    /// <summary>A table a <see cref="CommandBatch"/> keeps for the dispatches it records; everything else rents one.</summary>
    internal BindingTable()
    {
    }

    /// <summary>
    /// An empty table for one dispatch. The thread's spare one is taken out of its slot while in use,
    /// so a dispatch recorded from inside another one's <c>SetArguments</c> gets a table of its own.
    /// </summary>
    internal static BindingTable Rent()
    {
        BindingTable table = t_spare ?? new BindingTable();
        t_spare = null;
        table.Clear();
        return table;
    }

    /// <summary>
    /// Hands the table back for the thread's next dispatch. A dispatch that fails never returns its
    /// table, which costs the next one an allocation and nothing else.
    /// </summary>
    internal void Return() => t_spare = this;

    /// <summary>Empties the table for the next dispatch of its owner.</summary>
    internal void Clear()
    {
        _count = 0;
        _anyWritten = false;
    }

    /// <summary>Records the buffer bound at <paramref name="index"/>, or throws when it overlaps an earlier binding and either is written.</summary>
    /// <exception cref="ArgumentException">The buffer overlaps another binding of the dispatch that the shader writes, or is written and overlaps one.</exception>
    internal void AddBuffer<T>(int index, Buffer<T> buffer, bool written)
        where T : unmanaged
    {
        nuint length = (nuint)buffer.Length * (nuint)sizeof(T);

        if (length == 0)
        {
            // An empty buffer or view has no element a shader may touch, wherever it starts: it
            // cannot be in conflict with anything, and is not recorded.
            return;
        }

        ref Entry entry = ref Next();

        if (buffer.IsCpuAccessible)
        {
            entry.Space = 0;
            entry.Start = buffer.ContentsAddress;
        }
        else
        {
            // GPU-only memory has no address: the MTLBuffer and the byte range in it stand in for one.
            entry.Space = buffer.Handle.DangerousGetHandle();
            entry.Start = buffer.ByteOffset;
        }

        entry.End = entry.Start + length;
        entry.Texture = 0;
        entry.Index = index;
        entry.Kind = BindingKind.Buffer;
        entry.Written = written;

        Commit(written);
    }

    /// <summary>
    /// Records the texture bound at texture <paramref name="index"/>, or throws when it is, or shares
    /// memory with, an earlier binding and either is written. <paramref name="isOutput"/> marks the
    /// texture a pixel shader writes its result to (<c>ForEach</c>), which the runtime binds itself.
    /// </summary>
    /// <exception cref="ArgumentException">The texture is bound twice, or shares memory with a bound buffer, and the shader writes one of the two.</exception>
    internal void AddTexture<T>(int index, Texture2D<T> texture, bool written, bool isOutput = false)
        where T : unmanaged
    {
        ref Entry entry = ref Next();

        entry.Space = 0;

        if (texture.IsCpuAccessible)
        {
            // A linear view of a shared buffer: the bytes from the first pixel to the last one. The
            // padding after the last row is not the texture's.
            entry.Start = (nuint)texture.Contents;
            entry.End = entry.Start + ((nuint)(texture.Height - 1) * texture.BytesPerRow) + ((nuint)texture.Width * (nuint)sizeof(T));
        }
        else
        {
            // No memory that can be compared: an empty range, which overlaps nothing.
            entry.Start = 0;
            entry.End = 0;
        }

        entry.Texture = texture.Handle.DangerousGetHandle();
        entry.Index = index;
        entry.Kind = isOutput ? BindingKind.Output : BindingKind.Texture;
        entry.Written = written;

        Commit(written);
    }

    /// <summary>The slot of the binding being added. It only counts once <see cref="Commit"/> has accepted it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref Entry Next()
    {
        if (_count == _entries.Length)
        {
            Array.Resize(ref _entries, _entries.Length * 2);
        }

        return ref _entries[_count];
    }

    /// <summary>
    /// Accepts the binding <see cref="Next"/> handed out, after comparing it with the ones before it
    /// when this one is written or one of those is.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Commit(bool written)
    {
        if (written | _anyWritten)
        {
            Compare();
            _anyWritten = true;
        }

        _count++;
    }

    private void Compare()
    {
        Entry[] entries = _entries;
        ref readonly Entry entry = ref entries[_count];

        for (int i = 0; i < _count; i++)
        {
            ref readonly Entry other = ref entries[i];

            if ((entry.Written || other.Written) && Overlaps(in entry, in other))
            {
                throw Conflict(in entry, in other);
            }
        }
    }

    /// <summary>
    /// True when the two are the same texture, or cover a byte in common. A range that covers no byte
    /// (a texture whose memory cannot be compared) overlaps nothing, wherever it starts: the test of
    /// two intervals alone would put an empty one inside any range around its start.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Overlaps(in Entry a, in Entry b) =>
        (a.Texture != 0 && a.Texture == b.Texture) ||
        (a.Space == b.Space && a.Start < a.End && b.Start < b.End && a.Start < b.End && b.Start < a.End);

    private static ArgumentException Conflict(in Entry entry, in Entry other)
    {
        bool bothTextures = entry.Kind != BindingKind.Buffer && other.Kind != BindingKind.Buffer;
        bool bothBuffers = entry.Kind == BindingKind.Buffer && other.Kind == BindingKind.Buffer;
        string message;

        if (bothTextures && entry.Texture == other.Texture)
        {
            message =
                $"{Describe(in entry, capitalize: true)} is the same texture as {Describe(in other)}, and the shader writes it: " +
                "the texture is captured twice, or together with its AsReadOnly() view. On the GPU a read through one of the two does not see " +
                "what was written through the other, so the dispatch would not compute what its C# does. " +
                "Read and write the texture through one capture, or write to a second texture and swap the two between dispatches.";
        }
        else if (bothTextures)
        {
            message =
                $"{Describe(in entry, capitalize: true)} and {Describe(in other)} are views of overlapping memory of one buffer, and the shader writes one of them. " +
                "On the GPU a read through one does not see what was written through the other. Bind textures whose storage does not overlap.";
        }
        else if (bothBuffers)
        {
            message =
                $"{Describe(in entry, capitalize: true)} overlaps {Describe(in other)}, and the shader writes one of them: " +
                "they are the same buffer captured twice, overlapping slices of one buffer, or buffers that wrap the same memory. " +
                "Metal compiles a kernel on the assumption that its buffer arguments do not overlap, so a read through one would not see " +
                "what was written through the other. Capture the buffer once, bind slices that do not overlap, or write to a second buffer " +
                "(GraphicsDevice.Copy moves the result back on the GPU).";
        }
        else
        {
            ref readonly Entry texture = ref entry.Kind == BindingKind.Buffer ? ref other : ref entry;
            ref readonly Entry buffer = ref entry.Kind == BindingKind.Buffer ? ref entry : ref other;

            message =
                $"{Describe(in texture, capitalize: true)} stores its pixels in memory that {Describe(in buffer)} also covers, and the shader writes one of them. " +
                "On the GPU a texture and a buffer over the same memory do not see each other's writes within a dispatch. " +
                "Bind only one of the two, or a slice of the buffer that leaves the texture's pixels out.";
        }

        return new ArgumentException(message, entry.Kind == BindingKind.Buffer ? "buffer" : "texture");
    }

    private static string Describe(in Entry entry, bool capitalize = false) => entry.Kind switch
    {
        BindingKind.Buffer => $"{(capitalize ? "The" : "the")} buffer bound at index {entry.Index}",
        BindingKind.Texture => $"{(capitalize ? "The" : "the")} texture bound at texture index {entry.Index}",
        _ => $"{(capitalize ? "The" : "the")} output texture of the pixel shader",
    };

    private enum BindingKind : byte
    {
        Buffer,
        Texture,

        /// <summary>The texture a pixel shader's result is written to, bound by the runtime at texture index 0.</summary>
        Output,
    }

    /// <summary>
    /// One binding: the bytes it covers (<see cref="Start"/> inclusive to <see cref="End"/> exclusive;
    /// equal when it covers none that can be compared) in <see cref="Space"/>, which is 0 for CPU
    /// addresses and the <c>MTLBuffer</c> for byte offsets into GPU-only memory, and for a texture
    /// its <c>MTLTexture</c>.
    /// </summary>
    private struct Entry
    {
        public nint Space;
        public nuint Start;
        public nuint End;
        public nint Texture;
        public int Index;
        public BindingKind Kind;
        public bool Written;
    }
}
