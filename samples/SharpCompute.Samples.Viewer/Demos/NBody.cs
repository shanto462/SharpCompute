// Original SharpCompute sample (MIT).
using System;
using System.Runtime.InteropServices;
using SharpCompute.Presentation;

namespace SharpCompute.Samples.Viewer.Demos;

/// <summary>
/// One O(n²) gravity step with semi-implicit Euler integration (G = 1, Plummer softening).
/// Positions are <c>xyz</c> plus the mass in <c>w</c>; velocities use <c>xyz</c>. Reads
/// <c>positions</c>, writes <c>nextPositions</c>; each thread updates its own velocity in place.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct NBodyStep(
    ReadWriteBuffer<Float4> positions, ReadWriteBuffer<Float4> velocities, ReadWriteBuffer<Float4> nextPositions,
    int count, float dt, float softening2) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        Float4 self = positions[i];
        Float3 p = self.XYZ;
        Float3 acceleration = new(0f, 0f, 0f);

        // The j == i term contributes nothing: d is zero and the softening keeps r² positive.
        for (int j = 0; j < count; j++)
        {
            Float4 other = positions[j];
            Float3 d = other.XYZ - p;
            float inverse = Gpu.Rsqrt(Gpu.Dot(d, d) + softening2);
            acceleration += d * (other.W * inverse * inverse * inverse);
        }

        Float3 velocity = velocities[i].XYZ + (acceleration * dt);
        velocities[i] = new Float4(velocity, 0f);
        nextPositions[i] = new Float4(p + (velocity * dt), self.W);
    }
}

/// <summary>
/// Projects each body orthographically through a yaw/pitch camera and adds <c>weight</c> to the
/// accumulation pixel it lands on.
/// </summary>
/// <remarks>
/// The add is a plain read-modify-write, not an atomic (the generator has no atomics yet). When
/// two bodies hit the same pixel in the same pass, one of the adds can be lost, so dense pixels
/// read slightly dimmer than they should. For a visualization that's an acceptable trade.
/// </remarks>
[GeneratedComputeShaderDescriptor]
public readonly partial struct NBodySplat(
    ReadWriteBuffer<Float4> positions, ReadWriteBuffer<uint> accumulation, int width, int height,
    float yaw, float pitch, float zoom, uint weight) : IComputeShader
{
    public void Execute()
    {
        Float3 p = positions[ThreadIds.X].XYZ;

        float cy = Gpu.Cos(yaw);
        float sy = Gpu.Sin(yaw);
        float x = (cy * p.X) + (sy * p.Z);
        float z = (cy * p.Z) - (sy * p.X);

        float cp = Gpu.Cos(pitch);
        float sp = Gpu.Sin(pitch);
        float y = (cp * p.Y) - (sp * z);

        float scale = zoom * height;
        int px = (int)Gpu.Floor((width * 0.5f) + (x * scale));
        int py = (int)Gpu.Floor((height * 0.5f) - (y * scale));

        if (px >= 0 && px < width && py >= 0 && py < height)
        {
            accumulation[(py * width) + px] += weight;
        }
    }
}

/// <summary>
/// Turns accumulated brightness into color (faint regions blue, dense cores white) and decays the
/// accumulation by <c>decay / 256</c>, which leaves short motion trails.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct NBodyTonemap(ReadWriteBuffer<uint> accumulation, ReadWriteBuffer<uint> pixels, float exposure, uint decay) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        uint value = accumulation[i];
        float v = value * exposure;

        Float3 color = new Float3(1f, 1f, 1f) - Gpu.Exp(new Float3(-0.45f, -0.7f, -1.1f) * v);
        color += new Float3(0.01f, 0.012f, 0.025f);

        UInt3 c = (UInt3)((Gpu.Saturate(color) * 255f) + new Float3(0.5f, 0.5f, 0.5f));
        pixels[i] = c.X | (c.Y << 8) | (c.Z << 16) | (255u << 24);
        accumulation[i] = (value >> 8) * decay;
    }
}

