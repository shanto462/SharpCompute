using System;

namespace SharpCompute;

// The bits of a half. The half vectors and their intrinsics are generated (Gpu.g.cs, with the scalar
// implementations in HalfMath.cs); these four are scalar, because the 16-bit integers a half
// reinterprets as are scalars only.
public static partial class Gpu
{
    /// <summary>Reinterprets the 16 bits of <paramref name="x"/> as a half (MSL <c>as_type&lt;half&gt;</c>).</summary>
    /// <remarks>
    /// No conversion happens, on the GPU either: the bits of every number survive a round trip
    /// through <see cref="AsUShort(Half)"/>, <c>-0</c> and subnormals included, and a NaN stays a NaN
    /// (measured on an Apple M4 Pro). The same as <see cref="BitConverter.UInt16BitsToHalf(ushort)"/>, which a shader
    /// can call as well.
    /// </remarks>
    [MslIntrinsic("as_type<half>")]
    public static Half AsHalf(ushort x) => BitConverter.UInt16BitsToHalf(x);

    /// <summary>Reinterprets the 16 bits of <paramref name="x"/> as a half (MSL <c>as_type&lt;half&gt;</c>).</summary>
    /// <remarks>The same as <see cref="BitConverter.Int16BitsToHalf(short)"/>; see <see cref="AsHalf(ushort)"/>.</remarks>
    [MslIntrinsic("as_type<half>")]
    public static Half AsHalf(short x) => BitConverter.Int16BitsToHalf(x);

    /// <summary>Reinterprets the 16 bits of <paramref name="x"/> as a ushort (MSL <c>as_type&lt;ushort&gt;</c>): sign, 5 bits of exponent, 10 of mantissa.</summary>
    /// <remarks>
    /// The same as <see cref="BitConverter.HalfToUInt16Bits(Half)"/>; see <see cref="AsHalf(ushort)"/>.
    /// <see cref="Half"/> converts implicitly from <c>byte</c> and <c>sbyte</c>, so an integer argument
    /// converts to a half first and its bits are those of the half: <c>Gpu.AsUShort((byte)1)</c> is
    /// <c>0x3C00</c> (1.0), not 1.
    /// </remarks>
    [MslIntrinsic("as_type<ushort>")]
    public static ushort AsUShort(Half x) => BitConverter.HalfToUInt16Bits(x);

    /// <summary>Reinterprets the 16 bits of <paramref name="x"/> as a short (MSL <c>as_type&lt;short&gt;</c>): negative exactly when the sign bit is set.</summary>
    /// <remarks>
    /// The same as <see cref="BitConverter.HalfToInt16Bits(Half)"/>; see <see cref="AsHalf(ushort)"/>.
    /// As for <see cref="AsUShort(Half)"/>, a <c>byte</c> or <c>sbyte</c> argument converts to a half
    /// first: <c>Gpu.AsShort((byte)1)</c> is <c>0x3C00</c>.
    /// </remarks>
    [MslIntrinsic("as_type<short>")]
    public static short AsShort(Half x) => BitConverter.HalfToInt16Bits(x);
}
