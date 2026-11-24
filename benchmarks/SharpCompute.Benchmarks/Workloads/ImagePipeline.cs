using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SharpCompute.Benchmarks.Shaders;

namespace SharpCompute.Benchmarks.Workloads;

/// <summary>
/// A decoded RGBA8 image (bytes R, G, B, A per pixel) blurred into RGBA8 that is ready to encode:
/// unpack to floats, separable Gaussian blur (clamped edges), round back to 8 bits. Decoding and
/// encoding are the same for every variant and not timed; this measures what sits between them.
/// <list type="bullet">
/// <item><b>CPU</b>: Parallel.For over rows, one <see cref="Vector4"/> (one NEON register) per pixel.</item>
/// <item><b>GPU with copies</b>: what a pipeline built on managed arrays has to do: the CPU converts
/// the bytes to floats, copies them into a GPU buffer, the GPU blurs, the result is copied out and the
/// CPU converts it back to bytes.</item>
/// <item><b>GPU zero copy</b>: the decoder writes into a GPU buffer's shared memory
/// (<c>AsSpan</c>), the GPU reads the bytes and writes bytes, and the encoder reads them in place.</item>
/// </list>
/// </summary>
public sealed class ImagePipeline : IDisposable
{
    [ThreadStatic]
    private static Vector4[]? t_row;

    public ImagePipeline(int width, int height, int radius, GraphicsDevice device)
    {
        Width = width;
        Height = height;
        Radius = radius;
        Weights = GaussianBlur.MakeWeights(radius);
        ScaledWeights = Array.ConvertAll(Weights, static w => w / 255f);

        Random random = new(11);
        Source = new uint[width * height];

        for (int i = 0; i < Source.Length; i++)
        {
            // Opaque pixels with random color: premultiplied and straight alpha are the same.
            Source[i] = (uint)random.Next(1 << 24) | 0xFF000000u;
        }

        Temp = new Vector4[width * height];
        Staged = new Float4[width * height];
        Output = new uint[width * height];
        CopyPathOutput = new uint[width * height];

        Device = device;
        GpuWeights = device.AllocateReadOnlyBuffer<float>(Weights);
        GpuScaledWeights = device.AllocateReadOnlyBuffer<float>(ScaledWeights);

        // The zero-copy input stands for "decoded straight into the buffer": the decoder would write
        // here through AsSpan; the benchmark fills it once.
        GpuPacked = device.AllocateReadOnlyBuffer<uint>(Source);
        GpuFloats = device.AllocateReadWriteBuffer<Float4>(width * height);
        GpuTemp = device.AllocateReadWriteBuffer<Float4>(width * height);
        GpuPackedOutput = device.AllocateReadWriteBuffer<uint>(width * height);
    }

    public int Width { get; }

    public int Height { get; }

    public int Radius { get; }

    public float[] Weights { get; }

    public float[] ScaledWeights { get; }

    public uint[] Source { get; }

    public uint[] Output { get; }

    public uint[] CopyPathOutput { get; }

    public GraphicsDevice Device { get; }

    public ReadWriteBuffer<uint> GpuPackedOutput { get; }

    private Vector4[] Temp { get; }

    private Float4[] Staged { get; }

    private ReadOnlyBuffer<float> GpuWeights { get; }

    private ReadOnlyBuffer<float> GpuScaledWeights { get; }

    private ReadOnlyBuffer<uint> GpuPacked { get; }

    private ReadWriteBuffer<Float4> GpuFloats { get; }

    private ReadWriteBuffer<Float4> GpuTemp { get; }

    /// <summary>Parallel.For per row for each pass; each pixel is one <see cref="Vector4"/>.</summary>
    public uint Cpu()
    {
        Parallel.For(0, Height, HorizontalRow);
        Parallel.For(0, Height, VerticalRow);
        return Output[Output.Length / 2];
    }

