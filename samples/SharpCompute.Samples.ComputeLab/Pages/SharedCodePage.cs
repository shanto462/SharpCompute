// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using static System.FormattableString;

namespace SharpCompute.Samples.ComputeLab.Pages;

/// <summary>
/// Code shared between shaders: a small path-traced scene whose shaders are built from code that is
/// not theirs. <see cref="ShaderMath"/> is a static class two shaders call; <see cref="Ray"/>,
/// <see cref="Sphere"/>, <see cref="Camera"/> and <see cref="RandomStream"/> are structs with
/// constructors and methods, called on locals, on elements of a buffer and on captured state.
/// </summary>
/// <remarks>
/// The check traces every <see cref="SampleStride"/>th pixel on the CPU, with <c>ForCpu</c> of the
/// same shader, and compares it with the GPU's pixel. A path is a chain of float computations with
/// branches (hit or miss, lit or shadowed), so a last-bit difference between the GPU and the CPU
/// can send a path the other way where a ray grazes an edge: the check asks for almost all sampled
/// pixels to agree closely, not for all of them.
/// </remarks>
public sealed class SharedCodePage : ILabPage
{
    /// <summary>The traced image.</summary>
    public const int ImageWidth = 768;
    public const int ImageHeight = 512;

    /// <summary>Paths traced per pixel.</summary>
    public const int Paths = 16;

    /// <summary>The CPU check traces every this-many-th pixel along each axis.</summary>
    public const int SampleStride = 8;

    public const int SampleWidth = ImageWidth / SampleStride;
    public const int SampleHeight = ImageHeight / SampleStride;

    /// <summary>How far apart a sampled pixel's GPU and CPU display colors may be to count as agreeing.</summary>
    public const float Tolerance = 2f / 255f;

    /// <summary>The part of the sampled pixels that must agree.</summary>
    public const double RequiredAgreement = 0.97;

    private static readonly string[] s_variants = ["16 paths per pixel"];

    private readonly GraphicsDevice _device;
    private readonly ReadWriteBuffer<Sphere> _spheres;
    private readonly ReadWriteBuffer<Float3> _light;
    private readonly ReadWriteBuffer<Float3> _cpuSample;
    private Camera _camera;

    public SharedCodePage(GraphicsDevice device)
    {
        _device = device;
        _spheres = device.AllocateReadWriteBuffer<Sphere>(SceneSpheres);
        _light = device.AllocateReadWriteBuffer<Float3>(ImageWidth * ImageHeight);
        _cpuSample = device.AllocateReadWriteBuffer<Float3>(SampleWidth * SampleHeight);
        device.Prepare(ShaderReference.Of<TraceScene>(), ShaderReference.Of<DrawSharedCode>());
    }

    /// <summary>The number of spheres in the scene.</summary>
    public const int SceneSpheres = 6;

    public string Name => "Shared code";

    public string Feature => "static helpers and struct methods";

    public string Topic => LabTopics.CodeReuse;

    /// <summary>The CPU time is extrapolated: <c>ForCpu</c> traces one pixel in <see cref="SampleStride"/>² and the time is scaled up.</summary>
    public string CpuNote => "all cores, scaled from every 8th pixel";

    public string Help => string.Empty;

    public IReadOnlyList<string> Variants => s_variants;

    public int Variant { get; set; }

    public double GpuMilliseconds { get; private set; }

    /// <summary>How many of the sampled pixels agreed with the CPU at the last <see cref="Check"/>.</summary>
    public int Agreeing { get; private set; }

    /// <summary>The GPU's traced image: light per pixel, before tone mapping.</summary>
    public ReadOnlySpan<Float3> Light => _light.AsReadOnlySpan();

