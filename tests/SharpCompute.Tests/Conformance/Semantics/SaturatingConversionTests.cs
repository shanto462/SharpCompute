using System;
using System.Linq;

namespace SharpCompute.Tests.Conformance.Semantics;

/// <summary>
/// A float or a half converts to an integer on the GPU exactly as in C#: it truncates, NaN gives 0
/// and a value beyond the range of the target gives its MinValue or MaxValue. Every shader runs on
/// the GPU and with <c>ForCpu</c>; the families over values read from a buffer are also compared
/// with the same casts evaluated here. MSL leaves these conversions undefined out of range and the
/// Metal compiler folds on that ground, so <see cref="SaturatingFolds"/> converts what the compiler
/// can see or bound: constants behind a select, the infinities of a half, and integers that went
/// through a float.
/// </summary>
[Trait("Category", "Conformance")]
public class SaturatingConversionTests
{
    /// <summary>NaNs, infinities, zeros, and for every target type the values just inside and just outside its range.</summary>
    private static readonly float[] Floats =
    [
        .. new uint[]
        {
            0x00000000, 0x80000000, 0x00000001, 0x80000001, // ±0, the smallest subnormals
            0x7F800000, 0xFF800000,                         // ±∞
            0x7FC00000, 0xFFC00000, 0x7FA00001,             // NaN, -NaN, a NaN with a payload
            0x7F7FFFFF, 0xFF7FFFFF,                         // ±float.MaxValue
            0x3F7FFFFF, 0xBF7FFFFF,                         // the floats just inside ±1
        }.Select(BitConverter.UInt32BitsToSingle),
        0.5f, -0.5f, 1f, -1f, 1.5f, -1.5f, 2.5f, -2.5f,
        126.5f, 127f, 127.5f, 128f, -127.5f, -128f, -128.5f, -129f,                 // sbyte
        254.5f, 255f, 255.5f, 256f,                                                 // byte
        32766.5f, 32767f, 32767.5f, 32768f, -32767.5f, -32768f, -32768.5f, -32769f, // short
        65534.5f, 65535f, 65535.5f, 65536f,                                         // ushort
        16777216f, 2147483520f, 2147483648f, 2147483904f, -2147483520f, -2147483648f, -2147483904f, // int: 2^31 - 128, 2^31, 2^31 + 256
        4294967040f, 4294967296f, 4294967808f,                                      // uint: 2^32 - 256, 2^32, 2^32 + 512
        9223371487098961920f, 9223372036854775808f, 9223373136366403584f,           // long: 2^63 - 2^39, 2^63, 2^63 + 2^40
        -9223371487098961920f, -9223372036854775808f, -9223373136366403584f,
        18446742974197923840f, 18446744073709551616f, 18446746272732807168f,        // ulong: 2^64 - 2^40, 2^64, 2^64 + 2^41
        1e10f, -1e10f, 1e20f, -1e20f, 1e30f, -1e30f,
    ];

    /// <summary>The halves, as bits: zeros, infinities, NaNs, ±65504 and the bounds of the 8-bit types (the wider ones hold every finite half).</summary>
    private static readonly Half[] Halves =
    [
        .. new ushort[]
        {
            0x0000, 0x8000, 0x0001, 0x8001, // ±0, the smallest subnormals
            0x7C00, 0xFC00,                 // ±∞
            0x7E00, 0xFE00, 0x7D01,         // NaN, -NaN, a NaN with a payload
            0x7BFF, 0xFBFF,                 // ±65504
            0x3BFF, 0xBBFF, 0x3800, 0xB800, // just inside ±1, ±0.5
        }.Select(BitConverter.UInt16BitsToHalf),
        (Half)1.5f, (Half)(-1.5f), (Half)2.5f, (Half)(-2.5f),
        (Half)126.5f, (Half)127f, (Half)127.5f, (Half)128f, (Half)(-127.5f), (Half)(-128f), (Half)(-128.5f), (Half)(-129f),
        (Half)254.5f, (Half)255f, (Half)255.5f, (Half)256f,
        (Half)32752f, (Half)32768f, (Half)(-32768f), (Half)(-32784f), (Half)65472f,
    ];

