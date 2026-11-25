// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab.Pages;

/// <summary>
/// The bit and packing intrinsics as pictures: eight 256 × 256 tiles, each a pattern that one
/// intrinsic draws by itself (popcount, parity, bit reversal, leading and trailing zero counts, the
/// bits of a float, float classification, and a unorm8 pack/unpack round trip).
/// </summary>
/// <remarks>
/// Everything in the kernel is integer arithmetic or exactly rounded float arithmetic, so the check
/// is strict: the same shader runs with <c>ForCpu</c> and all 921,600 pixels must be identical.
/// </remarks>
public sealed class BitsPage : ILabPage
{
    private static readonly string[] s_variants = ["1280 x 720 pixels"];

    private static readonly string[] s_titles =
    [
        "CountBits(x ^ y)", "CountBits(x & y) & 1", "ReverseBits(x) >> 24", "LeadingZeroCount(x * y)",
        "TrailingZeroCount(x)", "AsUInt(float): 32 bits", "AsFloat: NaN/Inf/finite", "Pack / UnpackUnorm4x8",
    ];

    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<uint> _gpu;
    private readonly ReadWriteBuffer<uint> _cpu;
    private int _tick;

    public BitsPage(GraphicsDevice device)
    {
        _device = device;
        _gpu = device.AllocateReadWriteBuffer<uint>(LabCanvas.Width * LabCanvas.Height);
        _cpu = device.AllocateReadWriteBuffer<uint>(LabCanvas.Width * LabCanvas.Height);
    }

    public string Name => "Bits";

    public string Feature => "bit and packing intrinsics";


    public string Topic => LabTopics.Math;

