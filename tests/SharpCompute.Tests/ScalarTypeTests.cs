using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// The 8-, 16- and 64-bit integer types in shaders (byte, sbyte, short, ushort, long, ulong): every
/// shader runs on the GPU and on the CPU (<c>ForCpu</c> of the same struct) over edge values and
/// random ones, and the two must agree element for element. Where plain C# has an obvious answer the
/// CPU side is checked against it too, so a mistake shared by both would still show.
/// </summary>
public class ScalarTypeTests
{
    private const int Threads = 4096;

    private static readonly long[] WideEdges =
    [
        0, 1, -1, 2, -2, 7, -7, 127, 128, -128, -129, 255, 256, 32767, 32768, -32768, -32769, 65535, 65536,
        int.MaxValue, int.MinValue, (long)int.MaxValue + 1, (long)int.MinValue - 1, uint.MaxValue, (long)uint.MaxValue + 1,
        long.MaxValue, long.MinValue, long.MaxValue - 1, long.MinValue + 1, 0x0123456789ABCDEF, unchecked((long)0xFEDCBA9876543210),
        0x00FF00FF00FF00FF, unchecked((long)0xAAAAAAAAAAAAAAAA), 0x5555555555555555, 1L << 53, (1L << 53) + 1, (1L << 24) + 1,
        0x7FFFFFBFFFFFFFFF, 0x7FFFFFC000000000, 1L << 62, 1L << 32, 1L << 31, 1L << 16, 1L << 8,
    ];

    /// <summary>Edge values, every single bit and run of ones, then random 64-bit patterns of every magnitude.</summary>
    private static long[] WideValues(int count, int seed)
    {
        Random random = new(seed);
        var values = new List<long>(WideEdges);

        for (int bit = 0; bit < 64; bit++)
        {
            values.Add(1L << bit);
            values.Add(-1L << bit);
            values.Add((long)(ulong.MaxValue >> bit));
        }

        while (values.Count < count)
        {
            // Half full-width patterns, half shifted down so small magnitudes are covered as well.
            long value = random.NextInt64(long.MinValue, long.MaxValue);
            values.Add(values.Count % 2 == 0 ? value : value >> random.Next(1, 64));
        }

        return [.. values.Take(count)];
    }

    private static T[] Narrow<T>(long[] values, Func<long, T> convert) => [.. values.Select(convert)];

    private static void AssertSame<T>(T[] cpu, T[] gpu, int stride, string what)
        where T : unmanaged, IEquatable<T>
    {
        Assert.Equal(cpu.Length, gpu.Length);

        string[] mismatches = Enumerable.Range(0, cpu.Length)
            .Where(i => !cpu[i].Equals(gpu[i]))
            .Select(i => $"thread {i / stride}, output {i % stride}: CPU {cpu[i]}, GPU {gpu[i]}")
            .ToArray();

        Assert.True(mismatches.Length == 0, $"{what}: {mismatches.Length} outputs differ:\n" + string.Join("\n", mismatches.Take(40)));
    }

    // ---- arithmetic -------------------------------------------------------------------------------

    [MetalFact]
    public void SmallIntegerArithmetic_PromotesAndWraps_GpuVsCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        long[] values = WideValues(Threads, seed: 11);
        using ReadOnlyBuffer<byte> bytes = device.AllocateReadOnlyBuffer(Narrow(values, static v => unchecked((byte)v)));
        using ReadOnlyBuffer<sbyte> sbytes = device.AllocateReadOnlyBuffer(Narrow(values, static v => unchecked((sbyte)(v >> 3))));
        using ReadOnlyBuffer<short> shorts = device.AllocateReadOnlyBuffer(Narrow(values, static v => unchecked((short)(v >> 5))));
        using ReadOnlyBuffer<ushort> ushorts = device.AllocateReadOnlyBuffer(Narrow(values, static v => unchecked((ushort)(v >> 7))));
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(Threads * SmallIntegerOps.Stride);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(Threads * SmallIntegerOps.Stride);

        device.For(Threads, new SmallIntegerOps(bytes, sbytes, shorts, ushorts, gpu));
        device.ForCpu(Threads, new SmallIntegerOps(bytes, sbytes, shorts, ushorts, cpu));

        int[] expected = cpu.ToArray();
        AssertSame(expected, gpu.ToArray(), SmallIntegerOps.Stride, "small integer arithmetic");

        // The CPU side is plain C#; restate a few outputs to pin what "promotes and wraps" means.
        byte[] b = bytes.ToArray();
        short[] s = shorts.ToArray();

