using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SharpCompute.Vectors.Tests;

/// <summary>Guards the attribute contract that the source generator relies on.</summary>
public class ContractTests
{
    /// <summary>The threadgroup barriers of <see cref="Gpu"/>, under their HLSL names.</summary>
    private static readonly string[] Barriers = ["GroupMemoryBarrierWithGroupSync", "DeviceMemoryBarrierWithGroupSync", "AllMemoryBarrierWithGroupSync"];

    /// <summary>MSL functions whose C# name is not simply the PascalCase MSL name (.NET or HLSL spelling instead).</summary>
    private static readonly Dictionary<string, string> Renamed = new()
    {
        ["mix"] = "Lerp",
        ["popcount"] = "CountBits",
        ["clz"] = "LeadingZeroCount",
        ["ctz"] = "TrailingZeroCount",
        ["reverse_bits"] = "ReverseBits",
        ["isnan"] = "IsNaN",
        ["isinf"] = "IsInfinity",
        ["isfinite"] = "IsFinite",
        ["copysign"] = "CopySign",
        ["pack_float_to_unorm4x8"] = "PackUnorm4x8",
        ["unpack_unorm4x8_to_float"] = "UnpackUnorm4x8",
    };

    /// <summary>
    /// Every public static <see cref="Gpu"/> method maps to MSL in exactly one way: a function call
    /// (<c>[MslIntrinsic]</c>: <c>metal::</c>, a <c>__ms_</c> prelude helper, or the <c>as_type</c>
    /// bit cast), a binary operator (<c>[MslBinaryOperator]</c>, only <c>Mul</c>), or an atomic
    /// function (<c>[MslAtomic]</c>, exactly the <c>Atomic*</c> methods). The exception is the three
    /// threadgroup barriers, which the generator recognizes by name: one MSL function with different
    /// flags, no arguments.
    /// </summary>
    [Fact]
    public void EveryPublicGpuMethodHasExactlyOneMslMapping()
    {
        MethodInfo[] methods = typeof(Gpu).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(methods);
        foreach (MethodInfo m in methods)
        {
            MslIntrinsicAttribute? attr = m.GetCustomAttribute<MslIntrinsicAttribute>();
            MslBinaryOperatorAttribute? op = m.GetCustomAttribute<MslBinaryOperatorAttribute>();
            MslAtomicAttribute? atomic = m.GetCustomAttribute<MslAtomicAttribute>();
            int mappings = (attr is null ? 0 : 1) + (op is null ? 0 : 1) + (atomic is null ? 0 : 1);

            if (Barriers.Contains(m.Name))
            {
                Assert.True(mappings == 0, $"{m}: barriers are recognized by name and carry no mapping attribute");
                Assert.Equal(typeof(void), m.ReturnType);
                Assert.Empty(m.GetParameters());
                continue;
            }

            Assert.True(mappings == 1, $"{m} needs exactly one of [MslIntrinsic], [MslBinaryOperator] and [MslAtomic]");
            Assert.True(atomic is not null == m.Name.StartsWith("Atomic", StringComparison.Ordinal), $"{m}: exactly the Atomic* methods are [MslAtomic]");
            if (op is not null)
            {
                Assert.Equal("Mul", m.Name);
                Assert.Equal("*", op.Operator);
                Assert.True(op.SwapOperands, $"{m}: Mul must swap operands (the MSL matrix is the transpose)");
                Assert.Equal(2, m.GetParameters().Length);
                continue;
            }

            if (atomic is not null)
            {
                continue; // AtomicsMapToTheExplicitMetalFunctions
            }

            if (attr!.Name.StartsWith("as_type<", StringComparison.Ordinal))
            {
                // as_type<T> is a keyword-like template, not a metal:: function: AsFloat -> as_type<float[N]>.
                string target = MslName(m.ReturnType);
                Assert.Equal($"as_type<{target}>", attr.Name);
                string scalar = target.TrimEnd('2', '3', '4');
                Assert.Equal("As" + (scalar == "uint" ? "UInt" : char.ToUpperInvariant(scalar[0]) + scalar[1..]), m.Name);
                continue;
            }

            string prefix = attr.Name.StartsWith("metal::", StringComparison.Ordinal) ? "metal::"
                : attr.Name.StartsWith("__ms_", StringComparison.Ordinal) ? "__ms_"
                : throw new InvalidOperationException($"{m}: {attr.Name} must start with metal:: or __ms_");

            // C# name is the PascalCase MSL name, except for the names in Renamed.
            string msl = attr.Name[prefix.Length..];
            string expected = Renamed.TryGetValue(msl, out string? renamed) ? renamed : char.ToUpperInvariant(msl[0]) + msl[1..];
            Assert.True(expected == m.Name, $"{m}: maps to {attr.Name}, expected method name {expected}");
        }
    }

