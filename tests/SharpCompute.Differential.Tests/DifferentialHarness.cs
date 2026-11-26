using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpCompute.Tests.Conformance;

namespace SharpCompute.Differential.Tests;

/// <summary>How the GPU's value of a case is compared (see tools/gen_differential.py).</summary>
internal enum DifferentialCheck
{
    /// <summary>Integers and bools computed without a float or a half: the same bits.</summary>
    Exact,

    /// <summary>
    /// The same value as ForCpu's for the case or for one of its model variants (flushed, fused),
    /// NaN-sign-insensitively and up to the flushing of a float result.
    /// </summary>
    Model,

    /// <summary>
    /// <see cref="Model"/>, with -0 equal to +0: the case has a float or half Min, Max, Clamp or
    /// Saturate in it, whose zero for zeros of both signs is open, as in the conformance suite.
    /// </summary>
    Value,

    /// <summary>A transcendental function at the root: within <see cref="DifferentialCase.Ulps"/> of a model.</summary>
    Tolerance,

    /// <summary>
    /// A composite function at the root (Lerp, Smoothstep, Dot, Length, Distance, Normalize, Reflect,
    /// Cross): <see cref="Tolerance"/>, except where an intermediate result may be subnormal and
    /// flushed: on a thread where a float input of the case is nonzero and within 2^-64 of zero, as
    /// the conformance suite's Near policy does, or where both results are within 2^-100 of zero.
    /// </summary>
    Near,
}

/// <summary>
/// One generated expression: where its lanes are in the shader's output and in its model shader's
/// output, and its C# for reports.
/// </summary>
internal sealed record DifferentialCase(
    string Id,
    string Source,
    ResultKind Kind,
    int Lanes,
    DifferentialCheck Check,
    int Ulps,
    int Offset,
    int ModelOffset,
    int Variants);

/// <summary>Runs a generated shader over <paramref name="threads"/> threads, on the GPU or with ForCpu.</summary>
internal delegate void DifferentialDispatch(GraphicsDevice device, bool cpu, int threads, ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output);

/// <summary>Runs a model shader with ForCpu.</summary>
internal delegate void ModelDispatch(GraphicsDevice device, int threads, ReadOnlyBuffer<ulong> input, ReadWriteBuffer<long> output);

/// <summary>A generated shader, its cases, and the model shader of the cases that have floats or halves in them.</summary>
internal sealed record DifferentialShader(string Name, int Slots, int ModelSlots, DifferentialCase[] Cases, DifferentialDispatch Dispatch, ModelDispatch? Model);

/// <summary>One case that differed, with the first thread it differed on.</summary>
internal sealed record DifferentialMismatch(DifferentialCase Case, int Threads, int Thread, string Detail, IReadOnlyDictionary<string, string> Inputs);

/// <summary>The outcome of one shader.</summary>
internal sealed record DifferentialResult(string Name, int Cases, long Comparisons, IReadOnlyList<DifferentialMismatch> Mismatches)
{
    public bool Passed => Mismatches.Count == 0;
}

/// <summary>
/// Runs the generated shaders on the GPU and with ForCpu over the same inputs and compares every
/// case with its policy. Every shader reads the same inputs: one row of slots per thread, the
/// conformance suite's edge values mixed with random values of each type, from a fixed seed, so a
/// case shrunk into a shader of its own sees what it saw before.
/// </summary>
/// <remarks>Stateless apart from the lazily built inputs and the report file, whose writes are serialized.</remarks>
internal static partial class DifferentialHarness
{
    /// <summary>Threads per dispatch: every case meets this many rows of inputs.</summary>
    public const int Threads = 1024;

    /// <summary>When set, each mismatching case is appended to this file as a JSON line (the long mode reads it).</summary>
    public const string ReportVariable = "SHARPCOMPUTE_DIFFERENTIAL_REPORT";

    private const int InputSeed = 20261126;

