using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace SharpCompute.Samples.Shaders;

/// <summary>
/// A minimal PNG writer for <see cref="Rgba32"/> images: 8-bit RGBA (color type 6), non-interlaced,
/// one <c>IDAT</c> chunk compressed with <see cref="ZLibStream"/>. No dependencies beyond the BCL.
/// </summary>
public static class PngEncoder
{
    private const int BytesPerPixel = 4;

    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Encodes the image and writes it to <paramref name="path"/>, replacing any existing file.</summary>
    public static void Write(string path, ReadOnlySpan<Rgba32> pixels, int width, int height, CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        using FileStream file = File.Create(path);
        Encode(file, pixels, width, height, compressionLevel);
    }

    /// <summary>Encodes the image into a new byte array.</summary>
    public static byte[] Encode(ReadOnlySpan<Rgba32> pixels, int width, int height, CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        using MemoryStream stream = new();
        Encode(stream, pixels, width, height, compressionLevel);
        return stream.ToArray();
    }

    /// <summary>
    /// Encodes a <paramref name="width"/> × <paramref name="height"/> image (row-major, tightly packed)
    /// as a PNG. With <see cref="CompressionLevel.NoCompression"/> every row uses filter type 0 (None),
    /// since filtering only helps the compressor; otherwise each row uses the filter with the smallest
    /// sum of absolute residuals (the heuristic recommended by the PNG specification).
    /// </summary>
    public static void Encode(Stream output, ReadOnlySpan<Rgba32> pixels, int width, int height, CompressionLevel compressionLevel = CompressionLevel.Fastest)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if ((long)width * height != pixels.Length)
        {
            throw new ArgumentException($"Expected {(long)width * height} pixels for a {width}×{height} image, got {pixels.Length}.", nameof(pixels));
        }

        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // color type: truecolor with alpha
        header[10] = 0; // compression method: deflate
        header[11] = 0; // filter method: adaptive (five filter types)
        header[12] = 0; // interlace: none
        WriteChunk(output, "IHDR"u8, header);

        WriteChunk(output, "IDAT"u8, Compress(Filter(MemoryMarshal.AsBytes(pixels), width, height, compressionLevel != CompressionLevel.NoCompression), compressionLevel));
        WriteChunk(output, "IEND"u8, []);
    }

    /// <summary>CRC-32 (ISO 3309 / ITU-T V.42, as used by PNG) over <paramref name="data"/>, continuing from <paramref name="crc"/>.</summary>
    public static uint Crc32(ReadOnlySpan<byte> data, uint crc = 0)
    {
        ReadOnlySpan<uint> table = CrcTable.Value;
        uint c = ~crc;

        foreach (byte b in data)
        {
            c = table[(int)((c ^ b) & 0xFF)] ^ (c >> 8);
        }

        return ~c;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];

        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        output.Write(type);
        output.Write(data);

        BinaryPrimitives.WriteUInt32BigEndian(word, Crc32(data, Crc32(type)));
        output.Write(word);
    }

    private static byte[] Compress(byte[] filtered, CompressionLevel compressionLevel)
    {
        using MemoryStream compressed = new();

        using (ZLibStream zlib = new(compressed, compressionLevel, leaveOpen: true))
        {
            zlib.Write(filtered);
        }

        return compressed.ToArray();
    }

    /// <summary>Returns the scanlines, each prefixed with its filter-type byte, ready for compression.</summary>
    private static byte[] Filter(ReadOnlySpan<byte> image, int width, int height, bool adaptive)
    {
        int stride = width * BytesPerPixel;
        byte[] result = new byte[(stride + 1) * height];

        if (!adaptive)
        {
            for (int y = 0; y < height; y++)
            {
                image.Slice(y * stride, stride).CopyTo(result.AsSpan(y * (stride + 1) + 1));
            }

            return result;
        }

        byte[] candidate = new byte[stride];
        byte[] zeros = new byte[stride];

        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> row = image.Slice(y * stride, stride);
            ReadOnlySpan<byte> above = y == 0 ? zeros : image.Slice((y - 1) * stride, stride);
            Span<byte> destination = result.AsSpan(y * (stride + 1), stride + 1);

            long bestScore = long.MaxValue;

            for (byte type = 0; type <= 4; type++)
            {
                long score = ApplyFilter(type, row, above, candidate);

                if (score < bestScore)
                {
                    bestScore = score;
                    destination[0] = type;
                    candidate.CopyTo(destination[1..]);
                }
            }
        }

        return result;
    }

    /// <summary>Writes the residuals of one filter type into <paramref name="output"/> and returns the sum of their absolute values (as signed bytes).</summary>
    private static long ApplyFilter(byte type, ReadOnlySpan<byte> row, ReadOnlySpan<byte> above, Span<byte> output)
    {
        long score = 0;

        for (int i = 0; i < row.Length; i++)
        {
            int left = i >= BytesPerPixel ? row[i - BytesPerPixel] : 0;
            int up = above[i];
            int upLeft = i >= BytesPerPixel ? above[i - BytesPerPixel] : 0;

            int predictor = type switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) >> 1,
                _ => Paeth(left, up, upLeft),
            };

            byte residual = (byte)(row[i] - predictor);
            output[i] = residual;
            score += Math.Abs((int)(sbyte)residual);
        }

        return score;
    }

    /// <summary>The Paeth predictor from the PNG specification, section 9.4.</summary>
    internal static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);

        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static class CrcTable
    {
        internal static readonly uint[] Value = Create();

        private static uint[] Create()
        {
            uint[] table = new uint[256];

            for (uint n = 0; n < 256; n++)
            {
                uint c = n;

                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