    /// <summary>
    /// <c>AtomicAdd</c> → <c>metal::atomic_fetch_add_explicit</c> and so on; the location is the first
    /// parameter, by <c>ref</c>, the result has its type, and int and uint are always both there.
    /// </summary>
    [Fact]
    public void AtomicsMapToTheExplicitMetalFunctions()
    {
        var expected = new Dictionary<string, string>
        {
            ["AtomicAdd"] = "metal::atomic_fetch_add_explicit",
            ["AtomicSub"] = "metal::atomic_fetch_sub_explicit",
            ["AtomicMin"] = "metal::atomic_fetch_min_explicit",
            ["AtomicMax"] = "metal::atomic_fetch_max_explicit",
            ["AtomicAnd"] = "metal::atomic_fetch_and_explicit",
            ["AtomicOr"] = "metal::atomic_fetch_or_explicit",
            ["AtomicXor"] = "metal::atomic_fetch_xor_explicit",
            ["AtomicExchange"] = "metal::atomic_exchange_explicit",
            ["AtomicCompareExchange"] = "__ms_atomic_compare_exchange",
        };
        MethodInfo[] atomics = typeof(Gpu).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<MslAtomicAttribute>() is not null)
            .ToArray();
        Assert.Equal(expected.Keys.Order(), atomics.Select(m => m.Name).Distinct().Order());

        foreach (MethodInfo m in atomics)
        {
            Assert.Equal(expected[m.Name], m.GetCustomAttribute<MslAtomicAttribute>()!.Name);
            ParameterInfo[] ps = m.GetParameters();
            Assert.True(ps[0].ParameterType.IsByRef && !ps[0].IsOut && !ps[0].IsIn, $"{m}: the location is a ref parameter");
            Type element = ps[0].ParameterType.GetElementType()!;
            Assert.Equal(element, m.ReturnType);
            Assert.All(ps.Skip(1), p => Assert.Equal(element, p.ParameterType));
            Assert.Equal(m.Name == "AtomicCompareExchange" ? 3 : 2, ps.Length);
        }

        // int and uint for every operation; float only for the add.
        foreach (string name in expected.Keys)
        {
            Type[] elements = atomics.Where(m => m.Name == name).Select(m => m.ReturnType).ToArray();
            Assert.Equal(name == "AtomicAdd" ? ["Int32", "Single", "UInt32"] : new[] { "Int32", "UInt32" }, elements.Select(t => t.Name).Order());
        }

