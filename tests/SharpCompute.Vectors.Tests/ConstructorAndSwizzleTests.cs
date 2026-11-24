using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace SharpCompute.Vectors.Tests;

public class ConstructorAndSwizzleTests
{
    [Fact]
    public void BroadcastConstructorFillsEveryComponent()
    {
        FloatAssert.SameVector(new Float4(2.5f, 2.5f, 2.5f, 2.5f), new Float4(2.5f));
        Assert.Equal(new Int3(-7, -7, -7), new Int3(-7));
        Assert.Equal(new UInt2(9, 9), new UInt2(9));
    }

    [Fact]
    public void CompositeConstructorsFillComponentsInOrder()
    {
        var xy = new Float2(1, 2);
        var zw = new Float2(3, 4);
        var expected = new Float4(1, 2, 3, 4);
        FloatAssert.SameVector(expected, new Float4(xy, zw));
        FloatAssert.SameVector(expected, new Float4(new Float3(1, 2, 3), 4));
        FloatAssert.SameVector(expected, new Float4(1, new Float3(2, 3, 4)));
        FloatAssert.SameVector(expected, new Float4(xy, 3, 4));
        FloatAssert.SameVector(expected, new Float4(1, new Float2(2, 3), 4));
        FloatAssert.SameVector(expected, new Float4(1, 2, zw));
        FloatAssert.SameVector(new Float3(1, 2, 3), new Float3(xy, 3));
        FloatAssert.SameVector(new Float3(1, 2, 3), new Float3(1, new Float2(2, 3)));
        Assert.Equal(new Int4(1, 2, 3, 4), new Int4(new Int3(1, 2, 3), 4));
        Assert.Equal(new UInt3(1, 2, 3), new UInt3(1, new UInt2(2, 3)));
    }

    /// <summary>
    /// Every constructor is either the broadcast <c>T(s)</c> or a sequence of scalars/smaller
    /// vectors of the same element type whose widths sum to the vector width (like MSL).
    /// </summary>
    [Theory]
    [MemberData(nameof(VectorTypes.AllData), MemberType = typeof(VectorTypes))]
    public void EveryConstructorMatchesAnMslConstructor(Type t)
    {
        int n = VectorTypes.Width(t);
        Type element = VectorTypes.Element(t);
        ConstructorInfo[] ctors = t.GetConstructors();

        // Broadcast + per-component + compositions of n into parts 1..n-1 (excluding all ones).
        int expectedCount = n switch { 2 => 2, 3 => 4, 4 => 8, _ => throw new InvalidOperationException() };
        Assert.Equal(expectedCount, ctors.Length);

        foreach (ConstructorInfo ctor in ctors)
        {
            ParameterInfo[] ps = ctor.GetParameters();
            if (ps.Length == 1 && ps[0].ParameterType == element)
            {
                object broadcast = ctor.Invoke([VectorTypes.Scalar(element, 5)]);
                Assert.All(VectorTypes.Get(broadcast), c => Assert.Equal(VectorTypes.Scalar(element, 5), c));
                continue;
            }

            int next = 1;
            var args = new List<object>();
            foreach (ParameterInfo p in ps)
            {
                if (p.ParameterType == element)
                {
                    args.Add(VectorTypes.Scalar(element, next++));
                }
                else
                {
                    Assert.True(VectorTypes.IsVector(p.ParameterType), $"{ctor}: unexpected parameter {p.ParameterType}");
                    Assert.Equal(element, VectorTypes.Element(p.ParameterType));
                    int k = VectorTypes.Width(p.ParameterType);
                    Assert.InRange(k, 2, n - 1);
                    args.Add(VectorTypes.Make(p.ParameterType, Enumerable.Range(next, k).Select(v => VectorTypes.Scalar(element, v))));
                    next += k;
                }
            }

            Assert.Equal(n + 1, next);
            object result = ctor.Invoke(args.ToArray());
            Assert.Equal(
                Enumerable.Range(1, n).Select(v => VectorTypes.Scalar(element, v)).ToArray(),
                VectorTypes.Get(result));
        }
    }

    [Fact]
    public void ComponentPropertiesReadAndWrite()
    {
        var v = new Float4(1, 2, 3, 4);
        v.X = 10;
        v.W = 40;
        Assert.Equal(10f, v.X);
        Assert.Equal(2f, v.Y);
        Assert.Equal(3f, v.Z);
        Assert.Equal(40f, v.W);
    }

