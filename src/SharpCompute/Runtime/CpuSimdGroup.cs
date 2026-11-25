using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SharpCompute;

/// <summary>A SIMD-group operation, as the CPU implementations of <see cref="Gpu"/>'s wave and quad methods name it.</summary>
/// <remarks>
/// Together with the file and line of the call this identifies a call site: the lanes of a SIMD group
/// must all be at the same one for the operation to have a meaning on the CPU.
/// </remarks>
internal enum SimdOp : byte
{
    IsFirstLane,
    AnyTrue,
    AllTrue,
    Ballot,
    CountBits,
    PrefixCountBits,
    AllEqual,
    Sum,
    Product,
    Min,
    Max,
    BitAnd,
    BitOr,
    BitXor,
    PrefixSum,
    PrefixProduct,
    InclusivePrefixSum,
    InclusivePrefixProduct,
    ReadLaneAt,
    ReadLaneFirst,
    ShuffleUp,
    ShuffleDown,
    ShuffleXor,
    RotateUp,
    RotateDown,
    ShuffleUpAndFill,
    ShuffleDownAndFill,
    QuadAnyTrue,
    QuadAllTrue,
    QuadBallot,
    QuadSum,
    QuadProduct,
    QuadMin,
    QuadMax,
    QuadBitAnd,
    QuadBitOr,
    QuadBitXor,
    QuadPrefixSum,
    QuadPrefixProduct,
    QuadInclusivePrefixSum,
    QuadInclusivePrefixProduct,
    QuadReadAcrossX,
    QuadReadAcrossY,
    QuadReadAcrossDiagonal,
    QuadReadLaneAt,
}

/// <summary>The component type of the value a SIMD-group operation works on; vectors are 2 to 4 of them.</summary>
internal enum SimdScalar : byte
{
    Float,
    Int,
    UInt,

    /// <summary>A <see cref="System.Half"/>: its 16 bits in the low half of the 32-bit component (see <see cref="SimdHalf"/>).</summary>
    Half,
}

/// <summary>
/// How a half and a half vector travel through a meeting: every component as its 16 bits,
/// zero-extended to the 32-bit component a <see cref="SimdValue"/> is made of. A half vector then
/// has as many components as a float vector of its width, and the exchanges, which only move
/// components, need to know nothing about halves; the folds do their arithmetic on
/// <see cref="SimdScalar.Half"/> components as <see cref="System.Half"/> does it.
/// </summary>
internal static class SimdHalf
{
    internal static uint Widen(Half value) => BitConverter.HalfToUInt16Bits(value);

    internal static UInt2 Widen(Half2 value) => new(Widen(value.X), Widen(value.Y));

    internal static UInt3 Widen(Half3 value) => new(Widen(value.X), Widen(value.Y), Widen(value.Z));

    internal static UInt4 Widen(Half4 value) => new(Widen(value.X), Widen(value.Y), Widen(value.Z), Widen(value.W));

    internal static Half Narrow(uint bits) => BitConverter.UInt16BitsToHalf((ushort)bits);

    internal static Half2 Narrow(UInt2 bits) => new(Narrow(bits.X), Narrow(bits.Y));

    internal static Half3 Narrow(UInt3 bits) => new(Narrow(bits.X), Narrow(bits.Y), Narrow(bits.Z));

    internal static Half4 Narrow(UInt4 bits) => new(Narrow(bits.X), Narrow(bits.Y), Narrow(bits.Z), Narrow(bits.W));
}

/// <summary>
/// What a lane brings to an operation and what it gets back: up to four 32-bit components, whatever
/// their type. Every value type of the operations (float, int, uint and their vectors, and half and
/// its vectors widened by <see cref="SimdHalf"/>) is 1 to 4 of them, which lets one meeting hold the
/// values of all its lanes without a type parameter.
/// </summary>
internal struct SimdValue
{
    internal uint X;
    internal uint Y;
    internal uint Z;
    internal uint W;

    internal static SimdValue From(bool value) => new() { X = value ? 1u : 0u };

    internal static SimdValue From<T>(T value)
        where T : unmanaged
    {
        SimdValue result = default;
        Unsafe.WriteUnaligned(ref Unsafe.As<SimdValue, byte>(ref result), value);
        return result;
    }

    internal readonly T To<T>()
        where T : unmanaged => Unsafe.ReadUnaligned<T>(ref Unsafe.As<SimdValue, byte>(ref Unsafe.AsRef(in this)));

    internal uint this[int component]
    {
        readonly get => Unsafe.Add(ref Unsafe.AsRef(in X), component);
        set => Unsafe.Add(ref X, component) = value;
    }
}