    /// <summary>CPU unpack + copy in, two dispatches, copy out + CPU pack.</summary>
    public uint GpuWithCopies()
    {
        Parallel.For(0, Height, y =>
        {
            for (int i = y * Width, end = i + Width; i < end; i++)
            {
                uint p = Source[i];
                Staged[i] = new Float4((p & 0xFF) / 255f, ((p >> 8) & 0xFF) / 255f, ((p >> 16) & 0xFF) / 255f, (p >> 24) / 255f);
            }
        });

        GpuFloats.CopyFrom(Staged);
        Device.For(Width, Height, new BlurFloat4Horizontal(GpuFloats, GpuTemp, GpuWeights, Width, Radius));
        Device.For(Width, Height, new BlurFloat4Vertical(GpuTemp, GpuFloats, GpuWeights, Width, Height, Radius));
        GpuFloats.CopyTo(Staged);

        Parallel.For(0, Height, y =>
        {
            for (int i = y * Width, end = i + Width; i < end; i++)
            {
                Float4 f = Staged[i];
                CopyPathOutput[i] = Pack(new Vector4(f.X, f.Y, f.Z, f.W));
            }
        });

        return CopyPathOutput[CopyPathOutput.Length / 2];
    }

    /// <summary>Two dispatches that read and write packed bytes in shared memory; nothing is copied.</summary>
    public uint GpuZeroCopy()
    {
        Device.For(Width, Height, new BlurRgba8Horizontal(GpuPacked, GpuTemp, GpuScaledWeights, Width, Radius));
        Device.For(Width, Height, new BlurRgba8Vertical(GpuTemp, GpuPackedOutput, GpuWeights, Width, Height, Radius));
        return GpuPackedOutput[GpuPackedOutput.Length / 2];
    }

    public void Dispose()
    {
        GpuWeights.Dispose();
        GpuScaledWeights.Dispose();
        GpuPacked.Dispose();
        GpuFloats.Dispose();
        GpuTemp.Dispose();
        GpuPackedOutput.Dispose();
    }

    /// <summary>Rounds to nearest after clamping to 0..1, as <see cref="BlurRgba8Vertical"/> does.</summary>
    private static uint Pack(Vector4 v)
    {
        Vector4 q = (Vector4.Clamp(v, Vector4.Zero, Vector4.One) * 255f) + new Vector4(0.5f);
        return (uint)q.X | ((uint)q.Y << 8) | ((uint)q.Z << 16) | ((uint)q.W << 24);
    }

    private void HorizontalRow(int y)
    {
        int width = Width, r = Radius;
        Vector4[] row = t_row is { } cached && cached.Length == width + (2 * r) ? cached : t_row = new Vector4[width + (2 * r)];
        ReadOnlySpan<uint> src = Source.AsSpan(y * width, width);

        // Unpack once into a row padded with the edge pixels, so the taps need no clamping.
        for (int i = 0; i < row.Length; i++)
        {
            uint p = src[Math.Clamp(i - r, 0, width - 1)];
            row[i] = new Vector4(p & 0xFF, (p >> 8) & 0xFF, (p >> 16) & 0xFF, p >> 24);
        }

        Span<Vector4> dst = Temp.AsSpan(y * width, width);
        float[] w = ScaledWeights;

        for (int x = 0; x < width; x++)
        {
            Vector4 sum = Vector4.Zero;

            for (int k = 0; k <= 2 * r; k++)
            {
                sum += row[x + k] * w[k];
            }

            dst[x] = sum;
        }
    }

    private void VerticalRow(int y)
    {
        int width = Width, r = Radius;
        Span<uint> dst = Output.AsSpan(y * width, width);
        float[] w = Weights;

        for (int x = 0; x < width; x++)
        {
            Vector4 sum = Vector4.Zero;

            for (int k = -r; k <= r; k++)
            {
                sum += Temp[(Math.Clamp(y + k, 0, Height - 1) * width) + x] * w[k + r];
            }

            dst[x] = Pack(sum);
        }
    }
}