    [MetalFact]
    public void Float_ConvertsToEveryIntegerTypeAsInCSharp()
    {
        long[] expected = [.. Floats.SelectMany(static x => new long[] { (byte)x, (sbyte)x, (short)x, (ushort)x, (int)x, (uint)x, (long)x, unchecked((long)(ulong)x) })];

        AssertIdentical(expected, Floats, SaturatingFloat.Slots, static (d, cpu, i, o) => Dispatch(d, cpu, i.Length, new SaturatingFloat(i, o)), static x => Describe(x));

        // The bounds themselves: (float)int.MaxValue is 2^31, which is out of range.
        Assert.Equal(int.MaxValue, expected[(Array.IndexOf(Floats, 2147483648f) * SaturatingFloat.Slots) + 4]);
        Assert.Equal(2147483520, expected[(Array.IndexOf(Floats, 2147483520f) * SaturatingFloat.Slots) + 4]);
        Assert.Equal(uint.MaxValue, expected[(Array.IndexOf(Floats, 4294967296f) * SaturatingFloat.Slots) + 5]);
        Assert.Equal(long.MaxValue, expected[(Array.IndexOf(Floats, 9223372036854775808f) * SaturatingFloat.Slots) + 6]);
        Assert.Equal(-1, expected[(Array.IndexOf(Floats, 18446744073709551616f) * SaturatingFloat.Slots) + 7]);
    }

    [MetalFact]
    public void Half_ConvertsToEveryIntegerTypeAsInCSharp()
    {
        long[] expected = [.. Halves.SelectMany(static x => new long[] { (byte)x, (sbyte)x, (short)x, (ushort)x, (int)x, (uint)x, (long)x, unchecked((long)(ulong)x) })];

        AssertIdentical(expected, Halves, SaturatingHalf.Slots, static (d, cpu, i, o) => Dispatch(d, cpu, i.Length, new SaturatingHalf(i, o)), static x => Describe((float)x));
    }

    [MetalFact]
    public void FloatVectors_ConvertToIntAndUIntVectorsAsInCSharp()
    {
        // Thread i converts the lanes i, i + 1, i + 2, i + 3 (wrapping): every value in every lane.
        long[] expected =
        [
            .. Enumerable.Range(0, Floats.Length).SelectMany(i =>
            {
                float x = Floats[i], y = Floats[(i + 1) % Floats.Length], z = Floats[(i + 2) % Floats.Length], w = Floats[(i + 3) % Floats.Length];
                return new long[]
                {
                    (int)x, (int)y, (int)z, (int)w, (uint)x, (uint)y, (uint)z, (uint)w,
                    (int)x, (int)y, (int)z, (uint)x, (uint)y, (uint)z,
                    (int)x, (int)y, (uint)x, (uint)y,
                };
            }),
        ];

        AssertIdentical(expected, Floats, SaturatingFloatVectors.Slots, static (d, cpu, i, o) => Dispatch(d, cpu, i.Length, new SaturatingFloatVectors(i, o, i.Length)), static x => Describe(x));
    }

    [MetalFact]
    public void HalfVectors_ConvertToIntAndUIntVectorsAsInCSharp()
    {
        long[] expected =
        [
            .. Enumerable.Range(0, Halves.Length).SelectMany(i =>
            {
                Half x = Halves[i], y = Halves[(i + 1) % Halves.Length], z = Halves[(i + 2) % Halves.Length], w = Halves[(i + 3) % Halves.Length];
                return new long[]
                {
                    (int)x, (int)y, (int)z, (int)w, (uint)x, (uint)y, (uint)z, (uint)w,
                    (int)x, (int)y, (int)z, (uint)x, (uint)y, (uint)z,
                    (int)x, (int)y, (uint)x, (uint)y,
                };
            }),
        ];

        AssertIdentical(expected, Halves, SaturatingHalfVectors.Slots, static (d, cpu, i, o) => Dispatch(d, cpu, i.Length, new SaturatingHalfVectors(i, o, i.Length)), static x => Describe((float)x));
    }

