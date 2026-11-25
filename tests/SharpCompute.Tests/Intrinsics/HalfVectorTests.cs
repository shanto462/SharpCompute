using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace SharpCompute.Tests.Intrinsics;

/// <summary>
/// Half vectors on the GPU against the CPU (<c>ForCpu</c> of the same shader), pinned to what an
/// Apple M4 Pro was measured to compute: a GPU that rounds halves differently fails here. The
/// arithmetic, the conversions and every function that is not transcendental must match bit for bit, half
/// subnormals included; the transcendental functions within one half ULP; and an <c>a * b + c</c>
/// the GPU may contract must be either the fused or the unfused result, nothing else. The inputs
/// cover all 65,536 halves in the first component, and zeros, infinities, NaN, subnormals and
/// operands of nearby exponents in the others.
/// </summary>
public class HalfVectorTests
{
    private const int Count = 65536;

    private static readonly ushort[] Special =
    [
        0x0000, 0x8000, 0x3C00, 0xBC00, 0x7C00, 0xFC00, 0x7E00, 0xFE00, 0x7BFF, 0xFBFF,
        0x0001, 0x8001, 0x03FF, 0x0400, 0x3BFF, 0x3C01, 0x4000, 0x3800,
    ];

    private static Half FromBits(int bits) => BitConverter.UInt16BitsToHalf((ushort)bits);

    private static ushort Bits(Half value) => BitConverter.HalfToUInt16Bits(value);

    private static bool IsNaN(ushort bits) => (bits & 0x7C00) == 0x7C00 && (bits & 0x03FF) != 0;

    /// <summary>The same half, where every NaN is the same (the GPU does not promise a payload or a sign for a NaN it produces).</summary>
    private static bool Same(ushort cpu, ushort gpu) => cpu == gpu || (IsNaN(cpu) && IsNaN(gpu));

    /// <summary>The number of halves between two halves: 0 for +0 and -0, 1 from the largest half to infinity.</summary>
    private static int UlpDistance(ushort a, ushort b)
    {
        static int Ordinal(ushort bits) => (bits & 0x8000) != 0 ? -(bits & 0x7FFF) : bits;

        return IsNaN(a) || IsNaN(b) ? (IsNaN(a) && IsNaN(b) ? 0 : int.MaxValue) : Math.Abs(Ordinal(a) - Ordinal(b));
    }

    /// <summary>
    /// Three operand sets per thread. <c>a.X</c> runs through every half; the other components are
    /// random bit patterns (so every exponent is as likely as any other), special values, operands
    /// within a few binades of each other (where sums round and cancel), and moderate magnitudes.
    /// </summary>
    private static (Half4[] A, Half4[] B, Half4[] C) Operands(int seed)
    {
        Random random = new(seed);
        Half4[] a = new Half4[Count], b = new Half4[Count], c = new Half4[Count];

        ushort Any() => random.Next(8) == 0 ? Special[random.Next(Special.Length)] : (ushort)random.Next(65536);
        ushort Near(ushort other, int spread) => (ushort)((random.Next(2) << 15) | (Math.Clamp(((other >> 10) & 31) + random.Next(-spread, spread + 1), 0, 31) << 10) | random.Next(1024));
        ushort Moderate() => (ushort)((random.Next(2) << 15) | (random.Next(10, 21) << 10) | random.Next(1024));

        for (int i = 0; i < Count; i++)
        {
            ushort x = (ushort)i;
            a[i] = new Half4(FromBits(x), FromBits(Any()), FromBits(Any()), FromBits(Moderate()));
            ushort ay = Bits(a[i].Y), az = Bits(a[i].Z);
            b[i] = new Half4(FromBits(Any()), FromBits(Near(ay, 3)), FromBits(Any()), FromBits(Moderate()));
            c[i] = new Half4(FromBits(Any()), FromBits(Near(ay, 12)), FromBits(Near(az, 2)), FromBits(Moderate()));
        }

        return (a, b, c);
    }

    private static ushort[] Words(ReadWriteBuffer<Half4> buffer) => MemoryMarshal.Cast<Half4, ushort>(buffer.AsSpan()).ToArray();

