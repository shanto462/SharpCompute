using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SharpCompute.Tests.Conformance;
using SharpCompute.Tests.Shaders;

namespace SharpCompute.Tests;

/// <summary>
/// What an Apple GPU does with a float subnormal, operation by operation, pinned against plain C#
/// (not against ForCpu, which keeps subnormals). Every operand pair of a set of subnormals, zeros,
/// the smallest normals and values that scale into or out of the subnormal range goes through
/// <see cref="SubnormalProbe"/>, and each slot must give exactly what its <see cref="Behaviour"/>
/// says: the C# result for the operands as they are, or for the operands with their subnormals
/// flushed to a zero of the same sign, with or without the result flushed as well. The table in
/// docs/shader-language.md#subnormals is this list.
/// </summary>
public class SubnormalFlushTests
{
    /// <summary>What a slot does with subnormals on the GPU.</summary>
    public enum Behaviour
    {
        /// <summary>Subnormal operands and results pass through unchanged: the C# result.</summary>
        Kept,

        /// <summary>Subnormal operands are read as zeros of the same sign; the result is never subnormal.</summary>
        FlushedOperands,

        /// <summary>
        /// Subnormal operands are read as zeros, and a subnormal result is written as a zero of the same
        /// sign. Tininess is decided before rounding: a result that rounds up to ±2^-126 from below is
        /// flushed as well.
        /// </summary>
        FlushedOperandsAndResult,

        /// <summary>The slot's own model (see <see cref="Op.Model"/>): Min, Max, Clamp, %, Pow and Atan2.</summary>
        Model,

        /// <summary>The same result whether subnormals are flushed or not: nothing to tell apart.</summary>
        Either,
    }

    /// <summary>The operands: every pair of them is one thread (x, y), with z drawn from them too.</summary>
    private static readonly float[] Operands =
    [
        .. new uint[]
        {
            0x00000000, 0x80000000,             // ±0
            0x00000001, 0x80000001,             // the smallest subnormals
            0x00400000, 0x007FFFFF, 0x807FFFFF, // half the smallest normal, the largest subnormals
            0x00800000, 0x80800000, 0x00C00000, // the smallest normals, and 1.5 times it
            0x00800001, 0x3F7FFFFF,             // just above the smallest normal; 1 - 2^-24 (times 2^-126 it rounds up to 2^-126)
        }.Select(BitConverter.UInt32BitsToSingle),
        1f, -1f, 0.5f, 2f, 1.5f, 3f, MathF.ScaleB(1f, -100), MathF.ScaleB(1f, 100), -MathF.ScaleB(1f, 100), MathF.ScaleB(1f, -24),
        -140f, -100f, -40f, 100f,
    ];

    /// <summary>The halves the threads convert: subnormals of both signs, the smallest normal, 1 and zeros.</summary>
    private static readonly ushort[] HalfBits = [0x0001, 0x8001, 0x0200, 0x03FF, 0x0400, 0x3C00, 0x0000, 0x8000];

    private const uint SmallestNormal = 0x00800000u;

    /// <summary>One thread's operands.</summary>
    public readonly record struct Args(float X, float Y, float Z, int N, Half H);

    /// <summary>
    /// A float slot of <see cref="SubnormalProbe"/>: what C# computes for it (<see cref="Raw"/>), what
    /// the GPU does with subnormals, and for <see cref="Behaviour.FlushedOperandsAndResult"/> the sign
    /// of the exact result minus the rounded one (where rounding can carry a tiny result up to the
    /// smallest normal). Transcendental functions are compared within <see cref="Ulps"/> where both
    /// results are normal, and exactly where either is zero, subnormal, infinite or NaN.
    /// </summary>
    public sealed record Op(string Name, Behaviour Behaviour, Func<Args, float> Raw, int Ulps = 0, Func<Args, float, double>? Residual = null, Func<Args, float>? Model = null)
    {
        public float Expected(Args a) => Behaviour switch
        {
            Behaviour.Kept or Behaviour.Either => Raw(a),
            Behaviour.FlushedOperands => Raw(Flushed(a)),
            Behaviour.FlushedOperandsAndResult => FlushResult(Flushed(a), Raw(Flushed(a)), Residual),
            _ => Model!(a),
        };

