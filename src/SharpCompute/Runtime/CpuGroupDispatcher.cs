using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace SharpCompute;

/// <summary>
/// Runs a box of the grid on the CPU for a shader whose threads cooperate within a threadgroup: one
/// that uses <see cref="GroupSharedAttribute"/> memory or a barrier
/// (<see cref="IComputeShaderDescriptor{TSelf}.UsesThreadgroupMemory"/>).
/// </summary>
/// <remarks>
/// <para>
/// A barrier stops a shader thread in the middle of <c>Execute</c> until every other thread of its
/// group has reached the same call. C# has no way to suspend a method there and resume it later on
/// another thread, so every thread of a threadgroup gets a dedicated OS thread for the dispatch
/// (up to <see cref="MaxGroupThreads"/>). They are dedicated rather than thread-pool threads because a
/// pool can run out of threads while all the ones it has are parked at a barrier, which would
/// deadlock, and because parking pool threads starves everything else that uses the pool.
/// </para>
/// <para>
/// The threads of one group run concurrently, as on the GPU, and meet at the group's
/// <see cref="CpuThreadgroup"/>. The groups run one after another, in row-major order: a
/// <see cref="GroupSharedAttribute"/> array is one static array, which can only be one group's memory
/// at a time. For the same reason dispatches of one shader type are serialized process-wide (the
/// <c>gate</c> passed to <see cref="Run"/>).
/// </para>
/// <para>
/// This is built to be correct, not fast: each barrier costs a block and a wake-up of every thread of
/// the group, a few microseconds per thread. It is meant for checking shaders against the GPU and for
/// the CPU part of a split, not for bulk work.
/// </para>
/// </remarks>
internal static class CpuGroupDispatcher
{
    /// <summary>
    /// The most threads a threadgroup can have on the CPU: each gets an OS thread. 1024 is also Metal's
    /// limit for a threadgroup on Apple GPUs, so every size the GPU accepts runs on the CPU too.
    /// </summary>
    internal const int MaxGroupThreads = 1024;

    /// <summary>
    /// The stack of each of those threads. Smaller than the runtime's default, so that a threadgroup of
    /// 1024 reserves half a gigabyte of address space rather than several; shader code does not recurse
    /// (the generator rejects it) and keeps its data in buffers, so it needs little stack.
    /// </summary>
    private const int WorkerStackSize = 512 * 1024;

