// Original SharpCompute sample (MIT).
using System;
using System.Runtime.InteropServices;
using SharpCompute.Presentation;

namespace SharpCompute.Samples.Viewer.Demos;

/// <summary>
/// One generation of Conway's Game of Life (B3/S23) on a toroidal grid. Each cell holds its age:
/// 0 is dead, 1 was just born, and survivors count up to 255. Reads <c>current</c>, writes <c>next</c>.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct LifeStep(ReadWriteBuffer<uint> current, ReadWriteBuffer<uint> next, int width, int height) : IComputeShader
{
    public void Execute()
    {
        int x = ThreadIds.X;
        int y = ThreadIds.Y;
        int neighbors = 0;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                int nx = (x + dx + width) % width;
                int ny = (y + dy + height) % height;

                if (current[(ny * width) + nx] != 0u)
                {
                    neighbors++;
                }
            }
        }

        uint age = current[(y * width) + x];
        uint result = 0u;

        if (age != 0u)
        {
            if (neighbors == 2 || neighbors == 3)
            {
                result = age < 255u ? age + 1u : 255u;
            }
        }
        else if (neighbors == 3)
        {
            result = 1u;
        }

        next[(y * width) + x] = result;
    }
}

/// <summary>Fills the grid with live cells at roughly <c>threshold / 2^32</c> density, from a PCG hash of the cell index and seed.</summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct LifeRandomize(ReadWriteBuffer<uint> cells, uint seed, uint threshold) : IComputeShader
{
    public void Execute()
    {
        int i = ThreadIds.X;
        cells[i] = Hash((uint)i ^ (seed * 0x9E3779B9u)) < threshold ? 1u : 0u;
    }

    private static uint Hash(uint input)
    {
        uint state = (input * 747796405u) + 2891336453u;
        uint word = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
        return (word >> 22) ^ word;
    }
}

/// <summary>
/// Draws the grid into an image of packed RGBA pixels (R in the low byte): newborn cells are mint,
/// old ones fade to blue. When a cell covers at least 4 pixels its top and left edges are darkened
/// so the grid reads as cells.
/// </summary>
[GeneratedComputeShaderDescriptor]
public readonly partial struct LifeRender(
    ReadWriteBuffer<uint> cells, ReadWriteBuffer<uint> pixels, int gridWidth, int gridHeight, int imageWidth, int imageHeight) : IComputeShader
{
    public void Execute()
    {
        int px = ThreadIds.X;
        int py = ThreadIds.Y;

        float fx = (px + 0.5f) * gridWidth / imageWidth;
        float fy = (py + 0.5f) * gridHeight / imageHeight;
        int cx = Gpu.Min((int)fx, gridWidth - 1);
        int cy = Gpu.Min((int)fy, gridHeight - 1);
        uint age = cells[(cy * gridWidth) + cx];

        Float3 color = new(0.035f, 0.04f, 0.07f);

        if (age != 0u)
        {
            float t = Gpu.Saturate((age - 1u) / 48f);
            color = Gpu.Lerp(new Float3(0.65f, 1f, 0.78f), new Float3(0.12f, 0.3f, 0.85f), Gpu.Sqrt(t));
        }

        float cellWidth = (float)imageWidth / gridWidth;
        float cellHeight = (float)imageHeight / gridHeight;

        if (cellWidth >= 4f && cellHeight >= 4f)
        {
            float localX = (fx - cx) * cellWidth;
            float localY = (fy - cy) * cellHeight;

            if (localX < 1f || localY < 1f)
            {
                color *= 0.6f;
            }
        }

        pixels[(py * imageWidth) + px] = PackRgba(color);
    }

    private static uint PackRgba(Float3 color)
    {
        UInt3 c = (UInt3)((Gpu.Saturate(color) * 255f) + new Float3(0.5f, 0.5f, 0.5f));
        return c.X | (c.Y << 8) | (c.Z << 16) | (255u << 24);
    }
}

/// <summary>
/// A Game of Life grid on the GPU: two ping-pong buffers of cell ages. Every operation is
/// synchronous, so the CPU may edit <see cref="Cells"/> (shared memory) between calls.
/// </summary>
public sealed class LifeGrid : IDisposable
{
    private readonly GraphicsDevice _device;
    private ReadWriteBuffer<uint> _current;
    private ReadWriteBuffer<uint> _next;