    private static string Describe(Half4[] a, Half4[] b, Half4[] c, int thread, int component)
    {
        static string One(Half4[] values, int thread, int component)
        {
            ushort bits = MemoryMarshal.Cast<Half4, ushort>(values.AsSpan())[(thread * 4) + component];
            return $"0x{bits:X4} ({(float)BitConverter.UInt16BitsToHalf(bits):R})";
        }

        return $"a = {One(a, thread, component)}, b = {One(b, thread, component)}, c = {One(c, thread, component)}";
    }

    // ---- arithmetic -------------------------------------------------------------------------------

    [MetalFact]
    public void Arithmetic_IsBitExact_AndAContractedMultiplyAddIsTheFusedOrTheUnfusedResult()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        (Half4[] a, Half4[] b, Half4[] c) = Operands(seed: 11);
        using ReadOnlyBuffer<Half4> bufferA = device.AllocateReadOnlyBuffer<Half4>(a), bufferB = device.AllocateReadOnlyBuffer<Half4>(b), bufferC = device.AllocateReadOnlyBuffer<Half4>(c);
        using ReadWriteBuffer<Half4> gpu = device.AllocateReadWriteBuffer<Half4>(Count * HalfArithmetic.Stride);
        using ReadWriteBuffer<Half4> cpu = device.AllocateReadWriteBuffer<Half4>(Count * HalfArithmetic.Stride);

        device.For(Count, new HalfArithmetic(bufferA, bufferB, bufferC, gpu));
        device.ForCpu(Count, new HalfArithmetic(bufferA, bufferB, bufferC, cpu));

        ushort[] expected = Words(cpu), actual = Words(gpu);
        var mismatches = new List<string>();
        int distinguishing = 0;

        for (int thread = 0; thread < Count; thread++)
        {
            Half4 x = a[thread], y = b[thread], z = c[thread];

            // What one fused multiply-add per contractable expression gives: x * y + z, z - x * y and
            // x * y + y * z (where the first product is the one that is fused into the sum).
            Half4[] fusedResults = [Gpu.Fma(x, y, z), Gpu.Fma(-x, y, z), Gpu.Fma(x, y, y * z)];

            for (int output = 0; output < HalfArithmetic.Stride; output++)
            {
                for (int component = 0; component < 4; component++)
                {
                    int index = (((thread * HalfArithmetic.Stride) + output) * 4) + component;
                    ushort e = expected[index], g = actual[index];

                    if (output is >= HalfArithmetic.FirstContractable and < HalfArithmetic.FirstContractable + 3)
                    {
                        // Either the fused result or the unfused one (which is checked below).
                        ushort f = MemoryMarshal.Cast<Half4, ushort>(fusedResults.AsSpan())[((output - HalfArithmetic.FirstContractable) * 4) + component];
                        distinguishing += Same(f, e) ? 0 : 1;

                        if (Same(f, g))
                        {
                            continue;
                        }
                    }

                    if (!Same(e, g) && mismatches.Count < 20)
                    {
                        mismatches.Add($"output {output}, thread {thread}, component {component}: CPU 0x{e:X4}, GPU 0x{g:X4}; {Describe(a, b, c, thread, component)}");
                    }
                }
            }
        }

        Assert.True(mismatches.Count == 0, "Half arithmetic differs between the GPU and the CPU:\n" + string.Join("\n", mismatches));

