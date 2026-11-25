// Original SharpCompute sample (MIT).
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab.Pages;

/// <summary>
/// The 8-, 16- and 64-bit integers at work. An 8-bit grayscale image lives in a
/// <c>ReadWriteBuffer&lt;byte&gt;</c>, one byte per pixel, and is histogram-equalised in place: a
/// <c>uint</c> histogram counted with atomics, a 256-entry <c>byte</c> lookup table built from it, and
/// one pass that replaces every pixel by its table entry. Next to it every pixel gets a 64-bit hash
/// (FNV-1a over its coordinates, with one xor-shift-multiply round to finish) in a
/// <c>ReadWriteBuffer&lt;ulong&gt;</c>, and a 64-bit atomic maximum finds the largest of them.
/// </summary>
/// <remarks>
/// Everything after the image is generated is integer arithmetic, so the check is strict: the same
/// shaders run with <c>ForCpu</c> and the histogram, the table, all the equalised bytes, all the
/// hashes and the maximum must be identical. The table and a sample of the hashes are also compared
/// with plain C# written without the shaders (<see cref="ReferenceLut"/>, <see cref="ReferenceHash"/>).
/// </remarks>
public sealed class IntegersPage : ILabPage
{
    /// <summary>The image is <see cref="Size"/> × <see cref="Size"/> pixels: one megabyte of bytes, and 2^20 hashes.</summary>
    public const int Size = 1024;

    /// <summary>The number of gray levels of a byte, and so of histogram bins and table entries.</summary>
    public const int Levels = 256;

    private static readonly string[] s_variants = ["1024 x 1024 pixels"];

    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<byte> _original;
    private readonly ReadWriteBuffer<byte> _image;
    private readonly ReadWriteBuffer<uint> _counts;
    private readonly ReadWriteBuffer<uint> _countsAfter;
    private readonly ReadWriteBuffer<byte> _lut;
    private readonly ReadWriteBuffer<ulong> _hashes;
    private readonly ReadWriteBuffer<ulong> _largest;
    private readonly ReadWriteBuffer<byte> _cpuImage;
    private readonly ReadWriteBuffer<uint> _cpuCounts;
    private readonly ReadWriteBuffer<byte> _cpuLut;
    private readonly ReadWriteBuffer<ulong> _cpuHashes;
    private readonly ReadWriteBuffer<ulong> _cpuLargest;
    private uint _seed;

    public IntegersPage(GraphicsDevice device)
    {
        _device = device;
        _original = device.AllocateReadWriteBuffer<byte>(Size * Size);
        _image = device.AllocateReadWriteBuffer<byte>(Size * Size);
        _counts = device.AllocateReadWriteBuffer<uint>(Levels);
        _countsAfter = device.AllocateReadWriteBuffer<uint>(Levels);
        _lut = device.AllocateReadWriteBuffer<byte>(Levels);
        _hashes = device.AllocateReadWriteBuffer<ulong>(Size * Size);
        _largest = device.AllocateReadWriteBuffer<ulong>(1);
        _cpuImage = device.AllocateReadWriteBuffer<byte>(Size * Size);
        _cpuCounts = device.AllocateReadWriteBuffer<uint>(Levels);
        _cpuLut = device.AllocateReadWriteBuffer<byte>(Levels);
        _cpuHashes = device.AllocateReadWriteBuffer<ulong>(Size * Size);
        _cpuLargest = device.AllocateReadWriteBuffer<ulong>(1);
    }

    public string Name => "Small and wide integers";

    public string Feature => "8-bit and 64-bit integers";

    public string Topic => LabTopics.Math;

    public string Help => string.Empty;

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    /// <summary>The generated image, before equalisation.</summary>
    public ReadOnlySpan<byte> Original => _original.AsReadOnlySpan();

    /// <summary>The image after the last <see cref="Compute"/> equalised it in place.</summary>
    public ReadOnlySpan<byte> Equalised => _image.AsReadOnlySpan();