    private static readonly Lazy<ulong[]> s_inputs = new(() => CreateInputs(Threads, InputSeed));

    private static readonly object s_reportLock = new();

    public static ulong[] Inputs => s_inputs.Value;

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
    /// The input rows: each slot of each thread a value of the slot's type, half of the time one of
    /// the conformance suite's edge values and otherwise a random one (random bits, a small
    /// integer, or for floats and halves a number of moderate size).
    /// </summary>
    public static ulong[] CreateInputs(int threads, int seed)
    {
        string[] slots = DifferentialShaders.InputSlots;
        Random random = new(seed);
        ulong[] rows = new ulong[threads * slots.Length];

        for (int thread = 0; thread < threads; thread++)
        {
            for (int slot = 0; slot < slots.Length; slot++)
            {
                rows[(thread * slots.Length) + slot] = Value(random, slots[slot]);
            }
        }

        return rows;
    }

    private static ulong Value(Random random, string type)
    {
        bool edge = random.Next(2) == 0;
        bool small = random.Next(3) == 0;
        ulong bits = (ulong)random.NextInt64() ^ ((ulong)random.Next(2) << 63);
        long smallValue = random.Next(-9, 10);

        return type switch
        {
            "bool" => (ulong)random.Next(2),
            "byte" => edge ? Pick(random, ConformanceValues.Bytes) : small ? (byte)smallValue : (byte)bits,
            "sbyte" => edge ? (ulong)Pick(random, ConformanceValues.SBytes) : small ? (ulong)smallValue : (ulong)(sbyte)bits,
            "short" => edge ? (ulong)Pick(random, ConformanceValues.Shorts) : small ? (ulong)smallValue : (ulong)(short)bits,
            "ushort" => edge ? Pick(random, ConformanceValues.UShorts) : small ? (ushort)smallValue : (ushort)bits,
            "int" => edge ? (ulong)Pick(random, ConformanceValues.Ints) : small ? (ulong)smallValue : (ulong)(int)bits,
            "uint" => edge ? Pick(random, ConformanceValues.UInts) : small ? (uint)smallValue : (uint)bits,
            "long" => edge ? (ulong)Pick(random, ConformanceValues.Longs) : small ? (ulong)smallValue : bits,
            "ulong" => edge ? Pick(random, ConformanceValues.ULongs) : small ? (ulong)smallValue : bits,
            "float" => BitConverter.SingleToUInt32Bits(edge ? Pick(random, ConformanceValues.Floats) : RandomFloat(random, small)),
            "Half" => BitConverter.HalfToUInt16Bits(edge ? Pick(random, ConformanceValues.Halves) : (Half)RandomFloat(random, small, 12)),
            _ => throw new ArgumentException($"No input values for '{type}'.", nameof(type)),
        };
    }

    private static T Pick<T>(Random random, T[] values) => values[random.Next(values.Length)];

    private static float RandomFloat(Random random, bool small, int range = 40)
    {
        if (small)
        {
            return random.Next(-32, 33) / (float)(1 << random.Next(4));
        }

        if (random.Next(8) == 0)
        {
            return BitConverter.UInt32BitsToSingle((uint)random.NextInt64());
        }

        float magnitude = (1f + random.NextSingle()) * MathF.ScaleB(1f, random.Next(-range, range + 1));
        return random.Next(2) == 0 ? magnitude : -magnitude;
    }