/// <summary>
/// The N-body simulation state: positions (ping-pong) and velocities on the GPU, initialized as two
/// disk galaxies on a collision course. Each galaxy is a heavy core body plus a disk whose bodies
/// start on circular orbits around the mass inside their radius.
/// </summary>
public sealed class NBodySimulation : IDisposable
{
    public const float Softening = 0.025f;

    private readonly GraphicsDevice _device;
    private ReadWriteBuffer<Float4> _positions;
    private ReadWriteBuffer<Float4> _nextPositions;

    public NBodySimulation(GraphicsDevice device, int count, int seed = 7)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 4);

        _device = device;
        Count = count;

        (Float4[] positions, Float4[] velocities) = CreateGalaxies(count, seed);
        _positions = device.AllocateReadWriteBuffer<Float4>(positions);
        _nextPositions = device.AllocateReadWriteBuffer<Float4>(count);
        Velocities = device.AllocateReadWriteBuffer<Float4>(velocities);
    }

    public int Count { get; }

    /// <summary>Current positions: <c>xyz</c> and mass in <c>w</c>.</summary>
    public ReadWriteBuffer<Float4> Positions => _positions;

    public ReadWriteBuffer<Float4> Velocities { get; }

    public void Step(float dt)
    {
        _device.For(Count, new NBodyStep(_positions, Velocities, _nextPositions, Count, dt, Softening * Softening));
        (_positions, _nextPositions) = (_nextPositions, _positions);
    }

    public void Dispose()
    {
        _positions.Dispose();
        _nextPositions.Dispose();
        Velocities.Dispose();
    }

    internal static (Float4[] Positions, Float4[] Velocities) CreateGalaxies(int count, int seed)
    {
        Random random = new(seed);
        Float4[] positions = new Float4[count];
        Float4[] velocities = new Float4[count];

        int first = count / 2;
        AddGalaxy(positions, velocities, 0, first, new Float3(-0.55f, 0.12f, 0f), new Float3(0.16f, -0.02f, 0f), tilt: 0.35f, spin: 1f, random);
        AddGalaxy(positions, velocities, first, count - first, new Float3(0.55f, -0.12f, 0f), new Float3(-0.16f, 0.02f, 0f), tilt: -0.9f, spin: -1f, random);

        return (positions, velocities);
    }

    private static void AddGalaxy(
        Float4[] positions, Float4[] velocities, int start, int count, Float3 center, Float3 drift, float tilt, float spin, Random random)
    {
        const float coreMass = 0.6f;
        const float diskMass = 0.4f;
        const float innerRadius = 0.06f;
        const float outerRadius = 0.45f;

        positions[start] = new Float4(center, coreMass);
        velocities[start] = new Float4(drift, 0f);

        int disk = count - 1;
        float bodyMass = diskMass / disk;

        // Radii sorted ascending, so the mass inside body k's orbit is the core plus k disk bodies.
        float[] radii = new float[disk];

        for (int k = 0; k < disk; k++)
        {
            // Denser toward the center: an exponential-ish profile, cut to [inner, outer].
            float u = (float)random.NextDouble();
            radii[k] = innerRadius + ((outerRadius - innerRadius) * (1f - MathF.Sqrt(1f - u)) * (0.4f + (0.6f * u)));
        }

        Array.Sort(radii);

        float cosTilt = MathF.Cos(tilt);
        float sinTilt = MathF.Sin(tilt);

        for (int k = 0; k < disk; k++)
        {
            float r = radii[k];
            float angle = (float)(random.NextDouble() * Math.Tau);
            float thickness = ((float)random.NextDouble() - 0.5f) * 0.02f;

            float enclosed = coreMass + (bodyMass * k);
            float softened = (r * r) + (Softening * Softening);
            float speed = MathF.Sqrt(enclosed * r * r / (softened * MathF.Sqrt(softened)));

            // In the disk's own plane (x, z = 0 → y is "up" before the tilt).
            Float3 local = new(r * MathF.Cos(angle), r * MathF.Sin(angle), thickness);
            Float3 localVelocity = new(-MathF.Sin(angle) * speed * spin, MathF.Cos(angle) * speed * spin, 0f);

            positions[start + 1 + k] = new Float4(center + Tilt(local, cosTilt, sinTilt), bodyMass);
            velocities[start + 1 + k] = new Float4(drift + Tilt(localVelocity, cosTilt, sinTilt), 0f);
        }
    }

    // Rotates about the x axis.
    private static Float3 Tilt(Float3 v, float cos, float sin) => new(v.X, (cos * v.Y) - (sin * v.Z), (sin * v.Y) + (cos * v.Z));
}

