// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SharpCompute.Samples.Imaging;

/// <summary>The image filters shared by the ImageProcessing CLI and Paint.</summary>
public enum FilterKind
{
    Blur,
    Bokeh,
    Sharpen,
    Edges,
    Grade,
    Pixelate,
    Invert,
    Posterize,
    Grayscale,
}

/// <summary>One numeric argument of a filter, with its default and allowed range.</summary>
public sealed record FilterParameter(string Name, float Default, float Min, float Max, bool IsInteger = false);

/// <summary>A filter's command-line name, description and arguments.</summary>
public sealed record FilterInfo(FilterKind Kind, string Name, string Description, IReadOnlyList<FilterParameter> Parameters)
{
    public string Usage => Parameters.Count == 0 ? Name : $"{Name} {string.Join(' ', Parameters.Select(static p => $"<{p.Name}>"))}";
}

/// <summary>A filter with all of its arguments resolved and validated.</summary>
public sealed class FilterSpec
{
    /// <summary>Radius of the Gaussian blur inside the unsharp mask (σ = radius / 3).</summary>
    public const int SharpenBlurRadius = 3;

    private static readonly FilterInfo[] s_all =
    [
        new(FilterKind.Blur, "blur", "separable Gaussian blur, σ = radius / 3", [new("radius", 8, 1, 128, IsInteger: true)]),
        new(FilterKind.Bokeh, "bokeh", "lens blur: brute-force disc average with highlight bloom", [new("radius", 12, 1, 64), new("highlight", 6, 0, 100)]),
        new(FilterKind.Sharpen, "sharpen", "unsharp mask (Gaussian radius 3)", [new("amount", 1, 0, 20)]),
        new(FilterKind.Edges, "edges", "Sobel edge magnitude of the luma", []),
        new(FilterKind.Grade, "grade", "exposure (stops), contrast and saturation", [new("exposure", 0, -8, 8), new("contrast", 1, 0, 4), new("saturation", 1, 0, 4)]),
        new(FilterKind.Pixelate, "pixelate", "block average", [new("size", 16, 1, 1024, IsInteger: true)]),
        new(FilterKind.Invert, "invert", "invert the color", []),
        new(FilterKind.Posterize, "posterize", "quantize each channel to N levels", [new("levels", 4, 2, 256, IsInteger: true)]),
        new(FilterKind.Grayscale, "grayscale", "Rec. 709 luma", []),
    ];

    private readonly float[] _arguments;

    private FilterSpec(FilterInfo info, float[] arguments)
    {
        Info = info;
        _arguments = arguments;
    }

    public static IReadOnlyList<FilterInfo> All => s_all;

    public FilterInfo Info { get; }

    public FilterKind Kind => Info.Kind;

    public IReadOnlyList<float> Arguments => _arguments;

    public float this[int index] => _arguments[index];

    public static FilterInfo Describe(FilterKind kind) => s_all.First(info => info.Kind == kind);

    /// <summary>Creates a filter; missing trailing arguments take their defaults.</summary>
    /// <exception cref="ArgumentException">Too many arguments, or one is out of range.</exception>
    public static FilterSpec Create(FilterKind kind, params ReadOnlySpan<float> arguments)
    {
        FilterInfo info = Describe(kind);

        if (arguments.Length > info.Parameters.Count)
        {
            throw new ArgumentException($"{info.Name} takes at most {info.Parameters.Count} argument(s): {info.Usage}", nameof(arguments));
        }

        float[] values = new float[info.Parameters.Count];

        for (int i = 0; i < values.Length; i++)
        {
            FilterParameter parameter = info.Parameters[i];
            float value = i < arguments.Length ? arguments[i] : parameter.Default;

            if (!float.IsFinite(value) || value < parameter.Min || value > parameter.Max || (parameter.IsInteger && value != MathF.Round(value)))
            {
                string kindText = parameter.IsInteger ? "an integer" : "a number";
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"{info.Name}: {parameter.Name} must be {kindText} in {parameter.Min}..{parameter.Max}, got {value}."),
                    nameof(arguments));
            }

            values[i] = value;
        }

        return new FilterSpec(info, values);
    }

    /// <summary>Parses a filter name and its arguments as typed on the command line.</summary>
    public static bool TryParse(string name, IReadOnlyList<string> arguments, out FilterSpec? spec, out string error)
    {
        spec = null;
        FilterInfo? info = s_all.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));

        if (info is null)
        {
            error = $"Unknown filter '{name}'. Use --list to see the filters.";
            return false;
        }

        float[] values = new float[arguments.Count];

        for (int i = 0; i < values.Length; i++)
        {
            if (!float.TryParse(arguments[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                error = $"{info.Name}: '{arguments[i]}' is not a number. Usage: {info.Usage}";
                return false;
            }
        }

        try
        {
            spec = Create(info.Kind, values);
            error = "";
            return true;
        }
        catch (ArgumentException e)
        {
            error = e.Message.Split(" (Parameter", 2)[0];
            return false;
        }
    }

    /// <summary>
    /// Normalized Gaussian taps for <paramref name="radius"/> (σ = radius / 3, so the window spans
    /// ±3σ), computed in double and rounded once. The GPU and CPU paths share these exact values.
    /// </summary>
    public static float[] GaussianWeights(int radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);

        double sigma = radius / 3.0;
        double[] taps = new double[(2 * radius) + 1];

        for (int k = -radius; k <= radius; k++)
        {
            taps[k + radius] = Math.Exp(-(k * k) / (2 * sigma * sigma));
        }

        double total = taps.Sum();
        return [.. taps.Select(t => (float)(t / total))];
    }

    public override string ToString() =>
        _arguments.Length == 0 ? Info.Name : $"{Info.Name} {string.Join(' ', _arguments.Select(static a => a.ToString(CultureInfo.InvariantCulture)))}";
}
