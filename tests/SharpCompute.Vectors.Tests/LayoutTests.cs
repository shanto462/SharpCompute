using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpCompute.Vectors.Tests;

/// <summary>The C# vectors must match MSL <c>packed_*</c> layout: tight, aligned like one component (4 bytes, or 2 for halves).</summary>
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

        // packed_half2/3/4: 4, 6 and 8 bytes. A Half3 really is 6 bytes, not padded to 8.
        Assert.Equal(4, Unsafe.SizeOf<Half2>());
        Assert.Equal(6, Unsafe.SizeOf<Half3>());
        Assert.Equal(8, Unsafe.SizeOf<Half4>());
    }

    /// <summary>Fields in component order at the stride of one component: 4 bytes, or 2 for a half vector.</summary>
    [Theory]
    [MemberData(nameof(VectorTypes.NumericData), MemberType = typeof(VectorTypes))]
    public void FieldsAreSequentialAtTheStrideOfOneComponent(Type t)
    {
        int stride = VectorTypes.ElementSize(VectorTypes.Element(t));
        Assert.Equal(LayoutKind.Sequential, t.StructLayoutAttribute!.Value);
        Assert.Equal(stride, t.StructLayoutAttribute.Pack);
        Assert.Equal(stride * VectorTypes.Width(t), Marshal.SizeOf(t));
        for (int i = 0; i < VectorTypes.Width(t); i++)
        {
            string name = "_" + char.ToLowerInvariant(VectorTypes.Components[i]);
            Assert.Equal(VectorTypes.Element(t), t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.FieldType);
            Assert.Equal(stride * i, (int)Marshal.OffsetOf(t, name));
        }
    }

    [Fact]
    public void ComponentsAreStoredInOrderInManagedMemory()
    {
        AssertInMemory<Float4, float>(new Float4(1, 2, 3, 4), [1f, 2f, 3f, 4f]);
        AssertInMemory<Float3, float>(new Float3(1, 2, 3), [1f, 2f, 3f]);
        AssertInMemory<Int3, int>(new Int3(-1, 2, -3), [-1, 2, -3]);
        AssertInMemory<UInt2, uint>(new UInt2(7, uint.MaxValue), [7u, uint.MaxValue]);
        AssertInMemory<Half3, Half>((Half3)new Float3(1, -2, 3), [(Half)1f, (Half)(-2f), (Half)3f]);
        AssertInMemory<Half4, ushort>(new Half4((Half)1, (Half)(-2), (Half)0.5f, Half.PositiveInfinity), [0x3C00, 0xC000, 0x3800, 0x7C00]);
    }

    [Fact]
    public void HalfVectorsAlignToTwoBytesInsideStructs()
    {
        // The offsets the generator computes for a struct of these fields, and the ones MSL gives the
        // packed_half* members: a half vector starts at the next even offset, a float after it at the
        // next multiple of 4.
        var probe = default(HalfAlignmentProbe);
        ref byte start = ref Unsafe.As<HalfAlignmentProbe, byte>(ref probe);
        Assert.Equal(2, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<Half3, byte>(ref probe.H3)));
        Assert.Equal(8, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<float, byte>(ref probe.F)));
        Assert.Equal(12, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<Half2, byte>(ref probe.H2)));
        Assert.Equal(16, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<Half, byte>(ref probe.H)));
        Assert.Equal(18, (int)Unsafe.ByteOffset(ref start, ref Unsafe.As<Half4, byte>(ref probe.H4)));
        Assert.Equal(28, Unsafe.SizeOf<HalfAlignmentProbe>());

        // A struct of halves alone is 2-byte aligned: no tail padding to a multiple of 4.
        Assert.Equal(14, Unsafe.SizeOf<HalfOnlyProbe>());
    }

    [Fact]
    public void Rgba16FloatAndHalf4AreTheSameEightBytes()
    {
        Assert.Equal(Unsafe.SizeOf<Half4>(), Unsafe.SizeOf<Rgba16Float>());

        var pixel = new Rgba16Float((Half)0.25f, (Half)(-1f), Half.PositiveInfinity, BitConverter.UInt16BitsToHalf(0x7E01));
        Half4 vector = pixel;
        Rgba16Float back = vector;

        // Bit for bit, a NaN payload included: nothing is converted on the way.
        AssertInMemory<Half4, ushort>(vector, [0x3400, 0xBC00, 0x7C00, 0x7E01]);
        AssertInMemory<Rgba16Float, ushort>(back, [0x3400, 0xBC00, 0x7C00, 0x7E01]);

        // Whole spans reinterpret in place, in either direction.
        Half4[] colors = [(Half4)new Float4(1, 2, 3, 4), (Half4)new Float4(5, 6, 7, 8)];
        Span<Rgba16Float> pixels = MemoryMarshal.Cast<Half4, Rgba16Float>(colors);
        Assert.Equal(2, pixels.Length);
        Assert.Equal((Half)7f, pixels[1].B);
        pixels[0].A = (Half)40f;
        Assert.Equal((Half)40f, colors[0].W);
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
    private struct HalfAlignmentProbe
    {
        public byte B0;    // 0
        public Half3 H3;   // 2..8
        public float F;    // 8
        public Half2 H2;   // 12..16
        public Half H;     // 16
        public Half4 H4;   // 18..26, then padded to 28 for the float
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HalfOnlyProbe
    {
        public Half3 A;    // 0..6
        public Half4 B;    // 6..14
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