    /// <summary>
    /// Runs the shader on the GPU and with ForCpu, and its model shader with ForCpu, and compares every
    /// lane of every case on every thread. With <paramref name="mutate"/>, every GPU value is changed
    /// first (an integer in many of its bits, a float or a half in its exponent), which every case must
    /// then report. Not by one: a bool or a sign computed from a float has models that disagree by one
    /// (flushed or not), so a value off by one can still be one of them.
    /// </summary>
    public static DifferentialResult Run(GraphicsDevice device, DifferentialShader shader, bool mutate = false)
    {
        ulong[] inputs = Inputs;
        int threads = Threads;
        using ReadOnlyBuffer<ulong> input = device.AllocateReadOnlyBuffer(inputs);
        long[] gpu = Execute(device, threads, shader.Slots, output => shader.Dispatch(device, false, threads, input, output));
        long[] cpu = Execute(device, threads, shader.Slots, output => shader.Dispatch(device, true, threads, input, output));
        long[] model = shader.Model is { } run ? Execute(device, threads, shader.ModelSlots, output => run(device, threads, input, output)) : [];

        if (mutate)
        {
            foreach (DifferentialCase c in shader.Cases)
            {
                for (int thread = 0; thread < threads; thread++)
                {
                    for (int lane = 0; lane < c.Lanes; lane++)
                    {
                        int at = (thread * shader.Slots) + c.Offset + lane;
                        gpu[at] = c.Kind switch
                        {
                            ResultKind.Float => gpu[at] ^ 0x40000000L,
                            ResultKind.Half => gpu[at] ^ 0x4000L,
                            _ => gpu[at] ^ 0x5A5A5A5A5A5A5A5AL,
                        };
                    }
                }
            }
        }

        List<DifferentialMismatch> mismatches = [];
        long comparisons = 0;

        foreach (DifferentialCase c in shader.Cases)
        {
            int differ = 0, first = -1;
            string detail = string.Empty;
            int[] floatSlots = c.Check == DifferentialCheck.Near ? FloatSlots(c) : [];

            for (int thread = 0; thread < threads; thread++)
            {
                if (HasTinyInput(thread, floatSlots))
                {
                    continue;
                }

                for (int lane = 0; lane < c.Lanes; lane++)
                {
                    comparisons++;
                    long g = gpu[(thread * shader.Slots) + c.Offset + lane];
                    long e = cpu[(thread * shader.Slots) + c.Offset + lane];
                    long[] models = new long[c.Variants];

                    for (int v = 0; v < c.Variants; v++)
                    {
                        models[v] = model[(thread * shader.ModelSlots) + c.ModelOffset + (v * c.Lanes) + lane];
                    }

                    if (Matches(c, g, e, models))
                    {
                        continue;
                    }

                    if (differ++ == 0)
                    {
                        first = thread;
                        string lanes = c.Lanes > 1 ? $"lane {"XYZW"[lane]}: " : string.Empty;
                        string modelText = models.Length > 0 ? $", models {string.Join(", ", models.Select(m => ConformanceHarness.Format(c.Kind, m)))}" : string.Empty;
                        detail = $"{lanes}GPU {ConformanceHarness.Format(c.Kind, g)}, ForCpu {ConformanceHarness.Format(c.Kind, e)}{modelText}";
                    }

                    break;
                }
            }

            if (differ > 0)
            {
                mismatches.Add(new DifferentialMismatch(c, differ, first, detail, InputsOf(c, first)));
            }
        }

        DifferentialResult result = new(shader.Name, shader.Cases.Length, comparisons, mismatches);

        if (!mutate && mismatches.Count > 0 && Environment.GetEnvironmentVariable(ReportVariable) is { Length: > 0 } path)
        {
            AppendReport(path, mismatches);
        }

        return result;
    }

    private static long[] Execute(GraphicsDevice device, int threads, int slots, Action<ReadWriteBuffer<long>> dispatch)
    {
        using ReadWriteBuffer<long> output = device.AllocateReadWriteBuffer<long>(threads * slots);
        dispatch(output);
        return output.ToArray();
    }

