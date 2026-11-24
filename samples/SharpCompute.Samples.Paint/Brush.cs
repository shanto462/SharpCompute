// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SharpCompute.Samples.Paint;

/// <summary>The brush: diameter, hardness, opacity, color, and whether it erases.</summary>
public sealed class BrushSettings
{
    public const float MinSize = 2f;
    public const float MaxSize = 512f;
    public const float SizeStep = 1.25f;
    public const float MinOpacity = 0.1f;

    /// <summary>The 1–9 palette (straight sRGB).</summary>
    public static IReadOnlyList<Float3> Palette { get; } =
    [
        new(0.07f, 0.07f, 0.08f), // 1 ink
        new(1f, 1f, 1f),          // 2 white
        new(0.93f, 0.23f, 0.19f), // 3 red
        new(1f, 0.58f, 0.1f),     // 4 orange
        new(1f, 0.84f, 0.16f),    // 5 yellow
        new(0.2f, 0.74f, 0.35f),  // 6 green
        new(0.16f, 0.72f, 0.92f), // 7 sky
        new(0.2f, 0.33f, 0.85f),  // 8 blue
        new(0.67f, 0.3f, 0.86f),  // 9 violet
    ];

    /// <summary>Diameter in canvas pixels.</summary>
    public float Size { get; set => field = Math.Clamp(value, MinSize, MaxSize); } = 36f;

    public float Radius => Size / 2f;

    /// <summary>0 = soft all the way from the center, 1 = hard edge (one anti-aliased pixel).</summary>
    public float Hardness { get; set => field = Math.Clamp(MathF.Round(value, 2), 0f, 1f); } = 0.6f;

    public float Opacity { get; set => field = Math.Clamp(MathF.Round(value, 2), MinOpacity, 1f); } = 1f;

    /// <summary>Straight sRGB color.</summary>
    public Float3 Color { get; set; } = Palette[0];

    public bool Erase { get; set; }

    /// <summary>Stamp spacing along a stroke: a fifth of the radius, at least half a pixel.</summary>
    public float Spacing => MathF.Max(0.5f, Radius * 0.2f);

    public string ColorHex => string.Create(CultureInfo.InvariantCulture, $"#{ToByte(Color.X):X2}{ToByte(Color.Y):X2}{ToByte(Color.Z):X2}");

    public static bool TryParseColor(string text, out Float3 color)
    {
        color = default;
        ReadOnlySpan<char> hex = text.AsSpan().TrimStart('#');

        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            return false;
        }

        color = new Float3(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        return true;
    }

    private static int ToByte(float value) => (int)((Math.Clamp(value, 0f, 1f) * 255f) + 0.5f);
}

/// <summary>
/// Turns pointer positions into evenly spaced brush stamps, so a fast drag (few, far-apart mouse
/// events) still paints a continuous stroke. The distance since the last stamp carries over from
/// one segment to the next, so the spacing stays even across event boundaries.
/// </summary>
public sealed class StrokePath
{
    private Float2 _last;
    private float _sinceLastStamp;
    private bool _active;

    /// <summary>Starts a stroke: one stamp at <paramref name="point"/>.</summary>
    public void Begin(Float2 point, List<Float2> stamps)
    {
        ArgumentNullException.ThrowIfNull(stamps);
        _last = point;
        _sinceLastStamp = 0f;
        _active = true;
        stamps.Add(point);
    }

    /// <summary>Extends the stroke to <paramref name="point"/>, adding a stamp every <paramref name="spacing"/> pixels along the way.</summary>
    public void MoveTo(Float2 point, float spacing, List<Float2> stamps)
    {
        ArgumentNullException.ThrowIfNull(stamps);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacing);

        if (!_active)
        {
            Begin(point, stamps);
            return;
        }

        float dx = point.X - _last.X;
        float dy = point.Y - _last.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));

        if (length > 0f)
        {
            float along = spacing - _sinceLastStamp;

            for (; along <= length; along += spacing)
            {
                float t = along / length;
                stamps.Add(new Float2(_last.X + (dx * t), _last.Y + (dy * t)));
            }

            _sinceLastStamp = length - (along - spacing);
        }

        _last = point;
    }

    public void End() => _active = false;
}