    /// <summary>The GPU's histogram of <see cref="Original"/>: how many pixels have each of the 256 levels.</summary>
    public ReadOnlySpan<uint> Counts => _counts.AsReadOnlySpan();

    /// <summary>The GPU's lookup table: the equalised level of each original level.</summary>
    public ReadOnlySpan<byte> Lut => _lut.AsReadOnlySpan();

    /// <summary>The GPU's hash of every pixel, row by row.</summary>
    public ReadOnlySpan<ulong> Hashes => _hashes.AsReadOnlySpan();

    /// <summary>The largest <see cref="HashPixels.Key"/> of the last <see cref="Compute"/>: the top 44 bits of a hash above the index of its pixel.</summary>
    public ulong Largest => _largest.AsReadOnlySpan()[0];

    public void Compute(LabFrame frame)
    {
        // The hashes change with the frame; the headless picture is the one of seed 0.
        _seed = frame.Deterministic ? 0u : (uint)(frame.Time * 24f);

        _device.For(Size, Size, new GenerateGray(_original, Size, frame.Time));

        // The equalisation works in place, so it starts from a fresh copy every time (shared memory: a memcpy).
        _original.AsReadOnlySpan().CopyTo(_image.AsSpan());
        _counts.AsSpan().Clear();
        _largest.AsSpan().Clear();

        long start = Stopwatch.GetTimestamp();
        _device.For(Size * Size, new ByteHistogram(_image, _counts));
        _device.For(Levels, new BuildEqualisationLut(_counts, _lut, Size * Size));
        _device.For(Size * Size, new ApplyLut(_image, _lut));
        _device.For(Size, Size, new HashPixels(_hashes, _largest, Size, _seed));
        GpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        // For the picture only: what the histogram looks like afterwards.
        _countsAfter.AsSpan().Clear();
        _device.For(Size * Size, new ByteHistogram(_image, _countsAfter));
    }