/// <summary>
/// A call site of a SIMD-group operation as the CPU sees it: the operation, the type of its value and
/// where the call is in the source (the file and line the C# compiler passed along). The lanes of a
/// SIMD group meet when they are all at the same one.
/// </summary>
internal readonly record struct SimdCall(SimdOp Op, SimdScalar Scalar, int Components, string File, int Line)
{
    /// <summary>The public method behind <see cref="Op"/>, for messages.</summary>
    internal string Method => Op switch
    {
        SimdOp.IsFirstLane => "WaveIsFirstLane",
        SimdOp.AnyTrue => "WaveActiveAnyTrue",
        SimdOp.AllTrue => "WaveActiveAllTrue",
        SimdOp.Ballot => "WaveActiveBallot",
        SimdOp.CountBits => "WaveActiveCountBits",
        SimdOp.PrefixCountBits => "WavePrefixCountBits",
        SimdOp.AllEqual => "WaveActiveAllEqual",
        SimdOp.Sum => "WaveActiveSum",
        SimdOp.Product => "WaveActiveProduct",
        SimdOp.Min => "WaveActiveMin",
        SimdOp.Max => "WaveActiveMax",
        SimdOp.BitAnd => "WaveActiveBitAnd",
        SimdOp.BitOr => "WaveActiveBitOr",
        SimdOp.BitXor => "WaveActiveBitXor",
        SimdOp.PrefixSum => "WavePrefixSum",
        SimdOp.PrefixProduct => "WavePrefixProduct",
        SimdOp.InclusivePrefixSum => "WaveInclusivePrefixSum",
        SimdOp.InclusivePrefixProduct => "WaveInclusivePrefixProduct",
        SimdOp.ReadLaneAt => "WaveReadLaneAt",
        SimdOp.ReadLaneFirst => "WaveReadLaneFirst",
        SimdOp.ShuffleUp => "WaveShuffleUp",
        SimdOp.ShuffleDown => "WaveShuffleDown",
        SimdOp.ShuffleXor => "WaveShuffleXor",
        SimdOp.RotateUp => "WaveRotateUp",
        SimdOp.RotateDown => "WaveRotateDown",
        SimdOp.ShuffleUpAndFill => "WaveShuffleUpAndFill",
        SimdOp.ShuffleDownAndFill => "WaveShuffleDownAndFill",
        SimdOp.QuadAnyTrue => "QuadActiveAnyTrue",
        SimdOp.QuadAllTrue => "QuadActiveAllTrue",
        SimdOp.QuadBallot => "QuadActiveBallot",
        SimdOp.QuadSum => "QuadActiveSum",
        SimdOp.QuadProduct => "QuadActiveProduct",
        SimdOp.QuadMin => "QuadActiveMin",
        SimdOp.QuadMax => "QuadActiveMax",
        SimdOp.QuadBitAnd => "QuadActiveBitAnd",
        SimdOp.QuadBitOr => "QuadActiveBitOr",
        SimdOp.QuadBitXor => "QuadActiveBitXor",
        SimdOp.QuadPrefixSum => "QuadPrefixSum",
        SimdOp.QuadPrefixProduct => "QuadPrefixProduct",
        SimdOp.QuadInclusivePrefixSum => "QuadInclusivePrefixSum",
        SimdOp.QuadInclusivePrefixProduct => "QuadInclusivePrefixProduct",
        SimdOp.QuadReadAcrossX => "QuadReadAcrossX",
        SimdOp.QuadReadAcrossY => "QuadReadAcrossY",
        SimdOp.QuadReadAcrossDiagonal => "QuadReadAcrossDiagonal",
        _ => "QuadReadLaneAt",
    };

    public override string ToString() =>
        File.Length == 0 ? $"Gpu.{Method} (line {Line})" : $"Gpu.{Method} ({System.IO.Path.GetFileName(File)}, line {Line})";
}