        /// <summary>What the other choice would give: flushed for a slot that keeps subnormals (or is the same either way), kept for one that flushes them.</summary>
        public float Opposite(Args a) => Behaviour is Behaviour.Kept or Behaviour.Either ? FlushResult(Flushed(a), Raw(Flushed(a)), Residual) : Raw(a);
    }

    /// <summary>A word slot: comparisons, conversions to integers and halves, bit casts.</summary>
    public sealed record WordOp(string Name, Behaviour Behaviour, Func<float, float, uint> Raw)
    {
        public uint Expected(Args a) => Behaviour == Behaviour.FlushedOperands ? Raw(Flush(a.X), Flush(a.Y)) : Raw(a.X, a.Y);

        public uint Opposite(Args a) => Behaviour == Behaviour.FlushedOperands ? Raw(a.X, a.Y) : Raw(Flush(a.X), Flush(a.Y));
    }

    /// <summary>The float slots of <see cref="SubnormalProbe"/>, in its order.</summary>
    public static readonly Op[] Reals =
    [
        new("load and store", Behaviour.Kept, static a => a.X),
        new("Gpu.AsFloat(uint)", Behaviour.Kept, static a => a.X),
        new("x + y", Behaviour.FlushedOperandsAndResult, static a => a.X + a.Y),
        new("x - y", Behaviour.FlushedOperandsAndResult, static a => a.X - a.Y),
        new("x * y", Behaviour.FlushedOperandsAndResult, static a => a.X * a.Y, Residual: static (a, r) => Math.FusedMultiplyAdd(a.X, (double)a.Y, -r)),
        new("x / y", Behaviour.FlushedOperandsAndResult, static a => a.X / a.Y, Residual: static (a, r) => Math.FusedMultiplyAdd(-r, a.Y, a.X) * Math.Sign(a.Y)),
        new("Gpu.Fma(x, y, z)", Behaviour.FlushedOperandsAndResult, static a => MathF.FusedMultiplyAdd(a.X, a.Y, a.Z), Residual: static (a, r) => Math.FusedMultiplyAdd(a.X, (double)a.Y, (double)a.Z - r)),
        new("x * y + z (fused)", Behaviour.FlushedOperandsAndResult, static a => MathF.FusedMultiplyAdd(a.X, a.Y, a.Z), Residual: static (a, r) => Math.FusedMultiplyAdd(a.X, (double)a.Y, (double)a.Z - r)),
        new("Gpu.Min(x, y)", Behaviour.Model, static a => Gpu.Min(a.X, a.Y), Model: static a => MetalMin(a.X, a.Y)),
        new("Gpu.Max(x, y)", Behaviour.Model, static a => Gpu.Max(a.X, a.Y), Model: static a => MetalMax(a.X, a.Y)),
        new("MathF.Min(x, y)", Behaviour.Model, static a => MathF.Min(a.X, a.Y), Model: static a => DotNetMin(a.X, a.Y)),
        new("MathF.Max(x, y)", Behaviour.Model, static a => MathF.Max(a.X, a.Y), Model: static a => DotNetMax(a.X, a.Y)),
        new("Gpu.Clamp(x, y, z)", Behaviour.Model, static a => Gpu.Clamp(a.X, a.Y, a.Z), Model: static a => MetalMin(MetalMax(a.X, a.Y), a.Z)),
        new("Gpu.Abs(x)", Behaviour.Kept, static a => MathF.Abs(a.X)),
        new("-x", Behaviour.Kept, static a => -a.X),
        new("Gpu.CopySign(x, y)", Behaviour.Kept, static a => MathF.CopySign(a.X, a.Y)),
        new("n != 0 ? x : y", Behaviour.Kept, static a => a.N != 0 ? a.X : a.Y),
        new("x % y", Behaviour.Model, static a => a.X % a.Y, Model: static a => Remainder(a.X, a.Y)),
        new("Gpu.Floor(x)", Behaviour.FlushedOperands, static a => MathF.Floor(a.X)),
        new("Gpu.Trunc(x)", Behaviour.Either, static a => MathF.Truncate(a.X)),
        new("(float)n", Behaviour.Either, static a => a.N),
        new("(float)h", Behaviour.Either, static a => (float)a.H),
        new("Gpu.Sqrt(x)", Behaviour.FlushedOperands, static a => MathF.Sqrt(a.X)),
        new("Gpu.Rsqrt(x)", Behaviour.FlushedOperands, static a => Gpu.Rsqrt(a.X)),
        new("Gpu.Exp(x)", Behaviour.FlushedOperandsAndResult, static a => MathF.Exp(a.X), Ulps: 4),
        new("Gpu.Exp2(x)", Behaviour.FlushedOperandsAndResult, static a => float.Exp2(a.X), Ulps: 4),
        new("Gpu.Log(x)", Behaviour.FlushedOperands, static a => MathF.Log(a.X), Ulps: 4),
        new("Gpu.Log2(x)", Behaviour.FlushedOperands, static a => MathF.Log2(a.X), Ulps: 4),
        new("Gpu.Sin(x)", Behaviour.Kept, static a => MathF.Sin(a.X), Ulps: 4),
        new("Gpu.Cos(x)", Behaviour.Either, static a => MathF.Cos(a.X), Ulps: 4),
        new("Gpu.Tanh(x)", Behaviour.Kept, static a => MathF.Tanh(a.X), Ulps: 4),
        new("Gpu.Pow(x, y)", Behaviour.Model, static a => MathF.Pow(a.X, a.Y), Ulps: 4, Model: static a => Power(a.X, a.Y)),
        new("Gpu.Ceil(x)", Behaviour.FlushedOperands, static a => MathF.Ceiling(a.X)),
        new("Gpu.Round(x)", Behaviour.Either, static a => Gpu.Round(a.X)),
        new("Gpu.Fract(x)", Behaviour.FlushedOperands, static a => Gpu.Fract(a.X)),
        new("Gpu.Sign(x)", Behaviour.FlushedOperands, static a => Gpu.Sign(a.X)),
        new("Gpu.Tan(x)", Behaviour.Kept, static a => MathF.Tan(a.X), Ulps: 4),
        new("Gpu.Asin(x)", Behaviour.Kept, static a => MathF.Asin(a.X), Ulps: 4),
        new("Gpu.Acos(x)", Behaviour.Either, static a => MathF.Acos(a.X), Ulps: 4),
        new("Gpu.Atan(x)", Behaviour.FlushedOperands, static a => Gpu.Atan(a.X), Ulps: 4),
        new("Gpu.Sinh(x)", Behaviour.Kept, static a => MathF.Sinh(a.X), Ulps: 4),
        new("Gpu.Cosh(x)", Behaviour.Either, static a => MathF.Cosh(a.X), Ulps: 4),
        new("Gpu.Exp10(x)", Behaviour.FlushedOperandsAndResult, static a => float.Exp10(a.X), Ulps: 4),
        new("Gpu.Log10(x)", Behaviour.FlushedOperands, static a => MathF.Log10(a.X), Ulps: 4),
        new("Gpu.Atan2(x, y)", Behaviour.Model, static a => MathF.Atan2(a.X, a.Y), Ulps: 4, Model: static a => ArcTangent2(a.X, a.Y)),
    ];

