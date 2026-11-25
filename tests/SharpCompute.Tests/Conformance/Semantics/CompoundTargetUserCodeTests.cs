using System;
using System.Linq;

namespace SharpCompute.Tests.Conformance.Semantics;

/// <summary>A key whose indexer, conversion and operator are user code that can sit in a buffer index.</summary>
public struct TargetKey
{
    public int V;

    public TargetKey(int v)
    {
        V = v;
    }

    public readonly int this[int i] => V + i;

    public static implicit operator int(TargetKey key) => key.V;

    public static TargetKey operator +(TargetKey a, int b)
    {
        a.V += b;
        return a;
    }
}

/// <summary>
/// <c>+=</c>, <c>++</c> and <c>*=</c> on <c>int</c>, <c>ushort</c> and <c>long</c> elements whose index
/// runs user code that is not a plain call: a getter with a body, a getter that calls a getter, a user
/// indexer, a user conversion and a user operator. Every index reads <see cref="Next"/>, which counts
/// its evaluations in <c>ticks[0]</c>, so a target evaluated twice moves the count and the element.
/// The elements start at the wrap edge of their type. One thread runs it.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct CompoundUserTargetShader(ReadWriteBuffer<int> ints, ReadWriteBuffer<ushort> shorts, ReadWriteBuffer<long> longs, ReadWriteBuffer<int> ticks) : IComputeShader
{
    public const int Length = 128;

    /// <summary>A getter with a body: one more tick per evaluation, and the new count.</summary>
    private int Next
    {
        get
        {
            ticks[0] += 1;
            return ticks[0];
        }
    }

    /// <summary>A getter that calls a getter.</summary>
    private int Skip => Next + 1;

    public void Execute()
    {
        if (ThreadIds.X != 0)
        {
            return;
        }

        TargetKey near = new TargetKey(50);
        TargetKey far = new TargetKey(80);

        // int elements: ticks 1-15.
        ints[Next] += int.MaxValue;
        ints[Next]++;
        ints[Next] *= 3;
        ints[Skip] += int.MaxValue;
        ints[Skip]++;
        ints[Skip] *= 3;
        ints[near[Next]] += int.MaxValue;
        ints[near[Next]]++;
        ints[near[Next]] *= 3;
        ints[new TargetKey(Next)] += int.MaxValue;
        ints[new TargetKey(Next)]++;
        ints[new TargetKey(Next)] *= 3;
        ints[(far + Next).V] += int.MaxValue;
        ints[(far + Next).V]++;
        ints[(far + Next).V] *= 3;

        // ushort elements: ticks 16-30.
        shorts[Next] += 1000;
        shorts[Next]++;
        shorts[Next] *= 65535;
        shorts[Skip] += 1000;
        shorts[Skip]++;
        shorts[Skip] *= 65535;
        shorts[near[Next]] += 1000;
        shorts[near[Next]]++;
        shorts[near[Next]] *= 65535;
        shorts[new TargetKey(Next)] += 1000;
        shorts[new TargetKey(Next)]++;
        shorts[new TargetKey(Next)] *= 65535;
        shorts[(far + Next).V] += 1000;
        shorts[(far + Next).V]++;
        shorts[(far + Next).V] *= 65535;

        // long elements: ticks 31-45.
        longs[Next] += long.MaxValue;
        longs[Next]++;
        longs[Next] *= 3;
        longs[Skip] += long.MaxValue;
        longs[Skip]++;
        longs[Skip] *= 3;
        longs[near[Next]] += long.MaxValue;
        longs[near[Next]]++;
        longs[near[Next]] *= 3;
        longs[new TargetKey(Next)] += long.MaxValue;
        longs[new TargetKey(Next)]++;
        longs[new TargetKey(Next)] *= 3;
        longs[(far + Next).V] += long.MaxValue;
        longs[(far + Next).V]++;
        longs[(far + Next).V] *= 3;

        // The value of the assignment used, -= and --: ticks 46-51.
        int used = ints[Next] += int.MaxValue;
        ints[120] = used;
        int post = ints[Next]++;
        ints[121] = post;
        --ints[Next];
        ints[Skip] -= 9;
        long widePost = longs[Next]++;
        longs[120] = widePost;
        ushort smallUsed = shorts[Next] *= 65535;
        shorts[120] = smallUsed;

        // User code without side effects in the target.
        TargetKey pure = new TargetKey(100);
        ints[pure] += 2;
        ints[pure[1]] -= 1;
        ints[(pure + 2).V] *= 5;
        ints[(pure + 3).V]++;
    }
}