    public void Compute(LabFrame frame)
    {
        // The CPU builds the scene with the same structs the shader uses: their constructors are
        // ordinary C# here and MSL functions there.
        Span<Sphere> spheres = _spheres.AsSpan();
        float bob = 0.25f + (0.2f * MathF.Sin(frame.Time * 1.3f));

        spheres[0] = new Sphere(new Float3(0f, 1f, 0f), 1f, new Float3(0.85f, 0.3f, 0.25f), mirror: false);
        spheres[1] = new Sphere(new Float3(-2.25f, 1f, 0.4f), 1f, new Float3(0.92f, 0.92f, 0.95f), mirror: true);
        spheres[2] = new Sphere(new Float3(2.1f, 0.8f, 0.1f), 0.8f, new Float3(0.25f, 0.45f, 0.85f), mirror: false);
        spheres[3] = new Sphere(new Float3(1f, 0.35f, -1.7f), 0.35f, new Float3(0.95f, 0.75f, 0.3f), mirror: true);
        spheres[4] = new Sphere(new Float3(-1.1f, 0.3f, -1.9f), 0.3f, new Float3(0.3f, 0.8f, 0.4f), mirror: false);
        spheres[5] = new Sphere(new Float3(-0.1f, 0.25f + bob, -2.4f), 0.25f, new Float3(0.9f, 0.9f, 0.9f), mirror: false);

        float angle = 0.35f * MathF.Sin(frame.Time * 0.4f);
        _camera = new Camera(
            new Float3(MathF.Sin(angle) * 7.5f, 2.3f, -MathF.Cos(angle) * 7.5f),
            new Float3(0f, 0.75f, 0f),
            verticalFov: 0.62f,
            aspect: (float)ImageWidth / ImageHeight);

        long start = Stopwatch.GetTimestamp();
        _device.For(ImageWidth, ImageHeight, new TraceScene(_light, _spheres, SceneSpheres, _camera, ImageWidth, ImageHeight, 1, Paths));
        GpuMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    public LabCheck Check()
    {
        // The same shader on the CPU, over every SampleStride-th pixel: thread (x, y) traces pixel
        // (x, y) * stride with the random numbers that pixel has on the GPU.
        long start = Stopwatch.GetTimestamp();
        _device.ForCpu(SampleWidth, SampleHeight, new TraceScene(_cpuSample, _spheres, SceneSpheres, _camera, ImageWidth, ImageHeight, SampleStride, Paths));
        double cpuMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        ReadOnlySpan<Float3> gpu = _light.AsReadOnlySpan(), cpu = _cpuSample.AsReadOnlySpan();
        int agreeing = 0;
        float largest = 0f;
        bool finite = true;

        for (int y = 0; y < SampleHeight; y++)
        {
            for (int x = 0; x < SampleWidth; x++)
            {
                // Compared as the colors that are shown, with the shader's own tone mapping run here.
                Float3 onGpu = ShaderMath.Tonemap(gpu[(y * SampleStride * ImageWidth) + (x * SampleStride)]);
                Float3 onCpu = ShaderMath.Tonemap(cpu[(y * SampleWidth) + x]);
                Float3 difference = Gpu.Abs(onGpu - onCpu);
                float worst = MathF.Max(difference.X, MathF.Max(difference.Y, difference.Z));

                finite &= float.IsFinite(worst);
                largest = MathF.Max(largest, worst);
                agreeing += worst <= Tolerance ? 1 : 0;
            }
        }

        Agreeing = agreeing;

        int sampled = SampleWidth * SampleHeight;
        bool passed = finite && agreeing >= RequiredAgreement * sampled;
        string summary = Invariant($"{agreeing:N0} of {sampled:N0} sampled pixels within 2/255 of ForCpu (worst {largest * 255f:F0}/255, on an edge)");

        // The CPU traced one pixel in SampleStride^2, evenly spread, so the whole image costs that many times more.
        return new LabCheck(passed, summary, cpuMs * SampleStride * SampleStride);
    }

    public void Draw(LabCanvas canvas, LabFrame frame)
    {
        _device.For(LabCanvas.Width, LabCanvas.Height, new DrawSharedCode(canvas.Pixels, _light, _cpuSample, LabCanvas.Width));

        int column = DrawSharedCode.SampleX;
        int row = DrawSharedCode.PanelY;

        canvas.Label(DrawSharedCode.PanelX, DrawSharedCode.PanelY - 24, Invariant($"{ImageWidth} x {ImageHeight}, {Paths} paths per pixel, {TraceScene.Bounces} bounces: one dispatch"));
        canvas.Label(column, row, "static class ShaderMath:", TextColor.Accent);
        canvas.Label(column, row + 16, " Hash  Sky  Tonemap  Pick<T>");
        canvas.Label(column, row + 48, "struct Ray:", TextColor.Accent);
        canvas.Label(column, row + 64, " new Ray(origin, direction)  At(t)");
        canvas.Label(column, row + 96, "struct Sphere:", TextColor.Accent);
        canvas.Label(column, row + 112, " spheres[i].Intersect(ray)");
        canvas.Label(column, row + 144, "struct Camera:", TextColor.Accent);
        canvas.Label(column, row + 160, " camera.RayThrough(uv)");
        canvas.Label(column, row + 192, "struct RandomStream:", TextColor.Accent);
        canvas.Label(column, row + 208, " random.Next() modifies a local");
        canvas.Label(column, DrawSharedCode.SampleY - 24, Invariant($"ForCpu, every {SampleStride}th pixel: {SampleWidth} x {SampleHeight}"));
        canvas.Label(column, DrawSharedCode.SampleY + (SampleHeight * DrawSharedCode.SampleScale) + 8, "the same C#, the same picture", TextColor.Cool);
    }

    public void Dispose()
    {
        _spheres.Dispose();
        _light.Dispose();
        _cpuSample.Dispose();
    }
}

/// <summary>A ray: where it starts and which way it goes (a unit vector).</summary>
public struct Ray
{
    public Float3 Origin;
    public Float3 Direction;

