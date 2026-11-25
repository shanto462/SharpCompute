// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using SharpCompute.Presentation;
using SharpCompute.Samples.ComputeLab;
using SharpCompute.Samples.ComputeLab.Pages;
using static System.FormattableString;

namespace SharpCompute.Samples.GroupShared.Pages;

/// <summary>
/// A separable blur of a 1024 × 1024 image, two ways. Naive: every thread reads all its taps from the
/// image buffer. Tiled: a threadgroup is 128 pixels of one row (then of one column); together its
/// threads copy those pixels and an apron of <c>radius</c> pixels on each side into group-shared
/// memory, one or two values per thread, and after a barrier every thread takes its taps from there.
/// </summary>
/// <remarks>
/// The weights are a tent (<c>radius + 1 - |k|</c>), which needs no table. Both variants add the same
/// taps in the same order, so they produce the same image.
/// </remarks>
public sealed class BlurPage : ILabPage
{
    public const int Size = 1024;
    public const int Count = Size * Size;

    /// <summary>The largest radius: the apron must fit the tile, and a thread loads at most one apron value.</summary>
    public const int MaxRadius = BlurRowsShared.MaxRadius;

    private const int OtherVariantInterval = 90;

    private static readonly string[] s_variants = ["tile + apron in shared memory", "naive: every tap from the image buffer"];

    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<float> _image;
    private readonly ReadWriteBuffer<float> _rows;
    private readonly ReadWriteBuffer<float> _blurred;
    private readonly double[] _variantMilliseconds = new double[2];
    private readonly float[] _cpuImage = new float[Count];
    private readonly float[] _cpuRows = new float[Count];
    private readonly float[] _cpuBlurred = new float[Count];
    private int _computes;

    public BlurPage(GraphicsDevice device)
    {
        _device = device;
        _image = device.AllocateReadWriteBuffer<float>(Count);
        _rows = device.AllocateReadWriteBuffer<float>(Count);
        _blurred = device.AllocateReadWriteBuffer<float>(Count);
    }

    public string Name => "Blur";

    public string Feature => "shared memory and barriers";

    public string Help => "V variant   Up/Down radius";

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    /// <summary>The blur reaches this many pixels to each side: <c>2 * Radius + 1</c> taps per pass.</summary>
    public int Radius { get; private set; } = 16;

    /// <summary>The largest difference between the GPU's and the C# pixels at the last <see cref="Check"/>.</summary>
    public float MaxError { get; private set; }

    public void Compute(LabFrame frame)
    {
        _device.For(Size, Size, new GenerateImage(_image, Size, frame.Time));

        if (!frame.Deterministic && _computes++ % OtherVariantInterval == 0)
        {
            _variantMilliseconds[1 - Variant] = Blur(1 - Variant);
        }

        GpuMilliseconds = Blur(Variant);
        _variantMilliseconds[Variant] = GpuMilliseconds;
    }

