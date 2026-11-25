using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SharpCompute.Tests.Conformance;

/// <summary>What a slot holds: an integer or a bool (as 0 or 1), or the bits of a float or a half.</summary>
internal enum ResultKind
{
    Integer,
    Float,
    Half,
}

/// <summary>How the GPU's value of a slot is compared with ForCpu's (see tools/gen_conformance.py).</summary>
internal enum Check
{
    /// <summary>The same value; for floats and halves the same bits, except that every NaN equals every NaN.</summary>
    Exact,

    /// <summary>
    /// <see cref="Exact"/>, or what ForCpu computes from the operands with their float subnormals
    /// flushed to (signed) zero, with a subnormal result flushed as well: an Apple GPU's float
    /// arithmetic. The GPU decides tininess before rounding, so a result that rounds up to the
    /// smallest normal from below is flushed too: a CPU result of ±2^-126 also accepts ±0.
    /// </summary>
    Ftz,

    /// <summary>Within <see cref="ConformanceSlot.Ulps"/> units in the last place of the exact or the flushed result.</summary>
    Ulp,

    /// <summary>
    /// The same number as the exact or the flushed result once subnormals are flushed on both sides,
    /// with -0 equal to +0: Min, Max and Clamp, which compare flushed operands on the GPU but return
    /// one of them unflushed, and whose zero for zeros of both signs IEEE leaves open.
    /// </summary>
    Value,

    /// <summary>
    /// <see cref="Ulp"/>, except where an operand is a nonzero float within 2^-64 of zero or both
    /// results are within 2^-120 of it: there an intermediate result of a composite function (Lerp,
    /// Smoothstep, Dot, Normalize, ...) may be subnormal, which the GPU flushes, and the result is
    /// not compared.
    /// </summary>
    Near,

    /// <summary>Runs on both sides, so it must not crash or hang, but the values are not compared.</summary>
    Unspecified,
}

/// <summary>One expression of a family: its C#, the kind of its value and the comparison policy.</summary>
internal sealed record ConformanceSlot(string Name, ResultKind Kind, Check Check, int Ulps = 0, string? Reason = null);

/// <summary>Runs a family's shader over <paramref name="threads"/> threads, on the GPU or with ForCpu.</summary>
internal delegate void ConformanceDispatch<T>(
    GraphicsDevice device,
    bool cpu,
    int threads,
    ReadOnlyBuffer<T> a,
    ReadOnlyBuffer<T> b,
    ReadOnlyBuffer<T> c,
    ReadOnlyBuffer<int> s,
    ReadWriteBuffer<long> output)
    where T : unmanaged;

/// <summary>A family without its element type: what the tests and the coverage summary need.</summary>
internal interface IConformanceFamily
{
    string Title { get; }

    int Lanes { get; }

    IReadOnlyList<ConformanceSlot> Slots { get; }

    /// <summary>
    /// Runs the family on the GPU and with ForCpu and compares every slot of every thread. With
    /// <paramref name="mutate"/>, every GPU value is changed first (an integer by one, a float or a
    /// half in its exponent), which every compared slot must then report.
    /// </summary>
    ConformanceResult Run(GraphicsDevice device, bool mutate = false);
}

/// <summary>A generated shader and the slots it writes, over operands of type <typeparamref name="T"/>.</summary>
internal sealed class ConformanceFamily<T>(string title, string values, int lanes, ConformanceSlot[] slots, ConformanceDispatch<T> dispatch) : IConformanceFamily
    where T : unmanaged
{
    public string Title { get; } = title;

    public int Lanes { get; } = lanes;

    public IReadOnlyList<ConformanceSlot> Slots { get; } = slots;

    public ConformanceResult Run(GraphicsDevice device, bool mutate = false) => ConformanceHarness.Run(device, this, ConformanceValues.For<T>(values), dispatch, mutate);
}