/// <summary>
/// A compound assignment whose index is a getter that writes the very buffer it indexes: the getter
/// runs once, before the element is read.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SelfIndexingGetterShader(ReadWriteBuffer<int> data) : IComputeShader
{
    private int Next
    {
        get
        {
            data[0] += 1;
            return data[0];
        }
    }

    public void Execute()
    {
        if (ThreadIds.X != 0)
        {
            return;
        }

        data[Next] += 5;
        data[Next]++;
        data[Next] *= 3;
        int q = 7;
        q *= Next;
        data[12] = q;
    }
}

/// <summary>
/// A compound assignment or increment evaluates its target once, also when the target's index is
/// user code. The GPU, <c>ForCpu</c> and the values worked out below, with every index written out,
/// must all agree.
/// </summary>
[Trait("Category", "Conformance")]
public class CompoundTargetUserCodeTests
{
    private const int Length = CompoundUserTargetShader.Length;

    private static int[] InitialInts() => Enumerable.Range(0, Length).Select(static k => int.MaxValue - (k % 4)).ToArray();

    private static ushort[] InitialShorts() => Enumerable.Range(0, Length).Select(static k => (ushort)(ushort.MaxValue - (k % 4))).ToArray();

    private static long[] InitialLongs() => Enumerable.Range(0, Length).Select(static k => long.MaxValue - (k % 4)).ToArray();

    [MetalFact]
    public void UserCodeInTheIndex_IsEvaluatedOnce()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        (int[] Ints, ushort[] Shorts, long[] Longs, int Ticks) gpu = Run(device, cpu: false);
        (int[] Ints, ushort[] Shorts, long[] Longs, int Ticks) cpu = Run(device, cpu: true);
        (int[] ints, ushort[] shorts, long[] longs) = Expected();