    /// <summary>Rows into the intermediate image, then columns into the result.</summary>
    private double Blur(int variant)
    {
        long start = Stopwatch.GetTimestamp();

        if (variant == 0)
        {
            int padded = (Size + BlurRowsShared.Group - 1) / BlurRowsShared.Group * BlurRowsShared.Group;
            _device.For(padded, Size, new BlurRowsShared(_image, _rows, Size, Size, Radius));
            _device.For(Size, padded, new BlurColumnsShared(_rows, _blurred, Size, Size, Radius));
        }
        else
        {
            _device.For(Size, Size, new BlurNaive(_image, _rows, Size, Size, Radius, 1, 0));
            _device.For(Size, Size, new BlurNaive(_rows, _blurred, Size, Size, Radius, 0, 1));
        }

        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    public LabCheck Check()
    {
        _image.CopyTo(_cpuImage);

        long start = Stopwatch.GetTimestamp();
        CpuBlur.Pass(_cpuImage, _cpuRows, Size, Size, Radius, horizontal: true);
        CpuBlur.Pass(_cpuRows, _cpuBlurred, Size, Size, Radius, horizontal: false);
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        ReadOnlySpan<float> gpu = _blurred.AsReadOnlySpan();
        float worst = 0;

        for (int i = 0; i < gpu.Length; i++)
        {
            worst = MathF.Max(worst, MathF.Abs(gpu[i] - _cpuBlurred[i]));
        }

        MaxError = worst;

        // The same taps in the same order; the GPU may fuse a multiply and an add, the CPU does not.
        bool passed = worst <= 1e-4f;
        string summary = passed
            ? Invariant($"all {Count:N0} pixels = the C# blur (radius {Radius}), max difference {worst:E1} (CPU: all cores)")
            : Invariant($"the blurred image differs from the C# blur by up to {worst:E1} (radius {Radius})");

        return new LabCheck(passed, summary, cpuMs);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        _device.For(LabCanvas.Width, LabCanvas.Height, new DrawBlur(canvas.Pixels, _image, _blurred, LabCanvas.Width, Size));

        int labelY = DrawBlur.PanelY - 24;
        int below = DrawBlur.PanelY + DrawBlur.PanelSize + 8;
        canvas.Label(DrawBlur.PanelX(0), labelY, "Input: 1024 x 1024");
        canvas.Label(DrawBlur.PanelX(1), labelY, Invariant($"Blurred: {(2 * Radius) + 1} taps along x, then y"));
        canvas.Label(DrawBlur.PanelX(0), below, Invariant($"radius {Radius}: 128 pixels + 2 x {Radius} apron per group"), TextColor.Accent);

        int textX = DrawBlur.PanelX(1) + DrawBlur.PanelSize + 24;
        int y = DrawBlur.PanelY;
        canvas.Label(textX, y, "A group is 128 pixels of a row.", TextColor.White);
        canvas.Label(textX, y + 16, "Each thread copies its pixel,", TextColor.White);
        canvas.Label(textX, y + 32, "the first and last few also", TextColor.White);
        canvas.Label(textX, y + 48, "one of the apron:", TextColor.White);
        canvas.Label(textX, y + 80, "tile[r + j] = image[x];", TextColor.Cool);
        canvas.Label(textX, y + 96, "if (j < r)", TextColor.Cool);
        canvas.Label(textX, y + 112, "  tile[j] = image[x - r];", TextColor.Cool);
        canvas.Label(textX, y + 128, "if (j >= 128 - r)", TextColor.Cool);
        canvas.Label(textX, y + 144, "  tile[j + 2r] = image[x + r];", TextColor.Cool);
        canvas.Label(textX, y + 160, "barrier();", TextColor.Cool);
        canvas.Label(textX, y + 176, "for (k = -r; k <= r; k++)", TextColor.Cool);
        canvas.Label(textX, y + 192, "  sum += w(k) * tile[r + j + k];", TextColor.Cool);
        canvas.Label(textX, y + 240, "Then the same down the columns.", TextColor.White);

        if (!frame.Deterministic && _variantMilliseconds[0] > 0 && _variantMilliseconds[1] > 0)
        {
            canvas.Label(textX, y + 288, Invariant($"shared tile  {_variantMilliseconds[0],7:F2} ms"), Variant == 0 ? TextColor.White : TextColor.Dim);
            canvas.Label(textX, y + 304, Invariant($"naive        {_variantMilliseconds[1],7:F2} ms"), Variant == 1 ? TextColor.White : TextColor.Dim);
        }
    }

    public bool OnKeyDown(Key key)
    {
        switch (key)
        {
            case Key.V:
                Variant = 1 - Variant;
                return true;

            case Key.Up or Key.Down:
                Radius = Math.Clamp(key == Key.Up ? Radius * 2 : Radius / 2, 1, MaxRadius);
                return true;

            default:
                return false;
        }
    }

    public void Dispose()
    {
        _image.Dispose();
        _rows.Dispose();
        _blurred.Dispose();
    }
}

/// <summary>The blur in plain C#: one pass along rows or columns, with the kernels' weights, edge rule and order of additions.</summary>
public static class CpuBlur
{
    public static void Pass(float[] source, float[] destination, int width, int height, int radius, bool horizontal)
    {
        float weight = (radius + 1) * (radius + 1);

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0f;

                for (int k = -radius; k <= radius; k++)
                {
                    int sx = horizontal ? Math.Clamp(x + k, 0, width - 1) : x;
                    int sy = horizontal ? y : Math.Clamp(y + k, 0, height - 1);

                    sum += (radius + 1 - Math.Abs(k)) * source[(sy * width) + sx];
                }

                destination[(y * width) + x] = sum / weight;
            }
        });
    }
}