    /// <summary>
    /// What the Metal compiler folded before the conversions clamped: with the plain MSL casts slot 0
    /// was 0, slot 21 was 0 and slot 22 was negated in 32 bits.
    /// </summary>
    [MetalFact]
    public void ValuesTheCompilerCanSeeOrBound_SaturateOnTheGpuAsWithForCpu()
    {
        ulong[] input =
        [
            0, 0xFC00, 0x7C00, uint.MaxValue, unchecked((ulong)long.MinValue), unchecked((ulong)-5L), ulong.MaxValue, 70000, unchecked((ulong)-70000L),
            BitConverter.SingleToUInt32Bits(3e9f),
        ];

        long[] gpu = Run(input, SaturatingFolds.Slots, static (d, i, o) => d.For(1, new SaturatingFolds(i, o)));
        long[] cpu = Run(input, SaturatingFolds.Slots, static (d, i, o) => d.ForCpu(1, new SaturatingFolds(i, o)));

        Assert.Equal(cpu, gpu);
        Assert.Equal(
            new long[]
            {
                int.MaxValue, 0, 255, 0, -128, 127, 0, short.MinValue, ushort.MaxValue, 0, uint.MaxValue, 0, long.MaxValue, long.MinValue, -1, 0, // 0..15
                0, int.MaxValue, int.MinValue, uint.MaxValue, 0, // 16..20
                1, 2147483648, 0, long.MinValue, 255, -128, 1, // 21..27
                int.MaxValue, int.MinValue, 0, long.MaxValue, 65535, -128, 255, // 28..34
                1, 1, 0, // 35..37
                int.MaxValue, int.MinValue, 1000000000, uint.MaxValue, // 38..41
            },
            cpu);
    }

    /// <summary>
    /// With FastMath the compiler may assume that there is no NaN and no infinity, so those two are
    /// unspecified. A finite value still saturates: the clamps do not depend on either.
    /// </summary>
    [MetalFact]
    public void FiniteValues_SaturateWithFastMathToo()
    {
        float[] finite = [.. Floats.Where(float.IsFinite)];
        long[] expected = [.. finite.SelectMany(static x => new long[] { (byte)x, (sbyte)x, (short)x, (ushort)x, (int)x, (uint)x, (long)x, unchecked((long)(ulong)x) })];

        AssertIdentical(expected, finite, SaturatingFloatFast.Slots, static (d, cpu, i, o) => Dispatch(d, cpu, i.Length, new SaturatingFloatFast(i, o)), static x => Describe(x));
    }

    private static string Describe(float x) => $"{x:R} (0x{BitConverter.SingleToUInt32Bits(x):X8})";

    private static void Dispatch<TShader>(GraphicsDevice device, bool cpu, int threads, in TShader shader)
        where TShader : struct, IComputeShader, IComputeShaderDescriptor<TShader>
    {
        if (cpu)
        {
            device.ForCpu(threads, shader);
        }
        else
        {
            device.For(threads, shader);
        }
    }

    private static void AssertIdentical<T>(long[] expected, T[] input, int slots, Action<GraphicsDevice, bool, ReadOnlyBuffer<T>, ReadWriteBuffer<long>> dispatch, Func<T, string> describe)
        where T : unmanaged
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<T> inputs = device.AllocateReadOnlyBuffer(input);
        using ReadWriteBuffer<long> gpuOutput = device.AllocateReadWriteBuffer<long>(input.Length * slots);
        using ReadWriteBuffer<long> cpuOutput = device.AllocateReadWriteBuffer<long>(input.Length * slots);
        dispatch(device, false, inputs, gpuOutput);
        dispatch(device, true, inputs, cpuOutput);
        long[] gpu = gpuOutput.ToArray();
        long[] cpu = cpuOutput.ToArray();

        Assert.Equal(input.Length * slots, expected.Length);
        string[] mismatches = Enumerable.Range(0, expected.Length)
            .Where(k => expected[k] != cpu[k] || expected[k] != gpu[k])
            .Select(k => $"{describe(input[k / slots])} slot {k % slots}: C# {expected[k]}, CPU {cpu[k]}, GPU {gpu[k]}")
            .ToArray();

