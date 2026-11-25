using System.Runtime.CompilerServices;

namespace SharpCompute;

// SIMD-group ("wave") operations, with the names HLSL gives them. The typed overloads (reductions,
// scans, exchanges and the quad operations, for float, int, uint and their vectors) are generated into
// Gpu.Wave.g.cs by tools/gen_vectors.py; the lane queries and the votes, which have one signature
// each, are written here.

/// <content>
/// <para>
/// <b>SIMD-group operations.</b> An Apple GPU runs the threads of a threadgroup in SIMD groups of 32
/// threads ("lanes") that execute together. The <c>Wave*</c> methods let the lanes of one group combine
/// or exchange values in a single step, with no shared memory and no barrier: reductions
/// (<c>WaveActiveSum</c>), votes (<c>WaveActiveBallot</c>), prefix scans (<c>WavePrefixSum</c>) and
/// reads of another lane (<c>WaveReadLaneAt</c>, <c>WaveShuffleUp</c>). The <c>Quad*</c> methods do the
/// same among the four lanes of a quad. The names and meanings are HLSL's wave and quad intrinsics;
/// they map to Metal's <c>simd_*</c> and <c>quad_*</c> functions.
/// </para>
/// <para>
/// <b>How groups are formed.</b> A threadgroup's threads are numbered by their linear index,
/// <c>(GroupIds.Z * sizeY + GroupIds.Y) * sizeX + GroupIds.X</c> (with the real size of a partial
/// threadgroup at the edge of the grid; <see cref="GroupIds.Index"/>). Lane <c>index % 32</c> of SIMD
/// group <c>index / 32</c> is thread <c>index</c>: lanes are consecutive along x, and a row of a 2D
/// threadgroup continues where the previous one stopped. A threadgroup of 256 threads has 8 groups;
/// one of 16 × 16 has 8 groups of two rows each; one of 12 × 12 has 4 full groups and one of 16 lanes.
/// A quad is four consecutive lanes. Metal does not promise this layout; it is what Apple GPUs do
/// (measured on Apple M4 Pro, and pinned by the SIMD-group tests), and what the CPU does.
/// </para>
/// <para>
/// <b>Which lanes take part.</b> An operation sees the <i>active</i> lanes: the lanes of the group
/// that are executing this very call. A lane that returned from <c>Execute</c> earlier (the usual
/// <c>if (i >= n) return;</c>) is not active, a lane in the other branch of an <c>if</c> is not active
/// in this one, and lanes past the end of a partial group (a threadgroup or grid that is not a
/// multiple of 32) do not exist. Reductions, votes and scans are over the active lanes only:
/// inactive lanes contribute nothing, and a scan numbers the active lanes without gaps.
/// </para>
/// <para>
/// <b>Reading a lane that is not active</b> (<c>WaveReadLaneAt</c>, the shuffles and rotations, the
/// quad reads) is undefined in Metal. Apple GPUs return 0 (measured) and the CPU does the same, but do
/// not rely on it: mask the result with what you know about the lane (see
/// <see cref="WaveActiveBallot"/>), or keep every lane active.
/// </para>
/// <para>
/// <b>On the CPU</b> (<c>GraphicsDevice.ForCpu</c>, <c>ForSplit</c>) the lanes of a SIMD group run on
/// threads of their own and meet at every operation: a lane waits there until every lane of its group
/// that has not returned from <c>Execute</c> has reached the same call, and the result is computed
/// from what they brought, in lane order. That reproduces the GPU whenever the lanes that are still
/// running make the same SIMD-group calls in the same order, which is the case for operations at the
/// top level of <c>Execute</c> (after any number of early returns) and inside loops and branches that
/// every lane takes alike. Two things differ from the GPU:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>Divergent control flow is refused where it shows.</b> If the lanes of a group arrive at
/// <i>different</i> calls (one branch of an <c>if</c> calls <c>WaveActiveSum</c>, the other does not,
/// or calls another operation), or some wait at an operation while others wait at a barrier, the
/// dispatch throws <see cref="System.NotSupportedException"/> ("SIMD-group operation in divergent
/// control flow is not supported on the CPU; it is supported on the GPU"). It never hangs.
/// </item>
/// <item>
/// <b>Limitation: divergence that does not show.</b> Lanes that reach the <i>same</i> call site along
/// different paths meet as if control flow were uniform, and the result then covers lanes the GPU
/// would have kept apart. That happens when a call sits in a loop and lanes are in different
/// iterations of it (some skipped an iteration, or the trip count differs and a later call matches up),
/// and when one helper method that contains the call is called from both branches of an <c>if</c>. The
/// CPU cannot detect these. Such shaders are correct on the GPU; check them there.
/// </item>
/// </list>
/// <para>
/// The trailing <c>callerFile</c> and <c>callerLine</c> parameters of every operation are filled in by
/// the compiler with the place of the call; leave them out. Together with the operation they are the
/// call site the lanes have to agree on, so keep two calls of the same operation on separate lines
/// (the generator warns otherwise, SCG0035). Float results are combined in lane order on the CPU and
/// in an unspecified order on the GPU: compare with a tolerance.
/// </para>
/// </content>
public static partial class Gpu
{
    // ---- lanes -------------------------------------------------------------------------------

