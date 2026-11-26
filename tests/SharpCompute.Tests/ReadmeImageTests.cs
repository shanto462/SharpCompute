using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace SharpCompute.Tests;

/// <summary>
/// The README's pictures: every image it shows exists, has alt text, is opaque and within its byte
/// limit; every file in docs/screenshots is shown or linked; the grids show thumbnails that link to
/// the full image; and the screenshots of demos that draw text keep that text off their top and
/// bottom edges. Every relative link and image in the README and in docs/*.md resolves as well.
/// <c>scripts/make-screenshots.sh</c> and <c>scripts/make-gifs.sh</c> write them, with
/// the same limits (<c>scripts/_common.sh</c>).
/// </summary>
public sealed partial class ReadmeImageTests
{
    // The limits the README states and scripts/_common.sh applies, in bytes: "400 KB" is 400,000.
    private const int MaxPngBytes = 400_000;
    private const int MaxThumbBytes = 100_000;
    private const int MaxGifBytes = 2_000_000;
    private const int ThumbWidth = 400;
    private const string Thumbs = "docs/screenshots/thumbs/";

    /// <summary>Screenshots of demos that draw text over their pictures; the rest are full-bleed images.</summary>
    private static readonly string[] s_textPrefixes = ["computelab-", "textures-"];

    private static readonly string? s_root = FindRoot();

    private static string Root => s_root ?? throw new InvalidOperationException("SharpCompute.slnx not found above the test output.");

    [Fact]
    public void EveryImageTheReadmeShows_ExistsHasAltText_AndIsWithinItsLimits()
    {
        List<(string Path, string Alt)> images = Images();

        Assert.True(images.Count >= 30, $"only {images.Count} images found: is the README's image syntax still recognized?");

        foreach ((string path, string alt) in images)
        {
            string file = Path.Combine(Root, path);
            Assert.True(File.Exists(file), $"README shows {path}, which does not exist");
            Assert.False(string.IsNullOrWhiteSpace(alt), $"{path} has no alt text");
            long bytes = new FileInfo(file).Length;

            switch (Path.GetExtension(path))
            {
                case ".png":
                    bool thumb = path.StartsWith(Thumbs, StringComparison.Ordinal);
                    int limit = thumb ? MaxThumbBytes : MaxPngBytes;
                    Assert.True(bytes <= limit, $"{path} is {bytes:N0} bytes, over {limit:N0}");

                    // Opaque: a transparent background shows GitHub's page through it, which in the
                    // dark theme hides dark text.
                    Png png = Png.Read(file);
                    Assert.True(png.IsOpaque, $"{path} has transparency");

                    if (thumb)
                    {
                        Assert.Equal(ThumbWidth, png.Width);
                    }
                    else
                    {
                        Assert.InRange(png.Width, 900, 1280);
                    }

                    break;

                case ".gif":
                    Assert.True(bytes <= MaxGifBytes, $"{path} is {bytes:N0} bytes, over {MaxGifBytes:N0}");
                    break;

                default:
                    Assert.Fail($"{path}: the README's images are PNGs and GIFs");
                    break;
            }
        }

        foreach (Match link in LinkPattern().Matches(Readme()))
        {
            string target = link.Groups[1].Value;
            Assert.True(File.Exists(Path.Combine(Root, target)), $"README links to {target}, which does not exist");
        }
    }

    [Fact]
    public void EveryThumbnail_LinksToItsFullImage()
    {
        MatchCollection linked = LinkedImagePattern().Matches(Readme());
        HashSet<string> linkedThumbs = [];

        foreach (Match match in linked)
        {
            (string target, string src) = (match.Groups[1].Value, match.Groups[2].Value);

            if (!src.StartsWith(Thumbs, StringComparison.Ordinal))
            {
                continue;
            }

            linkedThumbs.Add(src);
            Assert.False(target.StartsWith(Thumbs, StringComparison.Ordinal), $"{src} links to another thumbnail");
            Assert.True(File.Exists(Path.Combine(Root, target)), $"{src} links to {target}, which does not exist");

            // The full image of a thumbnail, or for a game, the animation it is a frame of.
            string stem = Path.GetFileNameWithoutExtension(src);
            Assert.Equal(stem, Path.GetFileNameWithoutExtension(target));
        }

        // A thumbnail is only ever shown as a link.
        Assert.All(Images().Where(static i => i.Path.StartsWith(Thumbs, StringComparison.Ordinal)), image => Assert.Contains(image.Path, linkedThumbs));
        Assert.True(linkedThumbs.Count >= 20, $"only {linkedThumbs.Count} linked thumbnails");
    }

