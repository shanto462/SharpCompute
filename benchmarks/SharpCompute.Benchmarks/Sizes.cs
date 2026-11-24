namespace SharpCompute.Benchmarks;

/// <summary>The problem sizes, shared by the benchmarks and --verify.</summary>
public static class Sizes
{
    public static readonly int[] Saxpy = [1 << 12, 1 << 16, 1 << 20, 1 << 24];

    public static readonly string[] Mandelbrot = ["256x256", "1024x1024", "1920x1080"];

    public const int BlurWidth = 3840;
    public const int BlurHeight = 2160;
    public const int BlurRadius = 8;

    public const int MatMul = 1024;

    public static readonly int[] NBody = [4096, 16384];

    /// <summary>Floats in the file the file-to-GPU workload sums: 64M floats = 256 MB.</summary>
    public const int FileFloats = 1 << 26;

    public static (int Width, int Height) Parse(string size)
    {
        string[] parts = size.Split('x');
        return (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
    }
}