/// <summary>
/// The CPU side of the SIMD-group (wave and quad) operations of <see cref="Gpu"/> and of
/// <see cref="SimdGroupIds"/>. Every public operation funnels into one of the methods below with its
/// <see cref="SimdOp"/>; they hand the call to the <see cref="CpuSimdGroups"/> of the threadgroup the
/// calling thread runs in, where the lanes of its SIMD group meet.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model.</b> A SIMD group is <see cref="Width"/> consecutive threads of a threadgroup in the
/// order of their linear index (<c>(z * sizeY + y) * sizeX + x</c>, with the real size of a partial
/// threadgroup at the edge of the grid): lane <c>index % 32</c> of group <c>index / 32</c>. That is how
/// Apple GPUs form them (measured; see the SIMD-group tests).
/// </para>
/// <para>
/// <b>Where the lanes come from.</b> An operation is computed from what the other lanes of the group
/// bring to the same call, so those threads have to be inside <c>Execute</c> at the same time. A CPU
/// dispatch of a shader whose descriptor reports <see cref="IComputeShaderDescriptor{TSelf}.UsesSimdGroups"/>
/// therefore runs on <see cref="CpuGroupDispatcher"/>, each lane on a dedicated thread. Outside a CPU
/// dispatch every method throws <see cref="ShaderOnly.Error"/>, like every other shader built-in; in a
/// dispatch of a shader that did not report its operations (a hand-written descriptor) the operations
/// that need the other lanes throw <see cref="InvalidOperationException"/>. The lane and group indices
/// need nobody else and work in any CPU dispatch.
/// </para>
/// </remarks>
internal static class CpuSimdGroup
{
    /// <summary>
    /// Threads per SIMD group: 32 on every Apple GPU. <see cref="Gpu.WaveActiveBallot"/> returns one bit
    /// per lane in a <see cref="uint"/> on the strength of it, and a dispatch checks it against the
    /// compiled kernel (<c>threadExecutionWidth</c>) for shaders that use SIMD-group operations.
    /// </summary>
    internal const int Width = 32;

    /// <summary>Threads per quad: four consecutive lanes of a SIMD group.</summary>
    internal const int QuadWidth = 4;

    /// <summary><c>thread_index_in_simdgroup</c>.</summary>
    internal static int LaneIndex() => CpuThread.Current.GroupIndex % Width;

    /// <summary><c>threads_per_simdgroup</c>.</summary>
    internal static int LaneCount()
    {
        _ = CpuThread.Current;
        return Width;
    }

    /// <summary><c>simdgroup_index_in_threadgroup</c>.</summary>
    internal static int GroupIndex() => CpuThread.Current.GroupIndex / Width;

    /// <summary><c>simdgroups_per_threadgroup</c>: as many as the threads the threadgroup really has need.</summary>
    internal static int GroupCount() => (CpuThread.Current.GroupThreadCount + Width - 1) / Width;

    /// <summary>The votes that answer with a bool: any, all (of the SIMD group or the quad) and "is this the first lane".</summary>
    internal static bool Vote(SimdOp op, bool value, string callerFile, int callerLine) =>
        Meet(new SimdCall(op, SimdScalar.UInt, 1, callerFile, callerLine), SimdValue.From(value), default, 0).X != 0;

    /// <summary>The ballots: bit <c>i</c> is lane <c>i</c>'s <paramref name="value"/> (of the SIMD group or of the quad).</summary>
    internal static uint Ballot(SimdOp op, bool value, string callerFile, int callerLine) =>
        Meet(new SimdCall(op, SimdScalar.UInt, 1, callerFile, callerLine), SimdValue.From(value), default, 0).X;

    /// <summary><see cref="SimdOp.CountBits"/> and <see cref="SimdOp.PrefixCountBits"/>.</summary>
    internal static int Count(SimdOp op, bool value, string callerFile, int callerLine) =>
        (int)Meet(new SimdCall(op, SimdScalar.Int, 1, callerFile, callerLine), SimdValue.From(value), default, 0).X;

    /// <summary>
    /// The reductions and scans: the result is computed componentwise from the
    /// <typeparamref name="T"/> of every participating lane. <typeparamref name="T"/> is a
    /// <paramref name="scalar"/> or a vector of 2 to 4 of them, which is all the meeting needs to know
    /// to do the arithmetic without a method per type.
    /// </summary>
    internal static T Reduce<T>(SimdOp op, SimdScalar scalar, T value, string callerFile, int callerLine)
        where T : unmanaged =>
        Meet(new SimdCall(op, scalar, Unsafe.SizeOf<T>() / sizeof(uint), callerFile, callerLine), SimdValue.From(value), default, 0).To<T>();

    /// <summary><see cref="SimdOp.AllEqual"/>: true when every participating lane brought the same value.</summary>
    internal static bool AllEqual<T>(SimdScalar scalar, T value, string callerFile, int callerLine)
        where T : unmanaged =>
        Meet(new SimdCall(SimdOp.AllEqual, scalar, Unsafe.SizeOf<T>() / sizeof(uint), callerFile, callerLine), SimdValue.From(value), default, 0).X != 0;

    /// <summary>
    /// The exchanges: the result is the <typeparamref name="T"/> another lane brought (its
    /// <paramref name="value"/>, or its <paramref name="fill"/> for the lanes a filling shuffle shifts
    /// in), chosen by <paramref name="argument"/> (a lane, a delta or a mask; unused by some operations).
    /// </summary>
    internal static T Exchange<T>(SimdOp op, T value, T fill, int argument, string callerFile, int callerLine)
        where T : unmanaged =>
        Meet(new SimdCall(op, SimdScalar.UInt, Unsafe.SizeOf<T>() / sizeof(uint), callerFile, callerLine), SimdValue.From(value), SimdValue.From(fill), argument).To<T>();