    public LabCheck Check()
    {
        _original.AsReadOnlySpan().CopyTo(_cpuImage.AsSpan());
        _cpuCounts.AsSpan().Clear();
        _cpuLargest.AsSpan().Clear();

        long start = Stopwatch.GetTimestamp();
        _device.ForCpu(Size * Size, new ByteHistogram(_cpuImage, _cpuCounts));
        _device.ForCpu(Levels, new BuildEqualisationLut(_cpuCounts, _cpuLut, Size * Size));
        _device.ForCpu(Size * Size, new ApplyLut(_cpuImage, _cpuLut));
        _device.ForCpu(Size, Size, new HashPixels(_cpuHashes, _cpuLargest, Size, _seed));
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        ReadOnlySpan<uint> counts = Counts;
        ReadOnlySpan<ulong> hashes = Hashes;
        long counted = 0;

        foreach (uint count in counts)
        {
            counted += count;
        }

        // Plain C#, written without the shaders: the table from the histogram, a sample of the hashes,
        // and the largest key of all of them.
        Span<byte> referenceLut = stackalloc byte[Levels];
        ReferenceLut(counts, referenceLut);
        bool hashesMatchReference = true;
        ulong referenceLargest = 0;

        for (int i = 0; i < hashes.Length; i++)
        {
            referenceLargest = Math.Max(referenceLargest, HashPixels.Key(hashes[i], i));

            if (i % 997 == 0)
            {
                hashesMatchReference &= hashes[i] == ReferenceHash(i % Size, i / Size, _seed);
            }
        }

        string? problem =
            counted != Size * Size ? Invariant($"the histogram counted {counted:N0} of {Size * Size:N0} pixels")
            : !counts.SequenceEqual(_cpuCounts.AsReadOnlySpan()) ? "the GPU's histogram differs from the CPU's"
            : !Lut.SequenceEqual(_cpuLut.AsReadOnlySpan()) ? "the GPU's lookup table differs from the CPU's"
            : !Lut.SequenceEqual(referenceLut) ? "the lookup table differs from the one plain C# builds"
            : !Equalised.SequenceEqual(_cpuImage.AsReadOnlySpan()) ? "the equalised bytes differ between the GPU and the CPU"
            : !hashes.SequenceEqual(_cpuHashes.AsReadOnlySpan()) ? "the 64-bit hashes differ between the GPU and the CPU"
            : !hashesMatchReference ? "the 64-bit hashes differ from the ones plain C# computes"
            : Largest != _cpuLargest.AsReadOnlySpan()[0] || Largest != referenceLargest ? "the 64-bit atomic maximum is not the largest key"
            : null;

        string summary = problem
            ?? Invariant($"{Size * Size:N0} bytes equalised in place, {hashes.Length:N0} 64-bit hashes: GPU, ForCpu and plain C# agree");

        return new LabCheck(problem is null, summary, cpuMs);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        uint tallestBefore = 1, tallestAfter = 1;

        foreach (uint count in _counts.AsReadOnlySpan())
        {
            tallestBefore = Math.Max(tallestBefore, count);
        }

        foreach (uint count in _countsAfter.AsReadOnlySpan())
        {
            tallestAfter = Math.Max(tallestAfter, count);
        }

        _device.For(
            LabCanvas.Width,
            LabCanvas.Height,
            new DrawIntegers(canvas.Pixels, _original, _image, _counts, _countsAfter, _lut, _hashes, LabCanvas.Width, Size, tallestBefore, tallestAfter, Largest));

        int titleY = DrawIntegers.PanelY - 24;
        int captionY = DrawIntegers.ChartY + DrawIntegers.ChartHeight + 8;
        int second = DrawIntegers.PanelX + DrawIntegers.PanelPitch;
        int third = DrawIntegers.PanelX + (2 * DrawIntegers.PanelPitch);
        int winner = (int)(Largest & HashPixels.IndexMask);

        canvas.Label(DrawIntegers.PanelX, titleY, "ReadWriteBuffer<byte> image");
        canvas.Label(second, titleY, "Equalised in place by a byte LUT");
        canvas.Label(third, titleY, "64-bit hash per pixel, 1 in 16");

        canvas.Label(DrawIntegers.PanelX, captionY, "uint histogram, and the LUT curve");
        canvas.Label(second, captionY, "histogram after equalisation");
        canvas.Label(third, captionY, "bits 63..0 of the top row's hashes");

        canvas.Label(DrawIntegers.PanelX, captionY + LabCanvas.CellHeight, LevelRange(_counts.AsReadOnlySpan()), TextColor.Cool);
        canvas.Label(second, captionY + LabCanvas.CellHeight, LevelRange(_countsAfter.AsReadOnlySpan()), TextColor.Cool);
        canvas.Label(third, captionY + LabCanvas.CellHeight, Invariant($"Gpu.AtomicMax: pixel ({winner % Size}, {winner / Size})"), TextColor.Accent);
    }

    /// <summary>"levels 76..163 of 0..255": the darkest and the brightest level a histogram holds.</summary>
    private static string LevelRange(ReadOnlySpan<uint> counts)
    {
        int lowest = 0, highest = Levels - 1;

        while (lowest < Levels - 1 && counts[lowest] == 0)
        {
            lowest++;
        }

        while (highest > 0 && counts[highest] == 0)
        {
            highest--;
        }

        return Invariant($"levels {lowest}..{highest} of 0..255");
    }

    /// <summary>
    /// Histogram equalisation in plain C#: level <c>v</c> maps to the share of the pixels at or below it,
    /// measured from the darkest level present and scaled to 0..255, rounded to nearest.
    /// </summary>
    public static void ReferenceLut(ReadOnlySpan<uint> counts, Span<byte> lut)
    {
        ulong total = 0, lowest = 0, below = 0;

        foreach (uint count in counts)
        {
            total += count;
            lowest = lowest == 0 ? count : lowest;
        }

        ulong range = total - lowest;

        for (int level = 0; level < counts.Length; level++)
        {
            below += counts[level];
            lut[level] = range == 0 || below <= lowest ? (byte)0 : (byte)((((below - lowest) * 255) + (range / 2)) / range);
        }
    }