        // The inputs do tell the two apart: thousands of them round differently fused and unfused.
        // Which of the two the GPU returns is its compiler's choice; either is correct.
        Assert.True(distinguishing > 10_000, $"only {distinguishing} operand sets distinguish a fused multiply-add from an unfused one");
    }

    [MetalFact]
    public void Arithmetic_WithContractionOff_IsBitExactThroughout()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        (Half4[] a, Half4[] b, Half4[] c) = Operands(seed: 12);
        using ReadOnlyBuffer<Half4> bufferA = device.AllocateReadOnlyBuffer<Half4>(a), bufferB = device.AllocateReadOnlyBuffer<Half4>(b), bufferC = device.AllocateReadOnlyBuffer<Half4>(c);
        using ReadWriteBuffer<Half4> gpu = device.AllocateReadWriteBuffer<Half4>(Count * HalfArithmetic.Stride);
        using ReadWriteBuffer<Half4> cpu = device.AllocateReadWriteBuffer<Half4>(Count * HalfArithmetic.Stride);

        device.For(Count, new HalfArithmeticUncontracted(bufferA, bufferB, bufferC, gpu));
        device.ForCpu(Count, new HalfArithmeticUncontracted(bufferA, bufferB, bufferC, cpu));

        AssertSame(Words(cpu), Words(gpu), HalfArithmetic.Stride, static output => $"output {output}", a, b, c);

        // And the CPU side is System.Half's arithmetic, one operation after another.
        Half4[] results = cpu.ToArray();

        for (int thread = 0; thread < Count; thread += 97)
        {
            Half x = a[thread].Y, y = b[thread].Y, z = c[thread].Y;
            Assert.True(Same(Bits(x + y), Bits(results[thread * HalfArithmetic.Stride].Y)));
            Assert.True(Same(Bits(x / y), Bits(results[(thread * HalfArithmetic.Stride) + 3].Y)));
            Assert.True(Same(Bits((x * y) + z), Bits(results[(thread * HalfArithmetic.Stride) + 8].Y)));
            Assert.True(Same(Bits(z - (x * y)), Bits(results[(thread * HalfArithmetic.Stride) + 9].Y)));
        }
    }

    /// <summary>Asserts that every half of every output is the same on both sides (any NaN for any NaN).</summary>
    private static void AssertSame(ushort[] expected, ushort[] actual, int stride, Func<int, string> name, Half4[] a, Half4[] b, Half4[] c, int[]? eitherZero = null)
    {
        Assert.Equal(expected.Length, actual.Length);
        var mismatches = new List<string>();
        int total = 0;

        for (int index = 0; index < expected.Length; index++)
        {
            ushort e = expected[index], g = actual[index];
            int output = index / 4 % stride;

            if (Same(e, g) || (eitherZero is not null && eitherZero.Contains(output) && (e & 0x7FFF) == 0 && (g & 0x7FFF) == 0))
            {
                continue;
            }

            total++;

            if (mismatches.Count < 20)
            {
                int thread = index / 4 / stride, component = index % 4;
                mismatches.Add($"{name(output)}, thread {thread}, component {component}: CPU 0x{e:X4}, GPU 0x{g:X4}; {Describe(a, b, c, thread, component)}");
            }
        }

        Assert.True(total == 0, $"{total} halves differ between the GPU and the CPU:\n" + string.Join("\n", mismatches));
    }

    // ---- functions --------------------------------------------------------------------------------

    [MetalFact]
    public void Functions_AreBitExact_AndTranscendentalsWithinOneUlp()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        (Half4[] a, Half4[] b, Half4[] c) = Operands(seed: 13);
        using ReadOnlyBuffer<Half4> bufferA = device.AllocateReadOnlyBuffer<Half4>(a), bufferB = device.AllocateReadOnlyBuffer<Half4>(b), bufferC = device.AllocateReadOnlyBuffer<Half4>(c);
        using ReadWriteBuffer<Half4> gpu = device.AllocateReadWriteBuffer<Half4>(Count * HalfFunctions.Stride);
        using ReadWriteBuffer<Half4> cpu = device.AllocateReadWriteBuffer<Half4>(Count * HalfFunctions.Stride);

        device.For(Count, new HalfFunctions(bufferA, bufferB, bufferC, gpu));
        device.ForCpu(Count, new HalfFunctions(bufferA, bufferB, bufferC, cpu));

        Assert.Equal(HalfFunctions.Stride, HalfFunctions.Names.Length);
        ushort[] expected = Words(cpu), actual = Words(gpu);
        var mismatches = new List<string>();
        int[] worst = new int[HalfFunctions.Stride];

        for (int index = 0; index < expected.Length; index++)
        {
            ushort e = expected[index], g = actual[index];
            int output = index / 4 % HalfFunctions.Stride;
            bool exact = output < HalfFunctions.FirstTranscendental;
            int distance = UlpDistance(e, g);
            bool ok = exact
                ? Same(e, g) || (HalfFunctions.EitherZero.Contains(output) && (e & 0x7FFF) == 0 && (g & 0x7FFF) == 0)
                : distance <= 1;

            worst[output] = Math.Max(worst[output], distance == int.MaxValue ? 99999 : distance);

            if (!ok && mismatches.Count < 30)
            {
                int thread = index / 4 / HalfFunctions.Stride, component = index % 4;
                mismatches.Add($"{HalfFunctions.Names[output]}, thread {thread}, component {component}: CPU 0x{e:X4}, GPU 0x{g:X4} ({(distance == int.MaxValue ? "NaN on one side" : distance + " ULP")}); {Describe(a, b, c, thread, component)}");
            }
        }

        Assert.True(mismatches.Count == 0, "Half functions differ between the GPU and the CPU:\n" + string.Join("\n", mismatches));
    }

    [MetalFact]
    public void GeometricFunctions_AreBitExact()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        (Half4[] a, Half4[] b, Half4[] c) = Operands(seed: 14);
        using ReadOnlyBuffer<Half4> bufferA = device.AllocateReadOnlyBuffer<Half4>(a), bufferB = device.AllocateReadOnlyBuffer<Half4>(b);
        using ReadWriteBuffer<Half4> gpu = device.AllocateReadWriteBuffer<Half4>(Count * HalfGeometry.Stride);
        using ReadWriteBuffer<Half4> cpu = device.AllocateReadWriteBuffer<Half4>(Count * HalfGeometry.Stride);

        device.For(Count, new HalfGeometry(bufferA, bufferB, gpu));
        device.ForCpu(Count, new HalfGeometry(bufferA, bufferB, cpu));

        AssertSame(Words(cpu), Words(gpu), HalfGeometry.Stride, static output => $"geometry output {output}", a, b, c);

        // The CPU side has the documented range: a length whose square overflows is infinity, one whose
        // square underflows is 0, and both happen with these inputs.
        Half4[] results = cpu.ToArray();
        Assert.Contains(results.Where((_, i) => i % HalfGeometry.Stride == 0), r => Half.IsPositiveInfinity(r.W));
        Assert.Contains(Enumerable.Range(0, Count), i => results[i * HalfGeometry.Stride].W == (Half)0f && (a[i].X != (Half)0f || a[i].Y != (Half)0f));
    }

    // ---- conversions ------------------------------------------------------------------------------

    [MetalFact]
    public void Conversions_AreBitExact()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Random random = new(15);
        var floats = new Float4[Count];
        var ints = new Int4[Count];
        var halves = new Half4[Count];

        float NearAHalf(int mode)
        {
            // A half, moved by exactly half its spacing (a tie), by a float ULP either side of the tie, or by less.
            int bits = BitConverter.SingleToInt32Bits((float)FromBits(random.Next(65536)));
            return BitConverter.Int32BitsToSingle(bits + (mode == 0 ? 1 << 12 : mode == 1 ? (1 << 12) + random.Next(-2, 3) : random.Next(1 << 13)));
        }

        for (int i = 0; i < Count; i++)
        {
            floats[i] = new Float4(NearAHalf(i % 3), BitConverter.Int32BitsToSingle(random.Next(int.MinValue, int.MaxValue)), (float)((random.NextDouble() * 2 - 1) * 70000), (float)((random.NextDouble() * 2 - 1) * 1e-4));
            ints[i] = new Int4(random.Next(int.MinValue, int.MaxValue), random.Next(-70000, 70000), random.Next(-2100, 2100), (random.Next(-5000, 5000) * 16) + (random.Next(-1, 2) * 8));
            halves[i] = new Half4(FromBits(i), FromBits(random.Next(65536)), FromBits(Special[i % Special.Length]), FromBits(random.Next(65536)));
        }

        floats[0] = new Float4(float.NaN, float.PositiveInfinity, float.NegativeInfinity, -0f);
        floats[1] = new Float4(65520f, MathF.BitDecrement(65520f), -65520f, MathF.ScaleB(1f, -25));
        ints[0] = new Int4(int.MinValue, int.MaxValue, 65519, 65520);
        ints[1] = new Int4(2049, 2051, -2049, -1);

        using ReadOnlyBuffer<Float4> floatInput = device.AllocateReadOnlyBuffer<Float4>(floats);
        using ReadOnlyBuffer<Int4> intInput = device.AllocateReadOnlyBuffer<Int4>(ints);
        using ReadOnlyBuffer<Half4> halfInput = device.AllocateReadOnlyBuffer<Half4>(halves);
        using ReadWriteBuffer<Half4> gpuHalf = device.AllocateReadWriteBuffer<Half4>(Count * 3), cpuHalf = device.AllocateReadWriteBuffer<Half4>(Count * 3);
        using ReadWriteBuffer<Float4> gpuFloat = device.AllocateReadWriteBuffer<Float4>(Count), cpuFloat = device.AllocateReadWriteBuffer<Float4>(Count);
        using ReadWriteBuffer<Int4> gpuInt = device.AllocateReadWriteBuffer<Int4>(Count), cpuInt = device.AllocateReadWriteBuffer<Int4>(Count);
        using ReadWriteBuffer<UInt4> gpuUInt = device.AllocateReadWriteBuffer<UInt4>(Count), cpuUInt = device.AllocateReadWriteBuffer<UInt4>(Count);

        device.For(Count, new HalfConversions(floatInput, intInput, halfInput, gpuHalf, gpuFloat, gpuInt, gpuUInt));
        device.ForCpu(Count, new HalfConversions(floatInput, intInput, halfInput, cpuHalf, cpuFloat, cpuInt, cpuUInt));

        ushort[] expectedHalf = Words(cpuHalf), actualHalf = Words(gpuHalf);
        string[] names = ["(Half4)Float4", "(Half4)Int4", "(Half4)UInt4"];

        for (int index = 0; index < expectedHalf.Length; index++)
        {
            Assert.True(
                Same(expectedHalf[index], actualHalf[index]),
                $"{names[index / 4 % 3]}, thread {index / 12}, component {index % 4}: CPU 0x{expectedHalf[index]:X4}, GPU 0x{actualHalf[index]:X4}");
        }

        // Half to float is exact, so the bits match too (a NaN stays a NaN).
        uint[] expectedFloat = MemoryMarshal.Cast<Float4, uint>(cpuFloat.AsSpan()).ToArray(), actualFloat = MemoryMarshal.Cast<Float4, uint>(gpuFloat.AsSpan()).ToArray();

        for (int index = 0; index < expectedFloat.Length; index++)
        {
            float e = BitConverter.UInt32BitsToSingle(expectedFloat[index]), g = BitConverter.UInt32BitsToSingle(actualFloat[index]);
            Assert.True(expectedFloat[index] == actualFloat[index] || (float.IsNaN(e) && float.IsNaN(g)), $"(Float4)Half4, thread {index / 4}, component {index % 4}: CPU {e:R}, GPU {g:R}");
        }

        Assert.Equal(MemoryMarshal.Cast<Int4, int>(cpuInt.AsSpan()).ToArray(), MemoryMarshal.Cast<Int4, int>(gpuInt.AsSpan()).ToArray());
        Assert.Equal(MemoryMarshal.Cast<UInt4, uint>(cpuUInt.AsSpan()).ToArray(), MemoryMarshal.Cast<UInt4, uint>(gpuUInt.AsSpan()).ToArray());

        // The definitions, restated: ties to even, infinity from 65520, saturation and NaN to 0.
        Half4[] toHalf = cpuHalf.ToArray();
        Assert.True(Half.IsNaN(toHalf[0].X));
        Assert.Equal([0x7C00, 0xFC00, 0x8000], new[] { Bits(toHalf[0].Y), Bits(toHalf[0].Z), Bits(toHalf[0].W) });
        Assert.Equal([0x7C00, 0x7BFF, 0xFC00, 0x0000], new[] { Bits(toHalf[3].X), Bits(toHalf[3].Y), Bits(toHalf[3].Z), Bits(toHalf[3].W) });
        Assert.Equal([0xFC00, 0x7C00, 0x7BFF, 0x7C00], new[] { Bits(toHalf[1].X), Bits(toHalf[1].Y), Bits(toHalf[1].Z), Bits(toHalf[1].W) });
        Assert.Equal(new Half4((Half)2048f, (Half)2052f, (Half)(-2048f), (Half)(-1f)), toHalf[4]);
        Assert.Equal(new Half4((Half)2048f, (Half)2052f, Half.PositiveInfinity, Half.PositiveInfinity), toHalf[5]);

        Int4[] toInt = cpuInt.ToArray();
        UInt4[] toUInt = cpuUInt.ToArray();
        Assert.Equal(new Int4(0, 0, 1, -1), new Int4(toInt[0].X, toInt[0x8000].X, toInt[0x3C00].X, toInt[0xBC00].X));
        Assert.Equal(new Int4(int.MaxValue, int.MinValue, 0, 65504), new Int4(toInt[0x7C00].X, toInt[0xFC00].X, toInt[0x7E00].X, toInt[0x7BFF].X));
        Assert.Equal(new UInt4(uint.MaxValue, 0u, 0u, 1u), new UInt4(toUInt[0x7C00].X, toUInt[0xFC00].X, toUInt[0xBC00].X, toUInt[0x3FFF].X));
    }

    // ---- the boundary -----------------------------------------------------------------------------

    [MetalFact]
    public void HalfVectors_InBuffersConstantsStructsAndGroupSharedMemory_MatchPlainCSharp()
    {
        const int N = 256;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Random random = new(16);

        // Values that are exact in every step, so the expected result needs no rounding argument:
        // multiples of 1/8 below 16, scaled by powers of two.
        Half Next() => (Half)(random.Next(-127, 128) / 8f);
        Half2[] twos = Enumerable.Range(0, N).Select(_ => new Half2(Next(), Next())).ToArray();
        Half3[] threes = Enumerable.Range(0, N).Select(_ => new Half3(Next(), Next(), Next())).ToArray();
        HalfTexel[] texels = Enumerable.Range(0, N).Select(i => new HalfTexel { Color = new Half3(Next()), Weight = i, Uv = new Half2(Next()), Bias = Next(), Tint = new Half4(Next()) }).ToArray();
        var scale = new Half2((Half)2f, (Half)0.5f);
        var offset = new Half3((Half)1f, (Half)(-2f), (Half)0.25f);
        var tint = new Half4((Half)0.125f, (Half)(-0.25f), (Half)4f, (Half)(-8f));
        Half exposure = (Half)1.5f;
        const float Gain = 0.7f;

        Assert.Equal(6, System.Runtime.CompilerServices.Unsafe.SizeOf<Half3>());
        Assert.Equal(28, System.Runtime.CompilerServices.Unsafe.SizeOf<HalfTexel>());

        (Half4[] Fours, HalfTexel[] Texels, Half3[] Sums) Run(bool onGpu)
        {
            using ReadOnlyBuffer<Half2> twoBuffer = device.AllocateReadOnlyBuffer<Half2>(twos);
            using ReadOnlyBuffer<Half3> threeBuffer = device.AllocateReadOnlyBuffer<Half3>(threes);
            using ReadWriteBuffer<Half4> fourBuffer = device.AllocateReadWriteBuffer<Half4>(N);
            using ReadWriteBuffer<HalfTexel> texelBuffer = device.AllocateReadWriteBuffer<HalfTexel>(texels);
            using ReadWriteBuffer<Half3> sumBuffer = device.AllocateReadWriteBuffer<Half3>(N);
            var shader = new HalfBoundary(twoBuffer, threeBuffer, fourBuffer, texelBuffer, sumBuffer, scale, Gain, offset, exposure, tint);

            if (onGpu)
            {
                device.For(N, shader);
            }
            else
            {
                device.ForCpu(N, shader);
            }

            return (fourBuffer.ToArray(), texelBuffer.ToArray(), sumBuffer.ToArray());
        }

        var gpu = Run(onGpu: true);
        var cpu = Run(onGpu: false);

        for (int i = 0; i < N; i++)
        {
            // The same statements, in plain C#. The neighbour is the other thread of the pair (i ^ 1):
            // 256 threads in groups of 16, so the pair is always in the same threadgroup.
            Half3 three = threes[i];
            Half2 two = twos[i] * scale;
            Half3 neighbour = threes[i ^ 1] + offset;
            Half4 four = new Half4(two, three.Z, exposure) + tint;
            Half3 color = neighbour * HalfBoundary.Weights;
            Half3 sum = color + three.ZYX;
            sum.Y = neighbour.X;

            foreach ((string side, (Half4[] Fours, HalfTexel[] Texels, Half3[] Sums) result) in new[] { ("GPU", gpu), ("CPU", cpu) })
            {
                Assert.True(four.Equals(result.Fours[i]), $"{side} fours[{i}]: expected {four}, got {result.Fours[i]}");
                Assert.True(sum.Equals(result.Sums[i]), $"{side} sums[{i}]: expected {sum}, got {result.Sums[i]}");

                HalfTexel texel = result.Texels[i];
                Assert.True(color.Equals(texel.Color), $"{side} texels[{i}].Color: expected {color}, got {texel.Color}");
                Assert.Equal((float)three.X, texel.Weight);
                Assert.True(two.YX.Equals(texel.Uv), $"{side} texels[{i}].Uv: expected {two.YX}, got {texel.Uv}");
                Assert.Equal((Half)Gain, texel.Bias);
                Assert.True(four.WZYX.Equals(texel.Tint), $"{side} texels[{i}].Tint: expected {four.WZYX}, got {texel.Tint}");
            }
        }
    }

    [MetalFact]
    public void Rgba16FloatPixels_RoundTripThroughHalf4_BitForBit()
    {
        const int Width = 64, Height = 48;
        GraphicsDevice device = GraphicsDevice.GetDefault();
        Random random = new(17);

        // Every pixel is four arbitrary finite halves, subnormals included: a Half4 array, uploaded as it is.
        ushort Finite()
        {
            ushort bits = (ushort)random.Next(65536);
            return (bits & 0x7C00) == 0x7C00 ? (ushort)(bits & 0x83FF) : bits;
        }

        Half4[] colors = Enumerable.Range(0, Width * Height).Select(_ => new Half4(FromBits(Finite()), FromBits(Finite()), FromBits(Finite()), FromBits(Finite()))).ToArray();
        var gain = new Half4((Half)2f, (Half)0.5f, (Half)(-1f), (Half)1.5f);

        Half4[] Run(bool onGpu)
        {
            using ReadWriteTexture2D<Rgba16Float> image = device.AllocateReadWriteTexture2D<Rgba16Float>(MemoryMarshal.Cast<Half4, Rgba16Float>(colors), Width, Height);

            if (onGpu)
            {
                device.For(Width, Height, new HalfTextureGain(image, gain));
            }
            else
            {
                device.ForCpu(Width, Height, new HalfTextureGain(image, gain));
            }

            return MemoryMarshal.Cast<Rgba16Float, Half4>(image.ToArray()).ToArray();
        }

        Half4[] gpu = Run(onGpu: true), cpu = Run(onGpu: false);

        // (Half4)pixel is exact (the texture stores halves), the product is one half multiplication,
        // and writing it back stores a value that is a half already: nothing rounds on the way.
        ushort[] expected = MemoryMarshal.Cast<Half4, ushort>(colors.Select(color => color * gain).ToArray().AsSpan()).ToArray();
        ushort[] gpuWords = MemoryMarshal.Cast<Half4, ushort>(gpu.AsSpan()).ToArray(), cpuWords = MemoryMarshal.Cast<Half4, ushort>(cpu.AsSpan()).ToArray();

        for (int index = 0; index < expected.Length; index++)
        {
            Assert.True(expected[index] == cpuWords[index], $"CPU pixel {index / 4}, channel {index % 4}: expected 0x{expected[index]:X4}, got 0x{cpuWords[index]:X4}");
            Assert.True(expected[index] == gpuWords[index], $"GPU pixel {index / 4}, channel {index % 4}: expected 0x{expected[index]:X4}, got 0x{gpuWords[index]:X4}");
        }
    }
}