    /// <summary>
    /// Runs the threads <paramref name="origin"/> .. <paramref name="origin"/> + <paramref name="count"/>
    /// (exclusive) of the grid, threadgroup by threadgroup. <paramref name="origin"/> must lie on a
    /// threadgroup boundary. If a thread throws, the rest of its group is stopped at the next barrier
    /// each thread reaches, no further group runs, and the first exception is rethrown.
    /// <paramref name="gate"/> is held for the whole dispatch: it is the lock of the shader type, whose
    /// static group-shared arrays this dispatch uses.
    /// </summary>
    /// <exception cref="NotSupportedException">A threadgroup has more than <paramref name="maxGroupThreads"/> threads.</exception>
    internal static void Run<TKernel>(
        TKernel kernel, int gridX, int gridY, int gridZ, Extent3 groupSize, Extent3 origin, Extent3 count, object gate, Type shaderType,
        int maxGroupThreads = MaxGroupThreads)
        where TKernel : struct, ICpuKernel
    {
        if ((long)count.X * count.Y * count.Z == 0)
        {
            return;
        }

        Debug.Assert(origin.X % groupSize.X == 0 && origin.Y % groupSize.Y == 0 && origin.Z % groupSize.Z == 0, "The box must start on a threadgroup boundary.");

        // Only the positions of a group that some group of this box really has need a thread: ten
        // threads dispatched in groups of 256 need ten.
        Extent3 slots = new(Math.Min(groupSize.X, count.X), Math.Min(groupSize.Y, count.Y), Math.Min(groupSize.Z, count.Z));
        long threads = (long)slots.X * slots.Y * slots.Z;

        if (threads > maxGroupThreads)
        {
            throw new NotSupportedException(
                $"Shader '{shaderType}' uses threadgroup memory or barriers, which the CPU runs with one dedicated thread per thread of a threadgroup. " +
                $"Its threadgroups of {groupSize.X} x {groupSize.Y} x {groupSize.Z} need {threads} threads here, more than the limit of {maxGroupThreads}. " +
                "Use a smaller [ThreadGroupSize], or run the shader on the GPU.");
        }

        lock (gate)
        {
            CpuThreadgroup group = new((int)threads);
            Thread[] workers = new Thread[threads];
            int started = 0;

            try
            {
                for (int z = 0; z < slots.Z; z++)
                {
                    for (int y = 0; y < slots.Y; y++)
                    {
                        for (int x = 0; x < slots.X; x++)
                        {
                            Worker<TKernel> worker = new(kernel, group, started, new Extent3(x, y, z), new CpuThread(gridX, gridY, gridZ, groupSize) { Group = group, Slot = started });

                            // Background threads: a shader that never returns must not keep the process alive.
                            workers[started] = new Thread(worker.Run, WorkerStackSize) { IsBackground = true, Name = "SharpCompute threadgroup" };
                            workers[started].Start();
                            started++;
                        }
                    }
                }

                Extent3 end = new(origin.X + count.X, origin.Y + count.Y, origin.Z + count.Z);

                for (int z = origin.Z; z < end.Z && !group.IsAborted; z += groupSize.Z)
                {
                    for (int y = origin.Y; y < end.Y && !group.IsAborted; y += groupSize.Y)
                    {
                        for (int x = origin.X; x < end.X && !group.IsAborted; x += groupSize.X)
                        {
                            Extent3 extent = new(Math.Min(groupSize.X, end.X - x), Math.Min(groupSize.Y, end.Y - y), Math.Min(groupSize.Z, end.Z - z));
                            group.RunGroup(new Extent3(x, y, z), extent, slots);
                        }
                    }
                }
            }
            finally
            {
                // Also on a failure to start a thread: the ones that did start are parked and must be let go.
                group.Stop(started);

                for (int i = 0; i < started; i++)
                {
                    workers[i].Join();
                }
            }

            group.ThrowIfFailed();
        }
    }

    /// <summary>One thread of the threadgroup: the same position in every group that has it.</summary>
    private sealed class Worker<TKernel>(TKernel kernel, CpuThreadgroup group, int slot, Extent3 position, CpuThread thread)
        where TKernel : struct, ICpuKernel
    {
        private TKernel _kernel = kernel;

        public void Run()
        {
            CpuThread.Enter(thread);

            while (group.WaitForGroup(slot, out Extent3 origin))
            {
                try
                {
                    if (!group.IsAborted)
                    {
                        thread.X = origin.X + position.X;
                        thread.Y = origin.Y + position.Y;
                        thread.Z = origin.Z + position.Z;
                        _kernel.Invoke(thread);
                    }
                }
                catch (CpuThreadgroupAbortedException)
                {
                    // Another thread of the group failed; this one was stopped at a barrier.
                }
                catch (Exception e)
                {
                    group.Fail(slot, e);
                }
                finally
                {
                    group.Leave(slot);
                }
            }
        }
    }
}