    /// <summary>The 64-bit FNV-1a hash of <paramref name="bytes"/> (offset basis 0xCBF29CE484222325, prime 0x100000001B3).</summary>
    public static ulong Fnv1a(ReadOnlySpan<byte> bytes)
    {
        ulong hash = 0xCBF29CE484222325;

        foreach (byte b in bytes)
        {
            hash ^= b;
            hash *= 0x100000001B3;
        }

        return hash;
    }

    /// <summary>
    /// A pixel's hash in plain C#: FNV-1a over the twelve bytes of x, y and the seed (little-endian),
    /// then one xor-shift-multiply round, because FNV alone leaves the high bits of such short inputs
    /// poorly mixed.
    /// </summary>
    public static ulong ReferenceHash(int x, int y, uint seed)
    {
        Span<byte> bytes = stackalloc byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, x);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], y);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], seed);

        ulong hash = Fnv1a(bytes);
        hash ^= hash >> 32;
        hash *= 0xD6E8FEB86659FD93;
        hash ^= hash >> 32;
        return hash;
    }

    public void Dispose()
    {
        _original.Dispose();
        _image.Dispose();
        _counts.Dispose();
        _countsAfter.Dispose();
        _lut.Dispose();
        _hashes.Dispose();
        _largest.Dispose();
        _cpuImage.Dispose();
        _cpuCounts.Dispose();
        _cpuLut.Dispose();
        _cpuHashes.Dispose();
        _cpuLargest.Dispose();
    }
}

/// <summary>
/// The page's input: a washed-out grayscale picture, one byte per pixel. Soft bands, rings around a
/// slowly orbiting light and a little noise, all squeezed into about a third of the 0..255 range, the
/// way an underexposed photo looks before its levels are stretched.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct GenerateGray(ReadWriteBuffer<byte> image, int size, float time) : IComputeShader
{
    private static uint Hash(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float2 uv = new Float2(x, y) / size;
        Float2 light = new Float2(0.5f + (0.22f * Gpu.Cos(time * 0.4f)), 0.46f + (0.2f * Gpu.Sin(time * 0.31f)));
        float distance = Gpu.Length(uv - light);

        float bands = 0.5f + (0.5f * Gpu.Sin(6.2831853f * ((uv.X * 1.4f) + (0.3f * Gpu.Sin((uv.Y * 5f) + (time * 0.3f))))));
        float rings = 0.5f + (0.5f * Gpu.Cos(46f * distance));
        float glow = Gpu.Smoothstep(0.55f, 0f, distance);
        float noise = (Hash((uint)x + (Hash((uint)y) * 31u)) >> 8) * (1f / 16777216f);
        float value = (0.4f * bands) + (0.25f * rings * glow) + (0.35f * glow * glow);

        // 0.29..0.63 of the range, plus a grain of noise: levels 70 to 160 or so.
        float gray = 0.29f + (0.34f * value) + ((noise - 0.5f) * 0.012f);

        // A float converts to a byte as in C#: truncated, and saturated at 0 and 255.
        image[(y * size) + x] = (byte)(gray * 255f);
    }
}

/// <summary>One thread per pixel adds 1 to the bin of the pixel's level: the byte is the bin index.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct ByteHistogram(ReadWriteBuffer<byte> image, ReadWriteBuffer<uint> counts) : IComputeShader
{
    public void Execute() => Gpu.AtomicAdd(ref counts[image[ThreadIds.X]], 1u);
}

