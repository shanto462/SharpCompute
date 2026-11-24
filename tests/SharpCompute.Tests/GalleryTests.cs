using System;
using System.Linq;
using SharpCompute.Samples.Shaders;

namespace SharpCompute.Tests;

public class GalleryTests
{
    [Fact]
    public void Registry_HasUniqueNamesAndSourceMetadata()
    {
        Assert.NotEmpty(Gallery.All);
        Assert.Equal(Gallery.All.Count, Gallery.All.Select(static s => s.Name.ToUpperInvariant()).Distinct().Count());

        Assert.All(Gallery.All, static s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.DoesNotContain(s.Name, static c => char.IsWhiteSpace(c) || c is '/' or '\\');
            Assert.False(string.IsNullOrWhiteSpace(s.Author));
            // Shadertoy ports must link their original; original SharpCompute samples link the repo.
            Assert.True(
                s.SourceUrl.StartsWith("https://www.shadertoy.com/", StringComparison.Ordinal) ||
                (s.Author == "SharpCompute" && s.SourceUrl.StartsWith("https://github.com/", StringComparison.Ordinal)),
                $"{s.Name}: unexpected source URL '{s.SourceUrl}'");
        });
    }

    [MetalFact]
    public void EveryShader_Renders256x144()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(256, 144);

        foreach (GalleryShader shader in Gallery.All)
        {
            shader.Render(device, texture, 1.5f);

            Rgba32[] pixels = texture.ToArray();

            // Not left blank, and not a single flat color.
            Assert.Contains(pixels, static p => p.A != 0);
            Assert.True(pixels.Distinct().Count() > 1, $"{shader.Name} rendered a uniform image.");
        }
    }

    /// <summary>
    /// End to end: the generated HelloWorld pixel shader (C# → MSL by the source generator), dispatched
    /// with <c>ForEach</c>, against a CPU evaluation of the same formula.
    /// </summary>
    [MetalTheory]
    [InlineData(0f)]
    [InlineData(1.5f)]
    [InlineData(100.25f)]
    public void HelloWorld_MatchesTheCpuReference(float time)
    {
        const int width = 1000, height = 563;

        GraphicsDevice device = GraphicsDevice.GetDefault();
        using ReadWriteTexture2D<Rgba32> texture = device.AllocateReadWriteTexture2D<Rgba32>(width, height);

        device.ForEach(texture, new HelloWorld(time));
        Rgba32[] pixels = texture.ToArray();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // ThreadIds.Normalized.XY is inclusive: tid / max(1, grid - 1), so the last pixel is 1.
                float u = (float)x / Math.Max(1, width - 1), v = (float)y / Math.Max(1, height - 1);
                Rgba32 actual = pixels[y * width + x];

                // ±1: cos is within a few ULP of the CPU (precise math), which can flip the rounding
                // of a channel that lands near a .5 boundary of the unorm conversion.
                AssertChannel(Channel(time + u + 0f), actual.R, x, y);
                AssertChannel(Channel(time + v + 2f), actual.G, x, y);
                AssertChannel(Channel(time + u + 4f), actual.B, x, y);
                Assert.Equal(255, actual.A);
            }
        }

        static float Channel(float angle) => 0.5f + 0.5f * MathF.Cos(angle);

        static void AssertChannel(float expected, byte actual, int x, int y)
        {
            float scaled = Math.Clamp(expected, 0f, 1f) * 255f;

            if (Math.Abs(scaled - actual) > 1f)
            {
                Assert.Fail($"Pixel ({x}, {y}): channel is {actual}, expected {scaled:F3}.");
            }
        }
    }

    [Fact]
    public void HelloWorld_IsGenerated()
    {
        // The generated descriptor, not a hand-written one: the MSL is the translation of Execute.
        Assert.Contains("__output.write(float4(col, 1.0f), __tid.xy);", Descriptor<HelloWorld>.MslSource);
        Assert.Contains("float2 uv = float2(__tid.xy) / float2(metal::max(int2(1), int2(__grid.xy) - 1));", Descriptor<HelloWorld>.MslSource);
    }

    private static class Descriptor<T>
        where T : struct, IComputeShaderDescriptor<T>
    {
        public static string MslSource => T.MslSource;
    }
}
