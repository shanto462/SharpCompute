using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using SharpCompute.Samples.Shaders;

namespace SharpCompute.Tests;

public class PngEncoderTests
{
    private static readonly Rgba32[] TwoByTwo =
    [
        new(255, 0, 0, 255), new(0, 255, 0, 128),
        new(0, 0, 255, 255), new(255, 255, 255, 0),
    ];

    // The same 2×2 image built independently with Python's zlib (zlib.compress(raw, 0), filter None
    // on both rows, zlib.crc32 for the chunk CRCs) and checked to open in macOS `sips`.
    private const string KnownGoodTwoByTwoHex =
        "89504e470d0a1a0a0000000d494844520000000200000002080600000072b60d24" +
        "0000001d494441547801011200edff00ff0000ff00ff0080000000ffffffffff00" +
        "43d30879b7e1a5b70000000049454e44ae426082";

    [Fact]
    public void TwoByTwo_Uncompressed_MatchesAKnownGoodPngByteForByte()
    {
        byte[] png = PngEncoder.Encode(TwoByTwo, 2, 2, CompressionLevel.NoCompression);

        Assert.Equal(Convert.FromHexString(KnownGoodTwoByTwoHex), png);
    }

    [Fact]
    public void Crc32_MatchesTheStandardCheckValue()
    {
        // The CRC-32 check value from the catalogue of parametrised CRC algorithms.
        Assert.Equal(0xCBF43926u, PngEncoder.Crc32("123456789"u8));
        Assert.Equal(0xAE426082u, PngEncoder.Crc32("IEND"u8));
        Assert.Equal(PngEncoder.Crc32("IHDRdata"u8), PngEncoder.Crc32("data"u8, PngEncoder.Crc32("IHDR"u8)));
    }

    [Theory]
    [InlineData(1, 1, CompressionLevel.Fastest)]
    [InlineData(2, 2, CompressionLevel.Optimal)]
    [InlineData(37, 19, CompressionLevel.Fastest)]
    [InlineData(37, 19, CompressionLevel.SmallestSize)]
    [InlineData(64, 3, CompressionLevel.NoCompression)]
    [InlineData(301, 97, CompressionLevel.Fastest)]
    public void Encode_DecodesBackToTheSamePixels(int width, int height, CompressionLevel level)
    {
        Rgba32[] pixels = TestImage(width, height);

        byte[] png = PngEncoder.Encode(pixels, width, height, level);
        (int decodedWidth, int decodedHeight, Rgba32[] decoded) = MinimalPngDecoder.Decode(png);

        Assert.Equal(width, decodedWidth);
        Assert.Equal(height, decodedHeight);
        Assert.Equal(pixels, decoded);
    }

    [Fact]
    public void Encode_UsesEveryFilterTypeOnSuitableImages()
    {
        // A smooth gradient with noise rows mixed in gives the adaptive heuristic a reason to pick
        // different filters, so the round trip above exercises all five unfilter paths.
        byte[] png = PngEncoder.Encode(TestImage(301, 97), 301, 97, CompressionLevel.Fastest);

        HashSet<byte> filters = MinimalPngDecoder.FilterTypes(png);

        Assert.True(filters.Count >= 3, $"Only filter types {string.Join(", ", filters)} were used.");
    }

