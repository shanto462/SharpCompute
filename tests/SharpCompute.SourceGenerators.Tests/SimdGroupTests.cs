using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;

namespace SharpCompute.SourceGenerators.Tests;

/// <summary>
/// The SIMD-group operations of <c>Gpu</c> (<c>Wave*</c>, <c>Quad*</c>) and <c>SimdGroupIds</c>: the
/// MSL of every operation for every type, the kernel built-ins, the descriptor's
/// <c>UsesSimdGroups</c>, SCG0024 in constant initializers and SCG0035 for call sites that share a
/// line; and, on a machine with a GPU, the value of every typed operation for every type.
/// </summary>
public class SimdGroupTests
{
    private const string Shader = "Tests.TestShader";

    private static string Normalize(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n');

    /// <summary>A user struct for the shaders that need one, in a file of its own (the shader's file has a file-scoped namespace).</summary>
    private const string Particle = """
        using SharpCompute;

        namespace Tests;

        public struct Particle
        {
            public Float3 Position;
            public float Mass;
        }
        """;

    private static GeneratorRun RunBody(string body, string extraMembers = "", string attributes = "") =>
        GeneratorHarness.Run(TestSources.Vectors, TestSources.Shader($$"""
                public readonly ReadWriteBuffer<int> output;
                public readonly ReadOnlyBuffer<Float3> vectors;
                public readonly Float4 tint;
                {{extraMembers}}

                public void Execute()
                {
                    {{body}}
                }
            """, attributes));

    private static bool UsesSimdGroups(GeneratorRun run, string typeName = Shader)
    {
        Type type = run.LoadType(typeName);
        return (bool)typeof(SimdGroupTests).GetMethod(nameof(ReadUsesSimdGroups), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type).Invoke(null, null)!;
    }

    private static bool ReadUsesSimdGroups<T>()
        where T : struct, IComputeShaderDescriptor<T> => T.UsesSimdGroups;

    // ---- the table and the public surface agree ---------------------------------------------------

    /// <summary>The C# name of every type the typed operations exist for, with its MSL name.</summary>
    private static readonly (Type Type, string CSharp, string Msl)[] ValueTypes =
    [
        (typeof(float), "float", "float"), (typeof(Float2), "Float2", "float2"), (typeof(Float3), "Float3", "float3"), (typeof(Float4), "Float4", "float4"),
        (typeof(int), "int", "int"), (typeof(Int2), "Int2", "int2"), (typeof(Int3), "Int3", "int3"), (typeof(Int4), "Int4", "int4"),
        (typeof(uint), "uint", "uint"), (typeof(UInt2), "UInt2", "uint2"), (typeof(UInt3), "UInt3", "uint3"), (typeof(UInt4), "UInt4", "uint4"),
    ];

    private static MethodInfo[] SimdMethods() =>
        typeof(Gpu).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(static m => m.Name.StartsWith("Wave", StringComparison.Ordinal) || m.Name.StartsWith("Quad", StringComparison.Ordinal))
            .ToArray();

    /// <summary>The typed operations (a value in, the same type out) that have an overload for <paramref name="type"/>, in a stable order.</summary>
    private static MethodInfo[] TypedOperations(Type type) =>
        SimdMethods()
            .Where(m => m.ReturnType == type && m.GetParameters().Length > 0 && m.GetParameters()[0].ParameterType == type)
            .OrderBy(static m => m.Name, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void TheGeneratorKnowsEveryOperationOfGpu_AndNothingElse()
    {
        string[] surface = SimdMethods().Select(static m => m.Name).Distinct().Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(SimdGroupInfo.Names.Order(StringComparer.Ordinal), surface);

        // Every operation ends in the call site the compiler fills in (file, then line), and nothing else is optional.
        foreach (MethodInfo method in SimdMethods())
        {
            ParameterInfo[] parameters = method.GetParameters();

            if (method.Name is "WaveGetLaneIndex" or "WaveGetLaneCount")
            {
                Assert.Empty(parameters);
                continue;
            }

            Assert.True(parameters.Length >= 2, $"{method}: no call-site parameters");
            Assert.True(parameters[^2].Name == "callerFile" && parameters[^2].ParameterType == typeof(string) && (string?)parameters[^2].DefaultValue == string.Empty, $"{method}: the last but one parameter must be the caller's file");
            Assert.True(parameters[^1].Name == "callerLine" && parameters[^1].ParameterType == typeof(int) && (int?)parameters[^1].DefaultValue == 0, $"{method}: the last parameter must be the caller's line");
            Assert.NotNull(parameters[^2].GetCustomAttribute<System.Runtime.CompilerServices.CallerFilePathAttribute>());
            Assert.NotNull(parameters[^1].GetCustomAttribute<System.Runtime.CompilerServices.CallerLineNumberAttribute>());
            Assert.All(parameters[..^2], p => Assert.False(p.HasDefaultValue, $"{method}: {p.Name}"));
        }
    }

    [Fact]
    public void TypedOperations_ExistForFloatIntUIntAndTheirVectors_BitOperationsForIntegersOnly()
    {
        string[] all =
        [
            "QuadActiveMax", "QuadActiveMin", "QuadActiveProduct", "QuadActiveSum",
            "QuadInclusivePrefixProduct", "QuadInclusivePrefixSum", "QuadPrefixProduct", "QuadPrefixSum",
            "QuadReadAcrossDiagonal", "QuadReadAcrossX", "QuadReadAcrossY", "QuadReadLaneAt",
            "WaveActiveMax", "WaveActiveMin", "WaveActiveProduct", "WaveActiveSum",
            "WaveInclusivePrefixProduct", "WaveInclusivePrefixSum", "WavePrefixProduct", "WavePrefixSum",
            "WaveReadLaneAt", "WaveReadLaneFirst", "WaveRotateDown", "WaveRotateUp",
            "WaveShuffleDown", "WaveShuffleDownAndFill", "WaveShuffleUp", "WaveShuffleUpAndFill", "WaveShuffleXor",
        ];
        string[] integerOnly = ["QuadActiveBitAnd", "QuadActiveBitOr", "QuadActiveBitXor", "WaveActiveBitAnd", "WaveActiveBitOr", "WaveActiveBitXor"];

        foreach ((Type type, string csharp, _) in ValueTypes)
        {
            string[] expected = csharp.StartsWith("float", StringComparison.OrdinalIgnoreCase) ? all : [.. all, .. integerOnly];

            Assert.Equal(expected.Order(StringComparer.Ordinal), TypedOperations(type).Select(static m => m.Name));

            // And the one that answers with a bool whatever the type.
            Assert.Contains(SimdMethods(), m => m.Name == "WaveActiveAllEqual" && m.ReturnType == typeof(bool) && m.GetParameters()[0].ParameterType == type);
        }

        // Nothing for bool, double or matrices: Metal has no SIMD-group functions for them.
        Assert.DoesNotContain(SimdMethods(), static m => m.ReturnType == typeof(double) || m.ReturnType == typeof(Float2x2) || m.ReturnType == typeof(Float3x3));
        Assert.DoesNotContain(SimdMethods(), static m => m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(bool) && m.ReturnType == typeof(bool) && m.Name.Contains("Read", StringComparison.Ordinal));
    }

    // ---- every typed operation, for every type ----------------------------------------------------

    /// <summary>The fixed lane, delta or mask the matrix shaders pass to the operations that take one.</summary>
    private static int? Extra(string operation) => operation switch
    {
        "WaveReadLaneAt" => 2,
        "WaveShuffleUp" or "WaveShuffleDown" or "WaveRotateUp" or "WaveRotateDown" or "WaveShuffleUpAndFill" or "WaveShuffleDownAndFill" => 3,
        "WaveShuffleXor" => 1,
        "QuadReadLaneAt" => 3,
        _ => null,
    };

    private static string MslCall(string operation) => operation switch
    {
        "WaveActiveSum" => "metal::simd_sum(v)",
        "WaveActiveProduct" => "metal::simd_product(v)",
        "WaveActiveMin" => "metal::simd_min(v)",
        "WaveActiveMax" => "metal::simd_max(v)",
        "WaveActiveBitAnd" => "metal::simd_and(v)",
        "WaveActiveBitOr" => "metal::simd_or(v)",
        "WaveActiveBitXor" => "metal::simd_xor(v)",
        "WavePrefixSum" => "metal::simd_prefix_exclusive_sum(v)",
        "WavePrefixProduct" => "metal::simd_prefix_exclusive_product(v)",
        "WaveInclusivePrefixSum" => "metal::simd_prefix_inclusive_sum(v)",
        "WaveInclusivePrefixProduct" => "metal::simd_prefix_inclusive_product(v)",
        "WaveReadLaneAt" => "metal::simd_shuffle(v, ushort(2))",
        "WaveReadLaneFirst" => "metal::simd_broadcast_first(v)",
        "WaveShuffleUp" => "metal::simd_shuffle_up(v, ushort(3))",
        "WaveShuffleDown" => "metal::simd_shuffle_down(v, ushort(3))",
        "WaveShuffleXor" => "metal::simd_shuffle_xor(v, ushort(1))",
        "QuadReadAcrossX" => "metal::quad_shuffle_xor(v, ushort(1))",
        "QuadReadAcrossY" => "metal::quad_shuffle_xor(v, ushort(2))",
        "QuadReadAcrossDiagonal" => "metal::quad_shuffle_xor(v, ushort(3))",
        "QuadReadLaneAt" => "metal::quad_shuffle(v, ushort(3))",
        "WaveRotateUp" => "metal::simd_shuffle_rotate_up(v, ushort(3))",
        "WaveRotateDown" => "metal::simd_shuffle_rotate_down(v, ushort(3))",
        "WaveShuffleUpAndFill" => "metal::simd_shuffle_and_fill_up(v, v, ushort(3))",
        "WaveShuffleDownAndFill" => "metal::simd_shuffle_and_fill_down(v, v, ushort(3))",
        "QuadActiveSum" => "metal::quad_sum(v)",
        "QuadActiveProduct" => "metal::quad_product(v)",
        "QuadActiveMin" => "metal::quad_min(v)",
        "QuadActiveMax" => "metal::quad_max(v)",
        "QuadActiveBitAnd" => "metal::quad_and(v)",
        "QuadActiveBitOr" => "metal::quad_or(v)",
        "QuadActiveBitXor" => "metal::quad_xor(v)",
        "QuadPrefixSum" => "metal::quad_prefix_exclusive_sum(v)",
        "QuadPrefixProduct" => "metal::quad_prefix_exclusive_product(v)",
        "QuadInclusivePrefixSum" => "metal::quad_prefix_inclusive_sum(v)",
        "QuadInclusivePrefixProduct" => "metal::quad_prefix_inclusive_product(v)",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "No expected MSL for this operation."),
    };

    private static string MatrixShaderName(string csharp) => "Ops" + char.ToUpperInvariant(csharp[0]) + csharp[1..];

    /// <summary>
    /// One shader per value type that calls every typed operation on <c>v</c>, a value built from four
    /// elements of a scalar buffer, and stores each result component by component: the buffers are then
    /// of one of three scalar types whatever the vector, and every operation is on a line of its own.
    /// Two more slots hold <c>WaveActiveAllEqual</c> of <c>v</c> and of <c>v - v</c> (which is equal
    /// everywhere), as 1 or 0. The filling shuffles take <c>v</c> as the fill too, so a lane that is
    /// filled gets the value of the lane the fill comes from.
    /// </summary>
    private static string MatrixSource()
    {
        var source = new StringBuilder("using SharpCompute;\n\nnamespace Tests;\n");
        string[] components = ["X", "Y", "Z", "W"];

        foreach ((Type type, string csharp, _) in ValueTypes)
        {
            int width = Width(type);
            string scalar = ScalarName(type);
            MethodInfo[] operations = TypedOperations(type);
            string value = width == 1 ? "input[i * 4]" : $"new {csharp}({string.Join(", ", Enumerable.Range(0, width).Select(static c => $"input[(i * 4) + {c}]"))})";

            source.AppendLine();
            source.AppendLine("[ThreadGroupSize(64)]");
            source.AppendLine("[GeneratedComputeShaderDescriptor]");
            source.AppendLine($"public readonly partial struct {MatrixShaderName(csharp)}(ReadOnlyBuffer<{scalar}> input, ReadWriteBuffer<{scalar}> output) : IComputeShader");
            source.AppendLine("{");
            source.AppendLine("    public void Execute()");
            source.AppendLine("    {");
            source.AppendLine("        int i = ThreadIds.X;");
            source.AppendLine($"        int o = i * {(operations.Length + 2) * 4};");
            source.AppendLine($"        {csharp} v = {value};");
            source.AppendLine($"        {csharp} r;");

            for (int k = 0; k < operations.Length; k++)
            {
                string extra = Extra(operations[k].Name) is { } argument ? $", {argument}" : string.Empty;
                string fill = operations[k].Name.EndsWith("AndFill", StringComparison.Ordinal) ? ", v" : string.Empty;
                source.AppendLine($"        r = Gpu.{operations[k].Name}(v{fill}{extra});");

                for (int c = 0; c < width; c++)
                {
                    source.AppendLine($"        output[o + {(k * 4) + c}] = r{(width == 1 ? string.Empty : "." + components[c])};");
                }
            }

            string one = scalar == "float" ? "1f" : scalar == "uint" ? "1u" : "1", zero = scalar == "float" ? "0f" : scalar == "uint" ? "0u" : "0";
            source.AppendLine($"        output[o + {operations.Length * 4}] = Gpu.WaveActiveAllEqual(v) ? {one} : {zero};");
            source.AppendLine($"        output[o + {(operations.Length + 1) * 4}] = Gpu.WaveActiveAllEqual(v - v) ? {one} : {zero};");
            source.AppendLine("    }");
            source.AppendLine("}");
        }

        return source.ToString();
    }

    private static readonly Lazy<GeneratorRun> Matrix = new(static () => GeneratorHarness.Run(MatrixSource()).AssertClean());

    private static int Width(Type type) => type == typeof(float) || type == typeof(int) || type == typeof(uint) ? 1 : type.Name[^1] - '0';

    private static string ScalarName(Type type) => type.Name.StartsWith("UInt", StringComparison.Ordinal) ? "uint" : type.Name.StartsWith("Int", StringComparison.Ordinal) ? "int" : "float";

    public static TheoryData<string> ValueTypeNames() => new(ValueTypes.Select(static t => t.CSharp));

    [Theory]
    [MemberData(nameof(ValueTypeNames))]
    public void EveryTypedOperation_MapsToItsMetalFunction_AndCompiles(string csharp)
    {
        (Type type, _, string msl) = ValueTypes.Single(t => t.CSharp == csharp);
        ShaderDescriptor descriptor = Matrix.Value.Descriptor("Tests." + MatrixShaderName(csharp)).AssertMslCompiles();
        string[] body = descriptor.KernelBody.Split('\n');

        Assert.Contains($"{msl} r;", body);

        foreach (MethodInfo operation in TypedOperations(type))
        {
            Assert.Contains($"r = {MslCall(operation.Name)};", body);
        }

        // All-equal is a helper of the prelude, overloaded for this type; a binary expression is unpacked first.
        Assert.Contains(body, static line => line.EndsWith("= __ms_wave_all_equal(v) ? " + (line.Contains("1.0f", StringComparison.Ordinal) ? "1.0f : 0.0f;" : line.Contains("1u", StringComparison.Ordinal) ? "1u : 0u;" : "1 : 0;"), StringComparison.Ordinal));
        // (A signed difference is computed unsigned, where it wraps as in C#, and the difference of an
        // integer vector lane by lane.)
        string difference = Width(type) > 1 && ScalarName(type) == "uint" ? "__ms_sub(v, v)"
            : ScalarName(type) != "int" ? "v - v"
            : Width(type) == 1 ? "int(uint(v) - uint(v))" : $"{msl}(__ms_sub(u{msl}(v), u{msl}(v)))";
        Assert.Contains(body, line => line.Contains(Width(type) == 1 ? $"__ms_wave_all_equal({difference})" : $"__ms_wave_all_equal({msl}({difference}))", StringComparison.Ordinal));
        Assert.Contains($"inline bool __ms_wave_all_equal({msl} v)", descriptor.MslSource);

        // Nothing of the call site reaches the MSL, and no SIMD built-in is needed for these.
        Assert.DoesNotContain("__lane", descriptor.MslSource);
        Assert.DoesNotContain("Source", descriptor.KernelBody);
        Assert.True(UsesSimdGroups(Matrix.Value, "Tests." + MatrixShaderName(csharp)));
    }

    [Theory]
    [MemberData(nameof(ValueTypeNames))]
    public void EveryTypedOperation_HasTheDocumentedValueOnTheGpu_AndTheSameOnTheCpu(string csharp)
    {
        if (!GraphicsDevice.IsSupported)
        {
            return;
        }

        Type type = ValueTypes.Single(t => t.CSharp == csharp).Type;

        if (ScalarName(type) == "float")
        {
            // Small integers with signs: sums are exact, products of 32 of them are not (hence the tolerance).
            RunMatrix<float>(type, static (i, c) => ((((i * 7) + (c * 13)) % 5) + 1) * ((i + c) % 3 == 0 ? -1f : 1f), static (expected, actual) => MathF.Abs(expected - actual) <= MathF.Abs(expected) * 1e-4f);
        }
        else if (ScalarName(type) == "int")
        {
            RunMatrix<int>(type, static (i, c) => ((((i * 7) + (c * 13)) % 5) + 1) * ((i + c) % 3 == 0 ? -1 : 1), static (expected, actual) => expected == actual);
        }
        else
        {
            RunMatrix<uint>(type, static (i, c) => (uint)((((i * 7) + (c * 13)) % 5) + 1) | (i % 9 == 0 ? 0x80000000u : 0u), static (expected, actual) => expected == actual);
        }
    }

    /// <summary>
    /// Dispatches the matrix shader of <paramref name="type"/> over 100 threads in threadgroups of 64
    /// (SIMD groups of 32, 32, 32 and 4 lanes) and compares every component of every result with the
    /// model: an operation combines, scans or reads the lanes of its own SIMD group (or quad). Then
    /// dispatches it again with <c>ForCpu</c> and compares that with the GPU's output, value for value.
    /// </summary>
    private static void RunMatrix<TScalar>(Type type, Func<int, int, TScalar> input, Func<TScalar, TScalar, bool> equal)
        where TScalar : unmanaged, IBinaryNumber<TScalar>
    {
        const int threads = 100, threadgroup = 64, lanesPerGroup = 32;
        int width = Width(type);
        MethodInfo[] operations = TypedOperations(type);
        int stride = (operations.Length + 2) * 4;

        TScalar[] values = new TScalar[threads * 4];

        for (int i = 0; i < threads; i++)
        {
            for (int c = 0; c < 4; c++)
            {
                values[(i * 4) + c] = input(i, c);
            }
        }

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadOnlyBuffer<TScalar> inputBuffer = device.AllocateReadOnlyBuffer<TScalar>(values);
        using ReadWriteBuffer<TScalar> outputBuffer = device.AllocateReadWriteBuffer<TScalar>(threads * stride);
        using ReadWriteBuffer<TScalar> cpuBuffer = device.AllocateReadWriteBuffer<TScalar>(threads * stride);

        Type shaderType = Matrix.Value.LoadType("Tests." + MatrixShaderName(ValueTypes.Single(t => t.Type == type).CSharp));

        foreach ((string method, ReadWriteBuffer<TScalar> output) in new[] { (nameof(GraphicsDevice.For), outputBuffer), (nameof(GraphicsDevice.ForCpu), cpuBuffer) })
        {
            try
            {
                typeof(GraphicsDevice).GetMethods()
                    .Single(m => m.Name == method && m.GetParameters().Length == 2)
                    .MakeGenericMethod(shaderType)
                    .Invoke(device, [threads, Activator.CreateInstance(shaderType, inputBuffer, output)!]);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(e.InnerException);
            }
        }

        TScalar[] actual = outputBuffer.ToArray();
        TScalar[] onCpu = cpuBuffer.ToArray();
        var failures = new List<string>();

        // The CPU returns what the GPU returns, also where the model says "undefined" (reads of lanes
        // that do not exist give 0 on both). Floats are folded in lane order there, hence the tolerance.
        for (int i = 0; i < actual.Length; i++)
        {
            if (!equal(actual[i], onCpu[i]))
            {
                string operation = i % stride / 4 < operations.Length ? operations[i % stride / 4].Name : "WaveActiveAllEqual";
                failures.Add($"{operation}, thread {i / stride}, component {i % 4}: GPU {actual[i]}, CPU {onCpu[i]}");
            }
        }

        for (int thread = 0; thread < threads; thread++)
        {
            int groupStart = thread - ((thread % threadgroup) % lanesPerGroup);
            int groupEnd = Math.Min(Math.Min(groupStart + lanesPerGroup, ((thread / threadgroup) + 1) * threadgroup), threads);
            int lane = thread - groupStart, active = groupEnd - groupStart;

            for (int k = 0; k < operations.Length; k++)
            {
                for (int c = 0; c < width; c++)
                {
                    TScalar Lane(int index) => values[((groupStart + index) * 4) + c];
                    IEnumerable<TScalar> all = Enumerable.Range(0, active).Select(Lane);
                    IEnumerable<TScalar> below = Enumerable.Range(0, lane).Select(Lane);
                    TScalar own = Lane(lane);

                    // Null where the source lane does not exist, which is undefined.
                    TScalar? Source(int index) => index >= 0 && index < active ? Lane(index) : null;

                    // The lanes of this lane's quad that exist, and those of them below this lane.
                    int quadStart = lane & ~3;
                    IEnumerable<TScalar> quad = Enumerable.Range(quadStart, 4).Where(l => l < active).Select(Lane);
                    IEnumerable<TScalar> quadBelow = Enumerable.Range(quadStart, lane - quadStart).Select(Lane);

                    TScalar? expected = operations[k].Name switch
                    {
                        "WaveActiveSum" => Sum(all),
                        "WaveActiveProduct" => Product(all),
                        "WaveActiveMin" => all.Aggregate(TScalar.Min),
                        "WaveActiveMax" => all.Aggregate(TScalar.Max),
                        "WaveActiveBitAnd" => all.Aggregate(static (a, b) => a & b),
                        "WaveActiveBitOr" => all.Aggregate(static (a, b) => a | b),
                        "WaveActiveBitXor" => all.Aggregate(static (a, b) => a ^ b),
                        "WavePrefixSum" => Sum(below),
                        "WavePrefixProduct" => Product(below),
                        "WaveInclusivePrefixSum" => Sum(below) + own,
                        "WaveInclusivePrefixProduct" => Product(below) * own,
                        "WaveReadLaneAt" => Source(2),
                        "WaveReadLaneFirst" => Lane(0),
                        "WaveShuffleUp" => lane < 3 ? own : Lane(lane - 3),
                        "WaveShuffleDown" => lane + 3 >= lanesPerGroup ? own : Source(lane + 3),
                        "WaveShuffleXor" => Source(lane ^ 1),
                        "QuadReadAcrossX" => Source(lane ^ 1),
                        "QuadReadAcrossY" => Source(lane ^ 2),
                        "QuadReadAcrossDiagonal" => Source(lane ^ 3),
                        "QuadReadLaneAt" => Source((lane & ~3) + 3),
                        "WaveRotateUp" => Source((lane - 3 + lanesPerGroup) % lanesPerGroup),
                        "WaveRotateDown" => Source((lane + 3) % lanesPerGroup),

                        // The fill is v as well: the lanes shifted in get the value of the lane 32 further on.
                        "WaveShuffleUpAndFill" => Source(lane >= 3 ? lane - 3 : lane - 3 + lanesPerGroup),
                        "WaveShuffleDownAndFill" => Source(lane + 3 < lanesPerGroup ? lane + 3 : lane + 3 - lanesPerGroup),
                        "QuadActiveSum" => Sum(quad),
                        "QuadActiveProduct" => Product(quad),
                        "QuadActiveMin" => quad.Aggregate(TScalar.Min),
                        "QuadActiveMax" => quad.Aggregate(TScalar.Max),
                        "QuadActiveBitAnd" => quad.Aggregate(static (a, b) => a & b),
                        "QuadActiveBitOr" => quad.Aggregate(static (a, b) => a | b),
                        "QuadActiveBitXor" => quad.Aggregate(static (a, b) => a ^ b),
                        "QuadPrefixSum" => Sum(quadBelow),
                        "QuadPrefixProduct" => Product(quadBelow),
                        "QuadInclusivePrefixSum" => Sum(quadBelow) + own,
                        "QuadInclusivePrefixProduct" => Product(quadBelow) * own,
                        _ => throw new InvalidOperationException(operations[k].Name),
                    };

                    TScalar got = actual[(thread * stride) + (k * 4) + c];

                    if (expected is { } value && !equal(value, got))
                    {
                        failures.Add($"{operations[k].Name}, thread {thread} (lane {lane} of {active}), component {c}: expected {value}, got {got}");
                    }
                }
            }
        }

        // All-equal: of v, which is the same in every lane of a group only by accident, and of v - v, which always is.
        for (int thread = 0; thread < threads; thread++)
        {
            int groupStart = thread - ((thread % threadgroup) % lanesPerGroup);
            int groupEnd = Math.Min(Math.Min(groupStart + lanesPerGroup, ((thread / threadgroup) + 1) * threadgroup), threads);
            bool same = Enumerable.Range(groupStart, groupEnd - groupStart).All(t => values.AsSpan(t * 4, width).SequenceEqual(values.AsSpan(groupStart * 4, width)));

            if (actual[(thread * stride) + (operations.Length * 4)] != (same ? TScalar.One : TScalar.Zero) || actual[(thread * stride) + ((operations.Length + 1) * 4)] != TScalar.One)
            {
                failures.Add($"WaveActiveAllEqual, thread {thread}: expected {(same ? 1 : 0)} and 1, got {actual[(thread * stride) + (operations.Length * 4)]} and {actual[(thread * stride) + ((operations.Length + 1) * 4)]}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} wrong results for {type.Name}:\n" + string.Join("\n", failures.Take(30)));

        static TScalar Sum(IEnumerable<TScalar> items) => items.Aggregate(TScalar.Zero, static (a, b) => a + b);
        static TScalar Product(IEnumerable<TScalar> items) => items.Aggregate(TScalar.One, static (a, b) => a * b);
    }

    // ---- lanes, votes and built-ins --------------------------------------------------------------

    [Fact]
    public void LanesVotesAndBuiltins_Snapshot()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            namespace MyApp;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Votes(ReadOnlyBuffer<float> input, ReadWriteBuffer<uint> output, float threshold) : IComputeShader
            {
                public void Execute()
                {
                    int i = ThreadIds.X;
                    bool keep = input[i] > threshold;

                    output[i * 8] = (uint)Gpu.WaveGetLaneIndex();
                    output[i * 8 + 1] = (uint)Gpu.WaveGetLaneCount();
                    output[i * 8 + 2] = (uint)((SimdGroupIds.Index * 100) + SimdGroupIds.Count);
                    output[i * 8 + 3] = Gpu.WaveActiveBallot(keep);
                    output[i * 8 + 4] = (uint)Gpu.WaveActiveCountBits(keep);
                    output[i * 8 + 5] = (uint)Gpu.WavePrefixCountBits(keep);

                    if (Gpu.WaveActiveAnyTrue(keep) && !Gpu.WaveActiveAllTrue(keep) && Gpu.WaveIsFirstLane())
                    {
                        output[i * 8 + 6] = 1u;
                    }
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("MyApp.Votes");

        Assert.Equal(
            Normalize("""
                #include <metal_stdlib>

                using namespace metal;

                struct __Constants
                {
                    float threshold;
                };
                static_assert(sizeof(__Constants) == 4, "SharpCompute: size of '__Constants' does not match its C# layout");
                static_assert(__builtin_offsetof(__Constants, threshold) == 0, "SharpCompute: offset of '__Constants.threshold' does not match its C# layout");

                struct __GridWindow
                {
                    packed_uint3 offset;
                    packed_uint3 size;
                    packed_uint3 groupOffset;
                };
                static_assert(sizeof(__GridWindow) == 36, "SharpCompute: size of '__GridWindow' does not match its C# layout");
                static_assert(__builtin_offsetof(__GridWindow, offset) == 0, "SharpCompute: offset of '__GridWindow.offset' does not match its C# layout");
                static_assert(__builtin_offsetof(__GridWindow, size) == 12, "SharpCompute: offset of '__GridWindow.size' does not match its C# layout");
                static_assert(__builtin_offsetof(__GridWindow, groupOffset) == 24, "SharpCompute: offset of '__GridWindow.groupOffset' does not match its C# layout");

                constant bool __windowed [[function_constant(0)]];

                kernel void MyApp_Votes(
                    const device float* input [[buffer(0)]],
                    device uint* output [[buffer(1)]],
                    constant __Constants& __c [[buffer(2)]],
                    constant __GridWindow& __window [[buffer(3), function_constant(__windowed)]],
                    uint3 __tid_in [[thread_position_in_grid]],
                    uint __lane [[thread_index_in_simdgroup]],
                    uint __lanes [[threads_per_simdgroup]],
                    uint __sgid [[simdgroup_index_in_threadgroup]],
                    uint __sgroups [[simdgroups_per_threadgroup]])
                {
                    const uint3 __tid = __windowed ? __tid_in + uint3(__window.offset) : __tid_in;
                    int i = int(__tid.x);
                    bool keep = input[i] > __c.threshold;
                    output[int(uint(i) * 8u)] = uint(int(__lane));
                    output[int(uint(i) * 8u + 1u)] = uint(int(__lanes));
                    output[int(uint(i) * 8u + 2u)] = uint(int((uint(int(__sgid)) * 100u) + uint(int(__sgroups))));
                    output[int(uint(i) * 8u + 3u)] = uint(metal::simd_vote::vote_t(metal::simd_ballot(keep)));
                    output[int(uint(i) * 8u + 4u)] = uint(metal::simd_sum(int(keep)));
                    output[int(uint(i) * 8u + 5u)] = uint(metal::simd_prefix_exclusive_sum(int(keep)));
                    if (metal::simd_any(keep) && !metal::simd_all(keep) && metal::simd_is_first())
                    {
                        output[int(uint(i) * 8u + 6u)] = 1u;
                    }
                }
                """),
            Normalize(descriptor.MslSource));

        Assert.True(UsesSimdGroups(run, "MyApp.Votes"));
        Assert.Contains("static bool global::SharpCompute.IComputeShaderDescriptor<global::MyApp.Votes>.UsesSimdGroups => true;", run.Source("MyApp.Votes.SharpCompute.g.cs"));
    }

    [Theory]
    [InlineData("[ShaderOptions(FastMath = true)]", ShaderCompileOptions.FastMath)]
    [InlineData("[ShaderOptions(FpContract = false)]", ShaderCompileOptions.NoFpContract)]
    [InlineData("[ShaderOptions(FastMath = true, FpContract = false), ThreadGroupSize(64)]", ShaderCompileOptions.FastMath | ShaderCompileOptions.NoFpContract)]
    public void WithShaderOptions_TheDescriptorReportsBothTheOptionsAndTheOperations(string attributes, ShaderCompileOptions options)
    {
        GeneratorRun run = RunBody(
            "output[ThreadIds.X] = Gpu.WaveActiveSum(ThreadIds.X) + (int)Gpu.WaveActiveMax(tint.X * 2f + 1f);",
            attributes: attributes);

        // The compile options change how the runtime compiles the kernel, not the kernel: the same MSL
        // as without them, and both members next to each other in the descriptor.
        ShaderDescriptor descriptor = run.Verify(Shader);
        string generated = run.Source("TestShader.SharpCompute.g.cs");

        Assert.Equal(options, descriptor.CompileOptions);
        Assert.True(UsesSimdGroups(run));
        Assert.Contains(".CompileOptions => global::SharpCompute.ShaderCompileOptions.", generated);
        Assert.Contains(".UsesSimdGroups => true;", generated);
        Assert.Equal("output[int(__tid.x)] = int(uint(metal::simd_sum(int(__tid.x))) + uint(__ms_sat_int(metal::simd_max(__c.tint.x * 2.0f + 1.0f))));", descriptor.KernelBody);
        Assert.DoesNotContain("pragma", descriptor.MslSource);
    }

    [Fact]
    public void AShaderWithoutSimdGroupOperations_DoesNotReportThem()
    {
        GeneratorRun run = RunBody("output[ThreadIds.X] = GroupIds.X;");

        run.Verify(Shader);
        Assert.False(UsesSimdGroups(run));
        Assert.DoesNotContain("UsesSimdGroups", Assert.Single(run.Sources).SourceText.ToString());
    }

    [Theory]
    [InlineData("output[0] = Gpu.WaveGetLaneIndex();", "uint __lane [[thread_index_in_simdgroup]]", "output[0] = int(__lane);")]
    [InlineData("output[0] = Gpu.WaveGetLaneCount();", "uint __lanes [[threads_per_simdgroup]]", "output[0] = int(__lanes);")]
    [InlineData("output[0] = SimdGroupIds.Index;", "uint __sgid [[simdgroup_index_in_threadgroup]]", "output[0] = int(__sgid);")]
    [InlineData("output[0] = SimdGroupIds.Count;", "uint __sgroups [[simdgroups_per_threadgroup]]", "output[0] = int(__sgroups);")]
    public void EachBuiltin_IsAScalarKernelArgument_ThatIsNeverRebasedByTheGridWindow(string body, string parameter, string statement)
    {
        GeneratorRun run = RunBody(body);
        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Contains(parameter, descriptor.MslSource);
        Assert.Equal(statement, descriptor.KernelBody);

        // A window is threadgroup aligned, so nothing about a SIMD group changes in a split dispatch:
        // no window, no specialization, no prologue.
        Assert.DoesNotContain("__window", descriptor.MslSource);
        Assert.Empty(descriptor.KernelPrologue);
        Assert.True(UsesSimdGroups(run));
    }

    [Fact]
    public void HelperMethods_ReceiveTheBuiltinsAsHiddenScalarParameters()
    {
        GeneratorRun run = RunBody(
            "output[ThreadIds.X] = Position() + Total(ThreadIds.X);",
            """
                private static int Lane() => Gpu.WaveGetLaneIndex();

                private static int Position() => (SimdGroupIds.Index * 32) + Lane();

                private static int Total(int value) => Gpu.WaveActiveSum(value);
            """);

        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Contains("static int Lane(uint __lane)", descriptor.MslSource);
        Assert.Contains("static int Position(uint __lane, uint __sgid)", descriptor.MslSource);
        Assert.Contains("static int Total(int value)", descriptor.MslSource);
        Assert.Equal("return int((uint(int(__sgid)) * 32u) + uint(::Lane(__lane)));", descriptor.FunctionBody("static int Position"));
        Assert.Equal("return metal::simd_sum(value);", descriptor.FunctionBody("static int Total"));
        Assert.Equal("output[int(__tid.x)] = int(uint(::Position(__lane, __sgid)) + uint(::Total(int(__tid.x))));", descriptor.KernelBody);
        Assert.True(UsesSimdGroups(run));
    }

    [Fact]
    public void AHelperThatIsNeverCalled_DoesNotMakeTheShaderUseSimdGroups()
    {
        GeneratorRun run = RunBody(
            "output[ThreadIds.X] = 1;",
            "private static int Unused(int value) => Gpu.WaveActiveSum(value);");

        run.Verify(Shader);
        Assert.False(UsesSimdGroups(run));
    }

    [Fact]
    public void PixelShaders_CanUseThem()
    {
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;

            namespace Tests;

            [GeneratedComputeShaderDescriptor]
            public readonly partial struct Pixel(float gain) : IComputeShader<Float4>
            {
                public Float4 Execute()
                {
                    Float4 color = new Float4(ThreadIds.Normalized.XY, gain, 1f);
                    Float4 mean = Gpu.WaveActiveSum(color) / Gpu.WaveActiveCountBits(true);

                    return Gpu.WaveIsFirstLane() ? color : mean;
                }
            }
            """);

        ShaderDescriptor descriptor = run.Verify("Tests.Pixel");

        Assert.Contains("float4 mean = metal::simd_sum(color) / float(metal::simd_sum(int(true)));", descriptor.MslSource);
        Assert.Contains("metal::simd_is_first() ? color : mean", descriptor.MslSource);
        Assert.True(UsesSimdGroups(run, "Tests.Pixel"));
    }

    // ---- what a lane brings ----------------------------------------------------------------------

    [Theory]
    [InlineData("Float3 v = vectors[0]; vectors2[0] = Gpu.WaveActiveSum(v);", "metal::simd_sum(v)")] // a local is unpacked already
    [InlineData("vectors2[0] = Gpu.WaveActiveSum(vectors[ThreadIds.X]);", "metal::simd_sum(float3(vectors[int(__tid.x)]))")] // a buffer element is packed
    [InlineData("vectors2[0] = Gpu.WaveActiveSum(tint).XYZ;", "metal::simd_sum(float4(__c.tint)).xyz")] // so is a captured constant
    [InlineData("vectors2[0] = Gpu.WaveActiveSum(particle.Position);", "metal::simd_sum(float3(particle.Position))")] // and a struct field
    [InlineData("vectors2[0] = Gpu.WaveActiveSum(new Float3(1f, 2f, 3f));", "metal::simd_sum(float3(1.0f, 2.0f, 3.0f))")]
    [InlineData("vectors2[0] = Gpu.WaveActiveSum(Gpu.Abs(vectors[0]));", "metal::simd_sum(metal::abs(vectors[0]))")]
    [InlineData("vectors2[0] = Gpu.WaveActiveSum(vectors[0] * 2f);", "metal::simd_sum(float3(vectors[0] * 2.0f))")]
    [InlineData("vectors2[0] = Gpu.WaveShuffleUp(vectors[0].ZYX, ThreadIds.X & 3);", "metal::simd_shuffle_up(float3(vectors[0].zyx), ushort(int(__tid.x) & 3))")]
    public void VectorValues_AreUnpackedWhereTheyMayBePacked(string body, string expected)
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, Particle, TestSources.Shader($$"""
                public readonly ReadOnlyBuffer<Float3> vectors;
                public readonly ReadWriteBuffer<Float3> vectors2;
                public readonly ReadOnlyBuffer<Particle> particles;
                public readonly Float4 tint;

                public void Execute()
                {
                    Particle particle = particles[0];
                    {{body}}
                }
            """));

        Assert.Contains(expected, run.Verify(Shader).KernelBody);
    }

    [Theory]
    [InlineData("output[0] = Gpu.WaveReadLaneAt(lane: 3, value: output[1]);", "output[0] = metal::simd_shuffle(output[1], ushort(3));")]
    [InlineData("output[0] = Gpu.WaveShuffleXor(output[1], mask: 16);", "output[0] = metal::simd_shuffle_xor(output[1], ushort(16));")]
    [InlineData("output[0] = Gpu.WaveActiveSum(output[1], \"Elsewhere.cs\", 7);", "output[0] = metal::simd_sum(output[1]);")] // an explicit call site is the CPU's business too
    [InlineData("output[0] = Gpu.WaveActiveSum(callerLine: 9, value: output[1]);", "output[0] = metal::simd_sum(output[1]);")]
    [InlineData("output[0] = Gpu.WaveShuffleUpAndFill(delta: 2, fill: -1, value: output[1]);", "output[0] = metal::simd_shuffle_and_fill_up(output[1], -1, ushort(2));")]
    [InlineData("output[0] = (int)Gpu.QuadActiveBallot(output[1] > 0) + (Gpu.QuadActiveAnyTrue(true) && Gpu.QuadActiveAllTrue(true) ? 1 : 0);", "output[0] = int(uint(metal::quad_vote::vote_t(metal::quad_ballot(output[1] > 0))) + uint(metal::quad_any(true) && metal::quad_all(true) ? 1 : 0));")]
    [InlineData("output[0] = Gpu.WaveActiveMax(1 + output[1]);", "output[0] = metal::simd_max(int(1u + uint(output[1])));")]
    [InlineData("output[0] = (int)Gpu.WaveActiveSum(2.5f) + (int)Gpu.WaveActiveMax(3u);", "output[0] = int(uint(__ms_sat_int(metal::simd_sum(2.5f))) + metal::simd_max(3u));")]
    public void Arguments_AreTranslatedInParameterOrder_WithoutTheCallSiteId(string body, string expected)
    {
        Assert.Equal(expected, RunBody(body).Verify(Shader).KernelBody);
    }

    // ---- diagnostics -----------------------------------------------------------------------------

    [Theory]
    [InlineData("Gpu.WaveActiveSum(1f)", "float", "Gpu.WaveActiveSum(float, string, int)")]
    [InlineData("Gpu.WaveGetLaneIndex()", "int", "Gpu.WaveGetLaneIndex()")]
    [InlineData("Gpu.WaveIsFirstLane() ? 1 : 0", "int", "Gpu.WaveIsFirstLane(string, int)")]
    [InlineData("Gpu.WaveActiveBallot(true)", "uint", "Gpu.WaveActiveBallot(bool, string, int)")]
    [InlineData("Gpu.QuadReadAcrossX(2)", "int", "Gpu.QuadReadAcrossX(int, string, int)")]
    [InlineData("Gpu.WaveActiveAllEqual(2f) ? 1 : 0", "int", "Gpu.WaveActiveAllEqual(float, string, int)")]
    [InlineData("Gpu.QuadActiveSum(2u)", "uint", "Gpu.QuadActiveSum(uint, string, int)")]
    public void InAStaticReadonlyInitializer_AnOperationIsNotAConstant(string initializer, string type, string member)
    {
        GeneratorRun run = RunBody(
            "output[ThreadIds.X] = (int)Value;",
            $"private static readonly {type} Value = {initializer};");

        Diagnostic diagnostic = Assert.Single(run.Diagnostics);

        Assert.Equal("SCG0024", diagnostic.Id);
        Assert.Contains("TestShader.Value", diagnostic.GetMessage());
        Assert.Contains($"'{member}' is a SIMD-group operation, which only has a value while the shader runs", diagnostic.GetMessage());
        Assert.StartsWith("Gpu.", run.TextAt(diagnostic));
        Assert.Contains("This shader has SharpCompute errors", Assert.Single(run.Sources).SourceText.ToString());
    }

    [Theory]
    [InlineData("SimdGroupIds.Index")]
    [InlineData("SimdGroupIds.Count")]
    public void InAStaticReadonlyInitializer_ABuiltinIsNotAConstant(string builtin)
    {
        GeneratorRun run = RunBody(
            "output[ThreadIds.X] = Value;",
            $"private static readonly int Value = {builtin} + 1;");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0024", builtin);

        Assert.Contains($"'{builtin}' is only known while the shader runs", diagnostic.GetMessage());
    }

    [Fact]
    public void TwoCallsOfOneOperationOnALine_GetAWarning_AndTheShaderStillTranslates()
    {
        GeneratorRun run = RunBody("output[ThreadIds.X] = ThreadIds.X > 4 ? Gpu.WaveActiveSum(1) : Gpu.WaveActiveSum(2);");

        Diagnostic diagnostic = run.AssertOnlyDiagnostic("SCG0035", "Gpu.WaveActiveSum(2)");

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'Gpu.WaveActiveSum' is called more than once on line", diagnostic.GetMessage());
        Assert.Contains("put each call on a line of its own", diagnostic.GetMessage());

        // A warning about the CPU: the GPU translation is complete.
        ShaderDescriptor descriptor = run.Descriptor(Shader).AssertMslCompiles();
        Assert.Equal("output[int(__tid.x)] = int(__tid.x) > 4 ? metal::simd_sum(1) : metal::simd_sum(2);", descriptor.KernelBody);
    }

    [Theory]
    // Different operations on one line differ by name; the same operation on two lines differs by line.
    [InlineData("output[0] = Gpu.WaveActiveSum(1) + Gpu.WaveActiveMax(2) + Gpu.WavePrefixSum(3);")]
    [InlineData("output[0] = Gpu.WaveActiveSum(1) +\n            Gpu.WaveActiveSum(2);")]
    [InlineData("output[0] = Gpu.WaveShuffleUp(Gpu.WaveShuffleDown(output[1], 1), 1);")]
    [InlineData("output[0] = Gpu.WaveGetLaneIndex() + Gpu.WaveGetLaneIndex() + SimdGroupIds.Index + SimdGroupIds.Index;")] // no call site: nothing to meet at
    public void CallSitesThatCanBeToldApart_GetNoWarning(string body)
    {
        RunBody(body).Verify(Shader);
    }

    [Fact]
    public void TwoCallsOnALine_InAHelperCalledTwice_AreReportedOnce()
    {
        GeneratorRun run = RunBody(
            "output[0] = Both(1) + Both(2);",
            "private static int Both(int value) => Gpu.WaveActiveMin(value) - Gpu.WaveActiveMin(-value);");

        run.AssertOnlyDiagnostic("SCG0035", "Gpu.WaveActiveMin(-value)");
    }

    [Fact]
    public void TheSameLineInTwoFilesOfAPartialStruct_IsTwoCallSites()
    {
        // Both calls of WaveActiveSum are on line 7 of their file. The file is part of the call site
        // (the CPU gets both from the compiler), so they can be told apart and nothing is reported.
        GeneratorRun run = GeneratorHarness.Run(
            """
            using SharpCompute;
            namespace Tests;
            [GeneratedComputeShaderDescriptor]
            public readonly partial struct TestShader : IComputeShader
            {
                public readonly ReadWriteBuffer<int> output;
                public void Execute() { output[0] = Gpu.WaveActiveSum(1) + Elsewhere(2); }
            }
            """,
            """
            using SharpCompute;
            namespace Tests;

            public readonly partial struct TestShader
            {

                private static int Elsewhere(int value) => Gpu.WaveActiveSum(value);
            }
            """);

        ShaderDescriptor descriptor = run.Verify(Shader);

        Assert.Equal("output[0] = int(uint(metal::simd_sum(1)) + uint(::Elsewhere(2)));", descriptor.KernelBody);
        Assert.Equal(7, run.Input.SyntaxTrees.Select(static tree => tree.GetText().ToString().Split('\n').ToList().FindIndex(static line => line.Contains("WaveActiveSum", StringComparison.Ordinal)) + 1).Distinct().Single());
    }

    [Theory]
    [InlineData("bool b = Gpu.WaveReadLaneFirst(true);")]            // no bool exchange in Metal
    [InlineData("float f = Gpu.WaveActiveBitAnd(1.5f);")]            // bit operations are for integers
    [InlineData("double d = 1.0; d = Gpu.WaveActiveSum(d);")]        // no double on the GPU
    [InlineData("Float2x2 m = default; m = Gpu.WaveActiveSum(m);")]  // no matrices
    [InlineData("Particle p = default; p = Gpu.WaveReadLaneAt(p, 0);")] // no structs
    public void UnsupportedTypes_HaveNoOverload_SoTheCSharpCompilerRejectsThem(string body)
    {
        GeneratorRun run = GeneratorHarness.Run(TestSources.Vectors, Particle, TestSources.Shader($$"""
                public readonly ReadWriteBuffer<int> output;

                public void Execute()
                {
                    {{body}}
                }
            """));

        // The error is C#'s own, at the argument; the generator adds nothing and does not crash.
        Assert.Contains(run.Output.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error && d.Id is "CS1503" or "CS0266" or "CS0029");
        Assert.Null(run.Result.Exception);
        Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "SCG9999");
    }
}