/// <summary>
/// One thread per level builds that level's entry of the equalisation table: the share of the pixels
/// at or below the level, measured from the darkest level present, scaled to 0..255.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct BuildEqualisationLut(ReadWriteBuffer<uint> counts, ReadWriteBuffer<byte> lut, uint total) : IComputeShader
{
    public void Execute()
    {
        int level = ThreadIds.X;
        uint below = 0;   // the pixels at or below this level: the cumulative histogram
        uint lowest = 0;  // the pixels at the darkest level present, where that cumulative count starts

        for (int k = 0; k < IntegersPage.Levels; k++)
        {
            uint count = counts[k];

            if (k <= level)
            {
                below += count;
            }

            if (lowest == 0u)
            {
                lowest = count;
            }
        }

        uint range = total - lowest;

        if (range == 0u || below <= lowest)
        {
            lut[level] = 0;
        }
        else
        {
            // 64-bit: the product of a pixel count and 255 outgrows a uint from 16.8 million pixels on.
            ulong scaled = ((ulong)(below - lowest) * 255UL) + (range / 2u);
            lut[level] = (byte)(scaled / range);
        }
    }
}

/// <summary>The equalisation itself, in place: every byte is replaced by its table entry.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct ApplyLut(ReadWriteBuffer<byte> image, ReadWriteBuffer<byte> lut) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;

        image[i] = lut[image[i]];
    }
}

/// <summary>
/// A 64-bit hash per pixel: FNV-1a over the bytes of x, y and the seed, finished with one
/// xor-shift-multiply round. Every thread also offers its hash to one 64-bit atomic maximum, with the
/// pixel's index in the low bits, so the winner says where it is.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct HashPixels(ReadWriteBuffer<ulong> hashes, ReadWriteBuffer<ulong> largest, int size, uint seed) : IComputeShader
{
    /// <summary>The low 20 bits of a key hold the index of the pixel (the image has 2^20 of them).</summary>
    public const ulong IndexMask = (1UL << 20) - 1;

    private const ulong OffsetBasis = 0xCBF29CE484222325;

    private const ulong Prime = 0x100000001B3;

    /// <summary>The value the pixels compete with: the top 44 bits of the hash, then the pixel's index.</summary>
    public static ulong Key(ulong hash, int index) => (hash & ~IndexMask) | (uint)index;

    /// <summary>FNV-1a over the four bytes of <paramref name="word"/>, low byte first: xor a byte in, multiply by the prime.</summary>
    private static ulong Feed(ulong hash, uint word)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            hash = (hash ^ (byte)(word >> shift)) * Prime;
        }

        return hash;
    }

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        ulong hash = Feed(Feed(Feed(OffsetBasis, (uint)x), (uint)y), seed);

        hash ^= hash >> 32;
        hash *= 0xD6E8FEB86659FD93UL;
        hash ^= hash >> 32;

        int i = (y * size) + x;

        hashes[i] = hash;
        Gpu.AtomicMax(ref largest[0], Key(hash, i));
    }
}

