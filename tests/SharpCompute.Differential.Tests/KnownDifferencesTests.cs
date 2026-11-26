using System;
using SharpCompute.Tests;

namespace SharpCompute.Differential.Tests;

/// <summary>
/// What the random-expression differential test found: the fixed float to integer conversions,
/// pinned here, and the differences between the GPU and <c>ForCpu</c> that are not fixed, each as a
/// minimal repro in a skipped test. The generator (tools/gen_differential.py, known_difference)
/// leaves the unfixed patterns out, under the same names, so that a long run reports only what is
/// new; un-skip a test here to check whether a Metal update or a change to the generator fixed it.
/// Every repro reads its operands from a buffer, so nothing is folded at build time.
/// </summary>
[Trait("Category", "Differential")]
public class KnownDifferencesTests
{
    /// <summary>
    /// Fixed: the Metal compiler folded a float to 8- or 16-bit conversion of an integer that went
    /// through a float or a half into a truncation, and a float to unsigned conversion of a small
    /// signed integer into a zero extension, where C# saturates.
    /// </summary>
    [MetalFact]
    public void ConversionsOfIntegersThroughAFloat_SaturateOnTheGpuAsWithForCpu()
    {
        ulong[] input = [unchecked((ulong)1747837037L), 300, unchecked((ulong)-1L), 16384, unchecked((ulong)-17L), unchecked((ulong)-129L), 70000];

        long[] gpu = Run(input, ConversionRegression.Slots, static (d, i, o) => d.For(1, new ConversionRegression(i, o)));
        long[] cpu = Run(input, ConversionRegression.Slots, static (d, i, o) => d.ForCpu(1, new ConversionRegression(i, o)));

        Assert.Equal(cpu, gpu);
        Assert.Equal(new long[] { 255, 255, 0, 127, 0, 0, 0, 65535, -32768, 255, 0 }, cpu);
    }

    private const string Unfixed = "A difference between the GPU and ForCpu that is not fixed (see the summary of this test and KnownDifferencesTests).";

    /// <summary>
    /// <c>4294967295u / x</c>: the Metal compiler's division of a constant dividend near 2^32 by a
    /// variable is wrong (3579139454 for x = 3), also through a <c>long</c> that it narrows to 32 bits.
    /// </summary>
    [MetalFact(Skip = Unfixed)]
    public void ConstantDividend() => AssertSame([3], ConstantDividendRepro.Slots, static (d, cpu, i, o) => Dispatch(d, cpu, new ConstantDividendRepro(i, o)));

    /// <summary><c>h / (Half)3f</c>: a half division by a constant is a multiplication by its reciprocal, one half ULP off (931 for 2794 / 3).</summary>
    [MetalFact(Skip = Unfixed)]
    public void HalfDivisionByConstant() => AssertSame([0x6975], HalfDivisionRepro.Slots, static (d, cpu, i, o) => Dispatch(d, cpu, new HalfDivisionRepro(i, o)));

    /// <summary>
    /// <c>Rsqrt(-0)</c> is -∞ when stored, but the compiler rewrites what uses it as if it were not:
    /// <c>Abs(Rsqrt(-0))</c> is -∞ (the Abs is folded away), <c>Sign(Rsqrt(-0))</c> is 0 instead of
    /// -1, and for halves <c>Fmod(1340, Rsqrt(-0))</c> is NaN instead of 1340 and
    /// <c>Tanh(Rsqrt(-0))</c> is -∞ instead of -1. A negative float subnormal is flushed to -0 first.
    /// </summary>
    [MetalFact(Skip = Unfixed)]
    public void RsqrtOfZeroConsumed() => AssertSame([0x80000000, 0x653C, 0x8000], RsqrtOfZeroRepro.Slots, static (d, cpu, i, o) => Dispatch(d, cpu, new RsqrtOfZeroRepro(i, o)));