    /// <summary>A constructor with parameters is a function that returns the struct in MSL.</summary>
    public Ray(Float3 origin, Float3 direction)
    {
        Origin = origin;
        Direction = Gpu.Normalize(direction);
    }

    /// <summary>The point <paramref name="distance"/> along the ray.</summary>
    public readonly Float3 At(float distance) => Origin + (Direction * distance);
}

/// <summary>A sphere of the scene, as it is stored in the scene's buffer (32 bytes).</summary>
public struct Sphere
{
    public Float3 Center;
    public float Radius;
    public Float3 Albedo;

    /// <summary>1 for a mirror, 0 for a matte surface (an <c>int</c>: <c>bool</c> has no layout a buffer can hold).</summary>
    public int Mirror;

    public Sphere(Float3 center, float radius, Float3 albedo, bool mirror)
    {
        Center = center;
        Radius = radius;
        Albedo = albedo;
        Mirror = mirror ? 1 : 0;
    }

    /// <summary>The distance along <paramref name="ray"/> at which it enters the sphere, or -1 when it misses (or starts inside).</summary>
    public readonly float Intersect(Ray ray)
    {
        Float3 offset = ray.Origin - Center;
        float b = Gpu.Dot(offset, ray.Direction);
        float discriminant = (b * b) - (Gpu.Dot(offset, offset) - (Radius * Radius));

        if (discriminant < 0f)
        {
            return -1f;
        }

        float distance = -b - Gpu.Sqrt(discriminant);
        return distance > 0f ? distance : -1f;
    }

    /// <summary>The outward unit normal at a point on the surface.</summary>
    public readonly Float3 NormalAt(Float3 point) => (point - Center) / Radius;
}

/// <summary>What a ray hit: how far away, and the surface there. <see cref="Distance"/> is negative for "nothing".</summary>
public struct Hit
{
    public float Distance;
    public Float3 Normal;
    public Float3 Albedo;
    public int Mirror;