    [Fact]
    public void EveryScreenshot_IsShownOrLinked()
    {
        HashSet<string> shown = [.. Images().Select(static image => image.Path), .. LinkPattern().Matches(Readme()).Select(static m => m.Groups[1].Value)];
        string[] screenshots =
        [
            .. Directory.GetFiles(Path.Combine(Root, "docs", "screenshots"), "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
                .Where(static f => !f.EndsWith(".DS_Store", StringComparison.Ordinal)),
        ];

        Assert.NotEmpty(screenshots);
        Assert.All(screenshots, screenshot => Assert.Contains(screenshot, shown));
    }

    [Fact]
    public void ScreenshotsWithText_HaveDarkBackgroundInTheirFirstAndLastRows()
    {
        string[] screenshots =
        [
            .. Directory.GetFiles(Path.Combine(Root, "docs", "screenshots"), "*.png", SearchOption.AllDirectories)
                .Where(static f => s_textPrefixes.Any(p => Path.GetFileName(f).StartsWith(p, StringComparison.Ordinal))),
        ];

        Assert.NotEmpty(screenshots);

        foreach (string file in screenshots)
        {
            Png png = Png.Read(file);

            // The text is light on a dark backing; a glyph cut by the edge leaves light pixels in
            // the edge row. The darkest text color (red, 1 / 0.4 / 0.35) has a luma above 0.5.
            foreach (int y in new[] { 0, png.Height - 1 })
            {
                double brightest = Enumerable.Range(0, png.Width).Max(x => png.Luma(x, y));
                Assert.True(brightest < 0.45, $"{Path.GetRelativePath(Root, file)}: row {y} reaches luma {brightest:F2}, text may be cut by the edge");
            }
        }
    }

    [Fact]
    public void EveryRelativeLinkAndImage_InTheReadmeAndTheDocs_Resolves()
    {
        // The reference lives in docs/*.md and links back and forth with the README; a page that is
        // renamed or an image that is moved must not leave a dead link behind. Code is skipped: a
        // C# indexer such as items[i](x) is not a link.
        string[] pages = [Path.Combine(Root, "README.md"), .. Directory.GetFiles(Path.Combine(Root, "docs"), "*.md")];
        int checkedLinks = 0;

        foreach (string page in pages)
        {
            string text = CodePattern().Replace(File.ReadAllText(page), string.Empty);

            foreach (Match image in MarkdownImagePattern().Matches(text))
            {
                Assert.False(string.IsNullOrWhiteSpace(image.Groups[1].Value), $"{Path.GetFileName(page)}: {image.Groups[2].Value} has no alt text");
            }

            foreach (Match link in RelativeTargetPattern().Matches(text))
            {
                string target = link.Groups[1].Success ? link.Groups[1].Value : link.Groups[2].Value;

                if (target.Contains(':', StringComparison.Ordinal) || target.StartsWith('#'))
                {
                    continue;
                }

                string file = target.Split('#')[0];
                string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(page)!, file));
                Assert.True(File.Exists(resolved) || Directory.Exists(resolved), $"{Path.GetRelativePath(Root, page)} links to {target}, which does not exist");
                checkedLinks++;
            }
        }