    /// <summary>The word slots of <see cref="SubnormalProbe"/>, in its order.</summary>
    public static readonly WordOp[] Words =
    [
        new("x < y", Behaviour.FlushedOperands, static (x, y) => x < y ? 1u : 0u),
        new("x <= y", Behaviour.FlushedOperands, static (x, y) => x <= y ? 1u : 0u),
        new("x == y", Behaviour.FlushedOperands, static (x, y) => x == y ? 1u : 0u),
        new("x != y", Behaviour.FlushedOperands, static (x, y) => x != y ? 1u : 0u),
        new("(int)x", Behaviour.Either, static (x, y) => (uint)(int)x),
        new("(uint)x", Behaviour.Either, static (x, y) => (uint)x),
        new("Gpu.AsUInt(x)", Behaviour.Kept, static (x, y) => BitConverter.SingleToUInt32Bits(x)),
        new("Gpu.AsInt(x)", Behaviour.Kept, static (x, y) => BitConverter.SingleToUInt32Bits(x)),
        new("(Half)x", Behaviour.Either, static (x, y) => BitConverter.HalfToUInt16Bits((Half)x)),
    ];

    private static readonly Args[] Threads = BuildThreads();

    private static readonly Lazy<(float[] Reals, uint[] Words)> GpuResults = new(RunOnGpu);