/// <summary>
/// The meeting point of the CPU threads of one threadgroup: starts them for each group, implements
/// the barrier they synchronize at, and stops them when one fails. Reused for every group and every
/// barrier of a dispatch.
/// </summary>
/// <remarks>
/// <para>
/// <b>The barrier.</b> A thread that reaches the barrier parks until all the others have arrived; the
/// last one to arrive does not park but wakes the rest. A thread that returns from <c>Execute</c>
/// leaves the group, so "all the others" means the threads that are still running the shader: a
/// shader whose threads disagree about how many barriers they call (undefined on the GPU) therefore
/// still finishes on the CPU instead of deadlocking. Arrivals and departures are counted in one
/// interlocked word, so exactly one thread sees the group become complete and does the waking.
/// </para>
/// <para>
/// <b>Wake-ups.</b> A thread about to park announces it in its slot (<see cref="Slot.Waiting"/>), and
/// whoever has a reason to wake the parked threads (the last arrival, a departure that leaves only
/// parked threads, a failure) scans the slots and takes each announcement with a compare-exchange
/// before signalling the slot. So an announcement is answered exactly once, a signal is only ever
/// sent to a thread that announced a wait, and every signal is consumed by the wait it was sent for.
/// </para>
/// <para>
/// <b>Generations.</b> Every group of the dispatch has a number, and an announcement carries the
/// number of the group it was made in. A scan only takes announcements of its own group. That matters
/// for the one scan that can outlive its group: a departing thread scans after it has stopped counting
/// as running, so the threads it wakes can finish the group, and the next group can start and reach
/// its first barrier, while the scan is still on its way through the slots. It then finds
/// announcements of the next group, which it must not answer, and by their number it does not.
/// </para>
/// <para>
/// <b>Memory.</b> Every arrival is an interlocked operation and every wake-up goes through a monitor,
/// so what a thread wrote before a barrier is visible to every thread after it, for the group-shared
/// arrays (plain static arrays) as well as for buffers and textures. That makes the three barrier
/// flavours (<c>GroupMemory</c>, <c>DeviceMemory</c>, <c>AllMemory</c>) the same thing here.
/// </para>
/// <para>Thread-safe: that is its purpose. One instance belongs to one dispatch.</para>
/// </remarks>
internal sealed class CpuThreadgroup
{
    private readonly Slot[] _slots;
    private readonly object _idle = new();

    // The running group: threads still inside Execute (high 32 bits) and, of those, the ones parked
    // at the barrier (low 32 bits).
    private long _state;

    // The running group's origin and number (from 1). Written by the dispatching thread before it
    // signals the group's workers, which read them after that signal.
    private Extent3 _origin;
    private long _generation;

    private bool _groupDone;

    // 0, or 1 once a thread has thrown. Set with an interlocked operation: see Barrier.
    private int _aborted;
    private volatile bool _stopped;
    private ExceptionDispatchInfo? _failure;

    internal CpuThreadgroup(int threads)
    {
        _slots = new Slot[threads];

        for (int i = 0; i < threads; i++)
        {
            _slots[i] = new Slot();
        }
    }

    /// <summary>True once a thread has thrown: the dispatch is being abandoned.</summary>
    internal bool IsAborted => Volatile.Read(ref _aborted) != 0;

    // ---- the dispatching thread --------------------------------------------------------------

    /// <summary>
    /// Runs one threadgroup and returns when all its threads have returned (or been stopped). The group
    /// starts at <paramref name="origin"/> and has <paramref name="extent"/> threads, which is less than
    /// the full size (<paramref name="slots"/> positions per axis) for a partial group at the edge.
    /// </summary>
    internal void RunGroup(Extent3 origin, Extent3 extent, Extent3 slots)
    {
        _origin = origin;
        _generation++;
        Volatile.Write(ref _state, (long)(extent.X * extent.Y * extent.Z) << 32);

        for (int z = 0; z < extent.Z; z++)
        {
            for (int y = 0; y < extent.Y; y++)
            {
                for (int x = 0; x < extent.X; x++)
                {
                    Slot slot = _slots[(((z * slots.Y) + y) * slots.X) + x];
                    Volatile.Write(ref slot.Start, 1);
                    slot.Signal();
                }
            }
        }

        lock (_idle)
        {
            while (!_groupDone)
            {
                Monitor.Wait(_idle);
            }

            _groupDone = false;
        }
    }