        Assert.True(mismatches.Length == 0, $"{mismatches.Length} of {expected.Length} values differ:\n" + string.Join("\n", mismatches.Take(40)));
    }

    private static long[] Run(ulong[] input, int slots, Action<GraphicsDevice, ReadOnlyBuffer<ulong>, ReadWriteBuffer<long>> dispatch)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<ulong> inputs = device.AllocateReadOnlyBuffer(input);
        using ReadWriteBuffer<long> output = device.AllocateReadWriteBuffer<long>(slots);
        dispatch(device, inputs, output);
        return output.ToArray();
    }
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct SaturatingFloat(ReadOnlyBuffer<float> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 8;

    public void Execute()
    {
        int o = ThreadIds.X * Slots;
        float x = input[ThreadIds.X];
        output[o + 0] = (byte)x;
        output[o + 1] = (sbyte)x;
        output[o + 2] = (short)x;
        output[o + 3] = (ushort)x;
        output[o + 4] = (int)x;
        output[o + 5] = (uint)x;
        output[o + 6] = (long)x;
        output[o + 7] = (long)(ulong)x;
    }
}

[GeneratedComputeShaderDescriptor]
[ShaderOptions(FastMath = true)]
public readonly partial struct SaturatingFloatFast(ReadOnlyBuffer<float> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 8;

    public void Execute()
    {
        int o = ThreadIds.X * Slots;
        float x = input[ThreadIds.X];
        output[o + 0] = (byte)x;
        output[o + 1] = (sbyte)x;
        output[o + 2] = (short)x;
        output[o + 3] = (ushort)x;
        output[o + 4] = (int)x;
        output[o + 5] = (uint)x;
        output[o + 6] = (long)x;
        output[o + 7] = (long)(ulong)x;
    }
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct SaturatingHalf(ReadOnlyBuffer<Half> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 8;

    public void Execute()
    {
        int o = ThreadIds.X * Slots;
        Half x = input[ThreadIds.X];
        output[o + 0] = (byte)x;
        output[o + 1] = (sbyte)x;
        output[o + 2] = (short)x;
        output[o + 3] = (ushort)x;
        output[o + 4] = (int)x;
        output[o + 5] = (uint)x;
        output[o + 6] = (long)x;
        output[o + 7] = (long)(ulong)x;
    }
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct SaturatingFloatVectors(ReadOnlyBuffer<float> input, ReadWriteBuffer<long> output, int count) : IComputeShader
{
    public const int Slots = 18;

    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * Slots;
        Float4 v = new(input[i], input[(i + 1) % count], input[(i + 2) % count], input[(i + 3) % count]);
        Int4 i4 = (Int4)v;
        UInt4 u4 = (UInt4)v;
        Int3 i3 = (Int3)v.XYZ;
        UInt3 u3 = (UInt3)v.XYZ;
        Int2 i2 = (Int2)v.XY;
        UInt2 u2 = (UInt2)v.XY;
        output[o + 0] = i4.X;
        output[o + 1] = i4.Y;
        output[o + 2] = i4.Z;
        output[o + 3] = i4.W;
        output[o + 4] = u4.X;
        output[o + 5] = u4.Y;
        output[o + 6] = u4.Z;
        output[o + 7] = u4.W;
        output[o + 8] = i3.X;
        output[o + 9] = i3.Y;
        output[o + 10] = i3.Z;
        output[o + 11] = u3.X;
        output[o + 12] = u3.Y;
        output[o + 13] = u3.Z;
        output[o + 14] = i2.X;
        output[o + 15] = i2.Y;
        output[o + 16] = u2.X;
        output[o + 17] = u2.Y;
    }
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct SaturatingHalfVectors(ReadOnlyBuffer<Half> input, ReadWriteBuffer<long> output, int count) : IComputeShader
{
    public const int Slots = 18;

    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * Slots;
        Half4 v = new(input[i], input[(i + 1) % count], input[(i + 2) % count], input[(i + 3) % count]);
        Int4 i4 = (Int4)v;
        UInt4 u4 = (UInt4)v;
        Int3 i3 = (Int3)v.XYZ;
        UInt3 u3 = (UInt3)v.XYZ;
        Int2 i2 = (Int2)v.XY;
        UInt2 u2 = (UInt2)v.XY;
        output[o + 0] = i4.X;
        output[o + 1] = i4.Y;
        output[o + 2] = i4.Z;
        output[o + 3] = i4.W;
        output[o + 4] = u4.X;
        output[o + 5] = u4.Y;
        output[o + 6] = u4.Z;
        output[o + 7] = u4.W;
        output[o + 8] = i3.X;
        output[o + 9] = i3.Y;
        output[o + 10] = i3.Z;
        output[o + 11] = u3.X;
        output[o + 12] = u3.Y;
        output[o + 13] = u3.Z;
        output[o + 14] = i2.X;
        output[o + 15] = i2.Y;
        output[o + 16] = u2.X;
        output[o + 17] = u2.Y;
    }
}

/// <summary>
/// Conversions of values the Metal compiler can see or bound. <c>input[0]</c> is 0, so <c>b</c> is
/// false, but only at run time; the halves are -∞ and +∞; the integers are read from the buffer and
/// go through a float or a half that cannot hold them.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SaturatingFolds(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 42;

    private static readonly float Big = 1e10f;
    private static readonly Half Small = (Half)(-300f);
    private static readonly int BigInt = (int)Big;
    private static readonly int BigNegated = (int)-Big;
    private static readonly int Scaled = (int)(Big * 0.1f);
    private static readonly uint SmallUInt = (uint)Small + (uint)Big;

    public void Execute()
    {
        bool b = input[0] != 0;
        Half negative = Gpu.AsHalf((ushort)input[1]);
        Half positive = Gpu.AsHalf((ushort)input[2]);
        uint u = (uint)input[3];
        long l = (long)input[4];
        long minusFive = (long)input[5];
        ulong ul = input[6];
        int i70000 = (int)input[7];
        int minus70000 = (int)input[8];
        float f3e9 = Gpu.AsFloat((uint)input[9]);

        // A constant behind a select, for every target type.
        output[0] = (int)(b ? float.NaN : 1e10f);
        output[1] = (int)(b ? 1e10f : float.NaN);
        output[2] = (byte)(b ? float.NaN : 1e10f);
        output[3] = (byte)(b ? 300f : float.NegativeInfinity);
        output[4] = (sbyte)(b ? float.NaN : -1e10f);
        output[5] = (sbyte)(b ? float.NaN : float.PositiveInfinity);
        output[6] = (short)(b ? 1e10f : float.NaN);
        output[7] = (short)(b ? float.NaN : float.NegativeInfinity);
        output[8] = (ushort)(b ? float.NaN : 65536f);
        output[9] = (ushort)(b ? 65536f : -1f);
        output[10] = (uint)(b ? float.NaN : 4294967296f);
        output[11] = (uint)(b ? 4294967296f : float.NaN);
        output[12] = (long)(b ? float.NaN : 1e30f);
        output[13] = (long)(b ? float.NaN : float.NegativeInfinity);
        output[14] = (long)(ulong)(b ? float.NaN : 1e30f);
        output[15] = (long)(ulong)(b ? 1e30f : -1e30f);

        // Vectors of constants behind a select.
        Int3 vi = (Int3)(b ? new Float3(1f, 2f, 3f) : new Float3(float.NaN, 1e10f, -1e10f));
        UInt2 vu = (UInt2)(b ? new Float2(1f, 2f) : new Float2(float.PositiveInfinity, float.NegativeInfinity));
        output[16] = vi.X;
        output[17] = vi.Y;
        output[18] = vi.Z;
        output[19] = vu.X;
        output[20] = vu.Y;

        // A half is finite within ±65504, and the compiler narrowed what it computed from the conversion.
        int t = (int)negative;
        output[21] = t == int.MinValue ? 1 : 0;
        output[22] = -(long)(int)negative;
        output[23] = (uint)negative;
        output[24] = (long)negative;
        output[25] = (byte)positive;
        output[26] = (sbyte)negative;
        output[27] = (int)positive > 65504 ? 1 : 0;

        // Integers through a float or a half that cannot hold them.
        output[28] = (int)(float)u;
        output[29] = (int)(float)l;
        output[30] = (uint)(float)minusFive;
        output[31] = (long)(float)ul;
        output[32] = (ushort)(Half)i70000;
        output[33] = (sbyte)(Half)minus70000;
        output[34] = (byte)(float)i70000;

        // Comparisons of a converted value with the bound it saturates to.
        int big = (int)f3e9;
        output[35] = big == int.MaxValue ? 1 : 0;
        output[36] = (uint)f3e9 > 2999999000u ? 1 : 0;
        output[37] = (int)(f3e9 * 0f);

        // Program-scope initializers, which the compiler evaluates.
        output[38] = BigInt;
        output[39] = BigNegated;
        output[40] = Scaled;
        output[41] = SmallUInt;
    }
}