    public LifeGrid(GraphicsDevice device, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _device = device;
        Width = width;
        Height = height;
        _current = device.AllocateReadWriteBuffer<uint>(width * height);
        _next = device.AllocateReadWriteBuffer<uint>(width * height);
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The current generation, row-major: 0 = dead, otherwise the cell's age (1..255).</summary>
    public ReadWriteBuffer<uint> Cells => _current;

    /// <summary>Advances one generation on the GPU.</summary>
    public void Step()
    {
        _device.For(Width, Height, new LifeStep(_current, _next, Width, Height));
        (_current, _next) = (_next, _current);
    }

    /// <summary>Replaces the grid with random cells; <paramref name="density"/> is the fraction alive.</summary>
    public void Randomize(uint seed, double density = 0.25)
    {
        uint threshold = (uint)Math.Clamp(density * 4294967296.0, 0, uint.MaxValue);
        _device.For(Width * Height, new LifeRandomize(_current, seed, threshold));
    }

    public void Clear() => _current.AsSpan().Clear();

    /// <summary>
    /// Sets (or clears) every cell within <paramref name="radius"/> of the segment from
    /// (<paramref name="x0"/>, <paramref name="y0"/>) to (<paramref name="x1"/>, <paramref name="y1"/>),
    /// in cell coordinates, wrapping around the edges like the simulation does.
    /// </summary>
    public void Paint(double x0, double y0, double x1, double y1, double radius, bool alive)
    {
        Span<uint> cells = _current.AsSpan();
        double length = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        int steps = Math.Max(1, (int)Math.Ceiling(length));
        int r = (int)Math.Ceiling(radius);

        for (int s = 0; s <= steps; s++)
        {
            double cx = x0 + ((x1 - x0) * s / steps);
            double cy = y0 + ((y1 - y0) * s / steps);

            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if ((dx * dx) + (dy * dy) > radius * radius)
                    {
                        continue;
                    }

                    int x = Mod((int)Math.Floor(cx) + dx, Width);
                    int y = Mod((int)Math.Floor(cy) + dy, Height);
                    ref uint cell = ref cells[(y * Width) + x];
                    cell = alive ? Math.Max(cell, 1u) : 0u;
                }
            }
        }
    }

    /// <summary>Draws the grid into <paramref name="pixels"/> (packed RGBA, <paramref name="imageWidth"/> × <paramref name="imageHeight"/>).</summary>
    public void Render(ReadWriteBuffer<uint> pixels, int imageWidth, int imageHeight) =>
        _device.For(imageWidth, imageHeight, new LifeRender(_current, pixels, Width, Height, imageWidth, imageHeight));

    public void Dispose()
    {
        _current.Dispose();
        _next.Dispose();
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}

/// <summary>
/// The live Game of Life demo: one generation per frame on a 320×180 grid (4×4-pixel cells at
/// 1280×720). Drag with the left button to draw cells, with the right button to erase them.
/// </summary>
public sealed class GameOfLifeDemo : IDemo
{
    public const int GridWidth = 320;
    public const int GridHeight = 180;

    private LifeGrid? _grid;
    private ReadWriteBuffer<uint>? _pixels;
    private uint _seed = 1;
    private double _lastX;
    private double _lastY;

    public string Name => "GameOfLife";

    public string Author => "SharpCompute";

    public string Controls => "drag: draw cells (right button erases), R: randomize, C: clear";

    public void Render(GraphicsDevice device, ReadWriteTexture2D<Rgba32> target, DemoFrame frame)
    {
        if (_grid is null)
        {
            _grid = new LifeGrid(device, GridWidth, GridHeight);
            _grid.Randomize(_seed);
        }

        if (!frame.Paused)
        {
            _grid.Step();
        }

        int count = target.Width * target.Height;

        if (_pixels is null || _pixels.Length != count)
        {
            _pixels?.Dispose();
            _pixels = device.AllocateReadWriteBuffer<uint>(count);
        }

        _grid.Render(_pixels, target.Width, target.Height);
        target.CopyFrom(MemoryMarshal.Cast<uint, Rgba32>(_pixels.AsSpan()));
    }

    public void OnKeyDown(Key key)
    {
        switch (key)
        {
            case Key.R:
                _grid?.Randomize(++_seed);
                break;
            case Key.C:
                _grid?.Clear();
                break;
        }
    }

    public void OnMouse(MouseAction action, MouseButton button, double x, double y, int targetWidth, int targetHeight)
    {
        if (_grid is null || action == MouseAction.Up)
        {
            return;
        }

        double cellX = x * GridWidth / targetWidth;
        double cellY = y * GridHeight / targetHeight;

        if (action == MouseAction.Down)
        {
            (_lastX, _lastY) = (cellX, cellY);
        }

        _grid.Paint(_lastX, _lastY, cellX, cellY, radius: 1.5, alive: button != MouseButton.Right);
        (_lastX, _lastY) = (cellX, cellY);
    }

    public void Dispose()
    {
        _grid?.Dispose();
        _pixels?.Dispose();
    }
}