    [Fact]
    public void SwizzleExamples()
    {
        var v = new Float4(1, 2, 3, 4);
        FloatAssert.SameVector(new Float4(4, 3, 2, 1), v.WZYX);
        FloatAssert.SameVector(new Float3(1, 2, 3), v.XYZ);
        FloatAssert.SameVector(new Float2(4, 4), v.WW);
        FloatAssert.SameVector(new Float4(1, 2, 1, 2), new Float2(1, 2).XYXY);
        Assert.Equal(new Int3(3, 3, 1), new Int3(1, 2, 3).ZZX);

        v.ZX = new Float2(30, 10);
        FloatAssert.SameVector(new Float4(10, 2, 30, 4), v);

        var u = new UInt3(1, 2, 3);
        u.YZ = u.ZY;
        Assert.Equal(new UInt3(1, 3, 2), u);
    }

    /// <summary>
    /// Every 2-4 letter combination of the vector's components exists, returns the right
    /// components, and is settable exactly when its components are distinct.
    /// </summary>
    [Theory]
    [MemberData(nameof(VectorTypes.AllData), MemberType = typeof(VectorTypes))]
    public void AllSwizzlesExistAndRoundTrip(Type t)
    {
        int n = VectorTypes.Width(t);
        Type element = VectorTypes.Element(t);
        string letters = VectorTypes.Components[..n];
        object source = VectorTypes.Make(t, Enumerable.Range(1, n).Select(i => VectorTypes.Scalar(element, i)));

        List<string> names = [];
        for (int length = 2; length <= 4; length++)
        {
            names.AddRange(Combinations(letters, length));
        }

        PropertyInfo[] swizzles = t.GetProperties().Where(p => p.Name.Length > 1).ToArray();
        Assert.Equal(names.Count, swizzles.Length);

        foreach (string name in names)
        {
            PropertyInfo p = t.GetProperty(name) ?? throw new InvalidOperationException($"{t.Name}.{name} missing");
            Assert.Equal(VectorTypes.Vector(element, name.Length), p.PropertyType);

            object value = p.GetValue(source)!;
            for (int i = 0; i < name.Length; i++)
            {
                Assert.Equal(VectorTypes.Scalar(element, letters.IndexOf(name[i], StringComparison.Ordinal) + 1), VectorTypes.Get(value, i));
            }

            bool distinct = name.Distinct().Count() == name.Length;
            Assert.True(distinct == (p.SetMethod is not null), $"{t.Name}.{name}: setter presence");
            if (!distinct)
            {
                continue;
            }

            // Write 10*(i+1) through the swizzle and check exactly those components changed.
            object target = VectorTypes.Make(t, Enumerable.Range(1, n).Select(i => VectorTypes.Scalar(element, i)));
            object written = VectorTypes.Make(p.PropertyType, Enumerable.Range(1, name.Length).Select(i => VectorTypes.Scalar(element, 10 * i)));
            p.SetValue(target, written); // boxed struct: mutated in place
            for (int c = 0; c < n; c++)
            {
                int at = name.IndexOf(letters[c], StringComparison.Ordinal);
                object expected = VectorTypes.Scalar(element, at >= 0 ? 10 * (at + 1) : c + 1);
                Assert.Equal(expected, VectorTypes.Get(target, c));
            }

            Assert.Equal(VectorTypes.Get(written), VectorTypes.Get(p.GetValue(target)!));
        }
    }

    [Fact]
    public void ToStringUsesInvariantCulture()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("<1.5, -2, 3>", new Float3(1.5f, -2, 3).ToString());
            Assert.Equal("<1, -2>", new Int2(1, -2).ToString());
            Assert.Equal("<1, 2, 3, 4294967295>", new UInt4(1, 2, 3, uint.MaxValue).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void DefaultEqualsComparesComponents()
    {
        Assert.True(new Int2(1, 2).Equals(new Int2(1, 2)));
        Assert.False(new Int2(1, 2).Equals(new Int2(2, 1)));
        Assert.True(new Float3(1, 2, 3).Equals(new Float3(1, 2, 3)));
        Assert.Equal(new UInt3(4, 5, 6).GetHashCode(), new UInt3(4, 5, 6).GetHashCode());
    }

    private static IEnumerable<string> Combinations(string letters, int length) =>
        length == 0 ? [string.Empty] : Combinations(letters, length - 1).SelectMany(prefix => letters.Select(c => prefix + c));
}