        Assert.Equal(51, cpu.Ticks);
        Assert.Equal(51, gpu.Ticks);
        AssertElements("int, ForCpu", ints, cpu.Ints);
        AssertElements("ushort, ForCpu", shorts, cpu.Shorts);
        AssertElements("long, ForCpu", longs, cpu.Longs);
        AssertElements("int, GPU", ints, gpu.Ints);
        AssertElements("ushort, GPU", shorts, gpu.Shorts);
        AssertElements("long, GPU", longs, gpu.Longs);
    }

    [MetalFact]
    public void AGetterThatWritesTheIndexedBuffer_RunsOnce()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] expected = [4, 15, 21, 90, 40, 50, 60, 70, 0, 0, 0, 0, 28, 0, 0, 0];

        foreach (bool cpu in new[] { true, false })
        {
            using ReadWriteBuffer<int> data = device.AllocateReadWriteBuffer<int>(new[] { 0, 10, 20, 30, 40, 50, 60, 70, 0, 0, 0, 0, 0, 0, 0, 0 });

            if (cpu)
            {
                device.ForCpu(1, new SelfIndexingGetterShader(data));
            }
            else
            {
                device.For(1, new SelfIndexingGetterShader(data));
            }

            AssertElements(cpu ? "ForCpu" : "GPU", expected, data.ToArray());
        }
    }

    private static (int[] Ints, ushort[] Shorts, long[] Longs, int Ticks) Run(GraphicsDevice device, bool cpu)
    {
        using ReadWriteBuffer<int> ints = device.AllocateReadWriteBuffer<int>(InitialInts());
        using ReadWriteBuffer<ushort> shorts = device.AllocateReadWriteBuffer<ushort>(InitialShorts());
        using ReadWriteBuffer<long> longs = device.AllocateReadWriteBuffer<long>(InitialLongs());
        using ReadWriteBuffer<int> ticks = device.AllocateReadWriteBuffer<int>(1);
        CompoundUserTargetShader shader = new(ints, shorts, longs, ticks);

        if (cpu)
        {
            device.ForCpu(1, shader);
        }
        else
        {
            device.For(1, shader);
        }

        return (ints.ToArray(), shorts.ToArray(), longs.ToArray(), ticks.ToArray()[0]);
    }

    /// <summary>
    /// The shader's statements with their indices worked out: tick t is the t-th evaluation of Next,
    /// so Next is t, Skip is t + 1, near[Next] is 50 + t, new TargetKey(Next) is t and (far + Next).V
    /// is 80 + t. Each block of three is +=, ++ and *=.
    /// </summary>
    private static (int[] Ints, ushort[] Shorts, long[] Longs) Expected()
    {
        int[] ints = InitialInts();
        ushort[] shorts = InitialShorts();
        long[] longs = InitialLongs();

        foreach (int[] block in new[] { new[] { 1, 2, 3 }, [5, 6, 7], [57, 58, 59], [10, 11, 12], [93, 94, 95] })
        {
            ints[block[0]] = unchecked(ints[block[0]] + int.MaxValue);
            ints[block[1]] = unchecked(ints[block[1]] + 1);
            ints[block[2]] = unchecked(ints[block[2]] * 3);
        }

        foreach (int[] block in new[] { new[] { 16, 17, 18 }, [20, 21, 22], [72, 73, 74], [25, 26, 27], [108, 109, 110] })
        {
            shorts[block[0]] = unchecked((ushort)(shorts[block[0]] + 1000));
            shorts[block[1]] = unchecked((ushort)(shorts[block[1]] + 1));
            shorts[block[2]] = unchecked((ushort)((uint)shorts[block[2]] * 65535u));
        }

        foreach (int[] block in new[] { new[] { 31, 32, 33 }, [35, 36, 37], [87, 88, 89], [40, 41, 42], [123, 124, 125] })
        {
            longs[block[0]] = unchecked(longs[block[0]] + long.MaxValue);
            longs[block[1]] = unchecked(longs[block[1]] + 1);
            longs[block[2]] = unchecked(longs[block[2]] * 3);
        }

        // Ticks 46-51.
        ints[46] = unchecked(ints[46] + int.MaxValue);
        ints[120] = ints[46];
        ints[121] = ints[47];
        ints[47] = unchecked(ints[47] + 1);
        ints[48] = unchecked(ints[48] - 1);
        ints[50] = unchecked(ints[50] - 9);
        longs[120] = longs[50];
        longs[50] = unchecked(longs[50] + 1);
        shorts[51] = unchecked((ushort)((uint)shorts[51] * 65535u));
        shorts[120] = shorts[51];

        // The key of 100, without side effects.
        ints[100] = unchecked(ints[100] + 2);
        ints[101] = unchecked(ints[101] - 1);
        ints[102] = unchecked(ints[102] * 5);
        ints[103] = unchecked(ints[103] + 1);

        return (ints, shorts, longs);
    }

    private static void AssertElements<T>(string name, T[] expected, T[] actual)
        where T : IEquatable<T>
    {
        Assert.Equal(expected.Length, actual.Length);
        int[] wrong = Enumerable.Range(0, expected.Length).Where(k => !expected[k].Equals(actual[k])).ToArray();

        Assert.True(
            wrong.Length == 0,
            $"{name}: {wrong.Length} elements differ from the expected values: " +
            string.Join(", ", wrong.Take(12).Select(k => $"[{k}] expected {expected[k]}, got {actual[k]}")));
    }
}
