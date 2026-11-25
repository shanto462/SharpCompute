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
/// <b>Cost.</b> One entry per binding, compared with the entries before it: a handful of integer
/// comparisons for a typical shader. The table is taken from, and handed back to, a per-thread slot,
/// so a dispatch allocates nothing for it once a thread has dispatched before.
/// </para>
/// <para>
/// <b>Threading.</b> Not thread-safe; a table belongs to the one dispatch being recorded on the
/// calling thread between <see cref="Rent"/> and <see cref="Return"/>.
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

    private BindingTable()
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
        table._count = 0;
        return table;
    }

    /// <summary>
    /// Hands the table back for the thread's next dispatch. A dispatch that fails never returns its
    /// table, which costs the next one an allocation and nothing else.
    /// </summary>
    internal void Return() => t_spare = this;

    /// <summary>Records the buffer bound at <paramref name="index"/>, or throws when it overlaps an earlier binding and either is written.</summary>
    /// <exception cref="ArgumentException">The buffer overlaps another binding of the dispatch that the shader writes, or is written and overlaps one.</exception>
    internal void AddBuffer<T>(int index, Buffer<T> buffer, bool written)
        where T : unmanaged
    {
        nuint length = (nuint)buffer.Length * (nuint)sizeof(T);
        Entry entry;

        if (buffer.IsCpuAccessible)
        {
            nuint start = buffer.ContentsAddress;
            entry = new Entry(space: 0, start, start + length, texture: 0, index, BindingKind.Buffer, written);
        }
        else
        {
            // GPU-only memory has no address: the MTLBuffer and the byte range in it stand in for one.
            nuint start = buffer.ByteOffset;
            entry = new Entry(buffer.Handle.DangerousGetHandle(), start, start + length, texture: 0, index, BindingKind.Buffer, written);
        }

        Add(in entry);
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
        nuint start = 0, end = 0;

        if (texture.IsCpuAccessible)
        {
            // A linear view of a shared buffer: the bytes from the first pixel to the last one. The
            // padding after the last row is not the texture's.
            start = (nuint)texture.Contents;
            end = start + ((nuint)(texture.Height - 1) * texture.BytesPerRow) + ((nuint)texture.Width * (nuint)sizeof(T));
        }

        Add(new Entry(space: 0, start, end, texture.Handle.DangerousGetHandle(), index, isOutput ? BindingKind.Output : BindingKind.Texture, written));
    }

    private void Add(in Entry entry)
    {
        Entry[] entries = _entries;

        for (int i = 0; i < _count; i++)
        {
            ref readonly Entry other = ref entries[i];

            if ((entry.Written || other.Written) && Overlaps(in entry, in other))
            {
                throw Conflict(in entry, in other);
            }
        }

        if (_count == entries.Length)
        {
            Array.Resize(ref _entries, entries.Length * 2);
        }

        _entries[_count++] = entry;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Overlaps(in Entry a, in Entry b) =>
        (a.Texture != 0 && a.Texture == b.Texture) ||
        (a.Space == b.Space && a.Start < b.End && b.Start < a.End);

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
    private readonly struct Entry(nint space, nuint start, nuint end, nint texture, int index, BindingKind kind, bool written)
    {
        public readonly nint Space = space;
        public readonly nuint Start = start;
        public readonly nuint End = end;
        public readonly nint Texture = texture;
        public readonly int Index = index;
        public readonly BindingKind Kind = kind;
        public readonly bool Written = written;
    }
}
