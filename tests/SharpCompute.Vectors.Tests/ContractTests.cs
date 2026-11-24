using System;
using System.Linq;
using System.Reflection;

namespace SharpCompute.Vectors.Tests;

/// <summary>Guards the attribute contract that the source generator relies on.</summary>
public class ContractTests
{
    /// <summary>
    /// Every public static <see cref="Gpu"/> method maps to MSL in exactly one way: a function call
    /// (<c>[MslIntrinsic]</c>, either <c>metal::</c> or a <c>__ms_</c> prelude helper) or a binary operator
    /// (<c>[MslBinaryOperator]</c>, only <c>Mul</c>).
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
            Assert.True(attr is null != (op is null), $"{m} needs exactly one of [MslIntrinsic] and [MslBinaryOperator]");
            if (op is not null)
            {
                Assert.Equal("Mul", m.Name);
                Assert.Equal("*", op.Operator);
                Assert.True(op.SwapOperands, $"{m}: Mul must swap operands (the MSL matrix is the transpose)");
                Assert.Equal(2, m.GetParameters().Length);
                continue;
            }

            string prefix = attr!.Name.StartsWith("metal::", StringComparison.Ordinal) ? "metal::"
                : attr.Name.StartsWith("__ms_", StringComparison.Ordinal) ? "__ms_"
                : throw new InvalidOperationException($"{m}: {attr.Name} must start with metal:: or __ms_");

            // C# name is the PascalCase MSL name, except mix -> Lerp.
            string msl = attr.Name[prefix.Length..];
            string expected = msl == "mix" ? "Lerp" : char.ToUpperInvariant(msl[0]) + msl[1..];
            Assert.True(expected == m.Name, $"{m}: maps to {attr.Name}, expected method name {expected}");
        }
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
        string[] expected =
        [
            "Abs", "Min", "Max", "Clamp", "Saturate", "Sign", "Floor", "Ceil", "Round", "Trunc", "Fract", "Fmod",
            "Sqrt", "Rsqrt", "Pow", "Exp", "Exp2", "Log", "Log2", "Sin", "Cos", "Tan", "Asin", "Acos", "Atan", "Atan2",
            "Sinh", "Cosh", "Tanh", "Lerp", "Step", "Smoothstep", "Dot", "Cross", "Length", "Distance", "Normalize", "Reflect",
            "Radians", "Degrees", "Mul",
        ];
        string[] actual = typeof(Gpu).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).Distinct().ToArray();
        Assert.Equal(expected.Order(), actual.Order());

        // Signatures other code already depends on.
        Assert.NotNull(typeof(Gpu).GetMethod("Sqrt", [typeof(float)]));
        Assert.NotNull(typeof(Gpu).GetMethod("Dot", [typeof(Float3), typeof(Float3)]));

        // float + Float2..4 for every componentwise function; int/uint vectors for abs/min/max/clamp.
        foreach (string name in expected.Except(["Dot", "Cross", "Length", "Distance", "Normalize", "Reflect", "Mul"]))
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
    public void MatricesHaveNoPackedLayout()
    {
        // Locals only in v1: without Packed, the generator rejects matrices at the CPU/GPU boundary.
        Assert.Equal("float2x2", typeof(Float2x2).GetCustomAttribute<MslTypeAttribute>()!.Name);
        Assert.Equal("float3x3", typeof(Float3x3).GetCustomAttribute<MslTypeAttribute>()!.Name);
        Assert.Null(typeof(Float2x2).GetCustomAttribute<MslTypeAttribute>()!.Packed);
        Assert.Null(typeof(Float3x3).GetCustomAttribute<MslTypeAttribute>()!.Packed);
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
        : t.GetCustomAttribute<MslTypeAttribute>()?.Name ?? throw new InvalidOperationException($"no MSL name for {t}");
}