    /// <summary>
    /// MSL leaves a float to integer conversion out of range undefined, and the compiler uses that
    /// where it can see the value or bound it: <c>(int)(b ? float.NaN : 1e10f)</c> is 0 instead of
    /// int.MaxValue, <c>(int)h == int.MinValue</c> is false for <c>h = -∞</c> (the conversion itself
    /// saturates to int.MinValue), and <c>-(long)(int)h</c> is negated in 32 bits, because a finite
    /// half converts to a value within ±65504.
    /// </summary>
    [MetalFact(Skip = Unfixed)]
    public void OutOfRangeConversion() => AssertSame([0, 0xFC00], ConversionFoldRepro.Slots, static (d, cpu, i, o) => Dispatch(d, cpu, new ConversionFoldRepro(i, o)));

    /// <summary>A 64-bit integer converted to a half: 65510L gives +∞ instead of 65504, and a huge one converted on to an int wraps.</summary>
    [MetalFact(Skip = Unfixed)]
    public void LongToHalf() => AssertSame([65510, 1125586534716449654], LongToHalfRepro.Slots, static (d, cpu, i, o) => Dispatch(d, cpu, new LongToHalfRepro(i, o)));

    /// <summary>
    /// The bit intrinsics of a value the compiler narrows to 16 bits see the wrong upper bits:
    /// <c>CountBits(b ? s : t)</c> of the short -2 is 31 instead of 15,
    /// <c>LeadingZeroCount((uint)(short)i)</c> of a negative short is 16 instead of 0, and so is
    /// <c>LeadingZeroCount(a % b)</c> of two sbytes widened to int, -48 % 53.
    /// </summary>
    [MetalFact(Skip = Unfixed)]
    public void BitsOfSmallIntegerValue() => AssertSame([1, unchecked((ulong)-2L), 255, 1747837037, unchecked((ulong)-48L), 53], BitsOfSmallIntegerRepro.Slots, static (d, cpu, i, o) => Dispatch(d, cpu, new BitsOfSmallIntegerRepro(i, o)));

    /// <summary>
    /// A guard against division by zero is dropped when the divisor is itself chosen by a ternary:
    /// with <c>t = l == long.MinValue ? 0 : Math.Abs(l)</c>, <c>t == 0 ? 0 : x % t</c> is <c>x</c>
    /// for <c>l = long.MinValue</c>, and with <c>t = d == 0 ? 0 : a / d</c>, <c>t == 0 ? 0 : x % t</c>
    /// is <c>x</c> for <c>d = 0</c>.
    /// </summary>
    [MetalFact(Skip = Unfixed)]
    public void GuardedDivisionChain() => AssertSame([2, unchecked((ulong)long.MinValue), 0, 31, 0x6120], GuardedDivisionRepro.Slots, static (d, cpu, i, o) => Dispatch(d, cpu, new GuardedDivisionRepro(i, o)));

    /// <summary>
    /// <c>Sqrt</c> and <c>Rsqrt</c> of a half converted to float are one ULP off the correctly
    /// rounded value the GPU gives for the same float read from a buffer: <c>Rsqrt((Float3)h)</c> of
    /// 1.5 and <c>Sqrt((float)h)</c> of 2794.
    /// </summary>
    [MetalFact(Skip = Unfixed)]
    public void SqrtOfConvertedHalf() => AssertSame([0x3E00, 0x6975], SqrtOfHalfRepro.Slots, static (d, cpu, i, o) => Dispatch(d, cpu, new SqrtOfHalfRepro(i, o)));

    private static void Dispatch<TShader>(GraphicsDevice device, bool cpu, in TShader shader)
        where TShader : struct, IComputeShader, IComputeShaderDescriptor<TShader> => DifferentialHarness.Dispatch(device, cpu, 1, shader);

