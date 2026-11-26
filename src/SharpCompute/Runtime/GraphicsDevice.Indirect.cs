using System;
using System.Runtime.InteropServices;
using SharpCompute.Interop;

namespace SharpCompute;

// Indirect dispatch: the threadgroup counts of a dispatch come from a GPU buffer, read when the
// dispatch runs, so an earlier pass on the GPU decides how much work a later pass does.
public sealed unsafe partial class GraphicsDevice
{
    /// <summary>
    /// Dispatches <paramref name="shader"/> with the threadgroup counts in
    /// <paramref name="arguments"/>[0] and waits for completion.
    /// </summary>
    /// <inheritdoc cref="ForIndirect{T}(Buffer{DispatchArguments}, int, in T)" path="/remarks"/>
    /// <inheritdoc cref="ForIndirect{T}(Buffer{DispatchArguments}, int, in T)" path="/exception"/>
    public void ForIndirect<T>(Buffer<DispatchArguments> arguments, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => ForIndirect(arguments, 0, in shader);

    /// <summary>
    /// Dispatches <paramref name="shader"/> with the threadgroup counts in
    /// <paramref name="arguments"/>[<paramref name="argumentsIndex"/>] and waits for completion
    /// (<c>dispatchThreadgroupsWithIndirectBuffer:indirectBufferOffset:threadsPerThreadgroup:</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The GPU reads the counts when the dispatch runs, not when it is recorded, so they can be the
    /// output of GPU work that has not finished yet: a compaction pass counts its survivors, a tiny
    /// pass turns the count into <see cref="DispatchArguments"/> (<c>WriteDispatchArguments</c>), and
    /// the follow-up pass is dispatched over exactly that many, with no read-back to the CPU in
    /// between. That is what <see cref="ComputeContext.ForIndirect{T}(Buffer{DispatchArguments}, in T)"/>
    /// is for; this synchronous form is the same dispatch in its own command buffer.
    /// </para>
    /// <para>
    /// <b>The grid is a whole number of threadgroups.</b> The counts are threadgroups, so the dispatch
    /// runs <c>groups × threadgroup size</c> threads per dimension and <see cref="DispatchSize"/> (and
    /// what <see cref="ThreadIds.Normalized"/> divides by) is that product, not an element count: Metal
    /// has no indirect dispatch of an exact grid. With 1000 elements and threadgroups of 256, 1024
    /// threads run and <c>DispatchSize.X</c> is 1024. <b>The shader must check its index against the
    /// real count itself</b> (for example <c>if (ThreadIds.X &gt;= count[0]) { return; }</c>, reading
    /// the buffer the count is in) before it touches an element.
    /// </para>
    /// <para>
    /// The threadgroup size is the shader's <see cref="ThreadGroupSizeAttribute"/>. Without one the
    /// dispatch is taken to be one-dimensional and gets the default for that, 256 × 1 × 1, as
    /// <c>For(n, shader)</c> does: the rank of an indirect dispatch is not known when it is recorded,
    /// so a two- or three-dimensional one needs the attribute.
    /// </para>
    /// <para>
    /// Counts that are too large for the device are not detected: they come from the GPU, and Metal
    /// reports nothing for them. A count of 0 dispatches nothing.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="argumentsIndex"/> is not an element of <paramref name="arguments"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="arguments"/> or a resource of <paramref name="shader"/> belongs to a different device, or two of the shader's buffers or textures overlap and it writes one of them (see <see cref="ShaderArgumentWriter"/>).</exception>
    /// <exception cref="ObjectDisposedException">The device, <paramref name="arguments"/> or a resource of <paramref name="shader"/> has been disposed.</exception>
    /// <exception cref="ShaderCompilationException">The shader failed to compile.</exception>
    /// <exception cref="MetalException">The GPU failed to execute the shader.</exception>
    public void ForIndirect<T>(Buffer<DispatchArguments> arguments, int argumentsIndex, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T>
    {
        nuint offset = ValidateIndirectArguments(arguments, argumentsIndex);

        Run(default, rank: 1, output: null, in shader, indirect: new IndirectArguments(arguments.Handle, arguments, offset));
    }

    /// <summary>
    /// Runs <paramref name="shader"/> on the CPU over the grid that
    /// <paramref name="arguments"/>[<paramref name="argumentsIndex"/>] describes, and returns when all
    /// threads have run: the CPU counterpart of <see cref="ForIndirect{T}(Buffer{DispatchArguments}, int, in T)"/>.
    /// </summary>
    /// <remarks>
    /// The counts are read from the buffer's shared memory when this is called, so GPU work that
    /// writes them must have finished: wait for it (a synchronous dispatch, <c>Execute</c> on a
    /// context, or an awaited task) first. The grid is the same whole number of threadgroups as on the
    /// GPU, with the same built-ins, and everything else is as for
    /// <see cref="ForCpu{T}(int, int, int, in T)"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="argumentsIndex"/> is not an element of <paramref name="arguments"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="arguments"/> or a resource of <paramref name="shader"/> belongs to a different device,
    /// or the counts describe a grid with more than <see cref="int.MaxValue"/> threads along one dimension.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The device, <paramref name="arguments"/> or a resource of <paramref name="shader"/> has been disposed.</exception>
    public void ForIndirectCpu<T>(Buffer<DispatchArguments> arguments, int argumentsIndex, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T>
    {
        ValidateIndirectArguments(arguments, argumentsIndex);

        DispatchArguments counts = arguments.AsReadOnlySpan()[argumentsIndex];
        Extent3 groupSize = ResolveCpuThreadGroupSize<T>(rank: 1);

        ulong x = (ulong)counts.ThreadGroupsX * (ulong)groupSize.X;
        ulong y = (ulong)counts.ThreadGroupsY * (ulong)groupSize.Y;
        ulong z = (ulong)counts.ThreadGroupsZ * (ulong)groupSize.Z;

        if (x > int.MaxValue || y > int.MaxValue || z > int.MaxValue)
        {
            throw new ArgumentException(
                $"The dispatch arguments ({counts}) with threadgroups of {groupSize.X} × {groupSize.Y} × {groupSize.Z} describe a {x} × {y} × {z} grid, which is more than int.MaxValue threads along one dimension.",
                nameof(arguments));
        }

        RunCpu((int)x, (int)y, (int)z, groupSize, in shader);
    }

    /// <inheritdoc cref="ForIndirectCpu{T}(Buffer{DispatchArguments}, int, in T)"/>
    public void ForIndirectCpu<T>(Buffer<DispatchArguments> arguments, in T shader)
        where T : struct, IComputeShader, IComputeShaderDescriptor<T> => ForIndirectCpu(arguments, 0, in shader);

    /// <summary>
    /// Turns the element count in <paramref name="count"/>[0] into the threadgroup counts of a
    /// one-dimensional indirect dispatch of <typeparamref name="T"/>, written to
    /// <paramref name="arguments"/>[0] on the GPU, and waits for completion.
    /// </summary>
    /// <inheritdoc cref="WriteDispatchArguments{T}(Buffer{int}, int, Buffer{DispatchArguments}, int)" path="/remarks"/>
    /// <inheritdoc cref="WriteDispatchArguments{T}(Buffer{int}, int, Buffer{DispatchArguments}, int)" path="/exception"/>
    public void WriteDispatchArguments<T>(Buffer<int> count, Buffer<DispatchArguments> arguments)
        where T : struct, IComputeShaderDescriptor<T> => WriteDispatchArguments<T>(count, 0, arguments, 0);

    /// <summary>
    /// Turns the element count in <paramref name="count"/>[<paramref name="countIndex"/>] into the
    /// threadgroup counts of a one-dimensional indirect dispatch of <typeparamref name="T"/>, written to
    /// <paramref name="arguments"/>[<paramref name="argumentsIndex"/>] on the GPU, and waits for completion.
    /// </summary>
    /// <typeparam name="T">The shader that will be dispatched with the arguments; its threadgroup size is what the count is divided by.</typeparam>
    /// <remarks>
    /// <para>
    /// A one-thread kernel reads the count and writes <c>(ceil(count / size), 1, 1)</c>, where
    /// <c>size</c> is the threadgroup size <c>ForIndirect</c> will use for <typeparamref name="T"/> on
    /// this device (its <see cref="ThreadGroupSizeAttribute"/>, or the one-dimensional default). A
    /// negative count gives 0 groups. The count is read on the GPU, in submission order: it may be
    /// the output of a dispatch that has not finished, and in a <see cref="ComputeContext"/> of one
    /// recorded just before. On the CPU the same arithmetic is <see cref="DispatchArguments.FromCount"/>.
    /// </para>
    /// <para>
    /// <typeparamref name="T"/> is compiled if it has not been yet, since its threadgroup size can
    /// depend on the compiled kernel.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">An index is not an element of its buffer.</exception>
    /// <exception cref="ArgumentException">A buffer belongs to a different device.</exception>
    /// <exception cref="ObjectDisposedException">The device or a buffer has been disposed.</exception>
    /// <exception cref="NotSupportedException"><paramref name="arguments"/> maps read-only memory.</exception>
    /// <exception cref="InvalidOperationException">
    /// The threadgroup size of <typeparamref name="T"/> is not one-dimensional (its y or z is not 1): an
    /// element count does not determine a two- or three-dimensional grid, so write those arguments in a shader.
    /// </exception>
    /// <exception cref="ShaderCompilationException"><typeparamref name="T"/> failed to compile.</exception>
    /// <exception cref="MetalException">The GPU failed to execute the kernel.</exception>
    public void WriteDispatchArguments<T>(Buffer<int> count, int countIndex, Buffer<DispatchArguments> arguments, int argumentsIndex)
        where T : struct, IComputeShaderDescriptor<T> =>
        Run(new MTLSize(1, 1, 1), rank: 1, output: null, CreateDispatchArgumentsKernel<T, int>(count, countIndex, arguments, argumentsIndex));

    /// <inheritdoc cref="WriteDispatchArguments{T}(Buffer{int}, Buffer{DispatchArguments})"/>
    public void WriteDispatchArguments<T>(Buffer<uint> count, Buffer<DispatchArguments> arguments)
        where T : struct, IComputeShaderDescriptor<T> => WriteDispatchArguments<T>(count, 0, arguments, 0);

    /// <inheritdoc cref="WriteDispatchArguments{T}(Buffer{int}, int, Buffer{DispatchArguments}, int)"/>
    public void WriteDispatchArguments<T>(Buffer<uint> count, int countIndex, Buffer<DispatchArguments> arguments, int argumentsIndex)
        where T : struct, IComputeShaderDescriptor<T> =>
        Run(new MTLSize(1, 1, 1), rank: 1, output: null, CreateDispatchArgumentsKernel<T, uint>(count, countIndex, arguments, argumentsIndex));

    /// <summary>
    /// The preconditions of an indirect dispatch: a live arguments buffer of this device and an index
    /// inside it. Returns the byte offset of the counts in the <c>MTLBuffer</c> (a multiple of 4, as
    /// Metal requires, since an element is three <c>uint</c>s).
    /// </summary>
    internal nuint ValidateIndirectArguments(Buffer<DispatchArguments> arguments, int argumentsIndex)
    {
        ValidateResource(arguments, nameof(arguments));

        if ((uint)argumentsIndex >= (uint)arguments.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(argumentsIndex), argumentsIndex, $"The arguments buffer has {arguments.Length} elements.");
        }

        return arguments.ByteOffset + ((nuint)argumentsIndex * (nuint)sizeof(DispatchArguments));
    }

    /// <summary>Encodes a validated indirect dispatch into <paramref name="batch"/>.</summary>
    internal void EncodeIndirect<T>(CommandBatch batch, Buffer<DispatchArguments> arguments, nuint offset, in T shader)
        where T : struct, IComputeShaderDescriptor<T>
    {
        ComputePipeline pipeline = GetPipeline<T>();
        MTLSize threadsPerThreadgroup = ResolveThreadGroupSize(
            T.ThreadGroupSizeX, T.ThreadGroupSizeY, T.ThreadGroupSizeZ, rank: 1, pipeline.MaxTotalThreadsPerThreadgroup, typeof(T));

        batch.Encode(pipeline, default, threadsPerThreadgroup, output: null, in shader, arguments.Handle, arguments, offset);
    }

    /// <summary>Encodes the count-to-arguments kernel of <see cref="WriteDispatchArguments{T}(Buffer{int}, int, Buffer{DispatchArguments}, int)"/> into <paramref name="batch"/>.</summary>
    internal void EncodeDispatchArguments<T, TCount>(CommandBatch batch, Buffer<TCount> count, int countIndex, Buffer<DispatchArguments> arguments, int argumentsIndex)
        where T : struct, IComputeShaderDescriptor<T>
        where TCount : unmanaged
    {
        DispatchArgumentsKernel<TCount> kernel = CreateDispatchArgumentsKernel<T, TCount>(count, countIndex, arguments, argumentsIndex);
        batch.Encode(GetPipeline<DispatchArgumentsKernel<TCount>>(), new MTLSize(1, 1, 1), new MTLSize(1, 1, 1), output: null, in kernel);
    }

    /// <summary>
    /// Validates the buffers and indices of <c>WriteDispatchArguments</c> and resolves the threadgroup
    /// size <c>ForIndirect</c> uses for <typeparamref name="T"/>, which must be one-dimensional.
    /// </summary>
    private DispatchArgumentsKernel<TCount> CreateDispatchArgumentsKernel<T, TCount>(
        Buffer<TCount> count, int countIndex, Buffer<DispatchArguments> arguments, int argumentsIndex)
        where T : struct, IComputeShaderDescriptor<T>
        where TCount : unmanaged
    {
        ValidateResource(count, nameof(count));
        ValidateResource(arguments, nameof(arguments));

        if ((uint)countIndex >= (uint)count.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(countIndex), countIndex, $"The count buffer has {count.Length} elements.");
        }

        if ((uint)argumentsIndex >= (uint)arguments.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(argumentsIndex), argumentsIndex, $"The arguments buffer has {arguments.Length} elements.");
        }

        ThrowIfReadOnlyMemory(arguments);

        // The same resolution as EncodeIndirect and ForIndirect, so the division matches the dispatch.
        MTLSize size = ResolveThreadGroupSize(
            T.ThreadGroupSizeX, T.ThreadGroupSizeY, T.ThreadGroupSizeZ, rank: 1, GetPipeline<T>().MaxTotalThreadsPerThreadgroup, typeof(T));

        if (size.Height != 1 || size.Depth != 1)
        {
            throw new InvalidOperationException(
                $"Shader '{typeof(T)}' has a threadgroup size of ({size.Width}, {size.Height}, {size.Depth}), which is not one-dimensional: " +
                "an element count does not determine its grid. Write its DispatchArguments in a shader instead.");
        }

        return new DispatchArgumentsKernel<TCount>(count, arguments, (uint)countIndex, (uint)argumentsIndex, (uint)size.Width);
    }
}