    public static IEnumerable<object[]> RealNames() => Reals.Select(static op => new object[] { op.Name });

    public static IEnumerable<object[]> WordNames() => Words.Select(static op => new object[] { op.Name });

    [MetalTheory]
    [MemberData(nameof(RealNames))]
    public void FloatResult_FlushesOrKeepsSubnormals_AsPinned(string name)
    {
        int slot = Array.FindIndex(Reals, op => op.Name == name);
        Op op = Reals[slot];
        float[] gpu = GpuResults.Value.Reals;
        List<string> mismatches = [];

        for (int k = 0; k < Threads.Length; k++)
        {
            uint expected = BitConverter.SingleToUInt32Bits(op.Expected(Threads[k]));
            uint actual = BitConverter.SingleToUInt32Bits(gpu[(k * SubnormalProbe.RealSlots) + slot]);

            if (!Matches(expected, actual, op.Ulps))
            {
                mismatches.Add($"{Describe(Threads[k])}: expected {Format(expected)}, GPU {Format(actual)}, C# {Format(BitConverter.SingleToUInt32Bits(op.Raw(Threads[k])))}");
            }
        }

        Assert.True(mismatches.Count == 0, $"{name} ({op.Behaviour}): {mismatches.Count} of {Threads.Length} results differ:\n" + string.Join("\n", mismatches.Take(20)));
    }

    [MetalTheory]
    [MemberData(nameof(WordNames))]
    public void WordResult_FlushesOrKeepsSubnormals_AsPinned(string name)
    {
        int slot = Array.FindIndex(Words, op => op.Name == name);
        WordOp op = Words[slot];
        uint[] gpu = GpuResults.Value.Words;
        string[] mismatches = Enumerable.Range(0, Threads.Length)
            .Where(k => op.Expected(Threads[k]) != gpu[(k * SubnormalProbe.WordSlots) + slot])
            .Select(k => $"{Describe(Threads[k])}: expected 0x{op.Expected(Threads[k]):X8}, GPU 0x{gpu[(k * SubnormalProbe.WordSlots) + slot]:X8}")
            .ToArray();

        Assert.True(mismatches.Length == 0, $"{name} ({op.Behaviour}): {mismatches.Length} of {Threads.Length} results differ:\n" + string.Join("\n", mismatches.Take(20)));
    }