/// <summary>
/// Draws the page: the image before and after, the hashes as colored noise with the winner of the
/// atomic maximum marked, and under each a strip: the two histograms (the first with the table's
/// curve over it) and the 64 bits of the hashes of the top row.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawIntegers(
    ReadWriteBuffer<uint> pixels,
    ReadWriteBuffer<byte> original,
    ReadWriteBuffer<byte> equalised,
    ReadWriteBuffer<uint> before,
    ReadWriteBuffer<uint> after,
    ReadWriteBuffer<byte> lut,
    ReadWriteBuffer<ulong> hashes,
    int width,
    int size,
    uint tallestBefore,
    uint tallestAfter,
    ulong largest) : IComputeShader
{
    // On the text grid (12 x 16 pixel cells), so titles and captions sit in the rows next to the panels.
    public const int PanelSize = 384;
    public const int PanelX = 32;
    public const int PanelY = 96;
    public const int PanelPitch = 416;
    public const int ChartY = 504;
    public const int ChartHeight = 128;

    /// <summary>The side of the block of screen pixels one hash is drawn as.</summary>
    public const int HashBlock = 4;

    private const uint Background = 0xFF16110Eu;
    private const uint ChartBackground = 0xFF1F1816u;
    private const uint Marker = 0xFF5ACCFFu;

    /// <summary>An opaque packed pixel from 0..255 channels: red in the low byte, as the canvas wants it.</summary>
    private static uint Rgb(uint r, uint g, uint b) => 0xFF000000u | (b << 16) | (g << 8) | r;

    private static uint Gray(byte level) => Rgb(level, level, level);

    /// <summary>Three bytes from different ends of the hash as a color: noise in every channel if the bits are well mixed.</summary>
    private static uint HashColor(ulong hash) => Rgb((uint)(hash >> 56), (uint)((hash >> 28) & 255UL), (uint)(hash & 255UL));

    /// <summary>
    /// The hash panel: every fourth hash of every fourth row, each as a block of <see cref="HashBlock"/>
    /// screen pixels so that single hashes can be told apart, with a framed crosshair where the pixel
    /// is that won the atomic maximum.
    /// </summary>
    private uint HashPanel(int u, int v)
    {
        int winner = (int)(largest & HashPixels.IndexMask);
        int dx = Gpu.Abs(u - ((winner % size) * PanelSize / size));
        int dy = Gpu.Abs(v - ((winner / size) * PanelSize / size));

        if (dx <= 15 && dy <= 15 && (dx > 1 || dy > 1))
        {
            bool cross = (dx <= 1 && dy >= 5) || (dy <= 1 && dx >= 5);
            bool frame = dx >= 14 || dy >= 14;

            return cross || frame ? Marker : 0xFF000000u;
        }

        int column = u / HashBlock * HashBlock;
        int row = v / HashBlock * HashBlock;

        return HashColor(hashes[((row * size / PanelSize) * size) + (column * size / PanelSize)]);
    }

    /// <summary>A histogram as bars, dark levels on the left; on the first one the lookup table as a curve.</summary>
    private uint Chart(int panel, int u, int c)
    {
        int bin = u * IntegersPage.Levels / PanelSize;
        uint count = panel == 0 ? before[bin] : after[bin];
        uint tallest = panel == 0 ? tallestBefore : tallestAfter;
        int height = (int)(count * (uint)ChartHeight / tallest);
        int fromBottom = ChartHeight - 1 - c;
        uint color = ChartBackground;

        if (fromBottom < height || (count != 0u && fromBottom == 0))
        {
            color = Rgb(70u + (uint)(bin / 2), 120u + (uint)(bin / 3), 215u);
        }

        if (panel == 0 && Gpu.Abs(fromBottom - (lut[bin] * (ChartHeight - 1) / 255)) <= 1)
        {
            color = Marker;
        }

        return color;
    }

    /// <summary>
    /// The hashes of the top row of the image under the blocks of the panel above, bit 63 on top: two
    /// screen rows per bit, <see cref="HashBlock"/> columns per hash.
    /// </summary>
    private uint HashBits(int u, int c)
    {
        ulong hash = hashes[(u / HashBlock * HashBlock) * size / PanelSize];
        int bit = 63 - (c / 2);
        bool set = ((hash >> bit) & 1UL) != 0UL;

        // Alternate the hue byte by byte, so the eight bytes of the ulong can be told apart.
        if (((bit / 8) & 1) == 0)
        {
            return set ? Rgb(110u, 235u, 130u) : Rgb(22u, 50u, 30u);
        }

        return set ? Rgb(100u, 170u, 255u) : Rgb(22u, 34u, 62u);
    }

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        int px = x - PanelX;
        uint color = Background;

        if (px >= 0)
        {
            int panel = px / PanelPitch;
            int u = px - (panel * PanelPitch);
            int v = y - PanelY;
            int c = y - ChartY;

            if (panel < 3 && u < PanelSize)
            {
                if (v >= 0 && v < PanelSize)
                {
                    int i = ((v * size / PanelSize) * size) + (u * size / PanelSize);

                    if (panel == 0)
                    {
                        color = Gray(original[i]);
                    }
                    else if (panel == 1)
                    {
                        color = Gray(equalised[i]);
                    }
                    else
                    {
                        color = HashPanel(u, v);
                    }
                }
                else if (c >= 0 && c < ChartHeight)
                {
                    color = panel == 2 ? HashBits(u, c) : Chart(panel, u, c);
                }
            }
        }

        pixels[(y * width) + x] = color;
    }
}