    /// <summary>Lets the first <paramref name="threads"/> worker threads go; they exit their loops.</summary>
    internal void Stop(int threads)
    {
        _stopped = true;

        for (int i = 0; i < threads; i++)
        {
            _slots[i].Signal();
        }
    }

    /// <summary>Rethrows the first exception a shader thread threw, with its original stack trace.</summary>
    internal void ThrowIfFailed() => Volatile.Read(ref _failure)?.Throw();

    // ---- the threads of the group ------------------------------------------------------------

    /// <summary>
    /// Parks worker <paramref name="slot"/> until its next group starts. Returns false when the dispatch
    /// is over and the worker should exit.
    /// </summary>
    internal bool WaitForGroup(int slot, out Extent3 origin)
    {
        Slot self = _slots[slot];

        while (true)
        {
            self.Wait();

            if (_stopped)
            {
                origin = default;
                return false;
            }

            // Only the dispatching thread signals a worker between groups (a start, or Stop above);
            // the check keeps the loop honest if that ever changes.
            if (Interlocked.Exchange(ref self.Start, 0) == 1)
            {
                // Both written by the dispatching thread before it signalled this slot.
                origin = _origin;
                self.Generation = _generation;
                return true;
            }
        }
    }

    /// <summary>
    /// The barrier: returns when every thread of the group that is still running the shader has called it.
    /// </summary>
    /// <exception cref="CpuThreadgroupAbortedException">Another thread of the group threw; this one unwinds.</exception>
    internal void Barrier(int slot)
    {
        Slot self = _slots[slot];
        long generation = self.Generation;

        // Announce the wait, then look for an abort; Fail sets the abort flag, then looks for
        // announcements. Both writes are interlocked operations, which are full fences: neither
        // thread's read can overtake its own write. So at least one of the two reads comes after the
        // other thread's write: either this thread sees the flag and unwinds without parking, or
        // Fail's scan (which starts after the flag is set) finds this announcement and signals this
        // slot, and the wait below returns to find the flag. (A volatile write is not enough for
        // this: on arm64 it does not stop a later read from being satisfied first, and then both
        // sides can miss each other.)
        Interlocked.Exchange(ref self.Waiting, generation);

        if (IsAborted)
        {
            Withdraw(self, generation);
            throw new CpuThreadgroupAbortedException();
        }

        while (true)
        {
            long state = Volatile.Read(ref _state);
            int live = (int)(state >> 32);
            int waiting = (int)state;

            if (waiting + 1 == live)
            {
                // The last to arrive: the barrier is complete. Claim it by clearing the count (only one
                // thread can), then wake the others. None of them can reach the next barrier call
                // before it is woken here, so the wake-ups of two barriers never mix.
                if (Interlocked.CompareExchange(ref _state, (long)live << 32, state) == state)
                {
                    Withdraw(self, generation);
                    WakeWaiting(generation);
                    return;
                }
            }
            else if (Interlocked.CompareExchange(ref _state, state + 1, state) == state)
            {
                // Parked until a scan takes the announcement: the last arrival's, that of a departure
                // which left only parked threads, or Fail's.
                self.Wait();

                if (IsAborted)
                {
                    throw new CpuThreadgroupAbortedException();
                }

                return;
            }
        }
    }

    /// <summary>
    /// Takes back an announcement this thread will not wait for after all. If a scan (Fail's) took it
    /// first, the signal it sends is consumed here, so that it cannot end a later wait early.
    /// </summary>
    private static void Withdraw(Slot self, long generation)
    {
        if (Interlocked.Exchange(ref self.Waiting, 0) != generation)
        {
            self.Wait();
        }
    }