    private static void AssertSame(ulong[] input, int slots, Action<GraphicsDevice, bool, ReadOnlyBuffer<ulong>, ReadWriteBuffer<long>> dispatch)
    {
        long[] gpu = Run(input, slots, (d, i, o) => dispatch(d, false, i, o));
        long[] cpu = Run(input, slots, (d, i, o) => dispatch(d, true, i, o));
        Assert.Equal(cpu, gpu);
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
public readonly partial struct ConversionRegression(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 11;

    public void Execute()
    {
        int i = (int)input[0];
        int i300 = (int)input[1];
        short minusOne = (short)input[2];
        short s16384 = (short)input[3];
        sbyte minus17 = (sbyte)input[4];
        long minus129 = (long)input[5];
        int i70000 = (int)input[6];

        output[0] = (byte)(float)i;              // 255: saturates
        output[1] = (byte)(float)i300;           // 255
        output[2] = (byte)(Half)minusOne;        // 0
        output[3] = (sbyte)(float)s16384;        // 127
        output[4] = (uint)(float)minusOne;       // 0: not a zero extension
        output[5] = (uint)(Half)minus17;         // 0
        output[6] = (long)(ulong)(float)minus17; // 0
        output[7] = (ushort)(float)i70000;       // 65535
        output[8] = (short)((float)minus129 * 256f); // -32768
        output[9] = (byte)(Half)i300;            // 255
        output[10] = (ushort)(Half)minusOne;     // 0
    }
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct ConstantDividendRepro(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 3;

    public void Execute()
    {
        uint x = Gpu.Max((uint)input[0], 1u);
        long y = x;
        output[0] = 4294967295u / x;
        output[1] = 4294967295u % x;
        output[2] = 4294967295L % y;
    }
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct HalfDivisionRepro(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 1;

    public void Execute() => output[0] = Gpu.AsUShort(Gpu.AsHalf((ushort)input[0]) / (Half)3f);
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct RsqrtOfZeroRepro(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 4;

    public void Execute()
    {
        float zero = Gpu.AsFloat((uint)input[0]);
        Half2 x = new(Gpu.AsHalf((ushort)input[1]));
        Half2 halfZero = new(Gpu.AsHalf((ushort)input[2]));
        output[0] = Gpu.AsUInt(Gpu.Abs(Gpu.Rsqrt(zero)));
        output[1] = Gpu.AsUInt(Gpu.Sign(Gpu.Rsqrt(zero)));
        output[2] = Gpu.AsUShort(Gpu.Fmod(x, Gpu.Rsqrt(halfZero)).X);
        output[3] = Gpu.AsUShort(Gpu.Tanh(Gpu.Rsqrt(halfZero)).X);
    }
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct ConversionFoldRepro(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 3;

    public void Execute()
    {
        bool b = input[0] != 0;
        Half h = Gpu.AsHalf((ushort)input[1]);
        int t = (int)h;
        output[0] = (int)(b ? float.NaN : 1e10f);
        output[1] = t == int.MinValue ? 0 : 1;
        output[2] = -(long)(int)h;
    }
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct LongToHalfRepro(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 2;

    public void Execute()
    {
        output[0] = Gpu.AsUShort((Half)(long)input[0]);
        output[1] = (int)(Half)input[1];
    }
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct BitsOfSmallIntegerRepro(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 3;

    public void Execute()
    {
        bool b = input[0] != 0;
        short s = (short)input[1];
        short t = (short)input[2];
        int i = (int)input[3];
        int x = (sbyte)input[4];
        int y = (sbyte)input[5];
        output[0] = Gpu.CountBits(b ? s : t);
        output[1] = Gpu.LeadingZeroCount((uint)(short)i);
        output[2] = Gpu.LeadingZeroCount(y == 0 ? 0 : x % y);
    }
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct GuardedDivisionRepro(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 2;

    public void Execute()
    {
        long x = (long)input[0];
        long l = (long)input[1];
        long t = l == long.MinValue ? 0 : Math.Abs(l);
        output[0] = t == 0 ? 0 : x % t;

        uint d = (uint)input[2];
        uint a = (uint)input[3];
        uint y = (uint)Gpu.AsHalf((ushort)input[4]);
        uint q = d == 0 ? 0 : a / d;
        output[1] = q == 0 ? 0 : y % q;
    }
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct SqrtOfHalfRepro(ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 2;

    public void Execute()
    {
        output[0] = Gpu.AsUInt(Gpu.Rsqrt((Float3)new Half3(Gpu.AsHalf((ushort)input[0]))).X);
        output[1] = Gpu.AsUInt(Gpu.Sqrt((float)Gpu.AsHalf((ushort)input[1])));
    }
}
