// Original SharpCompute sample (MIT), not a Shadertoy port.
using System;

namespace SharpCompute.Samples.Shaders;

/// <summary>
/// The reference orbit <c>Z_0 = 0, Z_{n+1} = Z_n² + C</c> of one point <c>C</c>, iterated in
/// <see cref="FixedPoint"/> and stored as <see cref="Float2"/>. Floats are enough for the stored
/// values because <c>|Z_n| ≤ 2</c>; it's the iteration itself that needs the high precision, since
/// rounding errors grow along the orbit.
/// </summary>
/// <remarks>
/// The orbit grows on demand (<see cref="EnsureLength"/>) and stops for good once <c>|Z|</c>
/// exceeds 2: the escaped point is not stored, so every stored value has <c>|Z| ≤ 2</c>. Not thread-safe.
/// </remarks>
public sealed class MandelbrotReferenceOrbit
{
    private static readonly FixedPoint s_four = FixedPoint.FromInt(4);

    private readonly FixedPoint _cx;
    private readonly FixedPoint _cy;
    private FixedPoint _zx;
    private FixedPoint _zy;
    private Float2[] _values = new Float2[1024];
    private int _length = 1; // Z_0 = 0

    public MandelbrotReferenceOrbit(FixedPoint cx, FixedPoint cy)
    {
        _cx = cx;
        _cy = cy;
    }

    /// <summary>Parses the center from decimal strings, e.g. <c>"-0.7436…", "0.1318…"</c>.</summary>
    public MandelbrotReferenceOrbit(string cx, string cy)
        : this(FixedPoint.Parse(cx), FixedPoint.Parse(cy))
    {
    }

    /// <summary>The real part of <c>C</c>.</summary>
    public FixedPoint CenterX => _cx;

    /// <summary>The imaginary part of <c>C</c>.</summary>
    public FixedPoint CenterY => _cy;

    /// <summary>Number of stored values <c>Z_0 … Z_{Length-1}</c>.</summary>
    public int Length => _length;

    /// <summary>True once the orbit has left the radius-2 disk; it can't grow after that.</summary>
    public bool Escaped { get; private set; }

    /// <summary>The stored values.</summary>
    public ReadOnlySpan<Float2> Values => _values.AsSpan(0, _length);

    /// <summary>
    /// Iterates until <see cref="Length"/> ≥ <paramref name="length"/> or the orbit escapes.
    /// Returns true if new values were added.
    /// </summary>
    public bool EnsureLength(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        int before = _length;

        if (length > _values.Length)
        {
            Array.Resize(ref _values, Math.Max(length, _values.Length * 2));
        }

        while (_length < length && !Escaped)
        {
            // (x + iy)² + c = (x² - y²) + 2xy i + c
            FixedPoint xx = _zx.Square();
            FixedPoint yy = _zy.Square();
            FixedPoint nextY = (_zx * _zy).ScaleB(1) + _cy;
            FixedPoint nextX = xx - yy + _cx;

            if (nextX.Square() + nextY.Square() > s_four)
            {
                Escaped = true;
                break;
            }

            _zx = nextX;
            _zy = nextY;
            _values[_length++] = new Float2(nextX.ToSingle(), nextY.ToSingle());
        }

        return _length != before;
    }
}
