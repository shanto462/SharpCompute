using System;
using SharpCompute.Interop;

namespace SharpCompute;

/// <summary>A compiled <c>id&lt;MTLComputePipelineState&gt;</c> for one shader type on one device.</summary>
internal sealed class ComputePipeline(ObjCHandle state, nuint maxTotalThreadsPerThreadgroup) : IDisposable
{
    internal ObjCHandle State { get; } = state;

    /// <summary>Upper bound on <c>x * y * z</c> of the threadgroup size for this particular kernel.</summary>
    internal nuint MaxTotalThreadsPerThreadgroup { get; } = maxTotalThreadsPerThreadgroup;

    public void Dispose() => State.Dispose();
}
