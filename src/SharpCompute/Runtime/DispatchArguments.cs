using System;
using System.Runtime.InteropServices;

namespace SharpCompute;

/// <summary>
/// The threadgroup counts of an indirect dispatch (<c>GraphicsDevice.ForIndirect</c>,
/// <c>ComputeContext.ForIndirect</c>): the element type of the buffer the GPU reads them from when
/// the dispatch runs. Byte for byte Metal's <c>MTLDispatchThreadgroupsIndirectArguments</c>.
/// </summary>
/// <remarks>
/// <para>
/// The counts are <b>threadgroups</b>, not threads: the dispatch runs
/// <c>ThreadGroupsX × ThreadGroupsY × ThreadGroupsZ</c> groups of the shader's threadgroup size. A
/// count of 0 in any dimension dispatches nothing.
/// </para>
/// <para>
/// A shader can write them like any other struct: capture a <c>ReadWriteBuffer&lt;DispatchArguments&gt;</c>
/// and assign the fields (<c>arguments[0].ThreadGroupsX = ...</c>). To turn an element count that a
/// previous pass left in a buffer into threadgroup counts without writing a shader, use
/// <c>WriteDispatchArguments</c> on the device or the context; on the CPU, <see cref="FromCount"/>.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct DispatchArguments : IEquatable<DispatchArguments>
{
    /// <summary>Threadgroups along x.</summary>
    public uint ThreadGroupsX;

    /// <summary>Threadgroups along y; 1 for a one-dimensional dispatch.</summary>
    public uint ThreadGroupsY;

    /// <summary>Threadgroups along z; 1 for a one- or two-dimensional dispatch.</summary>
    public uint ThreadGroupsZ;

    public DispatchArguments(uint threadGroupsX, uint threadGroupsY = 1, uint threadGroupsZ = 1)
    {
        ThreadGroupsX = threadGroupsX;
        ThreadGroupsY = threadGroupsY;
        ThreadGroupsZ = threadGroupsZ;
    }

    /// <summary>
    /// The arguments of a one-dimensional dispatch that covers <paramref name="count"/> elements with
    /// threadgroups of <paramref name="threadGroupSize"/> threads: <c>ceil(count / threadGroupSize)</c>
    /// groups, by 1, by 1. The dispatch then runs up to <c>threadGroupSize - 1</c> threads more than
    /// <paramref name="count"/>, which is why an indirectly dispatched shader checks its index against
    /// the count itself.
    /// </summary>
    /// <param name="count">The number of elements; 0 gives a dispatch that runs nothing.</param>
    /// <param name="threadGroupSize">The shader's threads per threadgroup along x (its <see cref="ThreadGroupSizeAttribute"/>, or 256 without one).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative or <paramref name="threadGroupSize"/> is not positive.</exception>
    public static DispatchArguments FromCount(int count, int threadGroupSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(threadGroupSize);

        // Divide before adding the remainder's group: count + threadGroupSize - 1 could overflow.
        return new DispatchArguments((uint)((count / threadGroupSize) + (count % threadGroupSize != 0 ? 1 : 0)));
    }

    public readonly bool Equals(DispatchArguments other) =>
        ThreadGroupsX == other.ThreadGroupsX && ThreadGroupsY == other.ThreadGroupsY && ThreadGroupsZ == other.ThreadGroupsZ;

    public override readonly bool Equals(object? obj) => obj is DispatchArguments other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(ThreadGroupsX, ThreadGroupsY, ThreadGroupsZ);

    public override readonly string ToString() => $"{ThreadGroupsX} × {ThreadGroupsY} × {ThreadGroupsZ} threadgroups";

    public static bool operator ==(DispatchArguments left, DispatchArguments right) => left.Equals(right);

    public static bool operator !=(DispatchArguments left, DispatchArguments right) => !left.Equals(right);
}