    /// <summary>
    /// The index of the current thread within its SIMD group (<c>thread_index_in_simdgroup</c>):
    /// 0 to <see cref="WaveGetLaneCount"/> - 1, which is <see cref="GroupIds.Index"/> modulo 32.
    /// </summary>
    public static int WaveGetLaneIndex() => CpuSimdGroup.LaneIndex();

    /// <summary>
    /// The number of lanes of a SIMD group (<c>threads_per_simdgroup</c>): 32 on every Apple GPU. It is
    /// the width of a full group, also in a partial one; count the active lanes with
    /// <c>WaveActiveCountBits(true)</c>.
    /// </summary>
    public static int WaveGetLaneCount() => CpuSimdGroup.LaneCount();

    /// <summary>
    /// True for exactly one active lane of the SIMD group, the one with the lowest index
    /// (<c>simd_is_first</c>). Use it to let one lane write a result the whole group computed.
    /// </summary>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    public static bool WaveIsFirstLane([CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        CpuSimdGroup.Vote(SimdOp.IsFirstLane, value: true, callerFile, callerLine);

    // ---- votes -------------------------------------------------------------------------------

    /// <summary>True when <paramref name="value"/> is true in at least one active lane (<c>simd_any</c>).</summary>
    /// <param name="value">This lane's vote.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    public static bool WaveActiveAnyTrue(bool value, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        CpuSimdGroup.Vote(SimdOp.AnyTrue, value, callerFile, callerLine);

    /// <summary>True when <paramref name="value"/> is true in every active lane (<c>simd_all</c>).</summary>
    /// <param name="value">This lane's vote.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    public static bool WaveActiveAllTrue(bool value, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        CpuSimdGroup.Vote(SimdOp.AllTrue, value, callerFile, callerLine);

    /// <summary>
    /// The votes of the whole SIMD group as a bit mask (<c>simd_ballot</c>): bit <c>i</c> is set when
    /// lane <c>i</c> is active and its <paramref name="value"/> is true. <c>WaveActiveBallot(true)</c>
    /// is the mask of the active lanes.
    /// </summary>
    /// <remarks>
    /// HLSL returns a <c>uint4</c> for waves of up to 128 lanes; a SIMD group of an Apple GPU has 32, so
    /// one <see cref="uint"/> holds every lane. A dispatch checks that width against the compiled
    /// kernel and throws <see cref="System.NotSupportedException"/> on a GPU whose groups are wider,
    /// rather than drop votes.
    /// </remarks>
    /// <param name="value">This lane's vote.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    public static uint WaveActiveBallot(bool value, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        CpuSimdGroup.Ballot(SimdOp.Ballot, value, callerFile, callerLine);

    /// <summary>
    /// The number of active lanes whose <paramref name="value"/> is true, the same in every lane
    /// (<c>simd_sum</c> of 0 or 1).
    /// </summary>
    /// <param name="value">This lane's vote.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    public static int WaveActiveCountBits(bool value, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        CpuSimdGroup.Count(SimdOp.CountBits, value, callerFile, callerLine);

    /// <summary>
    /// The number of active lanes <i>below</i> this one whose <paramref name="value"/> is true
    /// (<c>simd_prefix_exclusive_sum</c> of 0 or 1): the slot of this lane when the lanes that voted
    /// true are packed together, which is all a stream compaction needs.
    /// </summary>
    /// <param name="value">This lane's vote.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    public static int WavePrefixCountBits(bool value, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        CpuSimdGroup.Count(SimdOp.PrefixCountBits, value, callerFile, callerLine);

    // ---- quad votes ---------------------------------------------------------------------------

    /// <summary>
    /// True when <paramref name="value"/> is true in at least one active lane of this lane's quad
    /// (<c>quad_any</c>; HLSL's <c>QuadAny</c>). A quad is four consecutive lanes of a SIMD group.
    /// </summary>
    /// <param name="value">This lane's vote.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    public static bool QuadActiveAnyTrue(bool value, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        CpuSimdGroup.Vote(SimdOp.QuadAnyTrue, value, callerFile, callerLine);

    /// <summary>
    /// True when <paramref name="value"/> is true in every active lane of this lane's quad
    /// (<c>quad_all</c>; HLSL's <c>QuadAll</c>). A quad is four consecutive lanes of a SIMD group.
    /// </summary>
    /// <param name="value">This lane's vote.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    public static bool QuadActiveAllTrue(bool value, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        CpuSimdGroup.Vote(SimdOp.QuadAllTrue, value, callerFile, callerLine);

    /// <summary>
    /// The votes of this lane's quad as a mask of four bits (<c>quad_ballot</c>): bit <c>i</c> is set
    /// when lane <c>i</c> of the quad (<c>x + 2 * y</c>) is active and its <paramref name="value"/> is
    /// true. <c>QuadActiveBallot(true)</c> is the mask of the quad's active lanes.
    /// </summary>
    /// <param name="value">This lane's vote.</param>
    /// <param name="callerFile">Filled in by the compiler; leave it out.</param>
    /// <param name="callerLine">Filled in by the compiler; leave it out.</param>
    public static uint QuadActiveBallot(bool value, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) =>
        CpuSimdGroup.Ballot(SimdOp.QuadBallot, value, callerFile, callerLine);
}