    [Fact]
    public void Encode_RejectsAMismatchedPixelCount()
    {
        Assert.Throws<ArgumentException>("pixels", () => PngEncoder.Encode(new Rgba32[5], 2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => PngEncoder.Encode([], 0, 1));
    }

    [Fact]
    public void Encode_ProducesAFileThatSipsReads()
    {
        // Optional third-party check: skipped silently where sips (macOS) is not available.
        if (!File.Exists("/usr/bin/sips"))
        {
            return;
        }

        string path = Path.Combine(Path.GetTempPath(), $"sharpcompute-png-{Guid.NewGuid():N}.png");

        try
        {
            PngEncoder.Write(path, TestImage(301, 97), 301, 97);

            ProcessStartInfo info = new("/usr/bin/sips") { RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("-g");
            info.ArgumentList.Add("pixelWidth");
            info.ArgumentList.Add("-g");
            info.ArgumentList.Add("pixelHeight");
            info.ArgumentList.Add("-g");
            info.ArgumentList.Add("hasAlpha");
            info.ArgumentList.Add(path);

            using Process process = Process.Start(info)!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            Assert.Equal(0, process.ExitCode);
            Assert.Contains("pixelWidth: 301", output);
            Assert.Contains("pixelHeight: 97", output);
            Assert.Contains("hasAlpha: yes", output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Rgba32[] TestImage(int width, int height)
    {
        Random random = new(width * 7919 + height);
        Rgba32[] pixels = new Rgba32[width * height];

        for (int y = 0; y < height; y++)
        {
            bool noisy = y % 5 == 3;

            for (int x = 0; x < width; x++)
            {
                pixels[y * width + x] = noisy
                    ? new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256))
                    : new Rgba32((byte)(x * 255 / Math.Max(1, width - 1)), (byte)(y * 3), (byte)(x + y), (byte)(255 - x));
            }
        }

        return pixels;
    }

    /// <summary>
    /// Just enough of a PNG reader to check the encoder: validates the signature, every chunk's CRC
    /// (with a bitwise CRC, independent of the encoder's table), IHDR, chunk order, then inflates
    /// IDAT and reverses all five filter types.
    /// </summary>
    private static class MinimalPngDecoder
    {
        public static (int Width, int Height, Rgba32[] Pixels) Decode(byte[] png)
        {
            (int width, int height, byte[] scanlines) = ReadImageData(png);
            int stride = width * 4;
            byte[] image = new byte[stride * height];

            for (int y = 0; y < height; y++)
            {
                byte filter = scanlines[y * (stride + 1)];
                ReadOnlySpan<byte> line = scanlines.AsSpan(y * (stride + 1) + 1, stride);

                for (int i = 0; i < stride; i++)
                {
                    int a = i >= 4 ? image[y * stride + i - 4] : 0;
                    int b = y > 0 ? image[(y - 1) * stride + i] : 0;
                    int c = i >= 4 && y > 0 ? image[(y - 1) * stride + i - 4] : 0;

                    int predictor = filter switch
                    {
                        0 => 0,
                        1 => a,
                        2 => b,
                        3 => (a + b) / 2,
                        4 => Paeth(a, b, c),
                        _ => throw new InvalidDataException($"Row {y} has invalid filter type {filter}."),
                    };

                    image[y * stride + i] = (byte)(line[i] + predictor);
                }
            }

            Rgba32[] pixels = new Rgba32[width * height];

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Rgba32(image[i * 4], image[i * 4 + 1], image[i * 4 + 2], image[i * 4 + 3]);
            }

            return (width, height, pixels);
        }

        public static HashSet<byte> FilterTypes(byte[] png)
        {
            (int width, int height, byte[] scanlines) = ReadImageData(png);
            return Enumerable.Range(0, height).Select(y => scanlines[y * (width * 4 + 1)]).ToHashSet();
        }

        private static (int Width, int Height, byte[] Scanlines) ReadImageData(byte[] png)
        {
            Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);

            List<string> order = [];
            int width = 0, height = 0;
            using MemoryStream idat = new();

            for (int offset = 8; offset < png.Length;)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
                string type = Encoding.ASCII.GetString(png, offset + 4, 4);
                ReadOnlySpan<byte> data = png.AsSpan(offset + 8, length);
                uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length));

                Assert.Equal(BitwiseCrc32(png.AsSpan(offset + 4, 4 + length)), crc);
                order.Add(type);

                switch (type)
                {
                    case "IHDR":
                        Assert.Equal(13, length);
                        width = BinaryPrimitives.ReadInt32BigEndian(data);
                        height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                        Assert.Equal([8, 6, 0, 0, 0], data[8..].ToArray()); // 8-bit RGBA, deflate, adaptive, no interlace
                        break;
                    case "IDAT":
                        idat.Write(data);
                        break;
                    case "IEND":
                        Assert.Equal(0, length);
                        Assert.Equal(png.Length, offset + 12);
                        break;
                }

                offset += 12 + length;
            }

            Assert.Equal("IHDR", order[0]);
            Assert.Equal("IEND", order[^1]);
            Assert.Contains("IDAT", order);

            idat.Position = 0;
            using ZLibStream zlib = new(idat, CompressionMode.Decompress);
            using MemoryStream scanlines = new();
            zlib.CopyTo(scanlines);

            Assert.Equal((long)(width * 4 + 1) * height, scanlines.Length);
            return (width, height, scanlines.ToArray());
        }

        private static uint BitwiseCrc32(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFF;

            foreach (byte b in data)
            {
                crc ^= b;

                for (int k = 0; k < 8; k++)
                {
                    crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
                }
            }

            return ~crc;
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }
}