    /// <summary>A computed property is a function of the struct in MSL.</summary>
    public readonly bool Found => Distance > 0f;
}

/// <summary>A pinhole camera: built once on the CPU, captured by the shader, asked for a ray per path.</summary>
public struct Camera
{
    public Float3 Position;
    public Float3 Forward;

    /// <summary>The image plane's half extents, one unit in front of the camera.</summary>
    public Float3 Right;
    public Float3 Up;

    public Camera(Float3 position, Float3 target, float verticalFov, float aspect)
    {
        Position = position;
        Forward = Gpu.Normalize(target - position);

        Float3 right = Gpu.Normalize(Gpu.Cross(new Float3(0f, 1f, 0f), Forward));
        float halfHeight = Gpu.Tan(verticalFov * 0.5f);

        Right = right * (halfHeight * aspect);
        Up = Gpu.Cross(Forward, right) * halfHeight;
    }

    /// <summary>The ray through a point of the image: (-1, -1) is the bottom-left corner, (1, 1) the top-right.</summary>
    public readonly Ray RayThrough(Float2 point) => new(Position, Forward + (Right * point.X) + (Up * point.Y));
}

/// <summary>
/// A stream of random numbers from a seed. <see cref="Next"/> changes the stream, so in MSL it takes
/// the struct by reference; the state is the primary-constructor parameter, which the struct keeps.
/// </summary>
public struct RandomStream(uint seed)
{
    /// <summary>The next number in 0..1 (exclusive). The same on the GPU and the CPU: integers until the last step.</summary>
    public float Next()
    {
        seed = ShaderMath.Hash(seed + 0x9E3779B9u);
        return (seed >> 8) * (1f / 16777216f);
    }

