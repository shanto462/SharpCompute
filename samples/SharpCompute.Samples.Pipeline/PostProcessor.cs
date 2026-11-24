using System;
using SharpCompute;

namespace SharpCompute.Samples.Pipeline;

/// <summary>
/// The CPU half of the pipeline: a luminance histogram of the rendered frame, auto-levels from its
/// 1st and 99th percentiles, and a vignette, written into <see cref="Output"/>. Single-threaded on
/// purpose: it stands for whatever per-frame CPU work (analysis, encoding, I/O) an application does
/// with GPU results.
/// </summary>
internal sealed class PostProcessor
{
    private readonly int _width;
    private readonly int _height;
    private readonly float[] _vignetteX;
    private readonly float[] _vignetteY;
    private readonly int[] _histogram = new int[256];

    public PostProcessor(int width, int height)
    {
        _width = width;
        _height = height;
        _vignetteX = Falloff(width);
        _vignetteY = Falloff(height);
        Output = new Rgba32[width * height];
    }

    /// <summary>The last processed frame.</summary>
    public Rgba32[] Output { get; }

    /// <summary>Processes the packed RGBA pixels in <paramref name="frame"/> and returns a checksum of the result.</summary>
    public ulong Process(ReadWriteBuffer<uint> frame)
    {
        ReadOnlySpan<uint> pixels = frame.AsSpan();
        Array.Clear(_histogram);

        foreach (uint pixel in pixels)
        {
            _histogram[Luma(pixel)]++;
        }

        int low = Percentile(pixels.Length / 100);
        int high = Math.Max(Percentile(pixels.Length - (pixels.Length / 100)), low + 1);
        float gain = 255f / (high - low);
        ulong checksum = 0;

        for (int y = 0; y < _height; y++)
        {
            ReadOnlySpan<uint> row = pixels.Slice(y * _width, _width);
            Span<Rgba32> output = Output.AsSpan(y * _width, _width);
            float vy = _vignetteY[y] * gain;

            for (int x = 0; x < row.Length; x++)
            {
                uint p = row[x];
                float scale = _vignetteX[x] * vy;
                byte r = Level(p & 0xFF, low, scale);
                byte g = Level((p >> 8) & 0xFF, low, scale);
                byte b = Level((p >> 16) & 0xFF, low, scale);
                output[x] = new Rgba32(r, g, b, 255);
                checksum += (uint)(r | (g << 8) | (b << 16)) * (ulong)(x + 1);
            }
        }

        // The span points into the GPU buffer and does not keep it alive on its own.
        GC.KeepAlive(frame);
        return checksum;
    }

    private static int Luma(uint pixel) => (int)((((pixel & 0xFF) * 54) + (((pixel >> 8) & 0xFF) * 183) + (((pixel >> 16) & 0xFF) * 19)) >> 8);

    private static byte Level(uint channel, int low, float scale) => (byte)Math.Clamp((int)(((int)channel - low) * scale), 0, 255);

    private int Percentile(int rank)
    {
        int seen = 0;

        for (int i = 0; i < _histogram.Length; i++)
        {
            seen += _histogram[i];

            if (seen > rank)
            {
                return i;
            }
        }

        return 255;
    }

    private static float[] Falloff(int length)
    {
        float[] weights = new float[length];

        for (int i = 0; i < length; i++)
        {
            float d = ((i + 0.5f) / length) - 0.5f;
            weights[i] = 1f - (0.9f * d * d);
        }

        return weights;
    }
}
