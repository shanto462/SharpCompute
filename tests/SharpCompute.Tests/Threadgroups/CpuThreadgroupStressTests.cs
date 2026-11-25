using System;
using System.Linq;
using System.Threading;
using static SharpCompute.Tests.Threadgroups.CpuGroupExecutionTests;

namespace SharpCompute.Tests.Threadgroups;

/// <summary>
/// The barrier of the CPU's group executor (<see cref="CpuThreadgroup"/>) under load: many short
/// groups whose barriers are completed by departures as often as by arrivals, threads that throw at
/// every point, and a wake-up scan that outlives its group. Each runs under a watchdog, so a lost
/// wake-up fails the test instead of hanging the run.
/// </summary>
public class CpuThreadgroupStressTests
{
    private const int GroupSize = 8;

    private static void Run<TKernel>(TKernel kernel, int groups)
        where TKernel : struct, ICpuKernel =>
        CpuGroupDispatcher.Run(
            kernel, groups * GroupSize, 1, 1, new Extent3(GroupSize, 1, 1), default, new Extent3(groups * GroupSize, 1, 1), new object(), typeof(CpuThreadgroupStressTests));

    [Fact]
    public void ManyShortGroups_WhoseBarriersAreCompletedByDepartures_NeverLetAThreadThroughEarly()
    {
        const int groups = 4000, runs = 3;

        WithinTimeout(
            () =>
            {
                for (int run = 0; run < runs; run++)
                {
                    DepartureKernel kernel = new(new int[GroupSize], new int[GroupSize], new int[2]);

                    Run(kernel, groups);

                    Assert.Equal(0, kernel.Counters[DepartureKernel.Errors]);

                    // The even threads below the last one go all the way: 0, 2, 4 and 6 of every group.
                    Assert.Equal(groups * 4, kernel.Counters[DepartureKernel.Completed]);
                }
            },
            seconds: 120);
    }

    /// <summary>
    /// Three kinds of thread per group. The last one stamps late and returns without calling a barrier:
    /// by then the others are usually parked, so its departure is what completes their first barrier.
    /// The odd ones return after the first barrier, which completes the second for the even ones. A
    /// thread that is let through a barrier early finds a stamp of the previous group and counts an error.
    /// </summary>
    private readonly struct DepartureKernel(int[] first, int[] second, int[] counters) : ICpuKernel
    {
        public const int Errors = 0;
        public const int Completed = 1;

        public int[] Counters => counters;

        public void Invoke(CpuThread thread)
        {
            int local = thread.GroupX;
            int stamp = thread.ThreadGroupX + 1;

            if (local == GroupSize - 1)
            {
                Thread.Yield();
                Volatile.Write(ref first[local], stamp);
                return;
            }

            Volatile.Write(ref first[local], stamp);
            thread.Barrier();

            for (int j = 0; j < GroupSize; j++)
            {
                if (Volatile.Read(ref first[j]) != stamp)
                {
                    Interlocked.Increment(ref counters[Errors]);
                }
            }

            if ((local & 1) == 1)
            {
                return;
            }

            Volatile.Write(ref second[local], stamp);
            thread.Barrier();

            for (int j = 0; j < GroupSize - 1; j += 2)
            {
                if (Volatile.Read(ref second[j]) != stamp)
                {
                    Interlocked.Increment(ref counters[Errors]);
                }
            }

            Interlocked.Increment(ref counters[Completed]);
        }
    }

    [Fact]
    public void ManyBarriersPerGroup_StayInStep_OverManyGroups()
    {
        // Every thread calls every barrier: 1,500 groups x 8 threads x 12 barriers, each round checked.
        const int groups = 1500, rounds = 12;
        int[] tile = new int[GroupSize];
        int[] errors = new int[1];

        WithinTimeout(() => Run(new RoundsKernel(tile, errors, rounds), groups), seconds: 120);

        Assert.Equal(0, errors[0]);
    }

    /// <summary>Every round each thread writes the round's stamp, and after the barrier checks that every other thread has.</summary>
    private readonly struct RoundsKernel(int[] tile, int[] errors, int rounds) : ICpuKernel
    {
        public void Invoke(CpuThread thread)
        {
            int local = thread.GroupX;

            for (int round = 0; round < rounds; round++)
            {
                int stamp = (thread.ThreadGroupX * rounds) + round + 1;

                Volatile.Write(ref tile[local], stamp);
                thread.Barrier();

                for (int j = 0; j < GroupSize; j++)
                {
                    if (Volatile.Read(ref tile[j]) != stamp)
                    {
                        Interlocked.Increment(ref errors[0]);
                    }
                }

                thread.Barrier();
            }
        }
    }

