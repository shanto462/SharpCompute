using System;
using System.Globalization;

namespace SharpCompute.Samples.Gallery;

internal enum Mode
{
    Help,
    List,
    Render,
    Frames,
    Bench,
}

/// <summary>Parsed command line. Plain hand-rolled parsing; see <see cref="Usage"/>.</summary>
internal sealed class Options
{
    public const string Usage = """
        SharpCompute shader gallery: renders the ported Shadertoy shaders to PNG with Metal.

        Usage:
          gallery --list
          gallery --shader <name> [--time <t>] [--size <W>x<H>] [--out <file.png>]
          gallery --all [--time <t>] [--size <W>x<H>] [--outdir <dir>]
          gallery (--shader <name> | --all) --frames <N> [--fps <F>] [--time <t>] [--size <W>x<H>] [--outdir <dir>]
          gallery --bench [--shader <name>] [--frames <N>] [--size <W>x<H>]
          gallery --help

        Options:
          --list            List the registered shaders.
          --shader <name>   Shader to render (case-insensitive).
          --all             Every registered shader.
          --time <t>        Shader time in seconds (start time for --frames). Default: 0.
          --size <W>x<H>    Output size in pixels, each 1..16384. Default: 1280x720.
          --out <file>      Output PNG for a single shader. Default: <name>.png.
          --outdir <dir>    Output directory for --all and --frames. Default: gallery-output.
          --frames <N>      Render N frames: a PNG sequence in <outdir>/<name>/, plus <outdir>/<name>.mp4
                            when ffmpeg is on PATH. With --bench, the number of timed frames (default 100).
          --fps <F>         Frame rate for --frames (time step 1/F, and the mp4 rate). Default: 30.
          --bench           Time ForEach per frame (wall clock, warm-up excluded): median and p95 ms.
          --help            Show this help.
        """;

    public Mode Mode { get; private set; } = Mode.Help;

    public string? Shader { get; private set; }

    public bool All { get; private set; }

    public float Time { get; private set; }

    public int Width { get; private set; } = 1280;

    public int Height { get; private set; } = 720;

    public string? Out { get; private set; }

    public string OutDir { get; private set; } = "gallery-output";

    public int? Frames { get; private set; }

    public double Fps { get; private set; } = 30;

    /// <summary>Parses <paramref name="args"/>; throws <see cref="FormatException"/> with a user-facing message on bad input.</summary>
    public static Options Parse(string[] args)
    {
        Options options = new();
        bool list = false, bench = false, help = args.Length == 0;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            switch (arg)
            {
                case "--help" or "-h" or "-?":
                    help = true;
                    break;
                case "--list":
                    list = true;
                    break;
                case "--all":
                    options.All = true;
                    break;
                case "--bench":
                    bench = true;
                    break;
                case "--shader":
                    options.Shader = Value(args, ref i);
                    break;
                case "--time":
                    options.Time = ParseFloat(arg, Value(args, ref i));
                    break;
                case "--size":
                    (options.Width, options.Height) = ParseSize(Value(args, ref i));
                    break;
                case "--out":
                    options.Out = Value(args, ref i);
                    break;
                case "--outdir":
                    options.OutDir = Value(args, ref i);
                    break;
                case "--frames":
                    options.Frames = ParsePositiveInt(arg, Value(args, ref i));
                    break;
                case "--fps":
                    options.Fps = ParseFloat(arg, Value(args, ref i));

                    if (!(options.Fps > 0) || !double.IsFinite(options.Fps))
                    {
                        throw new FormatException("--fps must be a positive number.");
                    }

                    break;
                default:
                    throw new FormatException($"Unknown argument '{arg}'.");
            }
        }

        if (help)
        {
            options.Mode = Mode.Help;
        }
        else if (list)
        {
            options.Mode = Mode.List;
        }
        else if (bench)
        {
            options.Mode = Mode.Bench;
        }
        else if (options.Shader is null && !options.All)
        {
            throw new FormatException("Specify --shader <name>, --all, --list, --bench or --help.");
        }
        else if (options.Shader is not null && options.All)
        {
            throw new FormatException("--shader and --all are mutually exclusive.");
        }
        else
        {
            options.Mode = options.Frames is null ? Mode.Render : Mode.Frames;
        }

        if (options.Out is not null && (options.Mode != Mode.Render || options.All))
        {
            throw new FormatException("--out applies only to a single --shader render; use --outdir otherwise.");
        }

        return options;
    }

    private static string Value(string[] args, ref int i)
    {
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new FormatException($"{args[i]} needs a value.");
        }

        return args[++i];
    }

    private static float ParseFloat(string name, string value) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) && float.IsFinite(result)
            ? result
            : throw new FormatException($"{name} expects a number, got '{value}'.");

    private static int ParsePositiveInt(string name, string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result) && result > 0
            ? result
            : throw new FormatException($"{name} expects a positive integer, got '{value}'.");

    private static (int Width, int Height) ParseSize(string value)
    {
        int x = value.IndexOfAny(['x', 'X']);

        if (x > 0
            && int.TryParse(value.AsSpan(0, x), NumberStyles.None, CultureInfo.InvariantCulture, out int width)
            && int.TryParse(value.AsSpan(x + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int height)
            && width is > 0 and <= 16384
            && height is > 0 and <= 16384)
        {
            return (width, height);
        }

        throw new FormatException($"--size expects <W>x<H> with each side in 1..16384, got '{value}'.");
    }
}
