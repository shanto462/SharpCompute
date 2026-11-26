using System;
using System.Linq;

namespace SharpCompute.Tests.Conformance.Semantics;

/// <summary>
/// A float converts to a half on the GPU as in C#: it rounds to the nearest half, ties to even, a
/// magnitude of 65520 or more is ±∞, and NaN stays NaN. An Apple GPU does that when it converts, but
/// where the Metal compiler converts a constant itself, one beyond the range of a half became 65504,
/// and it moves the conversion to the constants of a max, a min or a select to do so.
/// <see cref="HalfConversionFolds"/> holds those shapes; the families over values read from a buffer
/// are compared with the same casts evaluated here.
/// </summary>
[Trait("Category", "Conformance")]
public class HalfConversionTests
{
    private const long PositiveInfinity = 0x7C00;
    private const long NegativeInfinity = 0xFC00;

    /// <summary>Zeros, NaNs, infinities, ties, the half subnormals and the values around ±65520, where a half overflows.</summary>
    private static readonly float[] Floats =
    [
        .. new uint[]
        {
            0x00000000, 0x80000000, 0x00000001, 0x80000001, // ±0, the smallest float subnormals
            0x7F800000, 0xFF800000,                         // ±∞
            0x7FC00000, 0xFFC00000, 0x7FA00001,             // NaN, -NaN, a NaN with a payload
            0x7F7FFFFF, 0xFF7FFFFF,                         // ±float.MaxValue
            0x477FDFFF, 0x477FE000, 0x477FEFFF, 0x477FF000, 0x477FF001, // 65503.996, 65504, 65519.996, 65520, 65520.004
            0xC77FDFFF, 0xC77FE000, 0xC77FEFFF, 0xC77FF000, 0xC77FF001, // and their negatives
            0x3F800800, 0x3F801000, 0x3F801800, 0x3F800801, // ties at 1: to even (down), exact, to even (up), just above a tie
            0x33800000, 0x33000000, 0x33000001, 0x32FFFFFF, // 2^-24 (the smallest half), 2^-25 (a tie, to 0), just above it, just below it
            0x38800000, 0x387FFFFF, 0x387FC000, 0x387FE000, // 2^-14 (the smallest normal half) and the subnormals below it
        }.Select(BitConverter.UInt32BitsToSingle),
        0.5f, -0.5f, 1f, -1f, 0.1f, -0.1f, 3.1415927f, 1000f, -1000f, 2049f, 4097f, 32767f, 65535f, 65536f, -65536f,
        1e5f, -1e5f, 1e10f, -1e10f, 1e20f, 1e30f, -1e30f,
    ];

    [MetalFact]
    public void Float_ConvertsToHalfAsInCSharp()
    {
        long[] expected = [.. Floats.Select(static x => (long)BitConverter.HalfToUInt16Bits((Half)x))];

        AssertIdentical(expected, Floats, HalfOfFloat.Slots, static (d, cpu, i, o) => Dispatch(d, cpu, i.Length, new HalfOfFloat(i, o)));

        Assert.Equal(PositiveInfinity, expected[Array.IndexOf(Floats, 65520f)]);
        Assert.Equal(0x7BFF, expected[Array.IndexOf(Floats, BitConverter.UInt32BitsToSingle(0x477FEFFF))]);
        Assert.Equal(NegativeInfinity, expected[Array.IndexOf(Floats, -65520f)]);
    }

    [MetalFact]
    public void FloatVectors_ConvertToHalfVectorsAsInCSharp()
    {
        // Thread i converts the lanes i, i + 1, i + 2, i + 3 (wrapping): every value in every lane.
        long[] expected =
        [
            .. Enumerable.Range(0, Floats.Length).SelectMany(i =>
            {
                long x = Bits(Floats[i]), y = Bits(Floats[(i + 1) % Floats.Length]), z = Bits(Floats[(i + 2) % Floats.Length]), w = Bits(Floats[(i + 3) % Floats.Length]);
                return new[] { x, y, z, w, x, y, z, x, y };
            }),
        ];

        AssertIdentical(expected, Floats, HalfOfFloatVectors.Slots, static (d, cpu, i, o) => Dispatch(d, cpu, i.Length, new HalfOfFloatVectors(i, o, i.Length)));

        static long Bits(float x) => BitConverter.HalfToUInt16Bits((Half)x);
    }