    private static SimdValue Meet(in SimdCall call, SimdValue value, SimdValue fill, int argument)
    {
        CpuThread thread = CpuThread.Current;

        if (thread.Group?.Simd is not { } simd)
        {
            // Generated descriptors report their SIMD-group operations, so this is a hand-written one that does not.
            throw new InvalidOperationException(
                $"Gpu.{call.Method} was called by a shader whose descriptor does not report UsesSimdGroups, so the lanes of its SIMD groups " +
                "were not run side by side on the CPU. Let the generator write the descriptor ([GeneratedComputeShaderDescriptor]), " +
                "or return true from IComputeShaderDescriptor<T>.UsesSimdGroups.");
        }

        return simd.Meet(thread, in call, value, fill, argument);
    }
}

/// <summary>
/// The SIMD groups of the threadgroup a <see cref="CpuThreadgroup"/> is running: where the lanes of
/// each group meet at a SIMD-group operation, and where its result is computed from what they brought.
/// </summary>
/// <remarks>
/// <para>
/// <b>The meeting.</b> A lane that calls an operation leaves its value at its SIMD group's meeting and
/// parks. When every lane of the group that is still inside <c>Execute</c> has arrived, the last
/// arrival computes all the results at once and wakes the others. A lane that returns from
/// <c>Execute</c> leaves its group, so a meeting never waits for it: its participants are the lanes
/// that have not returned, which is what the GPU calls the active lanes as long as the lanes that are
/// still running make the same calls.
/// </para>
/// <para>
/// <b>Divergence.</b> The first arrival fixes what the meeting is about (<see cref="SimdCall"/>). A
/// lane that arrives with another call is in a different place of the shader than its group, which
/// the CPU cannot give a meaning to: it throws <see cref="NotSupportedException"/>. The same exception
/// ends a threadgroup in which lanes wait at a meeting for threads that wait at a barrier: when every
/// thread still inside <c>Execute</c> is parked at one or the other and neither can complete, nothing
/// would ever move again. What is <i>not</i> detected is lanes that reach the same call site along
/// different paths (see <see cref="Gpu.WaveActiveSum(float, string, int)"/>).
/// </para>
/// <para>
/// Thread-safe: that is its purpose. All state is guarded by one lock per threadgroup; lanes park
/// outside it, each on a spot of its own.
/// </para>
/// </remarks>
internal sealed class CpuSimdGroups
{
    private const int Width = CpuSimdGroup.Width;

    private readonly CpuThreadgroup _group;
    private readonly bool _lanesOnly;
    private readonly object _lock = new();
    private readonly Meeting[] _meetings;
    private readonly Parker[] _parkers;

    // Lanes parked at a meeting that is not complete. Guarded by _lock.
    private int _parked;

    /// <param name="group">The threadgroup whose worker threads are the lanes.</param>
    /// <param name="workers">The number of worker threads.</param>
    /// <param name="lanesOnly">
    /// True when the workers are the lanes of one SIMD group at a time (a shader without threadgroup
    /// memory or barriers); false when they are a whole threadgroup, which holds several SIMD groups.
    /// </param>
    internal CpuSimdGroups(CpuThreadgroup group, int workers, bool lanesOnly)
    {
        _group = group;
        _lanesOnly = lanesOnly;
        _meetings = new Meeting[lanesOnly ? 1 : (workers + Width - 1) / Width];
        _parkers = new Parker[workers];

        for (int i = 0; i < _meetings.Length; i++)
        {
            _meetings[i] = new Meeting();
        }

        for (int i = 0; i < workers; i++)
        {
            _parkers[i] = new Parker();
        }
    }

    /// <summary>
    /// Called before the threads of a threadgroup (or, with lanes only, of one SIMD group) start:
    /// <paramref name="threads"/> of them, numbered from 0, are about to enter <c>Execute</c>.
    /// </summary>
    internal void Begin(int threads)
    {
        lock (_lock)
        {
            for (int i = 0; i < _meetings.Length; i++)
            {
                _meetings[i].Reset(Math.Clamp(threads - (i * Width), 0, Width));
            }

            _parked = 0;
        }
    }