/// <summary>The input: hard-edged shapes that move, on a checkerboard, with grain; values in 0..1.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct GenerateImage(ReadWriteBuffer<float> image, int size, float time) : IComputeShader
{
    private static float Grain(uint x, uint y)
    {
        uint h = x + (y * 0x9E3779B9u);
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return (h >> 8) * (1f / 16777216f);
    }

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float2 uv = new Float2(x, y) / size;

        float value = (((x >> 6) + (y >> 6)) & 1) == 0 ? 0.16f : 0.3f;

        // Three discs on slow orbits, a ring, and a few thin bars: edges for the blur to soften.
        Float2 a = new Float2(0.5f + (0.28f * Gpu.Cos(time * 0.5f)), 0.5f + (0.28f * Gpu.Sin(time * 0.5f)));
        Float2 b = new Float2(0.5f + (0.33f * Gpu.Cos((time * 0.31f) + 2.1f)), 0.5f + (0.2f * Gpu.Sin((time * 0.43f) + 1f)));
        Float2 c = new Float2(0.3f + (0.15f * Gpu.Sin(time * 0.27f)), 0.72f + (0.12f * Gpu.Cos(time * 0.37f)));

        if (Gpu.Length(uv - a) < 0.11f || Gpu.Length(uv - b) < 0.07f || Gpu.Length(uv - c) < 0.05f)
        {
            value = 1f;
        }

        float ring = Gpu.Abs(Gpu.Length(uv - new Float2(0.5f, 0.5f)) - 0.42f);

        if (ring < 0.006f || (x & 127) < 3 && uv.Y > 0.86f)
        {
            value = 0.9f;
        }

        image[(y * size) + x] = Gpu.Saturate(value + ((Grain((uint)x, (uint)y) - 0.5f) * 0.08f));
    }
}

/// <summary>
/// The feature kernel, along rows: a threadgroup is <see cref="Group"/> consecutive pixels of one row.
/// Its threads fill a tile of <c>Group + 2 * radius</c> values in group-shared memory (each its own
/// pixel; the first and the last <c>radius</c> threads also one apron pixel beyond the group's ends,
/// clamped to the image), and after the barrier every thread sums its taps from the tile.
/// </summary>
/// <remarks>
/// Dispatched over a multiple of <see cref="Group"/> threads along x; threads past the image's width
/// load the clamped edge pixel and write nothing. <paramref name="radius"/> may be at most
/// <see cref="MaxRadius"/>, so that the two aprons are loaded by different threads.
/// </remarks>
[ThreadGroupSize(BlurRowsShared.Group, 1)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct BlurRowsShared(ReadWriteBuffer<float> source, ReadWriteBuffer<float> destination, int width, int height, int radius) : IComputeShader
{
    public const int Group = 128;
    public const int MaxRadius = Group / 2;

    [GroupShared]
    private static readonly float[] tile = new float[Group + (2 * MaxRadius)];

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        int local = GroupIds.X;
        int row = y * width;

        tile[radius + local] = source[row + Gpu.Min(x, width - 1)];

        if (local < radius)
        {
            tile[local] = source[row + Gpu.Clamp(x - radius, 0, width - 1)];
        }

        if (local >= Group - radius)
        {
            tile[local + (2 * radius)] = source[row + Gpu.Clamp(x + radius, 0, width - 1)];
        }

        Gpu.GroupMemoryBarrierWithGroupSync();

        float sum = 0f;

        for (int k = -radius; k <= radius; k++)
        {
            sum += (radius + 1 - Gpu.Abs(k)) * tile[radius + local + k];
        }

        if (x < width)
        {
            destination[row + x] = sum / ((radius + 1) * (radius + 1));
        }
    }
}