    internal static bool Matches(DifferentialCase c, long gpu, long cpu, long[] models)
    {
        if (c.Check == DifferentialCheck.Exact)
        {
            return ConformanceHarness.Same(c.Kind, gpu, cpu);
        }

        if (MatchesOne(c, gpu, cpu))
        {
            return true;
        }

        foreach (long m in models)
        {
            if (MatchesOne(c, gpu, m))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesOne(DifferentialCase c, long gpu, long model)
    {
        long flushed = ConformanceHarness.FlushResult(c.Kind, model);

        long gpuFlushed = ConformanceHarness.FlushResult(c.Kind, gpu);

        if (ConformanceHarness.SameFlushed(c.Kind, gpu, model, flushed) || ConformanceHarness.Same(c.Kind, gpuFlushed, flushed))
        {
            return true;
        }

        if (c.Check == DifferentialCheck.Value && IsZero(c.Kind, gpuFlushed) && IsZero(c.Kind, flushed))
        {
            return true;
        }

        return c.Check is DifferentialCheck.Tolerance or DifferentialCheck.Near
            && (ConformanceHarness.Close(c.Kind, gpu, model, c.Ulps)
                || ConformanceHarness.Close(c.Kind, gpu, flushed, c.Ulps)
                || (ConformanceHarness.NearZero(c.Kind, gpu) && ConformanceHarness.NearZero(c.Kind, model))
                || (c.Check == DifferentialCheck.Near && NearZero100(c.Kind, gpu) && NearZero100(c.Kind, model)));
    }

    /// <summary>
    /// Whether a float result is within 2^-100 of zero: a composite function of operands the case
    /// computed (rather than read) can have subnormal intermediates there, which the GPU flushes.
    /// </summary>
    private static bool NearZero100(ResultKind kind, long bits) =>
        kind == ResultKind.Float && MathF.Abs(BitConverter.UInt32BitsToSingle((uint)bits)) < 7.888609e-31f;

    private static bool IsZero(ResultKind kind, long bits) => kind switch
    {
        ResultKind.Float => ((uint)bits & 0x7FFFFFFFu) == 0,
        ResultKind.Half => ((ushort)bits & 0x7FFF) == 0,
        _ => false,
    };

    /// <summary>The input slots of the floats a case reads.</summary>
    private static int[] FloatSlots(DifferentialCase c)
    {
        HashSet<string> used = [.. Identifier().Matches(c.Source).Select(static m => m.Value)];
        string[] slots = DifferentialShaders.InputSlots;
        return [.. Enumerable.Range(0, slots.Length).Where(slot => slots[slot] == "float" && used.Contains(DifferentialShaders.InputNames[slot].Split('.')[0]))];
    }

    /// <summary>Whether one of the float inputs is nonzero and within 2^-64 of zero on the thread.</summary>
    private static bool HasTinyInput(int thread, int[] floatSlots)
    {
        foreach (int slot in floatSlots)
        {
            float value = BitConverter.UInt32BitsToSingle((uint)Inputs[(thread * DifferentialShaders.InputSlots.Length) + slot]);

            if (value != 0f && MathF.Abs(value) < 5.421011e-20f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The inputs a case reads on a thread: each lane by name, as text and as the bits of its slot.</summary>
    private static Dictionary<string, string> InputsOf(DifferentialCase c, int thread)
    {
        HashSet<string> used = [.. Identifier().Matches(c.Source).Select(static m => m.Value)];
        string[] slots = DifferentialShaders.InputSlots;
        string[] names = DifferentialShaders.InputNames;
        Dictionary<string, string> inputs = [];

        for (int slot = 0; slot < slots.Length; slot++)
        {
            string name = names[slot];
            string variable = name.Split('.')[0];

            if (!used.Contains(variable))
            {
                continue;
            }

            ulong bits = Inputs[(thread * slots.Length) + slot];
            inputs[name] = Describe(slots[slot], bits);
            inputs[name + "#bits"] = $"0x{bits:X}";
        }

        return inputs;
    }

    private static string Describe(string type, ulong bits) => type switch
    {
        "bool" => (bits & 1) != 0 ? "true" : "false",
        "float" => ConformanceHarness.Format(ResultKind.Float, (long)(uint)bits),
        "Half" => ConformanceHarness.Format(ResultKind.Half, (long)(ushort)bits),
        "byte" => ((byte)bits).ToString(CultureInfo.InvariantCulture),
        "sbyte" => ((sbyte)bits).ToString(CultureInfo.InvariantCulture),
        "short" => ((short)bits).ToString(CultureInfo.InvariantCulture),
        "ushort" => ((ushort)bits).ToString(CultureInfo.InvariantCulture),
        "int" => ((int)bits).ToString(CultureInfo.InvariantCulture),
        "uint" => ((uint)bits).ToString(CultureInfo.InvariantCulture),
        "long" => ((long)bits).ToString(CultureInfo.InvariantCulture),
        _ => bits.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>The report of one shader: each case that differed, its C#, and the inputs of the first thread it differed on.</summary>
    public static string Report(DifferentialResult result)
    {
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"{result.Name}: {result.Mismatches.Count} of {result.Cases} cases differ between the GPU and ForCpu ({DifferentialShaders.Title}).");

        foreach (DifferentialMismatch m in result.Mismatches)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  case {m.Case.Id} ({m.Case.Check}): {m.Threads} of {Threads} threads differ; thread {m.Thread}: {m.Detail}");
            text.AppendLine("    inputs: " + string.Join(", ", m.Inputs.Where(static p => !p.Key.EndsWith("#bits", StringComparison.Ordinal)).Select(static p => $"{p.Key} = {p.Value}")));
            text.AppendLine("    " + m.Case.Source.Replace("\n", "\n    ", StringComparison.Ordinal));
        }

        text.AppendLine("Shrink it to a minimal repro with: scripts/fuzz-differential.sh --seed S --count N (S and N from the case id S:i, N > i).");
        return text.ToString();
    }

    /// <summary>
    /// In the long mode (with <see cref="ReportVariable"/> set), records a shader whose pipeline
    /// Metal's compiler crashed building (XPC_ERROR_CONNECTION_INTERRUPTED) and returns true, so the
    /// run goes on: random shaders hit compiler crashes that no single case of them causes (see
    /// docs/limitations.md), and the cases of such a shader cannot be compared. Returns false in the
    /// default mode, and for any other compilation error, which fails the test.
    /// </summary>
    public static bool ReportCompilerCrash(DifferentialShader shader, ShaderCompilationException exception)
    {
        if (!exception.Message.Contains("XPC_ERROR_CONNECTION_INTERRUPTED", StringComparison.Ordinal) ||
            Environment.GetEnvironmentVariable(ReportVariable) is not { Length: > 0 } path)
        {
            return false;
        }

        using MemoryStream stream = new();
        using (Utf8JsonWriter json = new(stream))
        {
            json.WriteStartObject();
            json.WriteString("crash", shader.Name);
            json.WriteNumber("cases", shader.Cases.Length);
            json.WriteEndObject();
        }

        lock (s_reportLock)
        {
            File.AppendAllText(path, Encoding.UTF8.GetString(stream.ToArray()) + "\n");
        }

        return true;
    }

    private static void AppendReport(string path, IReadOnlyList<DifferentialMismatch> mismatches)
    {
        StringBuilder lines = new();

        foreach (DifferentialMismatch m in mismatches)
        {
            using MemoryStream stream = new();
            using (Utf8JsonWriter json = new(stream))
            {
                json.WriteStartObject();
                json.WriteString("case", m.Case.Id);
                json.WriteString("check", m.Case.Check.ToString());
                json.WriteNumber("threads", m.Threads);
                json.WriteString("detail", m.Detail);
                json.WriteStartObject("inputs");

                foreach ((string key, string value) in m.Inputs)
                {
                    json.WriteString(key, value);
                }

                json.WriteEndObject();
                json.WriteEndObject();
            }

            lines.AppendLine(Encoding.UTF8.GetString(stream.ToArray()));
        }

        lock (s_reportLock)
        {
            File.AppendAllText(path, lines.ToString());
        }
    }

    [GeneratedRegex("[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex Identifier();
}