    /// <summary>
    /// A lane arrives at <paramref name="call"/> with its <paramref name="value"/>. Returns the lane's
    /// result once every lane of its SIMD group that is still running has arrived at the same call.
    /// </summary>
    /// <exception cref="NotSupportedException">The lanes of the group are not at the same call (divergent control flow).</exception>
    /// <exception cref="CpuThreadgroupAbortedException">Another thread of the threadgroup threw; this one unwinds.</exception>
    internal SimdValue Meet(CpuThread thread, in SimdCall call, SimdValue value, SimdValue fill, int argument)
    {
        int index = thread.GroupIndex;
        int lane = index % Width;
        Meeting meeting = _meetings[_lanesOnly ? 0 : index / Width];
        Parker parker = _parkers[thread.Slot];

        lock (_lock)
        {
            if (_group.IsAborted)
            {
                throw new CpuThreadgroupAbortedException();
            }

            if (meeting.Arrived == 0)
            {
                meeting.Call = call;
            }
            else if (meeting.Call != call)
            {
                throw new NotSupportedException(
                    $"{DivergenceMessage} The lanes of a SIMD group have to reach the same call together, but lane {lane} is at {call} " +
                    $"while other lanes of its group wait at {meeting.Call}.");
            }

            meeting.Values[lane] = value;
            meeting.Fills[lane] = fill;

            // The GPU takes lanes, deltas and masks as ushort.
            meeting.Arguments[lane] = (ushort)argument;
            meeting.Slots[lane] = thread.Slot;
            meeting.ArrivedMask |= 1u << lane;
            meeting.Arrived++;

            if (meeting.Arrived == meeting.Live)
            {
                Complete(meeting, lane);
                return meeting.Results[lane];
            }

            _parked++;

            if (IsStalled())
            {
                throw Stalled(meeting);
            }
        }

        if (!parker.Wait(out SimdValue result))
        {
            throw new CpuThreadgroupAbortedException();
        }

        return result;
    }

    /// <summary>
    /// A lane is done with <c>Execute</c> (it returned, threw or was stopped). If the rest of its SIMD
    /// group was waiting only for it, their meeting is complete without it.
    /// </summary>
    internal void Leave(CpuThread thread)
    {
        lock (_lock)
        {
            Meeting meeting = _meetings[_lanesOnly ? 0 : thread.GroupIndex / Width];
            meeting.Live--;

            if (meeting.Arrived > 0 && meeting.Arrived == meeting.Live && !_group.IsAborted)
            {
                Complete(meeting, lane: -1);
            }
        }
    }

    /// <summary>
    /// Called when a thread has parked at a barrier or left the threadgroup, the two events besides an
    /// arrival at a meeting that can leave every remaining thread parked. If lanes are waiting at a
    /// meeting for threads that are waiting at the barrier, the group can never continue: it is failed
    /// with the divergence exception.
    /// </summary>
    internal void CheckStall()
    {
        NotSupportedException? failure = null;

        lock (_lock)
        {
            if (!_group.IsAborted && IsStalled())
            {
                foreach (Meeting meeting in _meetings)
                {
                    if (meeting.Arrived > 0)
                    {
                        failure = Stalled(meeting);
                        break;
                    }
                }
            }
        }

        if (failure is not null)
        {
            _group.FailRunning(failure);
        }
    }

    /// <summary>The threadgroup is being abandoned: lanes parked at a meeting wake up and unwind.</summary>
    internal void Abort()
    {
        lock (_lock)
        {
            foreach (Meeting meeting in _meetings)
            {
                // Every lane that has arrived is parked, except one that is on its way out with an
                // exception of its own; telling that one too does no harm.
                for (int lane = 0; lane < Width; lane++)
                {
                    if ((meeting.ArrivedMask & (1u << lane)) != 0)
                    {
                        _parkers[meeting.Slots[lane]].Abort();
                    }
                }

                meeting.ArrivedMask = 0;
                meeting.Arrived = 0;
            }

            _parked = 0;
        }
    }

    private const string DivergenceMessage = "SIMD-group operation in divergent control flow is not supported on the CPU; it is supported on the GPU.";

    /// <summary>
    /// True when lanes are parked at meetings and every other thread still inside <c>Execute</c> is
    /// parked at the barrier. Exact under <see cref="_lock"/>: the lanes counted in <see cref="_parked"/>
    /// cannot move while it is held, so if the barrier's waiters make up the rest of the live threads at
    /// the instant its state is read, every live thread is parked at something that waits for one of
    /// the others.
    /// </summary>
    private bool IsStalled()
    {
        if (_parked == 0 || _lanesOnly)
        {
            return false;
        }

        (int live, int atBarrier) = _group.LiveAndWaiting;
        return atBarrier > 0 && atBarrier + _parked == live;
    }

    private static NotSupportedException Stalled(Meeting meeting) =>
        new($"{DivergenceMessage} Lanes of a SIMD group wait at {meeting.Call} for the rest of their group, while other threads of the " +
            "threadgroup wait at a barrier for them: every thread of a threadgroup has to reach the same barriers and SIMD-group operations in the same order.");

