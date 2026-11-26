using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SharpCompute.Tests;

namespace SharpCompute.Differential.Tests;

/// <summary>
/// The model's tininess before rounding (<see cref="GpuModel.FlushProduct(float, float, float)"/>,
/// <see cref="GpuModel.FlushQuotient(float, float, float)"/> and
/// <see cref="GpuModel.FlushFma(float, float, float, float)"/>), which decide in floats whether an
/// exact product, quotient or fused multiply-add is below the smallest normal. They are compared
/// with the same decision made in exact integer arithmetic, and with what the GPU computes, over
/// operands whose results are at the smallest normal and its neighbours.
/// </summary>
[Trait("Category", "Differential")]
public class GpuModelTests
{
    private const float SmallestNormal = 1.17549435E-38f;

    [Fact]
    public void TininessBeforeRounding_IsDecidedAsInExactArithmetic()
    {
        (float A, float B, float C)[] operands = Operands();
        List<string> wrong = [];
        int flushedAtTheBound = 0;

        foreach ((float a, float b, float c) in operands)
        {
            Check("a * b", GpuModel.FlushProduct(a * b, a, b), Expected(a * b, Compare(Product(a, b), Exact.One)));
            Check("a / b", GpuModel.FlushQuotient(a / b, a, b), Expected(a / b, Compare(Exact.Of(a), Exact.Of(b))));
            Check("fma(a, b, c)", GpuModel.FlushFma(MathF.FusedMultiplyAdd(a, b, c), a, b, c), Expected(MathF.FusedMultiplyAdd(a, b, c), Compare(Product(a, b).Plus(Exact.Of(c)), Exact.One)));

            void Check(string what, float model, float expected)
            {
                if (BitConverter.SingleToUInt32Bits(model) != BitConverter.SingleToUInt32Bits(expected) && !(float.IsNaN(model) && float.IsNaN(expected)))
                {
                    wrong.Add($"{what} of {Describe(a)}, {Describe(b)}, {Describe(c)}: model {Describe(model)}, exact {Describe(expected)}");
                }
            }
        }

        Assert.True(wrong.Count == 0, $"{wrong.Count} results differ:\n" + string.Join("\n", wrong.Take(30)));

        // The operands must reach the case the helpers exist for: a result of ±2^-126 that is flushed.
        Assert.True(flushedAtTheBound > 50, $"only {flushedAtTheBound} results at the smallest normal were flushed");

        // What the GPU gives for a rounded result, given how the exact value compares with 2^-126 times the divisor.
        float Expected(float rounded, int comparison)
        {
            if (float.IsNaN(rounded) || float.IsInfinity(rounded) || comparison >= 0)
            {
                return rounded;
            }

            flushedAtTheBound += MathF.Abs(rounded) == SmallestNormal ? 1 : 0;
            return BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(rounded) & 0x80000000u);
        }
    }

    /// <summary>The GPU flushes a product, a quotient and a fused multiply-add exactly where the model says: it decides tininess before rounding.</summary>
    [MetalFact]
    public void TheGpu_FlushesWhatTheModelFlushes()
    {
        (float A, float B, float C)[] operands = Operands();
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<Float3> input = device.AllocateReadOnlyBuffer(operands.Select(static o => new Float3(o.A, o.B, o.C)).ToArray());
        using ReadWriteBuffer<uint> output = device.AllocateReadWriteBuffer<uint>(operands.Length * 3);
        device.For(operands.Length, new TininessProbe(input, output));
        uint[] gpu = output.ToArray();
        List<string> wrong = [];

        for (int i = 0; i < operands.Length; i++)
        {
            // The GPU reads a subnormal operand as a zero.
            float a = GpuModel.Flush(operands[i].A), b = GpuModel.Flush(operands[i].B), c = GpuModel.Flush(operands[i].C);
            float[] model = [GpuModel.FlushProduct(a * b, a, b), GpuModel.FlushQuotient(a / b, a, b), GpuModel.FlushFma(MathF.FusedMultiplyAdd(a, b, c), a, b, c)];

            for (int k = 0; k < 3; k++)
            {
                float value = BitConverter.UInt32BitsToSingle(gpu[(i * 3) + k]);

                if (gpu[(i * 3) + k] != BitConverter.SingleToUInt32Bits(model[k]) && !(float.IsNaN(value) && float.IsNaN(model[k])))
                {
                    wrong.Add($"{new[] { "a * b", "a / b", "fma(a, b, c)" }[k]} of {Describe(a)}, {Describe(b)}, {Describe(c)}: GPU {Describe(value)}, model {Describe(model[k])}");
                }
            }
        }

        Assert.True(wrong.Count == 0, $"{wrong.Count} of {operands.Length * 3} results differ:\n" + string.Join("\n", wrong.Take(30)));
    }

    private static string Describe(float x) => $"{x:R} (0x{BitConverter.SingleToUInt32Bits(x):X8})";

    /// <summary>
    /// Operands whose product, quotient or fused multiply-add is the smallest normal or within a few
    /// units in the last place of it, from a fixed seed, with the plain cases (exact powers of two,
    /// zeros, the largest subnormal) among them.
    /// </summary>
    private static (float A, float B, float C)[] Operands()
    {
        Random random = new(20261126);
        List<(float, float, float)> list =
        [
            (0.99999994f, SmallestNormal, 0f), (1f, SmallestNormal, 0f), (1.0000001f, SmallestNormal, 0f), (-0.99999994f, SmallestNormal, 0f),
            (MathF.ScaleB(1f, -63), MathF.ScaleB(1f, -63), 0f), (SmallestNormal, 1.25f, 0f), (SmallestNormal, 0.99999994f, 0f),
            (0f, 0f, SmallestNormal), (0f, 1f, -SmallestNormal), (1f, 0f, MathF.BitDecrement(SmallestNormal)),
        ];

        float[] addends = [0f, float.Epsilon, -float.Epsilon, MathF.ScaleB(1f, -140), -MathF.ScaleB(1f, -140)];

        for (int i = 0; i < 400; i++)
        {
            float sign = random.Next(2) == 0 ? 1f : -1f;

            // A product at the bound: a, and 2^-126 / a moved by a few units in the last place.
            float a = MathF.ScaleB(1f + random.NextSingle(), random.Next(-120, -5)) * sign;
            float b = Step(SmallestNormal / a, random.Next(-3, 4));
            list.Add((a, b, addends[i % addends.Length]));

            // A quotient at the bound: b, and 2^-126 b moved by a few units in the last place.
            float divisor = MathF.ScaleB(1f + random.NextSingle(), random.Next(-22, 120));
            list.Add((Step(SmallestNormal * divisor, random.Next(-3, 4)) * sign, divisor, 0f));

            // An addend at the bound and a product far below it, of either sign.
            float tiny = MathF.ScaleB(1f + random.NextSingle(), random.Next(-100, -40));
            list.Add((tiny, MathF.ScaleB(random.Next(2) == 0 ? 1f : -1f, random.Next(-100, -30)), Step(SmallestNormal, random.Next(-1, 2)) * sign));

            // A product and an addend that cancel down to the bound.
            float x = MathF.ScaleB(1f + random.NextSingle(), random.Next(-60, -40));
            float y = MathF.ScaleB(1f + random.NextSingle(), random.Next(-60, -40));
            float c = Step((float)(MathF.ScaleB(sign, -126) - ((double)x * y)), random.Next(-1, 2));
            list.Add((x, y, c));
        }

        return [.. list];

        static float Step(float value, int ulps)
        {
            for (; ulps > 0; ulps--)
            {
                value = MathF.BitIncrement(value);
            }

            for (; ulps < 0; ulps++)
            {
                value = MathF.BitDecrement(value);
            }

            return value;
        }
    }

    private static Exact Product(float a, float b) => Exact.Of(a).Times(Exact.Of(b));

    /// <summary>
    /// The sign of |value| - 2^-126 |unit|, exactly; 0 where either is not finite (nothing is tiny then).
    /// </summary>
    private static int Compare(Exact value, Exact unit)
    {
        if (!value.IsFinite || !unit.IsFinite)
        {
            return 0;
        }

        // |m1| 2^e1 against |m2| 2^(e2 - 126): shift the one with the larger exponent.
        int e1 = value.Exponent, e2 = unit.Exponent - 126;
        int shift = Math.Min(e1, e2);
        return (BigInteger.Abs(value.Mantissa) << (e1 - shift)).CompareTo(BigInteger.Abs(unit.Mantissa) << (e2 - shift));
    }

    /// <summary>A finite float, or a product or a sum of some, as an integer times a power of two.</summary>
    private readonly record struct Exact(BigInteger Mantissa, int Exponent, bool IsFinite)
    {
        public static readonly Exact One = new(BigInteger.One, 0, true);

        public static Exact Of(float value)
        {
            if (!float.IsFinite(value))
            {
                return new Exact(BigInteger.Zero, 0, false);
            }

            uint bits = BitConverter.SingleToUInt32Bits(value);
            int exponent = (int)((bits >> 23) & 0xFF);
            long mantissa = exponent == 0 ? bits & 0x7FFFFF : (bits & 0x7FFFFF) | 0x800000;
            return new Exact((bits >> 31) != 0 ? -mantissa : mantissa, Math.Max(exponent, 1) - 150, true);
        }

        public Exact Times(Exact other) => new(Mantissa * other.Mantissa, Exponent + other.Exponent, IsFinite && other.IsFinite);

        public Exact Plus(Exact other)
        {
            int exponent = Math.Min(Exponent, other.Exponent);
            return new Exact((Mantissa << (Exponent - exponent)) + (other.Mantissa << (other.Exponent - exponent)), exponent, IsFinite && other.IsFinite);
        }
    }
}

[GeneratedComputeShaderDescriptor]
public readonly partial struct TininessProbe(ReadOnlyBuffer<Float3> input, ReadWriteBuffer<uint> output) : IComputeShader
{
    public void Execute()
    {
        Float3 v = input[ThreadIds.X];
        int o = ThreadIds.X * 3;
        output[o] = Gpu.AsUInt(v.X * v.Y);
        output[o + 1] = Gpu.AsUInt(v.X / v.Y);
        output[o + 2] = Gpu.AsUInt(Gpu.Fma(v.X, v.Y, v.Z));
    }
}