        for (int i = 0; i < Threads; i++)
        {
            int j = ((i * 7) + 3) % Threads;
            ReadOnlySpan<int> o = expected.AsSpan(i * SmallIntegerOps.Stride, SmallIntegerOps.Stride);

            Assert.Equal(b[i] + b[j], o[0]);                    // int: up to 510, no wrap
            Assert.Equal((b[i] + b[j]) & 0xFF, o[24]);          // cast back: wraps at 8 bits
            Assert.Equal(b[i] << (i % 40 % 32), o[18]);         // the shift count is masked with 31
            Assert.Equal(unchecked((short)(s[i] + s[j])), o[26]);
            Assert.Equal(unchecked((byte)(b[i] + 2)), o[41]);   // ++ twice, wrapping at 255
        }
    }

    [MetalFact]
    public void WideIntegerArithmetic_WrapsShiftsAndConverts_GpuVsCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        long[] values = WideValues(Threads, seed: 12);
        using ReadOnlyBuffer<long> longs = device.AllocateReadOnlyBuffer(values);
        using ReadOnlyBuffer<ulong> ulongs = device.AllocateReadOnlyBuffer(Narrow(WideValues(Threads, seed: 13), static v => unchecked((ulong)v)));
        using ReadWriteBuffer<long> gpu = device.AllocateReadWriteBuffer<long>(Threads * WideIntegerOps.Stride);
        using ReadWriteBuffer<long> cpu = device.AllocateReadWriteBuffer<long>(Threads * WideIntegerOps.Stride);

        device.For(Threads, new WideIntegerOps(longs, ulongs, gpu));
        device.ForCpu(Threads, new WideIntegerOps(longs, ulongs, cpu));

        long[] expected = cpu.ToArray();
        AssertSame(expected, gpu.ToArray(), WideIntegerOps.Stride, "64-bit arithmetic");

        for (int i = 0; i < Threads; i++)
        {
            int j = ((i * 5) + 1) % Threads;
            int count = (i % 140) - 5;
            long a = values[i], b = values[j];
            ReadOnlySpan<long> o = expected.AsSpan(i * WideIntegerOps.Stride, WideIntegerOps.Stride);

            Assert.Equal(unchecked(a + b), o[0]);
            Assert.Equal(unchecked(a * b), o[2]);
            Assert.Equal(a << (count & 63), o[10]);              // the shift count is masked with 63
            Assert.Equal(a >> (count & 63), o[11]);
            Assert.Equal((long)((ulong)a >> (count & 63)), o[12]);
            Assert.Equal((long)(int)a, o[28]);                   // truncation, then sign extension
            Assert.Equal((long)(uint)a, o[29]);
            Assert.Equal((long)unchecked((int)a * (int)a), o[50]); // C#'s wrapped int product, via uint
        }
    }

    [MetalFact]
    public void DivisionByZero_DoesNotTrapOnTheGpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Count = 256;
        long[] divisors = [.. Enumerable.Range(0, Count).Select(static i => i % 4 == 0 ? 0L : i)];
        using ReadOnlyBuffer<long> input = device.AllocateReadOnlyBuffer(divisors);
        using ReadWriteBuffer<long> output = device.AllocateReadWriteBuffer<long>(Count * DivideByMaybeZero.Stride);

        // C# throws DivideByZeroException here; the GPU has no exceptions and the dispatch completes.
        device.For(Count, new DivideByMaybeZero(input, output));

        long[] results = output.ToArray();

        for (int i = 0; i < Count; i++)
        {
            long d = divisors[i];

            if (d == 0)
            {
                continue; // whatever the GPU computed for x / 0 is not specified, and not checked
            }

            long x = 1000003L * (i + 1);
            ReadOnlySpan<long> o = results.AsSpan(i * DivideByMaybeZero.Stride, DivideByMaybeZero.Stride);

            Assert.Equal(x / d, o[0]);
            Assert.Equal(x % d, o[1]);
            Assert.Equal((long)((ulong)x / (ulong)d), o[2]);
            Assert.Equal((byte)x / (byte)d, o[3]);
            Assert.Equal((short)x % (short)d, o[4]);
            Assert.Equal((ushort)x / (ushort)d, o[5]);
        }
    }

    // ---- conversions ------------------------------------------------------------------------------

    private static readonly float[] FloatEdges =
    [
        0f, -0f, 0.5f, -0.5f, 0.9f, -0.9f, 1f, -1f, 1.5f, -1.5f, 126.9f, 127f, 127.5f, 128f, -128f, -128.5f, -129f, 254.9f, 255f, 255.9f, 256f, 300f,
        32767f, 32767.5f, 32768f, -32768f, -32768.5f, -32769f, 65535f, 65535.5f, 65536f, 16777216f, 16777218f,
        2147483520f, 2147483648f, -2147483648f, -2147483904f, 4294967040f, 4294967296f,
        9223371487098961920f, 9223372036854775808f, 9223373136366403584f, -9223372036854775808f, -9223373136366403584f,
        18446742974197923840f, 18446744073709551616f, 1e30f, -1e30f, float.MaxValue, float.MinValue, float.Epsilon,
        float.PositiveInfinity, float.NegativeInfinity, float.NaN, -float.NaN,
    ];

    [MetalFact]
    public void FloatAndIntegerConversions_SaturateAsInDotNet_GpuVsCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Count = 2048;
        Random random = new(21);
        var inputs = new List<float>(FloatEdges);

        while (inputs.Count < Count)
        {
            // Every magnitude from fractions to beyond 2^64, both signs.
            float magnitude = MathF.Pow(2f, (float)((random.NextDouble() * 72) - 4));
            inputs.Add(random.Next(2) == 0 ? magnitude : -magnitude);
        }

        float[] floats = [.. inputs];
        long[] longs = WideValues(Count, seed: 22);
        using ReadOnlyBuffer<float> floatInput = device.AllocateReadOnlyBuffer(floats);
        using ReadOnlyBuffer<long> longInput = device.AllocateReadOnlyBuffer(longs);
        using ReadWriteBuffer<long> gpuIntegers = device.AllocateReadWriteBuffer<long>(Count * FloatIntegerConversions.IntegerStride);
        using ReadWriteBuffer<long> cpuIntegers = device.AllocateReadWriteBuffer<long>(Count * FloatIntegerConversions.IntegerStride);
        using ReadWriteBuffer<float> gpuReals = device.AllocateReadWriteBuffer<float>(Count * FloatIntegerConversions.RealStride);
        using ReadWriteBuffer<float> cpuReals = device.AllocateReadWriteBuffer<float>(Count * FloatIntegerConversions.RealStride);

        device.For(Count, new FloatIntegerConversions(floatInput, longInput, gpuIntegers, gpuReals));
        device.ForCpu(Count, new FloatIntegerConversions(floatInput, longInput, cpuIntegers, cpuReals));

        long[] expected = cpuIntegers.ToArray(), actual = gpuIntegers.ToArray();
        string[] mismatches = Enumerable.Range(0, expected.Length)
            .Where(i => expected[i] != actual[i])
            .Select(i => $"float {floats[i / FloatIntegerConversions.IntegerStride]:R}, output {i % FloatIntegerConversions.IntegerStride}: CPU {expected[i]}, GPU {actual[i]}")
            .ToArray();

        Assert.True(mismatches.Length == 0, $"{mismatches.Length} float to integer conversions differ:\n" + string.Join("\n", mismatches.Take(40)));

        // Integer to float rounds to nearest even on both; compare the bits.
        AssertSame(
            [.. cpuReals.ToArray().Select(BitConverter.SingleToUInt32Bits)],
            [.. gpuReals.ToArray().Select(BitConverter.SingleToUInt32Bits)],
            FloatIntegerConversions.RealStride,
            "integer to float conversions");

        // The saturation the two agree on, restated: the bounds of the target type, and 0 for NaN.
        long At(float value, int output) => expected[(Array.IndexOf(floats, value) * FloatIntegerConversions.IntegerStride) + output];

        Assert.Equal(long.MaxValue, At(1e30f, 0));
        Assert.Equal(long.MinValue, At(-1e30f, 0));
        Assert.Equal(long.MaxValue, At(9223372036854775808f, 0)); // 2^63 itself is one past the largest long
        Assert.Equal(long.MinValue, At(-9223372036854775808f, 0));
        Assert.Equal(long.MaxValue, At(float.PositiveInfinity, 0));
        Assert.Equal(0, At(float.NaN, 0));
        Assert.Equal(-1, At(1e30f, 1));                            // ulong.MaxValue
        Assert.Equal(0, At(-1f, 1));
        Assert.Equal(long.MinValue, At(9223372036854775808f, 1)); // 2^63 fits a ulong
        Assert.Equal(255, At(300f, 2));
        Assert.Equal(0, At(-1f, 2));
        Assert.Equal(127, At(300f, 3));
        Assert.Equal(-128, At(-129f, 3));
        Assert.Equal(32767, At(65536f, 4));
        Assert.Equal(65535, At(65536f, 5));
        Assert.Equal(0, At(float.NaN, 5));
    }

    // ---- intrinsics -------------------------------------------------------------------------------

    [MetalFact]
    public void IntegerIntrinsics_AreExactForEveryType_GpuVsCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        long[] values = WideValues(Threads, seed: 31);
        Random random = new(32);

        // Rotate counts: 0, the widths, one either side of them, negative ones, and int's extremes.
        int[] edgeCounts = [0, 1, 7, 8, 9, 15, 16, 17, 31, 32, 33, 63, 64, 65, -1, -7, -8, -63, -64, -65, int.MaxValue, int.MinValue];
        int[] counts = [.. Enumerable.Range(0, Threads).Select(i => i % 3 == 0 ? edgeCounts[i / 3 % edgeCounts.Length] : random.Next(-200, 200))];

        using ReadOnlyBuffer<long> input = device.AllocateReadOnlyBuffer(values);
        using ReadOnlyBuffer<int> countInput = device.AllocateReadOnlyBuffer(counts);
        using ReadWriteBuffer<long> gpu = device.AllocateReadWriteBuffer<long>(Threads * IntegerIntrinsics.Stride);
        using ReadWriteBuffer<long> cpu = device.AllocateReadWriteBuffer<long>(Threads * IntegerIntrinsics.Stride);

        device.For(Threads, new IntegerIntrinsics(input, countInput, gpu));
        device.ForCpu(Threads, new IntegerIntrinsics(input, countInput, cpu));

        long[] expected = cpu.ToArray();
        AssertSame(expected, gpu.ToArray(), IntegerIntrinsics.Stride, "integer intrinsics");

        for (int i = 0; i < Threads; i++)
        {
            long x = values[i];
            int n = counts[i];
            ReadOnlySpan<long> o = expected.AsSpan(i * IntegerIntrinsics.Stride, IntegerIntrinsics.Stride);

            // Counts are those of the type's own width: a byte has at most 8 leading zeros.
            Assert.Equal(byte.LeadingZeroCount(unchecked((byte)x)), o[5]);
            Assert.InRange(o[5], 0, 8);
            Assert.InRange(o[26], 0, 16);
            Assert.Equal(long.PopCount(x), o[44]);
            Assert.Equal(ulong.RotateLeft((ulong)x, n), (ulong)o[58]);
            Assert.Equal(byte.RotateRight(unchecked((byte)x), n), o[9]);
            Assert.Equal(-128, o[85]);              // abs wraps at the minimum, as in MSL
            Assert.Equal(long.MinValue, o[86]);
            Assert.Equal(short.MinValue, o[87]);    // bit 0 becomes bit 15
        }
    }

    // ---- layout: constants, struct fields, threadgroup memory -------------------------------------

    [MetalFact]
    public void Constants_OfEveryNewType_ArriveIntact_GpuVsCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Count = 64;
        using ReadWriteBuffer<long> gpu = device.AllocateReadWriteBuffer<long>(Count * MixedConstants.Stride);
        using ReadWriteBuffer<long> cpu = device.AllocateReadWriteBuffer<long>(Count * MixedConstants.Stride);

        // Distinct bit patterns per field, so a field read at a wrong offset cannot go unnoticed.
        const byte Tag = 0xA5;
        const long Key = unchecked((long)0x8877665544332211);
        const short Delta = -12345;
        const ulong Hash = 0xFEDCBA9876543210;
        const sbyte Sign = -77;
        const ushort Counter = 54321;
        const int Offset = -1234567890;
        const long Last = 0x0102030405060708;
        const float Scale = 123456.75f;

        device.For(Count, new MixedConstants(Tag, Key, Delta, Hash, Sign, Counter, Offset, Last, Scale, gpu));
        device.ForCpu(Count, new MixedConstants(Tag, Key, Delta, Hash, Sign, Counter, Offset, Last, Scale, cpu));

        long[] expected = cpu.ToArray();
        AssertSame(expected, gpu.ToArray(), MixedConstants.Stride, "mixed constants");

        Assert.Equal([Tag, Key, Delta, unchecked((long)Hash), Sign, Counter, Offset, Last, 123456], expected.AsSpan(0, 9).ToArray());
        Assert.Equal(Count * MixedConstants.Stride, expected[11]);
    }

    [MetalFact]
    public void Constants_OfSmallTypesOnly_KeepTheSequentialLayout_GpuVsCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Count = 16;
        using ReadWriteBuffer<int> gpu = device.AllocateReadWriteBuffer<int>(Count * SmallConstants.Stride);
        using ReadWriteBuffer<int> cpu = device.AllocateReadWriteBuffer<int>(Count * SmallConstants.Stride);

        device.For(Count, new SmallConstants(0xC3, -987654321, -100, -30000, 0x5A, 60000, 77.9f, gpu));
        device.ForCpu(Count, new SmallConstants(0xC3, -987654321, -100, -30000, 0x5A, 60000, 77.9f, cpu));

        int[] expected = cpu.ToArray();
        AssertSame(expected, gpu.ToArray(), SmallConstants.Stride, "small constants");
        Assert.Equal([0xC3, -987654321, -100, -30000, 0x5A, 60000, 77, 0xC3 - 100 - 30000 + 0x5A + 60000], expected.AsSpan(0, SmallConstants.Stride).ToArray());
    }

    [Fact]
    public void MixedRecord_HasTheLayoutMslGivesIt()
    {
        // Natural alignment on both sides: 64-bit fields at multiples of 8, 40 bytes in all.
        Assert.Equal(40, Unsafe.SizeOf<MixedRecord>());

        var record = default(MixedRecord);
        ref byte start = ref Unsafe.As<MixedRecord, byte>(ref record);

        Assert.Equal(8, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<long, byte>(ref record.Key)));
        Assert.Equal(16, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<short, byte>(ref record.Delta)));
        Assert.Equal(24, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<ulong, byte>(ref record.Hash)));
        Assert.Equal(32, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<sbyte, byte>(ref record.Sign)));
        Assert.Equal(34, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<ushort, byte>(ref record.Count)));
    }

    [MetalFact]
    public void StructFields_OfEveryNewType_InABufferAndAsAConstant_GpuVsCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Count = 1000;
        long[] values = WideValues(Count * 2, seed: 41);
        MixedRecord[] records =
        [
            .. Enumerable.Range(0, Count).Select(i => new MixedRecord
            {
                Tag = unchecked((byte)values[i]),
                Key = values[i],
                Delta = unchecked((short)(values[i] >> 9)),
                Hash = unchecked((ulong)values[Count + i]),
                Sign = unchecked((sbyte)(values[i] >> 20)),
                Count = unchecked((ushort)(values[i] >> 30)),
            }),
        ];
        var bias = new MixedRecord { Tag = 200, Key = unchecked((long)0x9E3779B97F4A7C15), Delta = -300, Hash = 0xD1B54A32D192ED03, Sign = -5, Count = 9 };

        using ReadWriteBuffer<MixedRecord> gpu = device.AllocateReadWriteBuffer(records);
        using ReadWriteBuffer<MixedRecord> cpu = device.AllocateReadWriteBuffer(records);

        device.For(Count, new RecordUpdate(gpu, bias));
        device.ForCpu(Count, new RecordUpdate(cpu, bias));

        MixedRecord[] expected = cpu.ToArray(), actual = gpu.ToArray();

        for (int i = 0; i < Count; i++)
        {
            Assert.True(
                expected[i].Tag == actual[i].Tag && expected[i].Key == actual[i].Key && expected[i].Delta == actual[i].Delta &&
                expected[i].Hash == actual[i].Hash && expected[i].Sign == actual[i].Sign && expected[i].Count == actual[i].Count,
                $"record {i}: CPU ({expected[i].Tag}, {expected[i].Key}, {expected[i].Delta}, {expected[i].Hash}, {expected[i].Sign}, {expected[i].Count}), " +
                $"GPU ({actual[i].Tag}, {actual[i].Key}, {actual[i].Delta}, {actual[i].Hash}, {actual[i].Sign}, {actual[i].Count})");
        }

        // And against plain C# for one field of each width.
        byte tag = unchecked((byte)(records[3].Tag + bias.Tag));
        Assert.Equal(tag, expected[3].Tag);
        Assert.Equal(unchecked((records[3].Key * 31) + bias.Key + tag), expected[3].Key);
        Assert.Equal(unchecked((ushort)(records[3].Count + 1 + 3)), expected[3].Count);
    }

    [MetalFact]
    public void GroupSharedArrays_OfEveryNewType_GpuVsCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Count = SharedSmallAndWide.GroupSize * 32;
        long[] values = WideValues(Count, seed: 51);
        using ReadOnlyBuffer<long> input = device.AllocateReadOnlyBuffer(values);
        using ReadWriteBuffer<long> gpu = device.AllocateReadWriteBuffer<long>(Count * SharedSmallAndWide.Stride);
        using ReadWriteBuffer<long> cpu = device.AllocateReadWriteBuffer<long>(Count * SharedSmallAndWide.Stride);

        device.For(Count, new SharedSmallAndWide(input, gpu));
        device.ForCpu(Count, new SharedSmallAndWide(input, cpu));

        long[] expected = cpu.ToArray();
        AssertSame(expected, gpu.ToArray(), SharedSmallAndWide.Stride, "group-shared arrays");

        for (int i = 0; i < Count; i++)
        {
            // Each thread reads what the thread opposite it in its group stored.
            int group = i / SharedSmallAndWide.GroupSize * SharedSmallAndWide.GroupSize;
            long other = values[group + (SharedSmallAndWide.GroupSize - 1 - (i - group))];
            ReadOnlySpan<long> o = expected.AsSpan(i * SharedSmallAndWide.Stride, SharedSmallAndWide.Stride);

            Assert.Equal(unchecked((byte)other), o[0]);
            Assert.Equal(unchecked((sbyte)(other >> 8)), o[1]);
            Assert.Equal(unchecked((short)(other >> 16)), o[2]);
            Assert.Equal(unchecked((ushort)(other >> 32)), o[3]);
            Assert.Equal(other, o[4]);
            Assert.Equal(~other, o[5]);
        }
    }

    [MetalFact]
    public void LocalArrays_OfTheNewTypes_GpuVsCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Count = 5000;
        Random random = new(55);
        var data = new byte[Count + LocalArraysOfIntegers.Window];
        random.NextBytes(data);

        using ReadOnlyBuffer<byte> input = device.AllocateReadOnlyBuffer(data);
        using ReadWriteBuffer<long> gpu = device.AllocateReadWriteBuffer<long>(Count * LocalArraysOfIntegers.Stride);
        using ReadWriteBuffer<long> cpu = device.AllocateReadWriteBuffer<long>(Count * LocalArraysOfIntegers.Stride);

        device.For(Count, new LocalArraysOfIntegers(input, gpu));
        device.ForCpu(Count, new LocalArraysOfIntegers(input, cpu));

        long[] expected = cpu.ToArray();
        AssertSame(expected, gpu.ToArray(), LocalArraysOfIntegers.Stride, "local arrays of integers");

        // The hash is FNV-1a over the window with one finishing step, restated here.
        for (int i = 0; i < Count; i += 97)
        {
            ulong hash = 0xCBF29CE484222325;

            for (int k = 0; k < LocalArraysOfIntegers.Window; k++)
            {
                hash = (hash ^ data[i + k]) * 0x100000001B3;
            }

            Assert.Equal((long)((hash ^ (hash >> 32)) * 0xD6E8FEB86659FD93), expected[(i * LocalArraysOfIntegers.Stride) + 4]);
        }
    }

    [MetalFact]
    public void SharedHelpers_GenericHelpers_AndStructMembers_OnTheNewTypes_GpuVsCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Count = 3000;
        long[] values = WideValues(Count, seed: 81);
        byte[] bytes = Narrow(WideValues(Count, seed: 82), static v => unchecked((byte)(v >> 11)));
        Tally[] start = [.. Enumerable.Range(0, Count).Select(i => new Tally(unchecked((ulong)values[i]) >> 3, unchecked((short)(values[i] >> 20))))];
        const byte Step = 37;

        using ReadOnlyBuffer<long> valueInput = device.AllocateReadOnlyBuffer(values);
        using ReadOnlyBuffer<byte> byteInput = device.AllocateReadOnlyBuffer(bytes);
        using ReadWriteBuffer<Tally> gpuTallies = device.AllocateReadWriteBuffer(start);
        using ReadWriteBuffer<Tally> cpuTallies = device.AllocateReadWriteBuffer(start);
        using ReadWriteBuffer<long> gpu = device.AllocateReadWriteBuffer<long>(Count * SharedIntegerCode.Stride);
        using ReadWriteBuffer<long> cpu = device.AllocateReadWriteBuffer<long>(Count * SharedIntegerCode.Stride);

        device.For(Count, new SharedIntegerCode(valueInput, byteInput, gpuTallies, gpu, Step));
        device.ForCpu(Count, new SharedIntegerCode(valueInput, byteInput, cpuTallies, cpu, Step));

        long[] expected = cpu.ToArray();
        AssertSame(expected, gpu.ToArray(), SharedIntegerCode.Stride, "shared integer code");

        // The mutating method ran on the buffer element itself, on both.
        Tally[] expectedTallies = cpuTallies.ToArray(), actualTallies = gpuTallies.ToArray();

        for (int i = 0; i < Count; i++)
        {
            Assert.True(
                expectedTallies[i].Total == actualTallies[i].Total && expectedTallies[i].Delta == actualTallies[i].Delta && expectedTallies[i].Count == actualTallies[i].Count,
                $"tally {i}: CPU ({expectedTallies[i].Total}, {expectedTallies[i].Delta}, {expectedTallies[i].Count}), GPU ({actualTallies[i].Total}, {actualTallies[i].Delta}, {actualTallies[i].Count})");
            Assert.Equal(start[i].Total + Step, expectedTallies[i].Total);
            Assert.Equal(2, expectedTallies[i].Count);
        }

        // Restated without the helpers for one thread.
        const int T = 5;
        ulong hash = ((unchecked((ulong)values[T]) ^ bytes[T]) * IntegerHelpers.Prime ^ (byte)(unchecked((ushort)values[T]) >> 8)) * IntegerHelpers.Prime;

        Assert.Equal(unchecked((long)hash), expected[T * SharedIntegerCode.Stride]);
        Assert.Equal(values[T + 1] - 1, expected[(T * SharedIntegerCode.Stride) + 2]);   // swapped
        Assert.Equal(bytes[T + 1], expected[(T * SharedIntegerCode.Stride) + 3]);
        Assert.Equal(Math.Max(values[T], values[T + 1]), expected[(T * SharedIntegerCode.Stride) + 4]);
    }

    // ---- buffers ----------------------------------------------------------------------------------

    [MetalFact]
    public void Buffers_OfEveryNewType_AreWrittenElementByElement_GpuVsCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();

        // An odd length: a buffer of bytes need not be a multiple of 4 bytes long.
        const int Count = 1237;
        long[] values = WideValues(Count, seed: 61);
        using ReadOnlyBuffer<long> input = device.AllocateReadOnlyBuffer(values);

        (byte[], sbyte[], short[], ushort[], long[], ulong[]) Run(bool onGpu)
        {
            using ReadWriteBuffer<byte> bytes = device.AllocateReadWriteBuffer<byte>(Count);
            using ReadWriteBuffer<sbyte> sbytes = device.AllocateReadWriteBuffer<sbyte>(Count);
            using ReadWriteBuffer<short> shorts = device.AllocateReadWriteBuffer<short>(Count);
            using ReadWriteBuffer<ushort> ushorts = device.AllocateReadWriteBuffer<ushort>(Count);
            using ReadWriteBuffer<long> longs = device.AllocateReadWriteBuffer<long>(Count);
            using ReadWriteBuffer<ulong> ulongs = device.AllocateReadWriteBuffer<ulong>(Count);
            var shader = new StoreEveryType(input, bytes, sbytes, shorts, ushorts, longs, ulongs);

            if (onGpu)
            {
                device.For(Count, shader);
            }
            else
            {
                device.ForCpu(Count, shader);
            }

            return (bytes.ToArray(), sbytes.ToArray(), shorts.ToArray(), ushorts.ToArray(), longs.ToArray(), ulongs.ToArray());
        }

        var gpu = Run(onGpu: true);
        var cpu = Run(onGpu: false);

        Assert.Equal(cpu.Item1, gpu.Item1);
        Assert.Equal(cpu.Item2, gpu.Item2);
        Assert.Equal(cpu.Item3, gpu.Item3);
        Assert.Equal(cpu.Item4, gpu.Item4);
        Assert.Equal(cpu.Item5, gpu.Item5);
        Assert.Equal(cpu.Item6, gpu.Item6);

        Assert.Equal(Narrow(values, static v => unchecked((byte)v)), gpu.Item1);
        Assert.Equal(Narrow(values, static v => unchecked((ushort)(v >> 32))), gpu.Item4);
        Assert.Equal(Narrow(values, static v => unchecked((ulong)v << 1)), gpu.Item6);
    }

    [MetalTheory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4097)]
    [InlineData(1 << 16)]
    public void ByteImage_IsProcessedInPlace_AtAnyLength(int length)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Random random = new(length);
        var pixels = new byte[length];
        random.NextBytes(pixels);

        using ReadWriteBuffer<byte> gpu = device.AllocateReadWriteBuffer(pixels);
        using ReadWriteBuffer<byte> cpu = device.AllocateReadWriteBuffer(pixels);

        device.For(length, new InvertBytes(gpu, 24));
        device.ForCpu(length, new InvertBytes(cpu, 24));

        byte[] expected = [.. pixels.Select(static p => (byte)Math.Min((255 - p) * 24 / 16, 255))];

        Assert.Equal(expected, cpu.ToArray());
        Assert.Equal(expected, gpu.ToArray());
    }

    [MetalFact]
    public void ByteBufferSlice_AtAnOddOffset_IsProcessedInPlace()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        byte[] pixels = [.. Enumerable.Range(0, 301).Select(static i => (byte)i)];
        using ReadWriteBuffer<byte> buffer = device.AllocateReadWriteBuffer(pixels);
        using ReadWriteBuffer<byte> view = buffer.Slice(7, 111);

        // A view of bytes starts at any byte: the element's alignment in MSL is 1.
        device.For(view.Length, new InvertBytes(view, 16));

        byte[] expected = [.. pixels.Select(static (p, i) => i is >= 7 and < 118 ? (byte)(255 - p) : p)];
        Assert.Equal(expected, buffer.ToArray());
    }

    [MetalFact]
    public void ShortSamples_AreScaledInPlace_GpuVsCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Count = 9999;
        Random random = new(71);
        short[] samples = [.. Enumerable.Range(0, Count).Select(i => i < 4 ? new[] { short.MinValue, short.MaxValue, (short)0, (short)-1 }[i] : (short)random.Next(short.MinValue, short.MaxValue + 1))];

        using ReadWriteBuffer<short> gpu = device.AllocateReadWriteBuffer(samples);
        using ReadWriteBuffer<short> cpu = device.AllocateReadWriteBuffer(samples);

        device.For(Count, new ScaleSamples(gpu, 700));
        device.ForCpu(Count, new ScaleSamples(cpu, 700));

        short[] expected = [.. samples.Select(static s => (short)Math.Clamp(s * 700 / 256, short.MinValue, short.MaxValue))];

        Assert.Equal(expected, cpu.ToArray());
        Assert.Equal(expected, gpu.ToArray());
    }

    // ---- atomics ----------------------------------------------------------------------------------

    [MetalFact]
    public void AtomicMinAndMax_OnUlongElements_UnderContention_GpuVsCpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        const int Count = 1 << 20;

        // Distinct 64-bit keys whose high and low halves both matter: a 32-bit atomic would get it wrong.
        var keys = new ulong[Count];

        for (int i = 0; i < Count; i++)
        {
            ulong z = ((ulong)i + 1) * 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            keys[i] = z ^ (z >> 27);
        }

        using ReadOnlyBuffer<ulong> input = device.AllocateReadOnlyBuffer(keys);
        using ReadWriteBuffer<ulong> gpu = device.AllocateReadWriteBuffer([ulong.MaxValue, 0UL]);
        using ReadWriteBuffer<ulong> cpu = device.AllocateReadWriteBuffer([ulong.MaxValue, 0UL]);

        device.For(Count, new AtomicExtremes(input, gpu));
        device.ForCpu(Count, new AtomicExtremes(input, cpu));

        ulong[] expected = [keys.Min(), keys.Max()];

        Assert.Equal(expected, cpu.ToArray());
        Assert.Equal(expected, gpu.ToArray());
    }

    [Fact]
    public void AtomicMinAndMax_OnUlong_OnTheCpu_KeepTheExtremeAndIgnoreTheRest()
    {
        ulong location = 100;

        Gpu.AtomicMin(ref location, 200);
        Assert.Equal(100UL, location);
        Gpu.AtomicMin(ref location, 100);
        Assert.Equal(100UL, location);
        Gpu.AtomicMin(ref location, 0x1_0000_0000UL - 1);
        Assert.Equal(100UL, location);
        Gpu.AtomicMin(ref location, 7);
        Assert.Equal(7UL, location);

        Gpu.AtomicMax(ref location, 3);
        Assert.Equal(7UL, location);
        Gpu.AtomicMax(ref location, 0x1_0000_0000UL);
        Assert.Equal(0x1_0000_0000UL, location);
        Gpu.AtomicMax(ref location, ulong.MaxValue);
        Assert.Equal(ulong.MaxValue, location);
    }
}