    /// <summary>
    /// While the conversion was a plain cast, slots 0, 4, 5, 14, 15 and 16 were ±65504 (0x7BFF, 0xFBFF)
    /// on the GPU: a max or a min with a constant beyond the range of a half, and the bound a float
    /// to long or uint conversion saturates to. The other shapes were not folded in this shader
    /// (<c>(Half)(float)(int)x</c> is in another: KnownDifferencesTests) and are pinned.
    /// </summary>
    [MetalFact]
    public void ValuesTheCompilerCanSeeOrBound_OverflowToInfinityOnTheGpuAsWithForCpu()
    {
        ulong[] input =
        [
            0, 0xB6FE8FF0, 0x807FFFFF, 0x7F800000, 0x429F0C1F, // b = false, and the four lanes of v
            BitConverter.SingleToUInt32Bits(3e9f), BitConverter.SingleToUInt32Bits(-3e9f), BitConverter.SingleToUInt32Bits(1e20f), 0x7F800000,
            BitConverter.SingleToUInt32Bits(2.5f),
        ];

        long[] gpu = Run(input, HalfConversionFolds.Slots, static (d, i, o) => d.For(1, new HalfConversionFolds(i, o)));
        long[] cpu = Run(input, HalfConversionFolds.Slots, static (d, i, o) => d.ForCpu(1, new HalfConversionFolds(i, o)));

        Assert.Equal(cpu, gpu);
        Assert.Equal(
            new long[]
            {
                PositiveInfinity, PositiveInfinity, PositiveInfinity, 0x54F8,             // 0..3: (Half4)Max(v, constants); 79.5
                PositiveInfinity, NegativeInfinity, 0x4100, 0x4100,                       // 4..7: scalar Max and Min with a constant; the in-range ones are 2.5
                PositiveInfinity, 0x4100, NegativeInfinity, PositiveInfinity,             // 8..11: constants behind a select
                PositiveInfinity, NegativeInfinity, PositiveInfinity, PositiveInfinity,   // 12..15: through int and long
                PositiveInfinity, PositiveInfinity, PositiveInfinity, NegativeInfinity,   // 16..19: through uint and ulong; vectors through Int3
                0x0000, PositiveInfinity, NegativeInfinity, 0x7BFF,                       // 20..23: NaN gives 0 as an int; constants
                PositiveInfinity, 0x4000, NegativeInfinity,                               // 24..26: a vector of constants
                PositiveInfinity, PositiveInfinity, NegativeInfinity, PositiveInfinity, 0x3C00, NegativeInfinity, // 27..32: program-scope initializers
                PositiveInfinity, PositiveInfinity, PositiveInfinity,                     // 33..35: integers that are converted directly
            },
            cpu);
    }

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

    /// <summary>Compares bit for bit, with every NaN as one value: the GPU and C# may keep different NaN bits.</summary>
    private static void AssertIdentical(long[] expected, float[] input, int slots, Action<GraphicsDevice, bool, ReadOnlyBuffer<float>, ReadWriteBuffer<long>> dispatch)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<float> inputs = device.AllocateReadOnlyBuffer(input);
        using ReadWriteBuffer<long> gpuOutput = device.AllocateReadWriteBuffer<long>(input.Length * slots);
        using ReadWriteBuffer<long> cpuOutput = device.AllocateReadWriteBuffer<long>(input.Length * slots);
        dispatch(device, false, inputs, gpuOutput);
        dispatch(device, true, inputs, cpuOutput);
        long[] gpu = gpuOutput.ToArray();
        long[] cpu = cpuOutput.ToArray();

        Assert.Equal(input.Length * slots, expected.Length);
        string[] mismatches = Enumerable.Range(0, expected.Length)
            .Where(k => Canonical(expected[k]) != Canonical(cpu[k]) || Canonical(expected[k]) != Canonical(gpu[k]))
            .Select(k => $"{input[k / slots]:R} (0x{BitConverter.SingleToUInt32Bits(input[k / slots]):X8}) slot {k % slots}: C# 0x{expected[k]:X4}, CPU 0x{cpu[k]:X4}, GPU 0x{gpu[k]:X4}")
            .ToArray();

        Assert.True(mismatches.Length == 0, $"{mismatches.Length} of {expected.Length} values differ:\n" + string.Join("\n", mismatches.Take(40)));

