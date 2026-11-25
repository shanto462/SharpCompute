using System;

namespace SharpCompute.Tests.Conformance;

/// <summary>
/// The operations that throw in .NET and so in <c>ForCpu</c>, run on the GPU, which has no exceptions.
/// The operands come from a buffer, so nothing is folded at compile time.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct ThrowingCases(
    ReadOnlyBuffer<int> ints,
    ReadOnlyBuffer<long> longs,
    ReadOnlyBuffer<float> floats,
    ReadOnlyBuffer<uint> uints,
    ReadWriteBuffer<long> output) : IComputeShader
{
    public const int Slots = 32;

    public void Execute()
    {
        int intMin = ints[0], minusOne = ints[1], zero = ints[2], seven = ints[3], nine = ints[4];
        long longMin = longs[0], longMinusOne = longs[1], longZero = longs[2];
        float nan = floats[0], half = floats[1], one = floats[2], quarter = floats[3], fzero = floats[4];
        uint uzero = uints[0], useven = uints[1];
        sbyte sbyteMin = (sbyte)ints[5];
        short shortMin = (short)ints[6];

        // Pinned: what the GPU returns where .NET throws.
        output[0] = Math.Abs(intMin);                          // OverflowException in .NET
        output[1] = Math.Abs(longMin);                         // OverflowException
        output[2] = Math.Abs(sbyteMin);                        // OverflowException
        output[3] = Math.Abs(shortMin);                        // OverflowException
        output[4] = MathF.Sign(nan);                           // ArithmeticException
        output[5] = Math.Sign(nan);                            // ArithmeticException
        output[6] = Gpu.AsUInt(Math.Clamp(one, half, fzero));  // ArgumentException: min > max
        output[7] = Gpu.AsUInt(Math.Clamp(quarter, half, fzero));
        output[8] = Gpu.AsUInt(float.Clamp(one, half, fzero));
        output[9] = Math.Clamp(seven, nine, zero);             // ArgumentException
        output[10] = Math.Clamp(intMin, nine, zero);
        output[11] = Math.Clamp(longMin, (long)nine, longZero);
        output[12] = intMin / minusOne;                        // OverflowException
        output[13] = intMin % minusOne;                        // OverflowException
        output[14] = longMin / longMinusOne;                   // OverflowException
        output[15] = longMin % longMinusOne;                   // OverflowException

        // Unspecified: division by zero (DivideByZeroException in .NET). It must not trap or hang.
        output[16] = seven / zero;
        output[17] = seven % zero;
        output[18] = intMin / zero;
        output[19] = useven / uzero;
        output[20] = useven % uzero;
        output[21] = longMin / longZero;
        output[22] = longMin % longZero;
        output[23] = (long)((ulong)longs[3] / (ulong)longZero);
        output[24] = (byte)seven / (byte)zero;
        output[25] = (short)seven % (short)zero;
        output[26] = seven;                                    // the dispatch got this far
        output[27] = 0;
        output[28] = 0;
        output[29] = 0;
        output[30] = 0;
        output[31] = 1;
    }
}

/// <summary>
/// Where .NET throws, the GPU carries on. These are the values it returns, pinned; ForCpu throws for
/// every one of them (checked here too), so a shader that depends on them is not portable.
/// </summary>
[Trait("Category", "Conformance")]
public class ThrowingCasesTests
{
    private static long[] RunOnTheGpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<int> ints = device.AllocateReadOnlyBuffer<int>([int.MinValue, -1, 0, 7, 9, sbyte.MinValue, short.MinValue]);
        using ReadOnlyBuffer<long> longs = device.AllocateReadOnlyBuffer<long>([long.MinValue, -1L, 0L, 12345L]);
        using ReadOnlyBuffer<float> floats = device.AllocateReadOnlyBuffer<float>([float.NaN, 0.5f, 1f, 0.25f, 0f]);
        using ReadOnlyBuffer<uint> uints = device.AllocateReadOnlyBuffer<uint>([0u, 7u]);
        using ReadWriteBuffer<long> output = device.AllocateReadWriteBuffer<long>(ThrowingCases.Slots);