/// <summary>
/// The live N-body demo: 16,384 bodies, every pair interacting each frame. Drag to orbit the
/// camera (it drifts slowly on its own otherwise) and scroll to zoom.
/// </summary>
public sealed class NBodyDemo : IDemo
{
    public const int BodyCount = 16_384;

    private const float TimeStep = 0.0025f;
    public const float DefaultZoom = 0.6f;
    public const float MinZoom = 0.15f;
    public const float MaxZoom = 6f;

    /// <summary>Zoom factor per wheel notch.</summary>
    public const float ZoomStep = 1.1f;

    private const uint SplatWeight = 256;
    private const uint Decay = 190;

    private NBodySimulation? _simulation;
    private ReadWriteBuffer<uint>? _accumulation;
    private ReadWriteBuffer<uint>? _pixels;
    private float _yaw;
    private float _pitch = 0.5f;
    private float _zoom = DefaultZoom;
    private double _dragX;
    private double _dragY;
    private bool _dragging;

    public string Name => "NBody";

    public string Author => "SharpCompute";

    public string Controls => "drag: orbit the camera, wheel: zoom, R: restart";

    /// <summary>World units to frame heights: at 1, a distance of 1 spans the frame's height.</summary>
    public float Zoom => _zoom;

    public void Render(GraphicsDevice device, ReadWriteTexture2D<Rgba32> target, DemoFrame frame)
    {
        _simulation ??= new NBodySimulation(device, BodyCount);

        if (!frame.Paused)
        {
            _simulation.Step(TimeStep);

            if (!_dragging)
            {
                _yaw += frame.DeltaTime * 0.08f;
            }
        }

        int count = target.Width * target.Height;

        if (_pixels is null || _pixels.Length != count)
        {
            _pixels?.Dispose();
            _accumulation?.Dispose();
            _pixels = device.AllocateReadWriteBuffer<uint>(count);
            _accumulation = device.AllocateReadWriteBuffer<uint>(count);
        }

        device.For(BodyCount, new NBodySplat(_simulation.Positions, _accumulation!, target.Width, target.Height, _yaw, _pitch, _zoom, SplatWeight));

        // A pixel hit once per frame settles near weight / (1 - decay/256) ≈ 4 × weight. Larger
        // targets spread the same bodies over more pixels, so exposure grows with the height.
        float exposure = 1f / (SplatWeight * 1.5f) * (target.Height / 720f);
        device.For(count, new NBodyTonemap(_accumulation!, _pixels, exposure, Decay));
        target.CopyFrom(MemoryMarshal.Cast<uint, Rgba32>(_pixels.AsSpan()));
    }

    public void OnKeyDown(Key key)
    {
        if (key == Key.R && _simulation is not null)
        {
            _simulation.Dispose();
            _simulation = null;
            _accumulation?.AsSpan().Clear();
        }
    }

    public void OnMouse(MouseAction action, MouseButton button, double x, double y, int targetWidth, int targetHeight)
    {
        switch (action)
        {
            case MouseAction.Down:
                _dragging = true;
                break;
            case MouseAction.Drag:
                _yaw += (float)((x - _dragX) / targetHeight * 3.0);
                _pitch = Math.Clamp(_pitch + (float)((y - _dragY) / targetHeight * 3.0), -1.5f, 1.5f);
                break;
            case MouseAction.Up:
                _dragging = false;
                break;
        }

        (_dragX, _dragY) = (x, y);
    }

    public void OnWheel(float steps)
    {
        if (float.IsFinite(steps))
        {
            _zoom = Math.Clamp(_zoom * MathF.Pow(ZoomStep, steps), MinZoom, MaxZoom);
        }
    }

    public void Dispose()
    {
        _simulation?.Dispose();
        _accumulation?.Dispose();
        _pixels?.Dispose();
    }
}