    /// <summary>
    /// Computes the result of every lane that arrived and wakes the ones that are parked
    /// (<paramref name="lane"/>, the caller that completed the meeting, takes its result itself).
    /// </summary>
    private void Complete(Meeting meeting, int lane)
    {
        meeting.Compute();

        for (int other = 0; other < Width; other++)
        {
            if (other != lane && (meeting.ArrivedMask & (1u << other)) != 0)
            {
                _parkers[meeting.Slots[other]].Deliver(meeting.Results[other]);
                _parked--;
            }
        }

        meeting.ArrivedMask = 0;
        meeting.Arrived = 0;
    }

    /// <summary>One SIMD group's rendezvous: what its lanes brought to the call they are meeting at.</summary>
    private sealed class Meeting
    {
        internal readonly SimdValue[] Values = new SimdValue[Width];
        internal readonly SimdValue[] Fills = new SimdValue[Width];
        internal readonly SimdValue[] Results = new SimdValue[Width];
        internal readonly int[] Arguments = new int[Width];
        internal readonly int[] Slots = new int[Width];

        /// <summary>Lanes of the group still inside <c>Execute</c>.</summary>
        internal int Live;

        /// <summary>Lanes that have arrived at the current call; bit <c>i</c> of <see cref="ArrivedMask"/> is lane <c>i</c>.</summary>
        internal int Arrived;

        internal uint ArrivedMask;

        internal SimdCall Call;

        internal void Reset(int live)
        {
            Live = live;
            Arrived = 0;
            ArrivedMask = 0;
        }

        private bool Has(int lane) => (uint)lane < Width && (ArrivedMask & (1u << lane)) != 0;

        /// <summary>The value of <paramref name="lane"/>, or 0 when it takes no part: undefined on the GPU, where Apple GPUs return 0 too.</summary>
        private SimdValue Value(int lane) => Has(lane) ? Values[lane] : default;

        private SimdValue Fill(int lane) => Has(lane) ? Fills[lane] : default;

        /// <summary>Fills <see cref="Results"/> for every lane that arrived.</summary>
        internal void Compute()
        {
            SimdOp op = Call.Op;

            switch (op)
            {
                case SimdOp.Sum or SimdOp.Product or SimdOp.Min or SimdOp.Max or SimdOp.BitAnd or SimdOp.BitOr or SimdOp.BitXor:
                    Fold(op, Call.Scalar, Width, Scan.None);
                    break;
                case SimdOp.QuadSum or SimdOp.QuadProduct or SimdOp.QuadMin or SimdOp.QuadMax or SimdOp.QuadBitAnd or SimdOp.QuadBitOr or SimdOp.QuadBitXor:
                    Fold(op - SimdOp.QuadSum + SimdOp.Sum, Call.Scalar, CpuSimdGroup.QuadWidth, Scan.None);
                    break;
                case SimdOp.PrefixSum or SimdOp.PrefixProduct:
                    Fold(op - SimdOp.PrefixSum + SimdOp.Sum, Call.Scalar, Width, Scan.Exclusive);
                    break;
                case SimdOp.InclusivePrefixSum or SimdOp.InclusivePrefixProduct:
                    Fold(op - SimdOp.InclusivePrefixSum + SimdOp.Sum, Call.Scalar, Width, Scan.Inclusive);
                    break;
                case SimdOp.QuadPrefixSum or SimdOp.QuadPrefixProduct:
                    Fold(op - SimdOp.QuadPrefixSum + SimdOp.Sum, Call.Scalar, CpuSimdGroup.QuadWidth, Scan.Exclusive);
                    break;
                case SimdOp.QuadInclusivePrefixSum or SimdOp.QuadInclusivePrefixProduct:
                    Fold(op - SimdOp.QuadInclusivePrefixSum + SimdOp.Sum, Call.Scalar, CpuSimdGroup.QuadWidth, Scan.Inclusive);
                    break;

                // The votes are folds of 0 and 1: any is their maximum, all their minimum, the count their sum.
                case SimdOp.AnyTrue:
                    Fold(SimdOp.Max, SimdScalar.UInt, Width, Scan.None);
                    break;
                case SimdOp.AllTrue:
                    Fold(SimdOp.Min, SimdScalar.UInt, Width, Scan.None);
                    break;
                case SimdOp.QuadAnyTrue:
                    Fold(SimdOp.Max, SimdScalar.UInt, CpuSimdGroup.QuadWidth, Scan.None);
                    break;
                case SimdOp.QuadAllTrue:
                    Fold(SimdOp.Min, SimdScalar.UInt, CpuSimdGroup.QuadWidth, Scan.None);
                    break;
                case SimdOp.CountBits:
                    Fold(SimdOp.Sum, SimdScalar.Int, Width, Scan.None);
                    break;
                case SimdOp.PrefixCountBits:
                    Fold(SimdOp.Sum, SimdScalar.Int, Width, Scan.Exclusive);
                    break;
                case SimdOp.Ballot:
                    Ballot(Width);
                    break;
                case SimdOp.QuadBallot:
                    Ballot(CpuSimdGroup.QuadWidth);
                    break;
                case SimdOp.IsFirstLane:
                    First();
                    break;
                case SimdOp.AllEqual:
                    AllEqual();
                    break;
                default:
                    Exchange(op);
                    break;
            }
        }

