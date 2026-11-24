using System;
using System.Collections.Generic;

namespace SharpCompute.Samples.Shaders;

/// <summary>One entry in the shader gallery.</summary>
/// <param name="Name">Short, unique name used on the command line (e.g. <c>HelloWorld</c>).</param>
/// <param name="Author">Author of the original Shadertoy shader.</param>
/// <param name="SourceUrl">URL of the original shader.</param>
/// <param name="Render">Renders one frame at the given time (seconds) into the texture, synchronously.</param>
public sealed record GalleryShader(string Name, string Author, string SourceUrl, Action<GraphicsDevice, ReadWriteTexture2D<Rgba32>, float> Render);

/// <summary>The registry of gallery shaders. Each port adds one entry here.</summary>
public static class Gallery
{
    /// <summary>Every registered shader, in display order.</summary>
    public static IReadOnlyList<GalleryShader> All { get; } =
    [
        new(
            "HelloWorld",
            HelloWorld.Author,
            HelloWorld.SourceUrl,
            static (device, texture, time) => device.ForEach(texture, new HelloWorld(time))),

        new(
            "ColorfulInfinity",
            ColorfulInfinity.Author,
            ColorfulInfinity.SourceUrl,
            static (device, texture, time) => device.ForEach(texture, new ColorfulInfinity(time))),

        new(
            "FourColorGradient",
            FourColorGradient.Author,
            FourColorGradient.SourceUrl,
            static (device, texture, time) => device.ForEach(texture, new FourColorGradient(time))),

        new(
            "FractalTiling",
            FractalTiling.Author,
            FractalTiling.SourceUrl,
            static (device, texture, time) => device.ForEach(texture, new FractalTiling(time))),

        new(
            "Octagrams",
            Octagrams.Author,
            Octagrams.SourceUrl,
            static (device, texture, time) => device.ForEach(texture, new Octagrams(time))),

        new(
            "TwoTiledTruchet",
            TwoTiledTruchet.Author,
            TwoTiledTruchet.SourceUrl,
            static (device, texture, time) => device.ForEach(texture, new TwoTiledTruchet(time))),

        new(
            "MengerJourney",
            MengerJourney.Author,
            MengerJourney.SourceUrl,
            static (device, texture, time) => device.ForEach(texture, new MengerJourney(time))),

        new(
            "ProteanClouds",
            ProteanClouds.Author,
            ProteanClouds.SourceUrl,
            static (device, texture, time) => device.ForEach(texture, new ProteanClouds(time))),

        new(
            "TerracedHills",
            TerracedHills.Author,
            TerracedHills.SourceUrl,
            static (device, texture, time) => device.ForEach(texture, new TerracedHills(time))),

        new(
            "PyramidPattern",
            PyramidPattern.Author,
            PyramidPattern.SourceUrl,
            static (device, texture, time) => device.ForEach(texture, new PyramidPattern(time))),

        new(
            "ExtrudedTruchetPattern",
            ExtrudedTruchetPattern.Author,
            ExtrudedTruchetPattern.SourceUrl,
            static (device, texture, time) => device.ForEach(texture, new ExtrudedTruchetPattern(time))),

        new(
            "TriangleGridContouring",
            TriangleGridContouring.Author,
            TriangleGridContouring.SourceUrl,
            static (device, texture, time) => device.ForEach(texture, new TriangleGridContouring(time))),

        // Original SharpCompute sample (MIT): a compute shader, not a pixel shader.
        new("Mandelbrot", "SharpCompute", "https://github.com/shanto462/SharpCompute", MandelbrotZoom.Render),
    ];
}