        // The strong compare-and-swap is a prelude helper, one overload per atomic type.
        Assert.Contains("inline int __ms_atomic_compare_exchange(device metal::atomic_int* location, int comparand, int value)", MslPrelude.Source, StringComparison.Ordinal);
        Assert.Contains("inline uint __ms_atomic_compare_exchange(device metal::atomic_uint* location, uint comparand, uint value)", MslPrelude.Source, StringComparison.Ordinal);
    }

    /// <summary>Every <c>__ms_</c> helper a <see cref="Gpu"/> method maps to is defined, for that signature, in <see cref="MslPrelude.Source"/>.</summary>
    [Fact]
    public void EveryPreludeIntrinsicIsDefinedInThePrelude()
    {
        MethodInfo[] prelude = typeof(Gpu).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<MslIntrinsicAttribute>()?.Name.StartsWith("__ms_", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.NotEmpty(prelude);
        foreach (MethodInfo m in prelude)
        {
            string name = m.GetCustomAttribute<MslIntrinsicAttribute>()!.Name;
            string type = MslName(m.ReturnType);
            string parameters = string.Join(", ", m.GetParameters().Select(p => $"{MslName(p.ParameterType)} {p.Name}"));
            string signature = $"inline {type} {name}({parameters})";
            Assert.True(MslPrelude.Source.Contains(signature, StringComparison.Ordinal), $"{m}: prelude lacks '{signature}'");
        }
    }

    [Fact]
    public void GpuSurfaceCoversTheContract()
    {
        string[] componentwise =
        [
            "Abs", "Min", "Max", "Clamp", "Saturate", "Sign", "Floor", "Ceil", "Round", "Trunc", "Fract", "Fmod",
            "Sqrt", "Rsqrt", "Pow", "Exp", "Exp2", "Exp10", "Log", "Log2", "Log10", "Sin", "Cos", "Tan", "Asin", "Acos", "Atan", "Atan2",
            "Sinh", "Cosh", "Tanh", "Lerp", "Step", "Smoothstep", "Fma", "CopySign", "Radians", "Degrees",
        ];
        string[] bits = ["CountBits", "LeadingZeroCount", "TrailingZeroCount", "ReverseBits"];
        string[] other =
        [
            "Dot", "Cross", "Length", "Distance", "Normalize", "Reflect", "Mul", "Transpose", "Determinant",
            "AsFloat", "AsInt", "AsUInt", "IsNaN", "IsInfinity", "IsFinite", "PackUnorm4x8", "UnpackUnorm4x8",
            "AtomicAdd", "AtomicSub", "AtomicMin", "AtomicMax", "AtomicAnd", "AtomicOr", "AtomicXor", "AtomicExchange", "AtomicCompareExchange",
        ];
        string[] expected = [.. componentwise, .. bits, .. other];
        string[] actual = typeof(Gpu).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).Distinct().ToArray();
        Assert.Equal(expected.Concat(Barriers).Order(), actual.Order());

        // Signatures other code already depends on.
        Assert.NotNull(typeof(Gpu).GetMethod("Sqrt", [typeof(float)]));
        Assert.NotNull(typeof(Gpu).GetMethod("Dot", [typeof(Float3), typeof(Float3)]));

        // int/uint scalars and vectors for the bit functions.
        foreach (string name in bits)
        {
            foreach (Type element in new[] { typeof(int), typeof(uint) })
            {
                Assert.Equal(element, typeof(Gpu).GetMethod(name, [element])?.ReturnType);
                for (int n = 2; n <= 4; n++)
                {
                    Type v = VectorTypes.Vector(element, n);
                    Assert.Equal(v, typeof(Gpu).GetMethod(name, [v])?.ReturnType);
                }
            }

            Assert.Null(typeof(Gpu).GetMethod(name, [typeof(float)]));
        }

        // Bit reinterpretation between float and int/uint, scalars and vectors of the same width.
        foreach ((string name, Type from, Type to) in new[] { ("AsFloat", typeof(int), typeof(float)), ("AsFloat", typeof(uint), typeof(float)), ("AsInt", typeof(float), typeof(int)), ("AsUInt", typeof(float), typeof(uint)) })
        {
            Assert.Equal(to, typeof(Gpu).GetMethod(name, [from])?.ReturnType);
            for (int n = 2; n <= 4; n++)
            {
                Assert.Equal(VectorTypes.Vector(to, n), typeof(Gpu).GetMethod(name, [VectorTypes.Vector(from, n)])?.ReturnType);
            }
        }

        // Scalar float -> bool classification; unorm8 packing of a Float4 into a uint and back.
        foreach (string name in new[] { "IsNaN", "IsInfinity", "IsFinite" })
        {
            Assert.Equal(typeof(bool), Assert.Single(typeof(Gpu).GetMethods(), m => m.Name == name).ReturnType);
            Assert.NotNull(typeof(Gpu).GetMethod(name, [typeof(float)]));
        }

        Assert.Equal(typeof(uint), typeof(Gpu).GetMethod("PackUnorm4x8", [typeof(Float4)])?.ReturnType);
        Assert.Equal(typeof(Float4), typeof(Gpu).GetMethod("UnpackUnorm4x8", [typeof(uint)])?.ReturnType);

        // Transpose, Determinant and the three Mul forms for every matrix size.
        foreach (Type matrix in MatrixTests.All)
        {
            Type row = VectorTypes.Vector(typeof(float), MatrixTests.Size(matrix));
            Assert.Equal(matrix, typeof(Gpu).GetMethod("Transpose", [matrix])?.ReturnType);
            Assert.Equal(typeof(float), typeof(Gpu).GetMethod("Determinant", [matrix])?.ReturnType);
            Assert.Equal(row, typeof(Gpu).GetMethod("Mul", [row, matrix])?.ReturnType);
            Assert.Equal(row, typeof(Gpu).GetMethod("Mul", [matrix, row])?.ReturnType);
            Assert.Equal(matrix, typeof(Gpu).GetMethod("Mul", [matrix, matrix])?.ReturnType);
        }

        // float + Float2..4 for every componentwise function; int/uint vectors for abs/min/max/clamp.
        foreach (string name in componentwise)
        {
            int arity = typeof(Gpu).GetMethods().First(m => m.Name == name && m.GetParameters()[0].ParameterType == typeof(float)).GetParameters().Length;
            Type[] elements = name is "Abs" or "Min" or "Max" or "Clamp" ? [typeof(float), typeof(int), typeof(uint)] : [typeof(float)];
            foreach (Type element in elements)
            {
                Assert.True(typeof(Gpu).GetMethod(name, Enumerable.Repeat(element, arity).ToArray()) is not null, $"{name}({element.Name})");
                for (int n = 2; n <= 4; n++)
                {
                    Type v = VectorTypes.Vector(element, n);
                    Assert.True(typeof(Gpu).GetMethod(name, Enumerable.Repeat(v, arity).ToArray()) is not null, $"{name}({v.Name})");
                }
            }
        }
    }

    [Fact]
    public void ExactlyTheVectorAndMatrixTypesCarryMslType()
    {
        Type[] tagged = typeof(Float2).Assembly.GetTypes().Where(t => t.GetCustomAttribute<MslTypeAttribute>() is not null).ToArray();
        Assert.Equal(VectorTypes.All.Concat(MatrixTests.All).OrderBy(t => t.Name), tagged.OrderBy(t => t.Name));
    }

    [Fact]
    public void OnlyFloat4x4HasAPackedLayout()
    {
        // Float2x2 and Float3x3 are locals only: without Packed, the generator rejects them at the
        // CPU/GPU boundary (MSL aligns float2x2 to 8 bytes and pads float3x3 columns to 16).
        Assert.Equal("float2x2", typeof(Float2x2).GetCustomAttribute<MslTypeAttribute>()!.Name);
        Assert.Equal("float3x3", typeof(Float3x3).GetCustomAttribute<MslTypeAttribute>()!.Name);
        Assert.Null(typeof(Float2x2).GetCustomAttribute<MslTypeAttribute>()!.Packed);
        Assert.Null(typeof(Float3x3).GetCustomAttribute<MslTypeAttribute>()!.Packed);

        // Float4x4 is 16 floats on both sides. Its boundary type is the prelude's struct of four
        // packed_float4 columns, with the conversions the generator emits around every load and store.
        MslTypeAttribute attribute = typeof(Float4x4).GetCustomAttribute<MslTypeAttribute>()!;
        Assert.Equal("float4x4", attribute.Name);
        Assert.Equal("__ms_packed_float4x4", attribute.Packed);
        Assert.Contains("struct __ms_packed_float4x4 { packed_float4 columns[4]; };", MslPrelude.Source, StringComparison.Ordinal);
        Assert.Contains("static_assert(sizeof(__ms_packed_float4x4) == 64 && alignof(__ms_packed_float4x4) == 4,", MslPrelude.Source, StringComparison.Ordinal);
        Assert.Contains("inline float4x4 __ms_unpack(__ms_packed_float4x4 m) { return float4x4(float4(m.columns[0]), float4(m.columns[1]), float4(m.columns[2]), float4(m.columns[3])); }", MslPrelude.Source, StringComparison.Ordinal);
        Assert.Contains("inline __ms_packed_float4x4 __ms_pack(float4x4 m) { return __ms_packed_float4x4{{m[0], m[1], m[2], m[3]}}; }", MslPrelude.Source, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(VectorTypes.AllData), MemberType = typeof(VectorTypes))]
    public void VectorStructHasMatchingMslType(Type t)
    {
        Assert.True(t.IsValueType);
        Assert.Equal("SharpCompute", t.Namespace);
        MslTypeAttribute attr = t.GetCustomAttribute<MslTypeAttribute>()!;
        string name = VectorTypes.MslScalar(VectorTypes.Element(t)) + VectorTypes.Width(t);
        Assert.Equal(name, attr.Name);
        Assert.Equal("packed_" + name, attr.Packed);
    }

    /// <summary>
    /// The generator maps every public property to a swizzle and every public method of a vector to
    /// nothing at all, so the public instance surface must be exactly swizzle properties + ToString.
    /// </summary>
    [Theory]
    [MemberData(nameof(VectorTypes.AllData), MemberType = typeof(VectorTypes))]
    public void PublicInstanceSurfaceIsOnlySwizzles(Type t)
    {
        string letters = VectorTypes.Components[..VectorTypes.Width(t)];
        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.True(p.Name.Length <= 4 && p.Name.All(c => letters.Contains(c, StringComparison.Ordinal)), $"{t.Name}.{p.Name} is not a swizzle");
            Assert.Empty(p.GetIndexParameters());
        }

        MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToArray();
        Assert.Equal(["ToString"], methods.Select(m => m.Name));
        Assert.Empty(t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
        Assert.Empty(t.GetProperties(BindingFlags.Public | BindingFlags.Static));
    }

    private static string MslName(Type t) =>
        t == typeof(float) ? "float"
        : t == typeof(int) ? "int"
        : t == typeof(uint) ? "uint"
        : t.GetCustomAttribute<MslTypeAttribute>()?.Name ?? throw new InvalidOperationException($"no MSL name for {t}");
}