        private enum Scan
        {
            None,
            Exclusive,
            Inclusive,
        }

        /// <summary>
        /// Folds the values of the arrived lanes of each block of <paramref name="blockWidth"/> lanes (the
        /// whole group, or a quad) with <paramref name="fold"/>, in lane order, per component. Without a
        /// scan every lane of the block gets the total; with one, the fold of the lanes below it
        /// (exclusive: the identity for the first) or up to and including it.
        /// </summary>
        private void Fold(SimdOp fold, SimdScalar scalar, int blockWidth, Scan scan)
        {
            for (int start = 0; start < Width; start += blockWidth)
            {
                for (int component = 0; component < Call.Components; component++)
                {
                    uint running = Identity(fold, scalar);
                    bool any = false;

                    for (int lane = start; lane < start + blockWidth; lane++)
                    {
                        if (!Has(lane))
                        {
                            continue;
                        }

                        uint value = Values[lane][component];
                        uint next = any || scan != Scan.None ? Combine(fold, scalar, running, value) : value;

                        // A scan's lanes get their results as it passes; a reduction's get the total below.
                        if (scan != Scan.None)
                        {
                            Results[lane][component] = scan == Scan.Exclusive ? running : next;
                        }

                        running = next;
                        any = true;
                    }

                    if (scan == Scan.None)
                    {
                        for (int lane = start; lane < start + blockWidth; lane++)
                        {
                            if (Has(lane))
                            {
                                Results[lane][component] = running;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// What an exclusive scan gives its first lane: 0 for a sum, 1 for a product. Scans exist for those
        /// two only. For a sum of floats or of halves the 0 is <c>-0</c>, which is what an Apple GPU returns
        /// (measured on an M4 Pro) and the identity of IEEE addition: <c>-0 + x</c> is <c>x</c> for every
        /// <c>x</c>, <c>-0</c> included, where <c>+0 + -0</c> would be <c>+0</c>.
        /// </summary>
        private static uint Identity(SimdOp fold, SimdScalar scalar) => (fold, scalar) switch
        {
            (SimdOp.Product, SimdScalar.Float) => BitConverter.SingleToUInt32Bits(1f),
            (SimdOp.Product, SimdScalar.Half) => SimdHalf.Widen((Half)1f),
            (SimdOp.Product, _) => 1u,
            (SimdOp.Sum, SimdScalar.Float) => BitConverter.SingleToUInt32Bits(-0f),
            (SimdOp.Sum, SimdScalar.Half) => SimdHalf.Widen(-(Half)0f),
            _ => 0u,
        };

        private static uint Combine(SimdOp fold, SimdScalar scalar, uint a, uint b)
        {
            if (scalar == SimdScalar.Float)
            {
                float x = BitConverter.UInt32BitsToSingle(a), y = BitConverter.UInt32BitsToSingle(b);

                return BitConverter.SingleToUInt32Bits(fold switch
                {
                    SimdOp.Sum => x + y,
                    SimdOp.Product => x * y,
                    SimdOp.Min => Gpu.Min(x, y),
                    _ => Gpu.Max(x, y),
                });
            }

            if (scalar == SimdScalar.Half)
            {
                // One correctly rounded half operation per lane, in lane order, as System.Half does it.
                Half x = SimdHalf.Narrow(a), y = SimdHalf.Narrow(b);

                return SimdHalf.Widen(fold switch
                {
                    SimdOp.Sum => x + y,
                    SimdOp.Product => x * y,
                    SimdOp.Min => HalfMath.Min(x, y),
                    _ => HalfMath.Max(x, y),
                });
            }

            return fold switch
            {
                // Two's complement: sums, products and the bit operations are the same for int and uint.
                SimdOp.Sum => unchecked(a + b),
                SimdOp.Product => unchecked(a * b),
                SimdOp.BitAnd => a & b,
                SimdOp.BitOr => a | b,
                SimdOp.BitXor => a ^ b,
                SimdOp.Min => scalar == SimdScalar.Int ? (uint)Math.Min((int)a, (int)b) : Math.Min(a, b),
                _ => scalar == SimdScalar.Int ? (uint)Math.Max((int)a, (int)b) : Math.Max(a, b),
            };
        }

        private void Ballot(int blockWidth)
        {
            for (int start = 0; start < Width; start += blockWidth)
            {
                uint ballot = 0;

                for (int lane = start; lane < start + blockWidth; lane++)
                {
                    if (Has(lane) && Values[lane].X != 0)
                    {
                        ballot |= 1u << (lane - start);
                    }
                }

                for (int lane = start; lane < start + blockWidth; lane++)
                {
                    Results[lane] = new SimdValue { X = ballot };
                }
            }
        }

        private int FirstLane() => System.Numerics.BitOperations.TrailingZeroCount(ArrivedMask);

        private void First()
        {
            int first = FirstLane();

            for (int lane = 0; lane < Width; lane++)
            {
                Results[lane] = new SimdValue { X = lane == first ? 1u : 0u };
            }
        }

        private void AllEqual()
        {
            SimdValue first = Values[FirstLane()];
            bool equal = true;

            for (int lane = 0; lane < Width && equal; lane++)
            {
                if (!Has(lane))
                {
                    continue;
                }

                for (int component = 0; component < Call.Components; component++)
                {
                    uint a = Values[lane][component], b = first[component];

                    // As `==` in the kernel: a NaN equals nothing (itself included), and -0 equals +0.
                    equal &= Call.Scalar switch
                    {
                        SimdScalar.Float => BitConverter.UInt32BitsToSingle(a) == BitConverter.UInt32BitsToSingle(b),
                        SimdScalar.Half => SimdHalf.Narrow(a) == SimdHalf.Narrow(b),
                        _ => a == b,
                    };
                }
            }

            for (int lane = 0; lane < Width; lane++)
            {
                Results[lane] = new SimdValue { X = equal ? 1u : 0u };
            }
        }

        private void Exchange(SimdOp op)
        {
            int first = FirstLane();

            for (int lane = 0; lane < Width; lane++)
            {
                if (!Has(lane))
                {
                    continue;
                }

                int argument = Arguments[lane];
                int quad = lane & ~(CpuSimdGroup.QuadWidth - 1);

                Results[lane] = op switch
                {
                    SimdOp.ReadLaneAt => Value(argument),
                    SimdOp.ReadLaneFirst => Values[first],

                    // A plain shuffle leaves a lane that has nothing to read with its own value.
                    SimdOp.ShuffleUp => lane - argument < 0 ? Values[lane] : Value(lane - argument),
                    SimdOp.ShuffleDown => lane + argument >= Width ? Values[lane] : Value(lane + argument),
                    SimdOp.ShuffleXor => Value(lane ^ argument),
                    SimdOp.RotateUp => Value((lane - (argument % Width) + Width) % Width),
                    SimdOp.RotateDown => Value((lane + argument) % Width),

                    // A filling shuffle shifts a row of 64: the fills of lanes 0..31 next to their values.
                    SimdOp.ShuffleUpAndFill => lane - argument >= 0 ? Value(lane - argument) : Fill(lane - argument + Width),
                    SimdOp.ShuffleDownAndFill => lane + argument < Width ? Value(lane + argument) : Fill(lane + argument - Width),
                    SimdOp.QuadReadAcrossX => Value(lane ^ 1),
                    SimdOp.QuadReadAcrossY => Value(lane ^ 2),
                    SimdOp.QuadReadAcrossDiagonal => Value(lane ^ 3),
                    _ => argument < CpuSimdGroup.QuadWidth ? Value(quad + argument) : default,
                };
            }
        }
    }

    /// <summary>
    /// One lane's parking spot: it waits here for its result, or for the news that the threadgroup is
    /// being abandoned. A monitor of its own, like the barrier's parking spots, so that waking the
    /// lanes of one meeting disturbs nobody else.
    /// </summary>
    private sealed class Parker
    {
        private const int Waiting = 0, Delivered = 1, Aborted = 2;

        private readonly object _gate = new();
        private SimdValue _result;
        private int _state;

        internal void Deliver(SimdValue result)
        {
            lock (_gate)
            {
                _result = result;
                _state = Delivered;
                Monitor.Pulse(_gate);
            }
        }

        internal void Abort()
        {
            lock (_gate)
            {
                _state = Aborted;
                Monitor.Pulse(_gate);
            }
        }

        /// <summary>Returns false when the wait ended without a result because the threadgroup was aborted.</summary>
        internal bool Wait(out SimdValue result)
        {
            lock (_gate)
            {
                while (_state == Waiting)
                {
                    Monitor.Wait(_gate);
                }

                bool delivered = _state == Delivered;
                result = _result;
                _state = Waiting;
                return delivered;
            }
        }
    }
}