        static long Canonical(long bits) => (bits & 0x7C00) == 0x7C00 && (bits & 0x03FF) != 0 ? 0x7E00 : bits;
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
public readonly partial struct HalfOfFloat(ReadOnlyBuffer<float> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 1;

    public void Execute() => output[ThreadIds.X] = Gpu.AsUShort((Half)input[ThreadIds.X]);
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct HalfOfFloatVectors(ReadOnlyBuffer<float> input, ReadWriteBuffer<long> output, int count) : IComputeShader
{
    public const int Slots = 9;

    public void Execute()
    {
        int i = ThreadIds.X;
        int o = i * Slots;
        Float4 v = new(input[i], input[(i + 1) % count], input[(i + 2) % count], input[(i + 3) % count]);
        Half4 h4 = (Half4)v;
        Half3 h3 = (Half3)v.XYZ;
        Half2 h2 = (Half2)v.XY;
        output[o + 0] = Gpu.AsUShort(h4.X);
        output[o + 1] = Gpu.AsUShort(h4.Y);
        output[o + 2] = Gpu.AsUShort(h4.Z);
        output[o + 3] = Gpu.AsUShort(h4.W);
        output[o + 4] = Gpu.AsUShort(h3.X);
        output[o + 5] = Gpu.AsUShort(h3.Y);
        output[o + 6] = Gpu.AsUShort(h3.Z);
        output[o + 7] = Gpu.AsUShort(h2.X);
        output[o + 8] = Gpu.AsUShort(h2.Y);
    }
}

/// <summary>
/// Float to half conversions of values the Metal compiler can see or bound. <c>input[0]</c> is 0, so
/// <c>b</c> is false, but only at run time; the floats are read from the buffer.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct HalfConversionFolds(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 36;

    private static readonly float Big = 1e30f;
    private static readonly Half BigHalf = (Half)1e30f;
    private static readonly Half FromBig = (Half)Big;
    private static readonly Half FromNegated = (Half)(-Big);
    private static readonly Half3 FromVector = (Half3)new Float3(Big, 1f, -Big);

    public void Execute()
    {
        bool b = input[0] != 0;
        Float4 v = new(Gpu.AsFloat((uint)input[1]), Gpu.AsFloat((uint)input[2]), Gpu.AsFloat((uint)input[3]), Gpu.AsFloat((uint)input[4]));
        float f3e9 = Gpu.AsFloat((uint)input[5]);
        float minus3e9 = Gpu.AsFloat((uint)input[6]);
        float f1e20 = Gpu.AsFloat((uint)input[7]);
        float infinity = Gpu.AsFloat((uint)input[8]);
        float x = Gpu.AsFloat((uint)input[9]);

        // A max, a min or a select with a constant beyond the range of a half.
        Half4 h = (Half4)Gpu.Max(v, new Float4(1e30f, float.PositiveInfinity, -3f, 1.5f));
        output[0] = Gpu.AsUShort(h.X);
        output[1] = Gpu.AsUShort(h.Y);
        output[2] = Gpu.AsUShort(h.Z);
        output[3] = Gpu.AsUShort(h.W);
        output[4] = Gpu.AsUShort((Half)Gpu.Max(x, 1e30f));
        output[5] = Gpu.AsUShort((Half)Gpu.Min(x, -1e30f));
        output[6] = Gpu.AsUShort((Half)Gpu.Min(x, 1e30f));
        output[7] = Gpu.AsUShort((Half)Gpu.Max(x, -65520f));
        output[8] = Gpu.AsUShort((Half)(b ? x : 1e10f));
        output[9] = Gpu.AsUShort((Half)(b ? 1e10f : x));
        output[10] = Gpu.AsUShort((Half)(b ? x : -65520f));
        output[11] = Gpu.AsUShort((Half)(b ? float.NaN : 65520f));

        // The bound a float to integer conversion saturates to, converted on to a half.
        output[12] = Gpu.AsUShort((Half)(float)(int)f3e9);
        output[13] = Gpu.AsUShort((Half)(float)(int)minus3e9);
        output[14] = Gpu.AsUShort((Half)(float)(long)f1e20);
        output[15] = Gpu.AsUShort((Half)(float)(long)infinity);
        output[16] = Gpu.AsUShort((Half)(float)(uint)f1e20);
        output[17] = Gpu.AsUShort((Half)(float)(ulong)infinity);
        Half3 through = (Half3)(Float3)(Int3)new Float3(f3e9, minus3e9, float.NaN);
        output[18] = Gpu.AsUShort(through.X);
        output[19] = Gpu.AsUShort(through.Y);
        output[20] = Gpu.AsUShort(through.Z);

        // Constants: C# converts these at run time, the Metal compiler at compile time.
        output[21] = Gpu.AsUShort((Half)1e30f);
        output[22] = Gpu.AsUShort((Half)(-70000f));
        output[23] = Gpu.AsUShort((Half)65519f);
        Half3 constants = (Half3)new Float3(1e30f, 2f, -65520f);
        output[24] = Gpu.AsUShort(constants.X);
        output[25] = Gpu.AsUShort(constants.Y);
        output[26] = Gpu.AsUShort(constants.Z);

        // Program-scope initializers.
        output[27] = Gpu.AsUShort(BigHalf);
        output[28] = Gpu.AsUShort(FromBig);
        output[29] = Gpu.AsUShort(FromNegated);
        output[30] = Gpu.AsUShort(FromVector.X);
        output[31] = Gpu.AsUShort(FromVector.Y);
        output[32] = Gpu.AsUShort(FromVector.Z);

        // Integers that are converted to a half directly were not folded: pinned.
        output[33] = Gpu.AsUShort((Half)(int)f3e9);
        output[34] = Gpu.AsUShort((Half)(uint)f3e9);
        output[35] = Gpu.AsUShort((Half)(ushort)f3e9);
    }
}