    /// <summary>
    /// The pins are not vacuous: for every slot that keeps or flushes, the other choice gives a
    /// different result for some operands, so the GPU test above tells them apart; and every slot
    /// marked <see cref="Behaviour.Either"/> really gives the same either way.
    /// </summary>
    [Fact]
    public void EveryPin_TellsFlushingFromKeeping()
    {
        Assert.Equal(SubnormalProbe.RealSlots, Reals.Length);
        Assert.Equal(SubnormalProbe.WordSlots, Words.Length);
        Assert.Equal(Reals.Length, Reals.Select(static op => op.Name).Distinct().Count());

        foreach (Op op in Reals)
        {
            int differ = Threads.Count(a => !Matches(BitConverter.SingleToUInt32Bits(op.Expected(a)), BitConverter.SingleToUInt32Bits(op.Opposite(a)), op.Ulps));
            Assert.True(op.Behaviour == Behaviour.Either ? differ == 0 : differ > 0, $"{op.Name} ({op.Behaviour}): {differ} threads tell flushing from keeping");
        }

        foreach (WordOp op in Words)
        {
            int differ = Threads.Count(a => op.Expected(a) != op.Opposite(a));
            Assert.True(op.Behaviour == Behaviour.Either ? differ == 0 : differ > 0, $"{op.Name} ({op.Behaviour}): {differ} threads tell flushing from keeping");
        }

        // The product that rounds up to the smallest normal from below is among the operands, and is flushed.
        Op product = Reals.Single(static op => op.Name == "x * y");
        Args roundsUp = new(BitConverter.UInt32BitsToSingle(0x3F7FFFFF), BitConverter.UInt32BitsToSingle(SmallestNormal), 0f, 0, default);
        Assert.Equal(SmallestNormal, BitConverter.SingleToUInt32Bits(roundsUp.X * roundsUp.Y));
        Assert.Equal(0u, BitConverter.SingleToUInt32Bits(product.Expected(roundsUp)));
        Assert.Contains(Threads, a => a.X == roundsUp.X && a.Y == roundsUp.Y);
    }

    // ---- the models -----------------------------------------------------------------------------

    private static float Flush(float value) => ConformanceValues.FlushSubnormal(value);

    private static Args Flushed(Args a) => a with { X = Flush(a.X), Y = Flush(a.Y), Z = Flush(a.Z) };

    /// <summary>
    /// A result as the GPU writes it: a subnormal becomes a zero of the same sign, and so does ±2^-126
    /// when the exact result (<paramref name="residual"/> is its sign relative to the rounded one) was
    /// smaller in magnitude.
    /// </summary>
    private static float FlushResult(Args operands, float result, Func<Args, float, double>? residual)
    {
        uint bits = BitConverter.SingleToUInt32Bits(result);

        if ((bits & 0x7F800000u) == 0 && (bits & 0x7FFFFFFFu) != 0)
        {
            return BitConverter.UInt32BitsToSingle(bits & 0x80000000u);
        }

        if ((bits & 0x7FFFFFFFu) == SmallestNormal && residual is not null)
        {
            double e = residual(operands, result);

            if (e != 0 && Math.Sign(e) != Math.Sign(result))
            {
                return BitConverter.UInt32BitsToSingle(bits & 0x80000000u);
            }
        }

        return result;
    }

    /// <summary>metal::fmin: compares the flushed operands, returns one of them unflushed; a tie gives <paramref name="y"/>.</summary>
    private static float MetalMin(float x, float y) => float.IsNaN(x) ? y : float.IsNaN(y) ? x : Flush(x) < Flush(y) ? x : y;

    /// <summary>metal::fmax, the same way.</summary>
    private static float MetalMax(float x, float y) => float.IsNaN(x) ? y : float.IsNaN(y) ? x : Flush(x) > Flush(y) ? x : y;

    /// <summary>The prelude's .NET Min, with its comparisons on the flushed operands and its sign test on the bits.</summary>
    private static float DotNetMin(float x, float y) =>
        Flush(x) != Flush(y) ? (float.IsNaN(x) ? x : (Flush(x) < Flush(y) ? x : y)) : (float.IsNegative(x) ? x : y);

    /// <summary>The prelude's .NET Max, the same way.</summary>
    private static float DotNetMax(float x, float y) =>
        Flush(x) != Flush(y) ? (float.IsNaN(x) ? x : (Flush(y) < Flush(x) ? x : y)) : (float.IsNegative(y) ? x : y);

    /// <summary>
    /// metal::fmod: a divisor that flushes to zero gives NaN; a dividend smaller in magnitude than the
    /// divisor is returned as it is, subnormal or not; otherwise the remainder of the flushed
    /// operands, with a subnormal remainder flushed.
    /// </summary>
    private static float Remainder(float x, float y)
    {
        float fy = Flush(y);

        if (fy == 0 || float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x))
        {
            return float.NaN;
        }

