using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace SharpCompute.Vectors.Tests;

/// <summary>Reflection helpers shared by the vector/intrinsic tests.</summary>
internal static class VectorTypes
{
    public const string Components = "XYZW";

    public static readonly Type[] All =
    [
        typeof(Float2), typeof(Float3), typeof(Float4),
        typeof(Int2), typeof(Int3), typeof(Int4),
        typeof(UInt2), typeof(UInt3), typeof(UInt4),
    ];

    public static TheoryData<Type> AllData
    {
        get
        {
            var data = new TheoryData<Type>();
            foreach (Type t in All)
            {
                data.Add(t);
            }

            return data;
        }
    }

    public static bool IsVector(Type t) => All.Contains(t);

    public static int Width(Type t) => t.Name[^1] - '0';

    public static Type Element(Type t) =>
        t.Name.StartsWith("Float", StringComparison.Ordinal) ? typeof(float)
        : t.Name.StartsWith("UInt", StringComparison.Ordinal) ? typeof(uint)
        : typeof(int);

    public static string MslScalar(Type element) =>
        element == typeof(float) ? "float" : element == typeof(uint) ? "uint" : "int";

    public static Type Vector(Type element, int width)
    {
        string prefix = element == typeof(float) ? "Float" : element == typeof(uint) ? "UInt" : "Int";
        return typeof(Float2).Assembly.GetType($"SharpCompute.{prefix}{width}", throwOnError: true)!;
    }

    /// <summary>Converts a small integer to a boxed scalar of <paramref name="element"/>.</summary>
    public static object Scalar(Type element, double value) =>
        element == typeof(float) ? (float)value
        : element == typeof(uint) ? (uint)value
        : (object)(int)value;

    /// <summary>Builds a vector from boxed components via the per-component constructor.</summary>
    public static object Make(Type t, IEnumerable<object> components)
    {
        object[] args = components.ToArray();
        Type[] sig = Enumerable.Repeat(Element(t), Width(t)).ToArray();
        ConstructorInfo ctor = t.GetConstructor(sig) ?? throw new InvalidOperationException($"{t.Name} has no per-component ctor");
        return ctor.Invoke(args);
    }

    public static object Get(object vector, int index) =>
        vector.GetType().GetProperty(Components[index].ToString())!.GetValue(vector)!;

    public static object[] Get(object vector) =>
        Enumerable.Range(0, Width(vector.GetType())).Select(i => Get(vector, i)).ToArray();

    /// <summary>
    /// Deterministic sample values: argument <paramref name="arg"/>, component <paramref name="index"/>.
    /// Distinct per component, non-zero (safe as divisors), and unsigned-safe for <c>uint</c>.
    /// </summary>
    public static object Sample(Type element, int arg, int index)
    {
        double[][] floats = [[0.25, -0.5, 0.75, -1.5], [0.5, 2, -0.25, 3], [1, 0.125, 4, -2]];
        double[][] ints = [[7, -3, 12, -9], [2, 5, -4, 3], [10, -1, 1, 6]];
        double[][] uints = [[7, 3, 12, 9], [2, 5, 4, 3], [10, 1, 1, 6]];
        double[][] table = element == typeof(float) ? floats : element == typeof(uint) ? uints : ints;
        return Scalar(element, table[arg][index]);
    }

    public static object SampleVector(Type t, int arg) =>
        Make(t, Enumerable.Range(0, Width(t)).Select(i => Sample(Element(t), arg, i)));

    /// <summary>Equality that treats all NaNs as equal and distinguishes +0 from -0.</summary>
    public static bool SameValue(object expected, object actual)
    {
        if (expected is float e && actual is float a)
        {
            return float.IsNaN(e) ? float.IsNaN(a) : BitConverter.SingleToInt32Bits(e) == BitConverter.SingleToInt32Bits(a);
        }

        return Equals(expected, actual);
    }

    public static string Format(object value) => value switch
    {
        float f when f == 0 && float.IsNegative(f) => "-0",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null",
    };
}

/// <summary>Assertions on exact float values (bitwise, NaN-aware, signed-zero-aware).</summary>
internal static class FloatAssert
{
    public static void Same(float expected, float actual)
    {
        Assert.True(
            VectorTypes.SameValue(expected, actual),
            $"expected {VectorTypes.Format(expected)}, got {VectorTypes.Format(actual)}");
    }

    public static void Near(float expected, float actual, float tolerance = 1e-6f)
    {
        Assert.True(
            MathF.Abs(expected - actual) <= tolerance * MathF.Max(1f, MathF.Abs(expected)),
            $"expected {VectorTypes.Format(expected)} ± {tolerance}, got {VectorTypes.Format(actual)}");
    }

    public static void SameVector(object expectedVector, object actualVector)
    {
        Assert.Equal(expectedVector.GetType(), actualVector.GetType());
        object[] e = VectorTypes.Get(expectedVector);
        object[] a = VectorTypes.Get(actualVector);
        for (int i = 0; i < e.Length; i++)
        {
            Assert.True(
                VectorTypes.SameValue(e[i], a[i]),
                $"component {VectorTypes.Components[i]}: expected {VectorTypes.Format(e[i])}, got {VectorTypes.Format(a[i])}");
        }
    }
}