/// <summary>
/// The kernel behind <c>WriteDispatchArguments</c>: one thread that reads an element count and writes
/// the threadgroup counts of a one-dimensional dispatch. Hand-written, because the runtime has no
/// generator; <typeparamref name="TCount"/> is <see cref="int"/> or <see cref="uint"/> and only decides
/// whether a count with the top bit set is negative (0 groups) or large.
/// </summary>
internal readonly struct DispatchArgumentsKernel<TCount>(
    Buffer<TCount> count, Buffer<DispatchArguments> arguments, uint countIndex, uint argumentsIndex, uint threadGroupSize)
    : IComputeShaderDescriptor<DispatchArgumentsKernel<TCount>>
    where TCount : unmanaged
{
    public static string EntryPoint => "sharpcompute_dispatch_arguments";

    // The division is done before the remainder's group is added: count + size - 1 could overflow.
    public static string MslSource => """
        #include <metal_stdlib>

        using namespace metal;

        struct Parameters
        {
            uint countIndex;
            uint argumentsIndex;
            uint threadGroupSize;
            uint countIsSigned;
        };

        kernel void sharpcompute_dispatch_arguments(
            const device uint* counts [[buffer(0)]],
            device uint* arguments [[buffer(1)]],
            constant Parameters& parameters [[buffer(2)]])
        {
            uint count = counts[parameters.countIndex];

            if (parameters.countIsSigned != 0u && int(count) < 0)
            {
                count = 0u;
            }

            uint size = parameters.threadGroupSize;
            device uint* target = arguments + (parameters.argumentsIndex * 3u);
            target[0] = (count / size) + (count % size != 0u ? 1u : 0u);
            target[1] = 1u;
            target[2] = 1u;
        }
        """;

    public static int ThreadGroupSizeX => 1;

    public static int ThreadGroupSizeY => 1;

    public static int ThreadGroupSizeZ => 1;

    public static void SetArguments(in DispatchArgumentsKernel<TCount> shader, ref ShaderArgumentWriter writer)
    {
        // The count is only read, whatever kind of buffer the caller keeps it in.
        writer.SetBuffer(0, shader._count, written: false);
        writer.SetBuffer(1, shader._arguments, written: true);
        writer.SetBytes(2, new Parameters(shader._countIndex, shader._argumentsIndex, shader._threadGroupSize, typeof(TCount) == typeof(int) ? 1u : 0u));
    }

    private readonly Buffer<TCount> _count = count;
    private readonly Buffer<DispatchArguments> _arguments = arguments;
    private readonly uint _countIndex = countIndex;
    private readonly uint _argumentsIndex = argumentsIndex;
    private readonly uint _threadGroupSize = threadGroupSize;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Parameters(uint countIndex, uint argumentsIndex, uint threadGroupSize, uint countIsSigned)
    {
        public readonly uint CountIndex = countIndex;
        public readonly uint ArgumentsIndex = argumentsIndex;
        public readonly uint ThreadGroupSize = threadGroupSize;
        public readonly uint CountIsSigned = countIsSigned;
    }
}
