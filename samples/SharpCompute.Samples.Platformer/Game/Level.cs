// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpCompute.Samples.Platformer.Game;

/// <summary>What occupies one tile of the level grid.</summary>
public enum TileKind : byte
{
    Empty = 0,

    /// <summary>Solid cave rock on every side.</summary>
    Rock = 1,

    /// <summary>A one-way ledge: solid from above only, so you can jump up through it.</summary>
    Ledge = 2,

    /// <summary>Thorny crystal spikes on the floor; touching them sends you back to the last lantern.</summary>
    Spikes = 3,
}

/// <summary>An entity placed by a level map.</summary>
public enum SpawnKind
{
    Player,
    Mote,
    Lantern,
    Crawler,
    Moth,
    Gate,
}

/// <summary>One entity in a level map: its kind and tile position.</summary>
public readonly record struct Spawn(SpawnKind Kind, int TileX, int TileY);

/// <summary>
/// A level: the tile grid plus where things start. Parsed from rows of characters (see
/// <see cref="Parse"/>), top row first.
/// </summary>
public sealed class Level
{
    /// <summary>Tile size in pixels; the whole game works in pixels.</summary>
    public const int TileSize = 16;

    private readonly TileKind[] _tiles;

    private Level(string name, int width, int height, TileKind[] tiles, IReadOnlyList<Spawn> spawns)
    {
        Name = name;
        Width = width;
        Height = height;
        _tiles = tiles;
        Spawns = spawns;
    }

    public string Name { get; }

    /// <summary>Width in tiles.</summary>
    public int Width { get; }

    /// <summary>Height in tiles.</summary>
    public int Height { get; }

    public int PixelWidth => Width * TileSize;

    public int PixelHeight => Height * TileSize;

    public IReadOnlyList<Spawn> Spawns { get; }

    /// <summary>The tile at (<paramref name="x"/>, <paramref name="y"/>); outside the map counts as rock, so nothing leaves it sideways or through the floor.</summary>
    public TileKind this[int x, int y] =>
        x < 0 || x >= Width || y >= Height ? TileKind.Rock
        : y < 0 ? TileKind.Empty
        : _tiles[(y * Width) + x];

    /// <summary>The tile grid, row-major from the top-left, for upload to the GPU.</summary>
    public ReadOnlySpan<TileKind> Tiles => _tiles;

    /// <summary>The pixel-space center of a tile.</summary>
    public static Vector2 Center(int tileX, int tileY) => new((tileX + 0.5f) * TileSize, (tileY + 0.5f) * TileSize);

    /// <summary>
    /// Parses a map. Every row must have the same length.
    /// <list type="bullet">
    /// <item><c>#</c> rock, <c>=</c> one-way ledge, <c>^</c> spikes, <c>.</c> or space: empty</item>
    /// <item><c>P</c> player start, <c>o</c> light mote, <c>L</c> lantern, <c>c</c> crawler, <c>m</c> moth, <c>G</c> gate (the exit)</item>
    /// </list>
    /// </summary>
    /// <exception cref="FormatException">The rows differ in length, a character is unknown, or there is not exactly one player start and one gate.</exception>
    public static Level Parse(string name, IReadOnlyList<string> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
        {
            throw new FormatException($"Level '{name}' has no rows.");
        }

        int width = rows[0].Length;
        TileKind[] tiles = new TileKind[width * rows.Count];
        List<Spawn> spawns = [];

        for (int y = 0; y < rows.Count; y++)
        {
            if (rows[y].Length != width)
            {
                throw new FormatException($"Level '{name}': row {y} has {rows[y].Length} characters, expected {width}.");
            }

            for (int x = 0; x < width; x++)
            {
                char c = rows[y][x];
                TileKind tile = TileKind.Empty;

                switch (c)
                {
                    case '#': tile = TileKind.Rock; break;
                    case '=': tile = TileKind.Ledge; break;
                    case '^': tile = TileKind.Spikes; break;
                    case '.' or ' ': break;
                    case 'P': spawns.Add(new Spawn(SpawnKind.Player, x, y)); break;
                    case 'o': spawns.Add(new Spawn(SpawnKind.Mote, x, y)); break;
                    case 'L': spawns.Add(new Spawn(SpawnKind.Lantern, x, y)); break;
                    case 'c': spawns.Add(new Spawn(SpawnKind.Crawler, x, y)); break;
                    case 'm': spawns.Add(new Spawn(SpawnKind.Moth, x, y)); break;
                    case 'G': spawns.Add(new Spawn(SpawnKind.Gate, x, y)); break;
                    default: throw new FormatException($"Level '{name}': unknown character '{c}' at column {x}, row {y}.");
                }

                tiles[(y * width) + x] = tile;
            }
        }

        int players = spawns.FindAll(static s => s.Kind == SpawnKind.Player).Count;
        int gates = spawns.FindAll(static s => s.Kind == SpawnKind.Gate).Count;

        if (players != 1 || gates != 1)
        {
            throw new FormatException($"Level '{name}' needs exactly one player start (P) and one gate (G); it has {players} and {gates}.");
        }

        return new Level(name, width, rows.Count, tiles, spawns);
    }

    /// <summary>True for tiles that block movement from every side.</summary>
    public bool IsSolid(int x, int y) => this[x, y] == TileKind.Rock;
}
