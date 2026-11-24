using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpCompute.Tests.Viewer;

/// <summary>The Viewer's Game of Life shaders against a CPU reference on a 64×64 toroidal grid.</summary>
public class GameOfLifeTests
{
    private const int Size = 64;

    [ViewerFact]
    public void GpuStep_MatchesTheCpuReference_IncludingAges()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using IDisposable disposable = ViewerAssembly.Create("Demos.LifeGrid", device, Size, Size);
        dynamic grid = disposable;

        grid.Randomize(42u, 0.3);
        uint[] expected = Cells(grid);

        // The GPU randomizer is a hash, not the CPU's Random; just check it produced a plausible soup.
        double density = expected.Count(static c => c != 0) / (double)expected.Length;
        Assert.InRange(density, 0.25, 0.35);
        Assert.All(expected, static c => Assert.True(c is 0 or 1));

        for (int generation = 1; generation <= 40; generation++)
        {
            grid.Step();
            expected = CpuStep(expected, Size, Size);
            Assert.Equal(expected, Cells(grid));
        }

        // Survivors kept aging.
        Assert.Contains(expected, static c => c > 1);
    }

    [ViewerFact]
    public void Glider_MovesOneCellDiagonallyEveryFourGenerations_AcrossTheEdges()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using IDisposable disposable = ViewerAssembly.Create("Demos.LifeGrid", device, Size, Size);
        dynamic grid = disposable;

        // .#.
        // ..#
        // ###   placed so it crosses both wrap-around edges.
        (int X, int Y)[] glider = [(1, 0), (2, 1), (0, 2), (1, 2), (2, 2)];
        Set(grid, glider, offsetX: 61, offsetY: 61);

        for (int generation = 1; generation <= 16; generation++)
        {
            grid.Step();

            if (generation % 4 == 0)
            {
                int shift = generation / 4;
                Assert.Equal(Alive(glider, 61 + shift, 61 + shift), Alive(Cells(grid)));
            }
        }
    }

    [ViewerFact]
    public void Blinker_HasPeriodTwo()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using IDisposable disposable = ViewerAssembly.Create("Demos.LifeGrid", device, Size, Size);
        dynamic grid = disposable;

        (int X, int Y)[] horizontal = [(-1, 0), (0, 0), (1, 0)];
        (int X, int Y)[] vertical = [(0, -1), (0, 0), (0, 1)];
        Set(grid, horizontal, offsetX: 20, offsetY: 30);

        for (int generation = 1; generation <= 6; generation++)
        {
            grid.Step();
            Assert.Equal(Alive(generation % 2 == 1 ? vertical : horizontal, 20, 30), Alive(Cells(grid)));
        }

        // The center cell survives every generation, so its age counts up; the ends are reborn.
        uint[] cells = Cells(grid);
        Assert.Equal(7u, cells[(30 * Size) + 20]);
        Assert.Equal(1u, cells[(30 * Size) + 19]);
    }

    [ViewerFact]
    public void Paint_DrawsAStrokeAndWrapsAroundEdges()
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using IDisposable disposable = ViewerAssembly.Create("Demos.LifeGrid", device, Size, Size);
        dynamic grid = disposable;

        grid.Paint(-1.0, 10.0, 3.0, 10.0, 0.0, true);
        HashSet<(int, int)> alive = Alive(Cells(grid));
        Assert.Equal(new HashSet<(int, int)> { (63, 10), (0, 10), (1, 10), (2, 10), (3, 10) }, alive);

        grid.Paint(0.0, 10.0, 0.0, 10.0, 0.0, false);
        Assert.DoesNotContain((0, 10), Alive(Cells(grid)));

        grid.Clear();
        Assert.Empty(Alive(Cells(grid)));
    }

    /// <summary>The reference rule: B3/S23 on a torus; survivors age by one (capped at 255), births start at 1.</summary>
    internal static uint[] CpuStep(uint[] current, int width, int height)
    {
        uint[] next = new uint[current.Length];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int neighbors = 0;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if ((dx != 0 || dy != 0) && current[(((y + dy + height) % height) * width) + ((x + dx + width) % width)] != 0)
                        {
                            neighbors++;
                        }
                    }
                }

                uint age = current[(y * width) + x];
                next[(y * width) + x] = age != 0
                    ? (neighbors is 2 or 3 ? Math.Min(age + 1, 255u) : 0u)
                    : (neighbors == 3 ? 1u : 0u);
            }
        }

        return next;
    }

    private static uint[] Cells(dynamic grid) => ((ReadWriteBuffer<uint>)grid.Cells).ToArray();

    private static void Set(dynamic grid, (int X, int Y)[] pattern, int offsetX, int offsetY)
    {
        grid.Clear();
        Span<uint> cells = ((ReadWriteBuffer<uint>)grid.Cells).AsSpan();

        foreach ((int x, int y) in pattern)
        {
            cells[(((y + offsetY) % Size) * Size) + ((x + offsetX) % Size)] = 1;
        }
    }

    private static HashSet<(int, int)> Alive((int X, int Y)[] pattern, int offsetX, int offsetY) =>
        pattern.Select(p => ((p.X + offsetX + Size) % Size, (p.Y + offsetY + Size) % Size)).ToHashSet();

    private static HashSet<(int, int)> Alive(uint[] cells)
    {
        HashSet<(int, int)> alive = [];

        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != 0)
            {
                alive.Add((i % Size, i / Size));
            }
        }

        return alive;
    }
}