        device.For(1, new ThrowingCases(ints, longs, floats, uints, output));
        return output.ToArray();
    }

    [MetalFact]
    public void WhereDotNetThrows_TheGpuReturnsThesePinnedValues()
    {
        long[] gpu = RunOnTheGpu();

        Assert.Equal(int.MinValue, gpu[0]);       // Math.Abs(int.MinValue): the minimum itself (two's complement wrap)
        Assert.Equal(long.MinValue, gpu[1]);
        Assert.Equal(sbyte.MinValue, gpu[2]);
        Assert.Equal(short.MinValue, gpu[3]);
        Assert.Equal(0, gpu[4]);                  // MathF.Sign(NaN): 0
        Assert.Equal(0, gpu[5]);
        Assert.Equal(BitConverter.SingleToUInt32Bits(0f), (uint)gpu[6]);     // Clamp(1, 0.5, 0): above the bounds gives max
        Assert.Equal(BitConverter.SingleToUInt32Bits(0.5f), (uint)gpu[7]);   // Clamp(0.25, 0.5, 0): below min gives min
        Assert.Equal(BitConverter.SingleToUInt32Bits(0f), (uint)gpu[8]);
        Assert.Equal(9, gpu[9]);                  // Clamp(7, 9, 0): below min gives min
        Assert.Equal(9, gpu[10]);
        Assert.Equal(9, gpu[11]);
        Assert.Equal(int.MinValue, gpu[12]);      // int.MinValue / -1: the wrapped quotient
        Assert.Equal(0, gpu[13]);                 // int.MinValue % -1: 0
        Assert.Equal(long.MinValue, gpu[14]);
        Assert.Equal(0, gpu[15]);
        Assert.Equal(7, gpu[26]);                 // division by zero did not stop the thread
        Assert.Equal(1, gpu[31]);
    }

    [MetalTheory]
    [InlineData(0, typeof(OverflowException))]
    [InlineData(4, typeof(ArithmeticException))]
    [InlineData(6, typeof(ArgumentException))]
    public void ForCpu_ThrowsForTheSameShader(int firstThrowing, Type expected)
    {
        // One operand set per exception type; the shader stops at its first throw.
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int[] intValues = [int.MinValue, -1, 0, 7, 9, sbyte.MinValue, short.MinValue];
        long[] longValues = [long.MinValue, -1L, 0L, 12345L];
        float[] floatValues = [float.NaN, 0.5f, 1f, 0.25f, 0f];

        switch (firstThrowing)
        {
            case 4:
                intValues[0] = 0;
                longValues[0] = 0;
                intValues[5] = 0;
                intValues[6] = 0;                 // MathF.Sign(NaN)
                break;
            case 6:
                intValues[0] = 0;
                longValues[0] = 0;
                intValues[5] = 0;
                intValues[6] = 0;
                floatValues[0] = 1f;              // Math.Clamp(1, 0.5, 0)
                break;
        }

        using ReadOnlyBuffer<int> ints = device.AllocateReadOnlyBuffer(intValues);
        using ReadOnlyBuffer<long> longs = device.AllocateReadOnlyBuffer(longValues);
        using ReadOnlyBuffer<float> floats = device.AllocateReadOnlyBuffer(floatValues);
        using ReadOnlyBuffer<uint> uints = device.AllocateReadOnlyBuffer<uint>([0u, 7u]);
        using ReadWriteBuffer<long> output = device.AllocateReadWriteBuffer<long>(ThrowingCases.Slots);

        Exception thrown = Assert.ThrowsAny<Exception>(() => device.ForCpu(1, new ThrowingCases(ints, longs, floats, uints, output)));
        Assert.Equal(expected, thrown.GetType());
    }
}