    public string Help => string.Empty;

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    public void Compute(LabFrame frame)
    {
        // The patterns scroll; the headless picture is the unscrolled one.
        _tick = frame.Deterministic ? 0 : (int)(frame.Time * 24f);

        long start = Stopwatch.GetTimestamp();
        _device.For(LabCanvas.Width, LabCanvas.Height, new BitPatterns(_gpu, LabCanvas.Width, _tick));
        GpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    public LabCheck Check()
    {
        long start = Stopwatch.GetTimestamp();
        _device.ForCpu(LabCanvas.Width, LabCanvas.Height, new BitPatterns(_cpu, LabCanvas.Width, _tick));
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        ReadOnlySpan<uint> gpu = _gpu.AsReadOnlySpan(), cpu = _cpu.AsReadOnlySpan();
        int different = 0;

        for (int i = 0; i < gpu.Length; i++)
        {
            different += gpu[i] != cpu[i] ? 1 : 0;
        }

        string summary = different == 0
            ? Invariant($"{gpu.Length:N0} pixels of bit patterns: the GPU and the CPU (ForCpu, same shader) agree bit for bit")
            : Invariant($"{different:N0} of {gpu.Length:N0} pixels differ between the GPU and the CPU");

        return new LabCheck(different == 0, summary, cpuMs);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        // The kernel already produced the whole frame in the canvas's format: shared memory, one copy.
        canvas.Pixels.CopyFrom(_gpu.AsReadOnlySpan());

        for (int tile = 0; tile < s_titles.Length; tile++)
        {
            int x = BitPatterns.OriginX + ((tile % 4) * BitPatterns.PitchX);
            int y = BitPatterns.OriginY + ((tile / 4) * BitPatterns.PitchY);
            canvas.Label(x, y + BitPatterns.TileSize, s_titles[tile], TextColor.Dim);
        }
    }

    /// <summary>The plain C# value of one tile pixel, for tests: what the definitions say, written without the intrinsics.</summary>
    public static uint ReferencePopCountTile(int u, int v) => BitPatterns.Ramp((uint)BitOperations.PopCount((uint)(u ^ v)) * 32u);

    public void Dispose()
    {
        _gpu.Dispose();
        _cpu.Dispose();
    }
}

/// <summary>The feature kernel: all eight tiles, straight into packed pixels.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BitPatterns(ReadWriteBuffer<uint> pixels, int width, int tick) : IComputeShader
{
    // On the text grid (12 x 16 pixel cells), so a tile's caption sits in the row right under it.
    public const int TileSize = 256;
    public const int OriginX = 48;
    public const int OriginY = 80;
    public const int PitchX = 300;
    public const int PitchY = 304;

    private const uint Background = 0xFF16110Eu;

    /// <summary>An opaque packed pixel from 0..255 channels (clamped): red in the low byte, like <see cref="Gpu.PackUnorm4x8"/>.</summary>
    private static uint Rgb(uint r, uint g, uint b) => 0xFF000000u | (Gpu.Min(b, 255u) << 16) | (Gpu.Min(g, 255u) << 8) | Gpu.Min(r, 255u);

    /// <summary>A dark blue → teal → warm white ramp over 0..255.</summary>
    public static uint Ramp(uint t)
    {
        t = Gpu.Min(t, 255u);
        return Rgb((t * t) >> 8, (t * 3u) >> 2, 60u + ((t * 3u) >> 2));
    }

    private static uint PopCount(uint u, uint v, uint tick) => Ramp(Gpu.CountBits((u + tick) ^ v) * 32u);

    private static uint Parity(uint u, uint v, uint tick)
    {
        // The parity of the bits two numbers share: a Walsh-Hadamard matrix.
        bool odd = (Gpu.CountBits((u + tick) & v) & 1u) != 0u;
        return odd ? Rgb(240u, 190u, 90u) : Rgb(30u, 40u, 70u);
    }

    private static uint Reverse(uint u, uint v, uint tick)
    {
        // The bit-reversed column index as a bar height: the van der Corput sequence.
        uint reversed = Gpu.ReverseBits((u + tick) & 255u) >> 24;
        return 255u - v <= reversed ? Ramp(reversed) : Rgb(20u, 24u, 40u);
    }

    private static uint LeadingZeros(uint u, uint v, uint tick)
    {
        // 32 for a zero product, down to 16 for the largest: one band per power of two of x * y.
        uint zeros = Gpu.LeadingZeroCount((u + (tick & 255u)) * v);
        return Ramp((32u - zeros) * 15u);
    }

    private static uint TrailingZeros(uint u, uint v, uint tick)
    {
        // The ruler function: column x is as tall as the number of times 2 divides it.
        uint zeros = Gpu.Min(Gpu.TrailingZeroCount(u + tick), 8u);
        return 255u - v <= (zeros * 28u) + 20u ? Ramp(60u + (zeros * 24u)) : Rgb(20u, 24u, 40u);
    }

    private static uint FloatBits(uint u, uint v, uint tick)
    {
        // Column: a float from -48 to +47.625 in steps of 3/8 (exact). Row: one of its 32 bits, the sign on top.
        float value = ((int)((u + tick) & 255u) - 128) * 0.375f;
        uint bits = Gpu.AsUInt(value);
        uint bit = 31u - (v >> 3);
        bool set = ((bits >> (int)bit) & 1u) != 0u;

        if ((v & 7u) == 7u)
        {
            return Background;
        }

        if (bit == 31u)
        {
            return set ? Rgb(255u, 90u, 80u) : Rgb(70u, 30u, 30u);
        }

        if (bit >= 23u)
        {
            return set ? Rgb(110u, 235u, 130u) : Rgb(28u, 62u, 36u);
        }

        return set ? Rgb(100u, 170u, 255u) : Rgb(26u, 40u, 72u);
    }

    private static uint Classify(uint u, uint v, uint tick)
    {
        // Row: the exponent field, 255 on top (then 247, 239, ... and 0 in the last row).
        // Column: the mantissa's top four bits, so the first 16 columns have a zero mantissa.
        uint row = v >> 3;
        uint exponent = row == 31u ? 0u : 255u - (row * 8u);
        uint mantissa = ((u + (tick & 15u)) >> 4) & 15u;
        float value = Gpu.AsFloat((exponent << 23) | (mantissa << 19));

        if (Gpu.IsNaN(value))
        {
            return Rgb(235u, 70u, 90u);
        }

        if (Gpu.IsInfinity(value))
        {
            return Rgb(255u, 255u, 255u);
        }

        return Gpu.IsFinite(value) ? Ramp(40u + ((exponent * 3u) >> 2) + (mantissa * 2u)) : Rgb(255u, 0u, 255u);
    }

    private static uint PackRoundTrip(uint u, uint v, uint tick)
    {
        // A float color, packed to four bytes, unpacked and packed again: the word must not change.
        Float4 color = new Float4(u / 255f, v / 255f, ((u + v + tick) & 255u) / 255f, 1f);
        uint packed = Gpu.PackUnorm4x8(color);
        uint again = Gpu.PackUnorm4x8(Gpu.UnpackUnorm4x8(packed));

        return again == packed ? packed : Rgb(255u, 0u, 255u);
    }

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        int cx = x - OriginX;
        int cy = y - OriginY;
        uint color = Background;

        if (cx >= 0 && cy >= 0)
        {
            int tileX = cx / PitchX;
            int tileY = cy / PitchY;
            uint u = (uint)(cx - (tileX * PitchX));
            uint v = (uint)(cy - (tileY * PitchY));

            if (tileX < 4 && tileY < 2 && u < 256u && v < 256u)
            {
                uint t = (uint)tick;

                switch (tileX + (tileY * 4))
                {
                    case 0:
                        color = PopCount(u, v, t);
                        break;
                    case 1:
                        color = Parity(u, v, t);
                        break;
                    case 2:
                        color = Reverse(u, v, t);
                        break;
                    case 3:
                        color = LeadingZeros(u, v, t);
                        break;
                    case 4:
                        color = TrailingZeros(u, v, t);
                        break;
                    case 5:
                        color = FloatBits(u, v, t);
                        break;
                    case 6:
                        color = Classify(u, v, t);
                        break;
                    default:
                        color = PackRoundTrip(u, v, t);
                        break;
                }
            }
        }

        pixels[(y * width) + x] = color;
    }
}
