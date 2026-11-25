namespace SharpCompute;

// Threadgroup barriers, under their HLSL names. The generator recognises them by name and emits
// metal::threadgroup_barrier; on the CPU they are the meeting point of the group's threads
// (CpuThreadgroup.Barrier).
public static partial class Gpu
{
    /// <summary>
    /// Blocks the calling thread until every thread of its threadgroup has reached this call, and makes
    /// everything they wrote to <see cref="GroupSharedAttribute"/> memory before it visible to all of
    /// them after it (<c>threadgroup_barrier(mem_flags::mem_threadgroup)</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes shared memory usable: threads write their own elements, call the barrier, and
    /// only then read what the others wrote. A read of another thread's element without a barrier in
    /// between is a data race, on the GPU and on the CPU.
    /// </para>
    /// <para>
    /// <b>Every thread of the group must reach the same barrier calls, the same number of times.</b>
    /// Calling a barrier where threads of one group can take different paths (inside an <c>if</c> or a
    /// loop whose condition depends on the thread, or after an early <c>return</c> that only some
    /// threads take) is undefined on the GPU, and the generator cannot detect it. Keep such conditions
    /// around the work, not around the barrier:
    /// <c>if (mine) { tile[i] += tile[i + step]; } Gpu.GroupMemoryBarrierWithGroupSync();</c>.
    /// On the CPU a thread that returns from <c>Execute</c> no longer takes part, so a mismatch does not
    /// hang there; it just computes something else than the GPU would.
    /// </para>
    /// <para>
    /// On the CPU (<c>GraphicsDevice.ForCpu</c>, <c>ForSplit</c>) all three barriers behave alike: the
    /// thread waits for the rest of its group, and all memory is synchronized.
    /// </para>
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">
    /// Called outside a shader, or on the CPU by a shader whose hand-written descriptor does not report
    /// <see cref="IComputeShaderDescriptor{TSelf}.UsesThreadgroupMemory"/>.
    /// </exception>
    public static void GroupMemoryBarrierWithGroupSync() => CpuThread.Current.Barrier();

    /// <summary>
    /// Blocks the calling thread until every thread of its threadgroup has reached this call, and makes
    /// everything they wrote to buffers and read-write textures before it visible to all of them after
    /// it (<c>threadgroup_barrier(mem_flags::mem_device)</c>, plus <c>mem_texture</c> when the shader
    /// captures a read-write texture).
    /// </summary>
    /// <remarks>
    /// Only orders the threads of one threadgroup: threads of other groups still run in no particular
    /// order. The rules of <see cref="GroupMemoryBarrierWithGroupSync"/> about where a barrier may be
    /// called apply.
    /// </remarks>
    /// <inheritdoc cref="GroupMemoryBarrierWithGroupSync" path="/exception"/>
    public static void DeviceMemoryBarrierWithGroupSync() => CpuThread.Current.Barrier();

    /// <summary>
    /// <see cref="GroupMemoryBarrierWithGroupSync"/> and <see cref="DeviceMemoryBarrierWithGroupSync"/>
    /// in one call: synchronizes group-shared memory, buffers and read-write textures
    /// (<c>threadgroup_barrier</c> with all of those flags).
    /// </summary>
    /// <inheritdoc cref="GroupMemoryBarrierWithGroupSync" path="/exception"/>
    public static void AllMemoryBarrierWithGroupSync() => CpuThread.Current.Barrier();

    // TODO: atomics on [GroupShared] memory. Gpu.Atomic*(ref name[i], value) already works on the CPU
    // (it is an interlocked operation on the array element); the generator rejects it (SCG0034) until
    // it emits the atomic for the threadgroup address space, see MslTranslator.GroupShared.cs. Until
    // then the threads of a group can only share an element across a barrier.
}