    /// <summary>
    /// Called by worker <paramref name="slot"/> when its thread is done with the group (it returned,
    /// threw or was stopped). If that leaves only parked threads, their barrier is complete; if it
    /// leaves none, the group is.
    /// </summary>
    internal void Leave(int slot)
    {
        long generation = _slots[slot].Generation;

        while (true)
        {
            long state = Volatile.Read(ref _state);
            int live = (int)(state >> 32) - 1;
            int waiting = (int)state;

            if (live > 0 && waiting == live)
            {
                if (Interlocked.CompareExchange(ref _state, (long)live << 32, state) == state)
                {
                    // From here on this thread no longer counts as running, so the group can finish
                    // and the next one start while this scan is under way: it is the scan that the
                    // generation number is for.
                    WakeWaiting(generation);
                    return;
                }
            }
            else if (Interlocked.CompareExchange(ref _state, ((long)live << 32) | (uint)waiting, state) == state)
            {
                if (live == 0)
                {
                    lock (_idle)
                    {
                        _groupDone = true;
                        Monitor.Pulse(_idle);
                    }
                }

                return;
            }
        }
    }

    /// <summary>
    /// Called by worker <paramref name="slot"/> when its thread threw: records the first exception and
    /// stops the group. Parked threads wake up and unwind, running ones unwind at their next barrier.
    /// </summary>
    internal void Fail(int slot, Exception exception)
    {
        Interlocked.CompareExchange(ref _failure, ExceptionDispatchInfo.Capture(exception), null);

        // Interlocked, and before the scan: see Barrier.
        Interlocked.Exchange(ref _aborted, 1);
        WakeWaiting(_slots[slot].Generation);
    }

    /// <summary>
    /// Wakes every thread that announced a wait in group <paramref name="generation"/>. Each
    /// announcement is answered once, whichever scan gets to it first; announcements of another
    /// group are not this scan's to answer. (Internal rather than private for the test that runs a
    /// scan of a finished group against the next one.)
    /// </summary>
    internal void WakeWaiting(long generation)
    {
        foreach (Slot slot in _slots)
        {
            if (Interlocked.CompareExchange(ref slot.Waiting, 0, generation) == generation)
            {
                slot.Signal();
            }
        }
    }

    /// <summary>
    /// One worker's parking spot: a binary signal that is not lost when it is sent before the wait. A
    /// monitor of its own rather than a semaphore or an event: those spin before they block, and with
    /// hundreds of threads parking at once the spinning only takes cores from the threads still running.
    /// </summary>
    private sealed class Slot
    {
        /// <summary>
        /// The number of the group in which the worker is (about to be) parked at the barrier and has
        /// not been woken for it; 0 otherwise. Group numbers start at 1.
        /// </summary>
        internal long Waiting;

        /// <summary>1 when the dispatching thread has started a group for the worker that it has not picked up yet.</summary>
        internal int Start;

        /// <summary>The number of the group the worker is running. Only the worker itself reads and writes it.</summary>
        internal long Generation;

        private readonly object _gate = new();
        private bool _signaled;

        internal void Signal()
        {
            lock (_gate)
            {
                _signaled = true;
                Monitor.Pulse(_gate);
            }
        }

        internal void Wait()
        {
            lock (_gate)
            {
                while (!_signaled)
                {
                    Monitor.Wait(_gate);
                }

                _signaled = false;
            }
        }
    }
}

/// <summary>
/// Unwinds a shader thread that was parked at (or arrives at) a barrier after another thread of its
/// threadgroup threw. Shader code cannot catch it (the generator rejects <c>try</c>), and the worker
/// that runs the thread swallows it: the dispatch rethrows the original exception instead.
/// </summary>
internal sealed class CpuThreadgroupAbortedException : Exception;

/// <summary>
/// The process-wide lock of shader type <typeparamref name="T"/> for CPU dispatches that use its
/// threadgroup memory: the <see cref="GroupSharedAttribute"/> arrays are static fields of
/// <typeparamref name="T"/>, so only one dispatch at a time may use them, whatever device it runs on.
/// </summary>
internal static class ThreadgroupGate<T>
{
    internal static readonly object Lock = new();
}