/// <summary>The same along columns: a threadgroup is <see cref="BlurRowsShared.Group"/> consecutive pixels of one column.</summary>
[ThreadGroupSize(1, BlurRowsShared.Group)]
[GeneratedComputeShaderDescriptor]
public readonly partial struct BlurColumnsShared(ReadWriteBuffer<float> source, ReadWriteBuffer<float> destination, int width, int height, int radius) : IComputeShader
{
    private const int Group = BlurRowsShared.Group;

    [GroupShared]
    private static readonly float[] tile = new float[Group + (2 * BlurRowsShared.MaxRadius)];

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        int local = GroupIds.Y;

        tile[radius + local] = source[(Gpu.Min(y, height - 1) * width) + x];

        if (local < radius)
        {
            tile[local] = source[(Gpu.Clamp(y - radius, 0, height - 1) * width) + x];
        }

        if (local >= Group - radius)
        {
            tile[local + (2 * radius)] = source[(Gpu.Clamp(y + radius, 0, height - 1) * width) + x];
        }

        Gpu.GroupMemoryBarrierWithGroupSync();

        float sum = 0f;

        for (int k = -radius; k <= radius; k++)
        {
            sum += (radius + 1 - Gpu.Abs(k)) * tile[radius + local + k];
        }

        if (y < height)
        {
            destination[(y * width) + x] = sum / ((radius + 1) * (radius + 1));
        }
    }
}

/// <summary>One pass without shared memory: every thread reads its taps from the buffer, along (<paramref name="stepX"/>, <paramref name="stepY"/>).</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BlurNaive(
    ReadWriteBuffer<float> source, ReadWriteBuffer<float> destination, int width, int height, int radius, int stepX, int stepY) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        float sum = 0f;

        for (int k = -radius; k <= radius; k++)
        {
            int sx = Gpu.Clamp(x + (k * stepX), 0, width - 1);
            int sy = Gpu.Clamp(y + (k * stepY), 0, height - 1);

            sum += (radius + 1 - Gpu.Abs(k)) * source[(sy * width) + sx];
        }

        destination[(y * width) + x] = sum / ((radius + 1) * (radius + 1));
    }
}

/// <summary>Draws the image and its blur side by side.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawBlur(ReadWriteBuffer<uint> pixels, ReadWriteBuffer<float> image, ReadWriteBuffer<float> blurred, int width, int size) : IComputeShader
{
    public const int PanelY = 104;
    public const int PanelSize = 384;
    public const int PanelPitch = 408;
    public const int FirstPanelX = 32;

    /// <summary>The left edge of panel 0 or 1.</summary>
    public static int PanelX(int panel) => FirstPanelX + (panel * PanelPitch);

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float3 color = new Float3(0.055f, 0.065f, 0.085f);
        int py = y - PanelY;
        int px = x - FirstPanelX;

        if (py >= 0 && py < PanelSize && px >= 0)
        {
            int panel = px / PanelPitch;
            int u = px % PanelPitch;

            if (panel < 2 && u < PanelSize)
            {
                int element = (Gpu.Min(size - 1, py * size / PanelSize) * size) + Gpu.Min(size - 1, u * size / PanelSize);
                float value = panel == 0 ? image[element] : blurred[element];

                color = Gpu.Lerp(new Float3(0.02f, 0.03f, 0.09f), new Float3(1f, 0.86f, 0.55f), value);
            }
        }

        pixels[(y * width) + x] = Gpu.PackUnorm4x8(new Float4(color, 1f));
    }
}