        return MathF.Abs(x) < MathF.Abs(fy) ? x : Flush(Flush(x) % fy);
    }

    /// <summary>metal::pow: <c>pow(x, 1)</c> is <paramref name="x"/> as it is; otherwise the power of the flushed operands, with a subnormal result flushed.</summary>
    private static float Power(float x, float y) => y == 1f ? x : FlushResult(default, MathF.Pow(Flush(x), Flush(y)), null);

    /// <summary>metal::atan2 of the flushed operands, where a nonzero result below the smallest normal is <c>+0</c>, whatever its sign.</summary>
    private static float ArcTangent2(float y, float x)
    {
        double exact = Math.Atan2(Flush(y), Flush(x));
        return exact != 0 && Math.Abs(exact) < 1.1754943508222875e-38 ? 0f : MathF.Atan2(Flush(y), Flush(x));
    }

    // ---- running --------------------------------------------------------------------------------

    private static Args[] BuildThreads()
    {
        int n = Operands.Length;
        Args[] threads = new Args[n * n];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                int k = (i * n) + j;
                threads[k] = new Args(Operands[i], Operands[j], Operands[((i * 7) + (j * 3) + 1) % n], j % 2, BitConverter.UInt16BitsToHalf(HalfBits[k % HalfBits.Length]));
            }
        }

        return threads;
    }

    private static (float[] Reals, uint[] Words) RunOnGpu()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        int count = Threads.Length;
        using ReadOnlyBuffer<float> xs = device.AllocateReadOnlyBuffer(Threads.Select(static a => a.X).ToArray());
        using ReadOnlyBuffer<float> ys = device.AllocateReadOnlyBuffer(Threads.Select(static a => a.Y).ToArray());
        using ReadOnlyBuffer<float> zs = device.AllocateReadOnlyBuffer(Threads.Select(static a => a.Z).ToArray());
        using ReadOnlyBuffer<uint> xBits = device.AllocateReadOnlyBuffer(Threads.Select(static a => BitConverter.SingleToUInt32Bits(a.X)).ToArray());
        using ReadOnlyBuffer<int> ints = device.AllocateReadOnlyBuffer(Threads.Select(static a => a.N).ToArray());
        using ReadOnlyBuffer<Half> halves = device.AllocateReadOnlyBuffer(Threads.Select(static a => a.H).ToArray());
        using ReadWriteBuffer<float> reals = device.AllocateReadWriteBuffer<float>(count * SubnormalProbe.RealSlots);
        using ReadWriteBuffer<uint> words = device.AllocateReadWriteBuffer<uint>(count * SubnormalProbe.WordSlots);

        device.For(count, new SubnormalProbe(xs, ys, zs, xBits, ints, halves, reals, words));
        return (reals.ToArray(), words.ToArray());
    }

    // ---- comparing and reporting ----------------------------------------------------------------

    /// <summary>
    /// The same bits (any NaN equals any NaN), or for a transcendental function within
    /// <paramref name="ulps"/> where both results are normal numbers of the same sign: a zero, a
    /// subnormal or an infinity must match exactly, which is what tells flushing from keeping.
    /// </summary>
    private static bool Matches(uint expected, uint actual, int ulps)
    {
        static bool Normal(uint bits) => (bits & 0x7F800000u) is not 0 and not 0x7F800000u;

        return FloatBits.SameBits(expected, actual)
            || (ulps > 0 && Normal(expected) && Normal(actual) && (expected >> 31) == (actual >> 31) && Math.Abs((long)expected - actual) <= ulps);
    }

    private static string Format(uint bits) => $"{BitConverter.UInt32BitsToSingle(bits).ToString("R", CultureInfo.InvariantCulture)} (0x{bits:X8})";

    private static string Describe(Args a) =>
        $"x = {Format(BitConverter.SingleToUInt32Bits(a.X))}, y = {Format(BitConverter.SingleToUInt32Bits(a.Y))}, z = {Format(BitConverter.SingleToUInt32Bits(a.Z))}, n = {a.N}, h = 0x{BitConverter.HalfToUInt16Bits(a.H):X4}";
}
