using System;
using SharpCompute;

namespace SharpCompute.Tests.Shaders;

// Shaders for the subnormal tests (SubnormalFlushTests). Each thread takes one triple of floats
// (x, y, z), the bits of x, an int and a half, and writes what every operation of the shader
// language gives for them: float results to `reals`, everything else (comparisons, conversions to
// integers and halves, bit casts) to `words`. The tests compare the GPU's values with plain C# on
// the operands as they are and with their subnormals flushed to zero.

/// <summary>Every float operation that a subnormal operand or result can meet, one slot each.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct SubnormalProbe(
    ReadOnlyBuffer<float> xs,
    ReadOnlyBuffer<float> ys,
    ReadOnlyBuffer<float> zs,
    ReadOnlyBuffer<uint> xBits,
    ReadOnlyBuffer<int> ints,
    ReadOnlyBuffer<Half> halves,
    ReadWriteBuffer<float> reals,
    ReadWriteBuffer<uint> words) : IComputeShader
{
    /// <summary>The float slots each thread writes (their names are in SubnormalFlushTests).</summary>
    public const int RealSlots = 45;

    /// <summary>The integer slots each thread writes.</summary>
    public const int WordSlots = 9;

    public void Execute()
    {
        int t = ThreadIds.X;
        float x = xs[t], y = ys[t], z = zs[t];
        int n = ints[t];
        Half h = halves[t];
        int r = t * RealSlots, w = t * WordSlots;

        reals[r + 0] = x;
        reals[r + 1] = Gpu.AsFloat(xBits[t]);
        reals[r + 2] = x + y;
        reals[r + 3] = x - y;
        reals[r + 4] = x * y;
        reals[r + 5] = x / y;
        reals[r + 6] = Gpu.Fma(x, y, z);
        reals[r + 7] = (x * y) + z;
        reals[r + 8] = Gpu.Min(x, y);
        reals[r + 9] = Gpu.Max(x, y);
        reals[r + 10] = MathF.Min(x, y);
        reals[r + 11] = MathF.Max(x, y);
        reals[r + 12] = Gpu.Clamp(x, y, z);
        reals[r + 13] = Gpu.Abs(x);
        reals[r + 14] = -x;
        reals[r + 15] = Gpu.CopySign(x, y);
        reals[r + 16] = n != 0 ? x : y;
        reals[r + 17] = x % y;
        reals[r + 18] = Gpu.Floor(x);
        reals[r + 19] = Gpu.Trunc(x);
        reals[r + 20] = n;
        reals[r + 21] = (float)h;
        reals[r + 22] = Gpu.Sqrt(x);
        reals[r + 23] = Gpu.Rsqrt(x);
        reals[r + 24] = Gpu.Exp(x);
        reals[r + 25] = Gpu.Exp2(x);
        reals[r + 26] = Gpu.Log(x);
        reals[r + 27] = Gpu.Log2(x);
        reals[r + 28] = Gpu.Sin(x);
        reals[r + 29] = Gpu.Cos(x);
        reals[r + 30] = Gpu.Tanh(x);
        reals[r + 31] = Gpu.Pow(x, y);
        reals[r + 32] = Gpu.Ceil(x);
        reals[r + 33] = Gpu.Round(x);
        reals[r + 34] = Gpu.Fract(x);
        reals[r + 35] = Gpu.Sign(x);
        reals[r + 36] = Gpu.Tan(x);
        reals[r + 37] = Gpu.Asin(x);
        reals[r + 38] = Gpu.Acos(x);
        reals[r + 39] = Gpu.Atan(x);
        reals[r + 40] = Gpu.Sinh(x);
        reals[r + 41] = Gpu.Cosh(x);
        reals[r + 42] = Gpu.Exp10(x);
        reals[r + 43] = Gpu.Log10(x);
        reals[r + 44] = Gpu.Atan2(x, y);

        words[w + 0] = x < y ? 1u : 0u;
        words[w + 1] = x <= y ? 1u : 0u;
        words[w + 2] = x == y ? 1u : 0u;
        words[w + 3] = x != y ? 1u : 0u;
        words[w + 4] = (uint)(int)x;
        words[w + 5] = (uint)x;
        words[w + 6] = Gpu.AsUInt(x);
        words[w + 7] = (uint)Gpu.AsInt(x);
        words[w + 8] = BitConverter.HalfToUInt16Bits((Half)x);
    }
}