    [Fact]
    public void AThreadThatThrows_AtAnyPointOfAnyGroup_StopsTheDispatch_EveryTime()
    {
        const int groups = 5, iterations = 360;

        WithinTimeout(
            () =>
            {
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    int failing = iteration % (groups * GroupSize);
                    int phase = iteration / (groups * GroupSize) % 3;
                    int[] finished = new int[groups];

                    InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Run(new ThrowingKernel(finished, failing, phase), groups));
                    Assert.Equal($"thread {failing}, phase {phase}", exception.Message);

                    // Groups run in order: the ones before the failing one completed, the ones after never started.
                    int failingGroup = failing / GroupSize;

                    for (int group = 0; group < groups; group++)
                    {
                        if (group < failingGroup)
                        {
                            Assert.Equal(GroupSize, finished[group]);
                        }
                        else if (group > failingGroup)
                        {
                            Assert.Equal(0, finished[group]);
                        }
                        else if (phase < 2)
                        {
                            // The failing thread never reaches the second barrier, so nobody gets past it.
                            Assert.Equal(0, finished[group]);
                        }
                        else
                        {
                            Assert.InRange(finished[group], 0, GroupSize - 1);
                        }
                    }
                }
            },
            seconds: 120);
    }

    /// <summary>Two barriers; one thread throws before the first, between the two, or after the second.</summary>
    private readonly struct ThrowingKernel(int[] finished, int failing, int phase) : ICpuKernel
    {
        public void Invoke(CpuThread thread)
        {
            Check(thread, 0);
            thread.Barrier();
            Check(thread, 1);
            thread.Barrier();
            Check(thread, 2);
            Interlocked.Increment(ref finished[thread.ThreadGroupX]);
        }

        private void Check(CpuThread thread, int at)
        {
            if (thread.X == failing && phase == at)
            {
                throw new InvalidOperationException($"thread {failing}, phase {phase}");
            }
        }
    }

    [Fact]
    public void AScanOfAFinishedGroup_DoesNotWakeTheNextGroupsThreads()
    {
        // A departing thread scans for parked threads after it has stopped counting as running, so its
        // scan can still be under way when the next group is at its first barrier. Here that late scan
        // is made by hand: group 1 has finished, group 2 has one thread parked and one held back.
        CpuThreadgroup group = new(2);
        Extent3 pair = new(2, 1, 1);
        using ManualResetEventSlim release = new();
        int running = 0, passed = 0;

        Thread[] workers = Enumerable.Range(0, 2).Select(slot => new Thread(() =>
        {
            while (group.WaitForGroup(slot, out _))
            {
                try
                {
                    if (Volatile.Read(ref running) == 2 && slot == 1)
                    {
                        release.Wait();
                    }

                    group.Barrier(slot);

                    if (Volatile.Read(ref running) == 2)
                    {
                        Interlocked.Increment(ref passed);
                    }
                }
                finally
                {
                    group.Leave(slot);
                }
            }
        }) { IsBackground = true }).ToArray();

        foreach (Thread worker in workers)
        {
            worker.Start();
        }

        try
        {
            Volatile.Write(ref running, 1);
            WithinTimeout(() => group.RunGroup(default, pair, pair), seconds: 30);

            Volatile.Write(ref running, 2);
            Thread second = new(() => group.RunGroup(new Extent3(2, 0, 0), pair, pair)) { IsBackground = true };
            second.Start();

            // Thread 0 parks at the barrier of group 2 (generation 2); thread 1 has not arrived.
            Thread.Sleep(200);
            group.WakeWaiting(generation: 1);
            Thread.Sleep(200);

            Assert.Equal(0, Volatile.Read(ref passed));
            Assert.True(second.IsAlive, "group 2 finished although one of its threads never reached the barrier");

            // The barrier completes the regular way, and only then.
            release.Set();
            Assert.True(second.Join(TimeSpan.FromSeconds(30)), "group 2 did not finish");
            Assert.Equal(2, Volatile.Read(ref passed));
        }
        finally
        {
            release.Set();
            group.Stop(2);

            foreach (Thread worker in workers)
            {
                Assert.True(worker.Join(TimeSpan.FromSeconds(30)), "a worker did not stop");
            }
        }
    }

    [Fact]
    public void AScanOfTheRunningGroup_WakesItsParkedThread()
    {
        // The counterpart: the same scan with the running group's number does wake the thread (this is
        // what a departure or a failure does), which shows that the test above could fail.
        CpuThreadgroup group = new(2);
        Extent3 pair = new(2, 1, 1);
        using ManualResetEventSlim release = new();
        int passed = 0;

        Thread[] workers = Enumerable.Range(0, 2).Select(slot => new Thread(() =>
        {
            while (group.WaitForGroup(slot, out _))
            {
                try
                {
                    if (slot == 1)
                    {
                        release.Wait();
                    }
                    else
                    {
                        group.Barrier(slot);
                        Interlocked.Increment(ref passed);
                    }
                }
                finally
                {
                    group.Leave(slot);
                }
            }
        }) { IsBackground = true }).ToArray();

        foreach (Thread worker in workers)
        {
            worker.Start();
        }

        try
        {
            Thread first = new(() => group.RunGroup(default, pair, pair)) { IsBackground = true };
            first.Start();

            Thread.Sleep(200);
            Assert.Equal(0, Volatile.Read(ref passed));
            group.WakeWaiting(generation: 1);

            SpinWait.SpinUntil(() => Volatile.Read(ref passed) == 1, TimeSpan.FromSeconds(30));
            Assert.Equal(1, Volatile.Read(ref passed));

            release.Set();
            Assert.True(first.Join(TimeSpan.FromSeconds(30)), "the group did not finish");
        }
        finally
        {
            release.Set();
            group.Stop(2);

            foreach (Thread worker in workers)
            {
                Assert.True(worker.Join(TimeSpan.FromSeconds(30)), "a worker did not stop");
            }
        }
    }
}