        Assert.True(checkedLinks >= 100, $"only {checkedLinks} relative links found: is the link syntax still recognized?");
    }

    [Fact]
    public void Png_DecodesRgbAndPaletteImages()
    {
        // 2 x 2 palette image: palette [black, white], pixels 0 1 / 1 0, filter 0 on each row.
        byte[] palette = Png.Encode(2, 2, 3, [0, 0, 1, 0, 1, 0], [0, 0, 0, 255, 255, 255]);
        Png decoded = Png.Read(palette);
        Assert.Equal((2, 2), (decoded.Width, decoded.Height));
        Assert.Equal([0.0, 1.0, 1.0, 0.0], new[] { decoded.Luma(0, 0), decoded.Luma(1, 0), decoded.Luma(0, 1), decoded.Luma(1, 1) }.Select(static l => Math.Round(l, 6)));
        Assert.True(decoded.IsOpaque);

        // RGB with the Sub filter (1) on the second row: (10, 20, 30) then (+5, +5, +5).
        byte[] rgb = Png.Encode(2, 2, 2, [0, 0, 0, 0, 0, 0, 0, 1, 10, 20, 30, 5, 5, 5], null);
        Png sub = Png.Read(rgb);
        Assert.Equal(Png.LumaOf(15, 25, 35), sub.Luma(1, 1), 6);
    }

    private static string Readme() => File.ReadAllText(Path.Combine(Root, "README.md"));

    /// <summary>The local images the README shows: Markdown <c>![alt](path)</c> and HTML <c>&lt;img&gt;</c>.</summary>
    private static List<(string Path, string Alt)> Images()
    {
        string readme = Readme();
        List<(string, string)> images = [];

        foreach (Match match in MarkdownImagePattern().Matches(readme))
        {
            images.Add((match.Groups[2].Value, match.Groups[1].Value));
        }

        foreach (Match tag in ImgTagPattern().Matches(readme))
        {
            string src = AttributePattern("src").Match(tag.Value).Groups[1].Value;
            string alt = AttributePattern("alt").Match(tag.Value).Groups[1].Value;
            images.Add((src, alt));
        }

        return [.. images.Where(static image => !image.Item1.StartsWith("http", StringComparison.Ordinal))];
    }

    private static Regex AttributePattern(string name) => new($"""\b{name}="([^"]*)" """.TrimEnd(), RegexOptions.CultureInvariant);

    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)\s]+)\)")]
    private static partial Regex MarkdownImagePattern();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.Singleline)]
    private static partial Regex ImgTagPattern();

    [GeneratedRegex("""<a href="(docs/[^"]+)">""")]
    private static partial Regex LinkPattern();

    [GeneratedRegex("<a href=\"([^\"]+)\"><img src=\"([^\"]+)\"")]
    private static partial Regex LinkedImagePattern();

    /// <summary>Fenced code blocks and inline code spans, which hold no links.</summary>
    [GeneratedRegex(@"```.*?```|`[^`\n]*`", RegexOptions.Singleline)]
    private static partial Regex CodePattern();

    /// <summary>The target of a Markdown link or image, <c>](target)</c>, or of an HTML <c>href</c> or <c>src</c>.</summary>
    [GeneratedRegex(@"\]\(([^)\s]+)\)|\b(?:href|src)=""([^""]+)""")]
    private static partial Regex RelativeTargetPattern();

    private static string? FindRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SharpCompute.slnx")))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// Just enough PNG for these checks: 8-bit RGB, RGBA and palette images, not interlaced, which
    /// is what ffmpeg and pngquant write.
    /// </summary>
    private sealed class Png
    {
        private readonly byte[] _rgb;

        private Png(int width, int height, byte[] rgb, bool opaque) => (Width, Height, _rgb, IsOpaque) = (width, height, rgb, opaque);

        public int Width { get; }

        public int Height { get; }

        public bool IsOpaque { get; }

        public static double LumaOf(byte r, byte g, byte b) => ((0.2126 * r) + (0.7152 * g) + (0.0722 * b)) / 255.0;

        public double Luma(int x, int y)
        {
            int i = ((y * Width) + x) * 3;
            return LumaOf(_rgb[i], _rgb[i + 1], _rgb[i + 2]);
        }

        public static Png Read(string path) => Read(File.ReadAllBytes(path));

        public static Png Read(byte[] file)
        {
            Assert.True(file.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }), "not a PNG");

            int width = 0, height = 0, colorType = 0;
            byte[] palette = [];
            byte[]? transparency = null;
            using MemoryStream compressed = new();

            for (int offset = 8; offset < file.Length;)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(offset));
                string type = System.Text.Encoding.ASCII.GetString(file, offset + 4, 4);
                ReadOnlySpan<byte> data = file.AsSpan(offset + 8, length);

                switch (type)
                {
                    case "IHDR":
                        (width, height) = (BinaryPrimitives.ReadInt32BigEndian(data), BinaryPrimitives.ReadInt32BigEndian(data[4..]));
                        Assert.Equal(8, data[8]);    // bit depth
                        colorType = data[9];
                        Assert.Equal(0, data[12]);   // not interlaced
                        break;
                    case "PLTE":
                        palette = data.ToArray();
                        break;
                    case "tRNS":
                        transparency = data.ToArray();
                        break;
                    case "IDAT":
                        compressed.Write(data);
                        break;
                }

                offset += 12 + length;
            }

            int channels = colorType switch { 2 => 3, 3 => 1, 6 => 4, _ => throw new NotSupportedException($"PNG color type {colorType}") };
            int stride = width * channels;
            byte[] raw = new byte[height * stride];
            compressed.Position = 0;

            using (ZLibStream zlib = new(compressed, CompressionMode.Decompress))
            {
                byte[] previous = new byte[stride], row = new byte[stride];

                for (int y = 0; y < height; y++)
                {
                    int filter = zlib.ReadByte();
                    zlib.ReadExactly(row);

                    for (int i = 0; i < stride; i++)
                    {
                        int left = i >= channels ? row[i - channels] : 0, up = previous[i], upLeft = i >= channels ? previous[i - channels] : 0;
                        row[i] = (byte)(row[i] + filter switch
                        {
                            0 => 0,
                            1 => left,
                            2 => up,
                            3 => (left + up) / 2,
                            4 => Paeth(left, up, upLeft),
                            _ => throw new InvalidDataException($"PNG filter {filter}"),
                        });
                    }

                    row.CopyTo(raw, y * stride);
                    (previous, row) = (row, previous);
                }
            }

            byte[] rgb = new byte[width * height * 3];
            bool opaque = true;

            for (int p = 0; p < width * height; p++)
            {
                switch (colorType)
                {
                    case 3:
                        int index = raw[p];
                        palette.AsSpan(index * 3, 3).CopyTo(rgb.AsSpan(p * 3));
                        opaque &= transparency is null || index >= transparency.Length || transparency[index] == 255;
                        break;
                    case 2:
                        raw.AsSpan(p * 3, 3).CopyTo(rgb.AsSpan(p * 3));
                        opaque &= transparency is null;
                        break;
                    default:
                        raw.AsSpan(p * 4, 3).CopyTo(rgb.AsSpan(p * 3));
                        opaque &= raw[(p * 4) + 3] == 255;
                        break;
                }
            }

            return new Png(width, height, rgb, opaque);
        }

        /// <summary>A PNG of already filtered scanlines (each starting with its filter byte), for the decoder's own test.</summary>
        public static byte[] Encode(int width, int height, byte colorType, byte[] scanlines, byte[]? palette)
        {
            using MemoryStream file = new();
            file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

            void Chunk(string type, ReadOnlySpan<byte> data)
            {
                Span<byte> length = stackalloc byte[4];
                BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
                file.Write(length);
                file.Write(System.Text.Encoding.ASCII.GetBytes(type));
                file.Write(data);
                file.Write(stackalloc byte[4]); // the CRC: not checked by Read
            }

            byte[] header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header, width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
            (header[8], header[9]) = (8, colorType);
            Chunk("IHDR", header);

            if (palette is not null)
            {
                Chunk("PLTE", palette);
            }

            using MemoryStream compressed = new();

            using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(scanlines);
            }

            Chunk("IDAT", compressed.ToArray());
            Chunk("IEND", []);
            return file.ToArray();
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }
}
