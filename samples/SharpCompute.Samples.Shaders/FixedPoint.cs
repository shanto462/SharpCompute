// Original SharpCompute sample (MIT), not a Shadertoy port.
using System;
using System.Globalization;
using System.Numerics;

namespace SharpCompute.Samples.Shaders;

/// <summary>
/// A small signed binary fixed-point number on <see cref="BigInteger"/>, with
/// <see cref="FractionalBits"/> bits after the point. It is the arbitrary-precision arithmetic behind
/// the deep-zoom Mandelbrot's reference orbit: <c>double</c> runs out at a view size of about 1e-14,
/// while 192 fractional bits resolve about 1e-57, far below the deepest view (about 1e-31).
/// </summary>
/// <remarks>
/// Values are exact multiples of 2^-<see cref="FractionalBits"/>. Addition, subtraction and negation
/// are exact; multiplication rounds to the nearest representable value (ties away from negative
/// infinity). The integer part is unbounded, so there is no overflow, only growing cost.
/// </remarks>
public readonly struct FixedPoint : IEquatable<FixedPoint>, IComparable<FixedPoint>
{
    /// <summary>Bits after the binary point.</summary>
    public const int FractionalBits = 192;

    private static readonly BigInteger s_half = BigInteger.One << (FractionalBits - 1);

    private readonly BigInteger _raw;

    private FixedPoint(BigInteger raw) => _raw = raw;

    /// <summary>0.</summary>
    public static FixedPoint Zero => default;

    /// <summary>1.</summary>
    public static FixedPoint One { get; } = new(BigInteger.One << FractionalBits);

    /// <summary>The smallest positive value, 2^-<see cref="FractionalBits"/>.</summary>
    public static FixedPoint Epsilon { get; } = new(BigInteger.One);

    /// <summary>The raw value: this number times 2^<see cref="FractionalBits"/>.</summary>
    public BigInteger Raw => _raw;

    /// <summary>Wraps a raw value (this number times 2^<see cref="FractionalBits"/>).</summary>
    public static FixedPoint FromRaw(BigInteger raw) => new(raw);

    /// <summary>The integer <paramref name="value"/>, exactly.</summary>
    public static FixedPoint FromInt(long value) => new(new BigInteger(value) << FractionalBits);

    /// <summary>The <c>double</c> <paramref name="value"/>, exact down to 2^-<see cref="FractionalBits"/>.</summary>
    public static FixedPoint FromDouble(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Only finite values can be converted.");
        }

        // A double is m * 2^e with a 53-bit m, so scaling by a power of two and truncating to an
        // integer is exact unless bits fall below 2^-FractionalBits.
        long bits = BitConverter.DoubleToInt64Bits(value);
        int exponent = (int)((bits >> 52) & 0x7FF);
        long mantissa = bits & 0xF_FFFF_FFFF_FFFFL;

        if (exponent == 0)
        {
            exponent = 1; // subnormal
        }
        else
        {
            mantissa |= 1L << 52;
        }

        // value = mantissa * 2^(exponent - 1075)
        int shift = exponent - 1075 + FractionalBits;
        BigInteger raw = shift >= 0 ? new BigInteger(mantissa) << shift : RoundShiftRight(new BigInteger(mantissa), -shift);
        return new(value < 0 ? -raw : raw);
    }

    /// <summary>
    /// Parses a decimal number with invariant formatting: an optional sign, digits with an optional
    /// <c>.</c>, and an optional exponent (<c>e-5</c>). Rounds to the nearest representable value.
    /// </summary>
    public static FixedPoint Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ReadOnlySpan<char> s = text.AsSpan().Trim();
        bool negative = false;

        if (s.Length > 0 && (s[0] == '-' || s[0] == '+'))
        {
            negative = s[0] == '-';
            s = s[1..];
        }

        int exponent = 0;
        int e = s.IndexOfAny('e', 'E');

        if (e >= 0)
        {
            if (!int.TryParse(s[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
            {
                throw new FormatException($"Invalid exponent in '{text}'.");
            }

            s = s[..e];
        }

        BigInteger mantissa = BigInteger.Zero;
        bool point = false, digits = false;

        foreach (char c in s)
        {
            if (c == '.' && !point)
            {
                point = true;
            }
            else if (c is >= '0' and <= '9')
            {
                mantissa = (mantissa * 10) + (c - '0');
                digits = true;

                if (point)
                {
                    exponent--;
                }
            }
            else
            {
                throw new FormatException($"'{text}' is not a decimal number.");
            }
        }

        if (!digits)
        {
            throw new FormatException($"'{text}' is not a decimal number.");
        }

        // value = mantissa * 10^exponent
        BigInteger raw;

        if (exponent >= 0)
        {
            raw = (mantissa * BigInteger.Pow(10, exponent)) << FractionalBits;
        }
        else
        {
            BigInteger divisor = BigInteger.Pow(10, -exponent);
            raw = ((mantissa << FractionalBits) + (divisor >> 1)) / divisor;
        }

        return new(negative ? -raw : raw);
    }

    /// <summary>The nearest <c>double</c> (truncated to 53 significant bits).</summary>
    public double ToDouble() => double.ScaleB((double)_raw, -FractionalBits);

    /// <summary>The nearest <c>float</c>, via <see cref="ToDouble"/>.</summary>
    public float ToSingle() => (float)ToDouble();

    /// <summary>Formats with exactly <paramref name="decimals"/> digits after the point, rounded to nearest.</summary>
    public string ToString(int decimals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);

        BigInteger magnitude = BigInteger.Abs(_raw);
        BigInteger scaled = RoundShiftRight(magnitude * BigInteger.Pow(10, decimals), FractionalBits);
        string digits = scaled.ToString(CultureInfo.InvariantCulture).PadLeft(decimals + 1, '0');
        string sign = _raw.Sign < 0 && !scaled.IsZero ? "-" : string.Empty;

        return decimals == 0 ? sign + digits : $"{sign}{digits[..^decimals]}.{digits[^decimals..]}";
    }

    /// <inheritdoc/>
    public override string ToString() => ToString(40);

    /// <summary>This value squared (one multiplication, rounded once).</summary>
    public FixedPoint Square() => this * this;

    /// <summary>This value times 2^<paramref name="power"/> (exact for <paramref name="power"/> ≥ 0).</summary>
    public FixedPoint ScaleB(int power) => power >= 0 ? new(_raw << power) : new(RoundShiftRight(_raw, -power));

    public static FixedPoint operator +(FixedPoint left, FixedPoint right) => new(left._raw + right._raw);

    public static FixedPoint operator -(FixedPoint left, FixedPoint right) => new(left._raw - right._raw);

    public static FixedPoint operator -(FixedPoint value) => new(-value._raw);

    public static FixedPoint operator *(FixedPoint left, FixedPoint right) => new(RoundShiftRight(left._raw * right._raw, FractionalBits));

    public static bool operator ==(FixedPoint left, FixedPoint right) => left._raw == right._raw;

    public static bool operator !=(FixedPoint left, FixedPoint right) => left._raw != right._raw;

    public static bool operator <(FixedPoint left, FixedPoint right) => left._raw < right._raw;

    public static bool operator >(FixedPoint left, FixedPoint right) => left._raw > right._raw;

    public static bool operator <=(FixedPoint left, FixedPoint right) => left._raw <= right._raw;

    public static bool operator >=(FixedPoint left, FixedPoint right) => left._raw >= right._raw;

    /// <inheritdoc/>
    public bool Equals(FixedPoint other) => _raw == other._raw;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is FixedPoint other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _raw.GetHashCode();

    /// <inheritdoc/>
    public int CompareTo(FixedPoint other) => _raw.CompareTo(other._raw);

    /// <summary><paramref name="value"/> / 2^<paramref name="shift"/>, rounded to nearest (ties toward +∞).</summary>
    private static BigInteger RoundShiftRight(BigInteger value, int shift) =>
        shift == FractionalBits ? (value + s_half) >> shift : (value + (BigInteger.One << (shift - 1))) >> shift;
}
