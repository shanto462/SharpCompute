using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpCompute.Vectors.Tests;

/// <summary>The C# vectors must match MSL <c>packed_*</c> layout: tight, 4-byte aligned.</summary>
public class LayoutTests
{
    [Fact]
    public void SizesMatchPackedMslTypes()
    {
        Assert.Equal(8, Unsafe.SizeOf<Float2>());
        Assert.Equal(12, Unsafe.SizeOf<Float3>());
        Assert.Equal(16, Unsafe.SizeOf<Float4>());
        Assert.Equal(8, Unsafe.SizeOf<Int2>());
        Assert.Equal(12, Unsafe.SizeOf<Int3>());
        Assert.Equal(16, Unsafe.SizeOf<Int4>());
        Assert.Equal(8, Unsafe.SizeOf<UInt2>());
        Assert.Equal(12, Unsafe.SizeOf<UInt3>());
        Assert.Equal(16, Unsafe.SizeOf<UInt4>());
    }

    [Theory]
    [MemberData(nameof(VectorTypes.AllData), MemberType = typeof(VectorTypes))]
    public void FieldsAreSequentialAtFourByteStride(Type t)
    {
        Assert.Equal(LayoutKind.Sequential, t.StructLayoutAttribute!.Value);
        Assert.Equal(4, t.StructLayoutAttribute.Pack);
        Assert.Equal(4 * VectorTypes.Width(t), Marshal.SizeOf(t));
        for (int i = 0; i < VectorTypes.Width(t); i++)
        {
            string name = "_" + char.ToLowerInvariant(VectorTypes.Components[i]);
            Assert.Equal(VectorTypes.Element(t), t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.FieldType);
            Assert.Equal(4 * i, (int)Marshal.OffsetOf(t, name));
        }
    }

    [Fact]
    public void ComponentsAreStoredInOrderInManagedMemory()
    {
        AssertInMemory<Float4, float>(new Float4(1, 2, 3, 4), [1f, 2f, 3f, 4f]);
        AssertInMemory<Float3, float>(new Float3(1, 2, 3), [1f, 2f, 3f]);
        AssertInMemory<Int3, int>(new Int3(-1, 2, -3), [-1, 2, -3]);
        AssertInMemory<UInt2, uint>(new UInt2(7, uint.MaxValue), [7u, uint.MaxValue]);
    }

    [Fact]
    public void VectorsAlignToFourBytesInsideStructs()
    {
        // Mirrors how the generator's constants struct / user structs embed vectors.
        var probe = default(AlignmentProbe);
        ref byte start = ref Unsafe.As<AlignmentProbe, byte>(ref probe);
        Assert.Equal(4, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<Float3, byte>(ref probe.F3)));
        Assert.Equal(20, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<Float4, byte>(ref probe.F4)));
        Assert.Equal(40, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<Int2, byte>(ref probe.I2)));
        Assert.Equal(52, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<UInt3, byte>(ref probe.U3)));
        Assert.Equal(64, Unsafe.SizeOf<AlignmentProbe>());
    }

    private static void AssertInMemory<TVector, TElement>(TVector vector, TElement[] expected)
        where TVector : unmanaged
        where TElement : unmanaged
    {
        ref TElement first = ref Unsafe.As<TVector, TElement>(ref vector);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], Unsafe.Add(ref first, i));
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AlignmentProbe
    {
        public byte B0;    // 0
        public Float3 F3;  // 4..16
        public byte B1;    // 16
        public Float4 F4;  // 20..36
        public short S;    // 36
        public Int2 I2;    // 40..48
        public byte B2;    // 48
        public UInt3 U3;   // 52..64
    }
}