/// <summary>The outcome of one family: how much was compared, and a table of what differed.</summary>
internal sealed record ConformanceResult(string Title, int Threads, int Slots, int ComparedSlots, long Comparisons, long Flushed, int FailedSlots, string Report)
{
    public bool Passed => FailedSlots == 0;
}

/// <summary>
/// Runs a family's shader on the GPU and with ForCpu over the same operands and compares every slot
/// with its policy. Floats are compared NaN-sign-insensitively: the GPU writes a positive canonical
/// NaN where .NET's <see cref="float.NaN"/> has its sign bit set, and IEEE leaves the sign and
/// payload of a NaN result open.
/// </summary>
internal static class ConformanceHarness
{
    /// <summary>The shift counts each operand meets: inside the width, at and beyond it, and negative.</summary>
    public static readonly int[] ShiftCounts = [0, 1, 2, 7, 8, 9, 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, -1, -7, -31, -32, -33, -64, int.MaxValue, int.MinValue];

    private const int MaxExamples = 3;

    public static void Dispatch<TShader>(GraphicsDevice device, bool cpu, int threads, in TShader shader)
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

    /// <summary>
    /// Thread <c>i * M + j</c> takes <c>x = values[i]</c>, <c>y = values[j % N]</c> and the shift
    /// count <c>ShiftCounts[j % ShiftCounts.Length]</c>, with <c>M = max(N, ShiftCounts.Length)</c>: lane
    /// 0 of every vector family sees every pair of values and every value every shift count. The
    /// other lanes and <c>z</c> are drawn from the values with a fixed seed.
    /// </summary>
    public static ConformanceResult Run<T>(GraphicsDevice device, IConformanceFamily family, ConformanceValues.Set<T> values, ConformanceDispatch<T> dispatch, bool mutate = false)
        where T : unmanaged
    {
        T[] v = values.Values;
        int n = v.Length, m = Math.Max(n, ShiftCounts.Length), threads = n * m, lanes = family.Lanes, slots = family.Slots.Count;
        T[] a = new T[threads * lanes], b = new T[threads * lanes], c = new T[threads * lanes];
        int[] s = new int[threads];
        Random random = new(1729 + n);

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                int k = (i * m) + j;
                s[k] = ShiftCounts[j % ShiftCounts.Length];

                for (int lane = 0; lane < lanes; lane++)
                {
                    a[(k * lanes) + lane] = lane == 0 ? v[i] : v[random.Next(n)];
                    b[(k * lanes) + lane] = lane == 0 ? v[j % n] : v[random.Next(n)];
                    c[(k * lanes) + lane] = lane == 0 ? v[((i * 7) + (j * 3) + 1) % n] : v[random.Next(n)];
                }
            }
        }

        long[] gpu = Execute(device, dispatch, false, threads, slots, a, b, c, s);

        if (mutate)
        {
            for (int at = 0; at < gpu.Length; at++)
            {
                gpu[at] = family.Slots[at % slots].Kind switch
                {
                    ResultKind.Float => gpu[at] ^ 0x40000000L,
                    ResultKind.Half => gpu[at] ^ 0x4000L,
                    _ => gpu[at] + 1,
                };
            }
        }
        long[] cpu = Execute(device, dispatch, true, threads, slots, a, b, c, s);
        long[]? flushed = null;

        if (values.Flush is { } flush)
        {
            // The same operands with their float subnormals flushed: the model of the GPU's float arithmetic.
            flushed = Execute(device, dispatch, true, threads, slots, [.. a.Select(flush)], [.. b.Select(flush)], [.. c.Select(flush)], s);
        }

        long comparisons = 0, flushedMatches = 0;
        int failedSlots = 0, comparedSlots = 0;
        StringBuilder report = new();

        for (int slot = 0; slot < slots; slot++)
        {
            ConformanceSlot info = family.Slots[slot];

            if (info.Check == Check.Unspecified)
            {
                continue;
            }

            comparedSlots++;
            int differ = 0;
            List<string> examples = [];

            for (int k = 0; k < threads; k++)
            {
                int at = (k * slots) + slot;
                long g = gpu[at], e = cpu[at];
                long? f = flushed?[at];
                comparisons++;

                if (info.Check == Check.Near && (Tiny(a, k, lanes) || Tiny(b, k, lanes) || Tiny(c, k, lanes)))
                {
                    comparisons--;
                    continue;
                }

                long model = FlushResult(info.Kind, f ?? e);

                if (info.Check == Check.Near && NearZero(info.Kind, g) && NearZero(info.Kind, e))
                {
                    comparisons--;
                    continue;
                }

                bool same = info.Check switch
                {
                    Check.Exact => Same(info.Kind, g, e),
                    Check.Ftz => Same(info.Kind, g, e) || Same(info.Kind, g, model) || (info.Kind == ResultKind.Float && ((uint)model & 0x7FFFFFFFu) == 0x00800000u && (uint)g == ((uint)model & 0x80000000u)),
                    Check.Value => SameNumber(info.Kind, FlushResult(info.Kind, g), FlushResult(info.Kind, e)) || SameNumber(info.Kind, FlushResult(info.Kind, g), model),
                    _ => Close(info.Kind, g, e, info.Ulps) || Close(info.Kind, g, model, info.Ulps),
                };

                if (same && info.Check != Check.Exact && !Same(info.Kind, g, e))
                {
                    flushedMatches++;
                }

                if (!same)
                {
                    differ++;

                    if (examples.Count < MaxExamples)
                    {
                        string operands = $"x = {Operand(a, k, lanes)}, y = {Operand(b, k, lanes)}, z = {Operand(c, k, lanes)}, n = {s[k]}";
                        string flushedText = f is { } fv ? $", CPU on flushed operands {Format(info.Kind, FlushResult(info.Kind, fv))}" : string.Empty;
                        examples.Add($"      {operands}: GPU {Format(info.Kind, g)}, CPU {Format(info.Kind, e)}{flushedText}");
                    }
                }
            }

            if (differ > 0)
            {
                failedSlots++;
                string policy = info.Check is Check.Ulp or Check.Near ? $"{info.Check}, within {info.Ulps} ULP" : info.Check.ToString();
                report.AppendLine(CultureInfo.InvariantCulture, $"  | {info.Name} | {policy} | {differ} of {threads} threads differ |");
                examples.ForEach(line => report.AppendLine(line));
            }
        }

        if (failedSlots > 0)
        {
            report.Insert(0, $"{family.Title}: {failedSlots} of {slots} slots differ between the GPU and ForCpu\n  | slot | check | |\n");
        }

        return new ConformanceResult(family.Title, threads, slots, comparedSlots, comparisons, flushedMatches, failedSlots, report.ToString());
    }

    private static long[] Execute<T>(GraphicsDevice device, ConformanceDispatch<T> dispatch, bool cpu, int threads, int slots, T[] a, T[] b, T[] c, int[] s)
        where T : unmanaged
    {
        using ReadOnlyBuffer<T> ab = device.AllocateReadOnlyBuffer(a);
        using ReadOnlyBuffer<T> bb = device.AllocateReadOnlyBuffer(b);
        using ReadOnlyBuffer<T> cb = device.AllocateReadOnlyBuffer(c);
        using ReadOnlyBuffer<int> sb = device.AllocateReadOnlyBuffer(s);
        using ReadWriteBuffer<long> output = device.AllocateReadWriteBuffer<long>(threads * slots);

        dispatch(device, cpu, threads, ab, bb, cb, sb, output);
        return output.ToArray();
    }

    // ---- comparisons ----------------------------------------------------------------------------

    public static bool Same(ResultKind kind, long gpu, long cpu) => kind switch
    {
        ResultKind.Float => (uint)gpu == (uint)cpu || (IsNaN32((uint)gpu) && IsNaN32((uint)cpu)),
        ResultKind.Half => (ushort)gpu == (ushort)cpu || (IsNaN16((ushort)gpu) && IsNaN16((ushort)cpu)),
        _ => gpu == cpu,
    };

    /// <summary>The same number: any NaN equals any NaN, and -0 equals +0.</summary>
    private static bool SameNumber(ResultKind kind, long gpu, long cpu) => kind switch
    {
        ResultKind.Float => Same(kind, gpu, cpu) || (((uint)gpu | (uint)cpu) & 0x7FFFFFFFu) == 0,
        ResultKind.Half => Same(kind, gpu, cpu) || (((ushort)gpu | (ushort)cpu) & 0x7FFF) == 0,
        _ => gpu == cpu,
    };

    /// <summary>Whether a float result is within 2^-120 of zero (a half result never is, for this purpose).</summary>
    private static bool NearZero(ResultKind kind, long value) =>
        kind == ResultKind.Float && MathF.Abs(BitConverter.UInt32BitsToSingle((uint)value)) < 7.5231638e-37f;

    /// <summary>Whether a float operand of the thread is nonzero and within 2^-64 of zero.</summary>
    private static bool Tiny<T>(T[] values, int thread, int lanes)
        where T : unmanaged
    {
        for (int lane = 0; lane < lanes; lane++)
        {
            if (values[(thread * lanes) + lane] is float v && v != 0f && MathF.Abs(v) < 5.421011e-20f)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Close(ResultKind kind, long gpu, long cpu, int ulps) => kind switch
    {
        ResultKind.Float => Same(kind, gpu, cpu) || (!IsNaN32((uint)gpu) && !IsNaN32((uint)cpu) && Math.Abs(Ordered32((uint)gpu) - Ordered32((uint)cpu)) <= ulps),
        ResultKind.Half => Same(kind, gpu, cpu) || (!IsNaN16((ushort)gpu) && !IsNaN16((ushort)cpu) && Math.Abs(Ordered16((ushort)gpu) - Ordered16((ushort)cpu)) <= ulps),
        _ => gpu == cpu,
    };

    /// <summary>A float result as the GPU stores it: a subnormal becomes a zero of the same sign.</summary>
    private static long FlushResult(ResultKind kind, long value) =>
        kind == ResultKind.Float && ((uint)value & 0x7F800000u) == 0 ? (uint)value & 0x80000000u : value;

    private static bool IsNaN32(uint bits) => (bits & 0x7FFFFFFFu) > 0x7F800000u;

    private static bool IsNaN16(ushort bits) => (bits & 0x7FFF) > 0x7C00;

    /// <summary>The bits as a signed integer that orders like the floats, with both zeros at 0.</summary>
    private static long Ordered32(uint bits) => (bits & 0x80000000u) != 0 ? -(long)(bits & 0x7FFFFFFFu) : bits;

    private static long Ordered16(ushort bits) => (bits & 0x8000) != 0 ? -(long)(bits & 0x7FFF) : bits;

    // ---- formatting -----------------------------------------------------------------------------

    private static string Format(ResultKind kind, long value) => kind switch
    {
        ResultKind.Float => $"{BitConverter.UInt32BitsToSingle((uint)value).ToString("R", CultureInfo.InvariantCulture)} (0x{(uint)value:X8})",
        ResultKind.Half => $"{((float)BitConverter.UInt16BitsToHalf((ushort)value)).ToString("R", CultureInfo.InvariantCulture)} (0x{(ushort)value:X4})",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    private static string Operand<T>(T[] values, int thread, int lanes)
        where T : unmanaged
    {
        string One(T value) => value switch
        {
            float f => $"{f.ToString("R", CultureInfo.InvariantCulture)} (0x{BitConverter.SingleToUInt32Bits(f):X8})",
            Half h => $"{((float)h).ToString("R", CultureInfo.InvariantCulture)} (0x{BitConverter.HalfToUInt16Bits(h):X4})",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        return lanes == 1
            ? One(values[thread])
            : "(" + string.Join(", ", Enumerable.Range(0, lanes).Select(lane => One(values[(thread * lanes) + lane]))) + ")";
    }
}
