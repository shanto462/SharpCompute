using System;
using SharpCompute.Interop;

namespace SharpCompute;

/// <summary>A compiled <c>id&lt;MTLComputePipelineState&gt;</c> for one shader type on one device.</summary>
internal sealed class ComputePipeline(ObjCHandle state, nuint maxTotalThreadsPerThreadgroup, nuint threadExecutionWidth) : IDisposable
{
    internal ObjCHandle State { get; } = state;

    /// <summary>Upper bound on <c>x * y * z</c> of the threadgroup size for this particular kernel.</summary>
    internal nuint MaxTotalThreadsPerThreadgroup { get; } = maxTotalThreadsPerThreadgroup;

    /// <summary>
    /// The kernel's <c>threadExecutionWidth</c>: the number of threads the GPU executes together, which is
    /// the size of a SIMD group (<c>threads_per_simdgroup</c> in the kernel). 32 on Apple GPUs.
    /// </summary>
    internal nuint ThreadExecutionWidth { get; } = threadExecutionWidth;

    public void Dispose() => State.Dispose();
}