    /// <summary>A uniformly distributed unit vector.</summary>
    public Float3 NextDirection()
    {
        float z = 1f - (2f * Next());
        float angle = ShaderMath.Tau * Next();
        float radius = Gpu.Sqrt(Gpu.Max(0f, 1f - (z * z)));

        return new Float3(radius * Gpu.Cos(angle), radius * Gpu.Sin(angle), z);
    }
}

/// <summary>
/// The feature kernel: traces <c>paths</c> paths through each pixel and stores their mean light.
/// Thread (x, y) traces pixel (x, y) × <c>stride</c>, so that the CPU check can trace a sample of the
/// pixels with the random numbers those pixels have in the full image.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct TraceScene(
    ReadWriteBuffer<Float3> light, ReadWriteBuffer<Sphere> spheres, int sphereCount, Camera camera, int width, int height, int stride, int paths) : IComputeShader
{
    /// <summary>The longest path, in surfaces hit.</summary>
    public const int Bounces = 4;

    // How far a new ray starts off the surface it leaves, so that it does not hit that surface again.
    private const float Offset = 0.001f;

    public void Execute()
    {
        int x = ThreadIds.X * stride;
        int y = ThreadIds.Y * stride;
        RandomStream random = new((uint)((y * width) + x));
        Float3 sum = default;

        for (int path = 0; path < paths; path++)
        {
            // A random point inside the pixel, which is what smooths the edges.
            float dx = random.Next();
            float dy = random.Next();
            Float2 point = new(((x + dx) / width * 2f) - 1f, 1f - ((y + dy) / height * 2f));

            sum += Trace(camera.RayThrough(point), ref random);
        }

        light[(ThreadIds.Y * DispatchSize.X) + ThreadIds.X] = sum / paths;
    }

    /// <summary>The light that arrives along one path: the sun at every matte surface it visits, and the sky where it leaves the scene.</summary>
    private Float3 Trace(Ray ray, ref RandomStream random)
    {
        Float3 arriving = default;
        Float3 carried = new(1f, 1f, 1f);

        for (int bounce = 0; bounce < Bounces; bounce++)
        {
            Hit hit = Nearest(ray);

            if (!hit.Found)
            {
                return arriving + (carried * ShaderMath.Sky(ray.Direction));
            }

            Float3 point = ray.At(hit.Distance) + (hit.Normal * Offset);
            carried *= hit.Albedo;

            if (hit.Mirror != 0)
            {
                ray = new Ray(point, Gpu.Reflect(ray.Direction, hit.Normal));
                continue;
            }

            // The sun, unless something is in the way; a little spread softens the shadow's edge.
            Ray toSun = new(point, ShaderMath.SunDirection + (random.NextDirection() * 0.03f));
            float facing = Gpu.Dot(hit.Normal, toSun.Direction);

            if (facing > 0f && !Nearest(toSun).Found)
            {
                arriving += carried * ShaderMath.SunColor * (facing * 0.35f);
            }

            // Onwards in a random direction, more likely near the normal (cosine weighted).
            ray = new Ray(point, hit.Normal + random.NextDirection());
        }

        return arriving;
    }

    /// <summary>The nearest surface along a ray: the ground plane (y = 0, a checkerboard) or a sphere.</summary>
    private Hit Nearest(Ray ray)
    {
        Hit hit = default;
        hit.Distance = -1f;

        if (ray.Direction.Y < -0.0001f)
        {
            float distance = -ray.Origin.Y / ray.Direction.Y;

            if (distance > 0f)
            {
                Float3 point = ray.At(distance);
                bool dark = (((int)Gpu.Floor(point.X) + (int)Gpu.Floor(point.Z)) & 1) != 0;

                // The squares fade into their average far away, where they are smaller than a pixel.
                Float3 square = ShaderMath.Pick(dark, new Float3(0.22f, 0.24f, 0.28f), new Float3(0.72f, 0.72f, 0.7f));

                hit.Distance = distance;
                hit.Normal = new Float3(0f, 1f, 0f);
                hit.Albedo = Gpu.Lerp(square, new Float3(0.47f, 0.48f, 0.49f), Gpu.Smoothstep(12f, 45f, distance));
            }
        }

        for (int i = 0; i < sphereCount; i++)
        {
            // A method of the struct, called on an element of the buffer.
            float distance = spheres[i].Intersect(ray);

            if (distance > 0f && (!hit.Found || distance < hit.Distance))
            {
                Sphere sphere = spheres[i];

                hit.Distance = distance;
                hit.Normal = sphere.NormalAt(ray.At(distance));
                hit.Albedo = sphere.Albedo;
                hit.Mirror = sphere.Mirror;
            }
        }

        return hit;
    }
}

/// <summary>
/// Draws the traced image on the left and the CPU's sample of it on the right, enlarged. A second
/// shader that uses <see cref="ShaderMath"/>: it gets its own copy of the functions it calls.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct DrawSharedCode(ReadWriteBuffer<uint> pixels, ReadWriteBuffer<Float3> light, ReadWriteBuffer<Float3> cpuSample, int width) : IComputeShader
{
    public const int PanelX = 40;
    public const int PanelY = 104;
    public const int SampleX = 848;
    public const int SampleY = 392;
    public const int SampleScale = 4;

    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        Float3 color = new Float3(0.055f, 0.065f, 0.085f);
        int ix = x - PanelX;
        int iy = y - PanelY;
        int sx = x - SampleX;
        int sy = y - SampleY;

        if (ix >= 0 && ix < SharedCodePage.ImageWidth && iy >= 0 && iy < SharedCodePage.ImageHeight)
        {
            color = ShaderMath.Tonemap(light[(iy * SharedCodePage.ImageWidth) + ix]);
        }
        else if (sx >= 0 && sx < SharedCodePage.SampleWidth * SampleScale && sy >= 0 && sy < SharedCodePage.SampleHeight * SampleScale)
        {
            color = ShaderMath.Tonemap(cpuSample[((sy / SampleScale) * SharedCodePage.SampleWidth) + (sx / SampleScale)]);
        }

        pixels[(y * width) + x] = Gpu.PackUnorm4x8(new Float4(color, 1f));
    }
}
