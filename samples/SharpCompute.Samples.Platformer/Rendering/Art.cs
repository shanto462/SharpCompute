// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;

namespace SharpCompute.Samples.Platformer.Rendering;

/// <summary>A 16 × 16 cell of the sprite atlas.</summary>
public enum Frame
{
    SpiritIdle0, SpiritIdle1, SpiritRun0, SpiritRun1, SpiritRun2, SpiritRun3, SpiritJump, SpiritFall,
    Crawler0, Crawler1, Moth0, Moth1, Mote0, Mote1, LanternDark, LanternLit0,
    LanternLit1, GateClosedTop, GateClosedBottom, GateOpenTop, GateOpenBottom,
}

/// <summary>
/// Glowfall's pixel art, drawn here as rows of palette characters and packed into one atlas. Each
/// sprite is 16 × 16. A packed pixel's alpha says how the renderer treats it: 0 transparent, 255
/// lit by the scene's lights, 128 emissive (it glows by itself and is never darkened).
/// </summary>
public static class Art
{
    public const int Cell = 16;
    public const int Columns = 8;

    /// <summary>Palette: character → RGB, and whether it glows.</summary>
    private static readonly Dictionary<char, (uint Rgb, bool Glows)> s_palette = new()
    {
        ['k'] = (0x1A1030, false), // outline, deep violet
        ['n'] = (0x3B2A5C, false), // shade
        ['g'] = (0x2E5E5A, false), // crawler shell, dark teal
        ['G'] = (0x4F8F7A, false), // shell highlight
        ['i'] = (0x5A5470, false), // iron
        ['I'] = (0x8C86A8, false), // iron highlight
        ['s'] = (0x6B5E7E, false), // gate stone
        ['S'] = (0x9A8CB0, false), // gate stone light
        ['p'] = (0xC9B8E8, false), // moth wing, pale lilac
        ['P'] = (0x8E7BB8, false), // moth wing shade
        ['o'] = (0xD9572B, true),  // ember orange
        ['F'] = (0xF29A3A, true),  // flame
        ['f'] = (0xFFD36E, true),  // flame light
        ['w'] = (0xFFF6D8, true),  // white-hot core
        ['t'] = (0x6FF2D2, true),  // crawler glow spots, mint
        ['c'] = (0x9FE8FF, true),  // cold portal light
        ['C'] = (0x4FB4FF, true),  // portal blue
    };

    private static readonly Dictionary<Frame, string[]> s_sprites = new()
    {
        [Frame.SpiritIdle0] =
        [
            "................",
            "................",
            ".......f........",
            "......fF........",
            "......fFf.......",
            ".....fFwFf......",
            ".....FwwwF......",
            "....fFwwwFf.....",
            "....FkwwwkF.....",
            "....FFwwwFF.....",
            "....oFFwFFo.....",
            ".....oFFFo......",
            "......ooo.......",
            ".....k...k......",
            ".....k...k......",
            "................",
        ],
        [Frame.SpiritIdle1] =
        [
            "................",
            "................",
            "........f.......",
            ".......Ff.......",
            "......fFf.......",
            ".....fFwFf......",
            ".....FwwwF......",
            "....fFwwwFf.....",
            "....FkwwwkF.....",
            "....FFwwwFF.....",
            "....oFFwFFo.....",
            ".....oFFFo......",
            "......ooo.......",
            ".....k...k......",
            ".....k...k......",
            "................",
        ],
        [Frame.SpiritRun0] =
        [
            "................",
            "................",
            ".....f..........",
            ".....Ff.........",
            "......fFf.......",
            ".....fFwFf......",
            ".....FwwwF......",
            "....fFwwwFf.....",
            "....FFwwkwF.....",
            "....FFwwwFF.....",
            "....oFFwFFo.....",
            ".....oFFFo......",
            "......ooo.......",
            "....k.....k.....",
            "...k.......k....",
            "................",
        ],
        [Frame.SpiritRun1] =
        [
            "................",
            "................",
            "................",
            "....fF..........",
            ".....fFf........",
            ".....fFwFf......",
            ".....FwwwF......",
            "....fFwwwFf.....",
            "....FFwwkwF.....",
            "....FFwwwFF.....",
            "....oFFwFFo.....",
            ".....oFFFo......",
            "......ooo.......",
            "......k.k.......",
            ".....k...k......",
            "................",
        ],
        [Frame.SpiritRun2] =
        [
            "................",
            "................",
            ".....f..........",
            ".....Ff.........",
            "......fFf.......",
            ".....fFwFf......",
            ".....FwwwF......",
            "....fFwwwFf.....",
            "....FFwwkwF.....",
            "....FFwwwFF.....",
            "....oFFwFFo.....",
            ".....oFFFo......",
            "......ooo.......",
            ".......k.k......",
            "......k...k.....",
            "................",
        ],
        [Frame.SpiritRun3] =
        [
            "................",
            "................",
            "................",
            "....fF..........",
            ".....fFf........",
            ".....fFwFf......",
            ".....FwwwF......",
            "....fFwwwFf.....",
            "....FFwwkwF.....",
            "....FFwwwFF.....",
            "....oFFwFFo.....",
            ".....oFFFo......",
            "......ooo.......",
            ".....k.k........",
            "....k...k.......",
            "................",
        ],
        [Frame.SpiritJump] =
        [
            "................",
            ".......f........",
            "......fF........",
            "......fF........",
            "......fFf.......",
            ".....fFwFf......",
            ".....FwwwF......",
            "....fFwwwFf.....",
            "....FkwwwkF.....",
            "....FFwwwFF.....",
            "....oFFwFFo.....",
            ".....oFFFo......",
            "......ooo.......",
            "......k.k.......",
            "................",
            "................",
        ],
        [Frame.SpiritFall] =
        [
            "................",
            "................",
            "................",
            "....f.....f.....",
            "....Ff...fF.....",
            ".....fFwFf......",
            ".....FwwwF......",
            "....fFwwwFf.....",
            "....FkwwwkF.....",
            "....FFwwwFF.....",
            "....oFFwFFo.....",
            ".....oFFFo......",
            "......ooo.......",
            ".....k...k......",
            "....k.....k.....",
            "................",
        ],
        [Frame.Crawler0] =
        [
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "......kkkk......",
            "....kkGGGGkk....",
            "...kgGtggtGgk...",
            "..kggggggggggk..",
            "..kgtggggggtgk..",
            "..kkggggggggkk..",
            "...k.k..k..k....",
            "..k...k..k..k...",
            "................",
        ],
        [Frame.Crawler1] =
        [
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "......kkkk......",
            "....kkGGGGkk....",
            "...kgGtggtGgk...",
            "..kggggggggggk..",
            "..kgtggggggtgk..",
            "..kkggggggggkk..",
            "....k..k..k.k...",
            "...k..k..k...k..",
            "................",
        ],
        [Frame.Moth0] =
        [
            "................",
            "................",
            "................",
            "..kk........kk..",
            ".kppk......kppk.",
            ".kpppk....kpppk.",
            ".kpPppk..kppPpk.",
            "..kpPpkttkpPpk..",
            "..kppppkkppppk..",
            "...kpPpkkpPpk...",
            "....kkk..kkk....",
            "................",
            "................",
            "................",
            "................",
            "................",
        ],
        [Frame.Moth1] =
        [
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "...kkk....kkk...",
            "..kpppkttkpppk..",
            ".kpPpppkkpppPpk.",
            ".kpppPk..kPpppk.",
            "..kkkk....kkkk..",
            "................",
            "................",
            "................",
            "................",
            "................",
        ],
        [Frame.Mote0] =
        [
            "................",
            "................",
            "................",
            "................",
            "................",
            ".......f........",
            "......fwf.......",
            ".....fwwwf......",
            "......fwf.......",
            ".......f........",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
        ],
        [Frame.Mote1] =
        [
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            ".......f........",
            "......fwf.......",
            ".......f........",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
        ],
        [Frame.LanternDark] =
        [
            "................",
            "......kkkk......",
            ".....k....k.....",
            "......kkkk......",
            ".....kiIIik.....",
            "....kinnnnik....",
            "....kInkknIk....",
            "....kinnnnik....",
            "....kInnnnIk....",
            "....kinnnnik....",
            "....kiIIIIik....",
            ".....kiiiik.....",
            "......kiik......",
            "......kiik......",
            ".....kiiiik.....",
            "....kkkkkkkk....",
        ],
        [Frame.LanternLit0] =
        [
            "................",
            "......kkkk......",
            ".....k....k.....",
            "......kkkk......",
            ".....kiIIik.....",
            "....kifFFfik....",
            "....kIfwwfIk....",
            "....kiFwwFik....",
            "....kIfwwfIk....",
            "....kiFFFFik....",
            "....kiIIIIik....",
            ".....kiiiik.....",
            "......kiik......",
            "......kiik......",
            ".....kiiiik.....",
            "....kkkkkkkk....",
        ],
        [Frame.LanternLit1] =
        [
            "................",
            "......kkkk......",
            ".....k....k.....",
            "......kkkk......",
            ".....kiIIik.....",
            "....kiFffFik....",
            "....kIfwwfIk....",
            "....kifwwFik....",
            "....kIFwwfIk....",
            "....kiFffFik....",
            "....kiIIIIik....",
            ".....kiiiik.....",
            "......kiik......",
            "......kiik......",
            ".....kiiiik.....",
            "....kkkkkkkk....",
        ],
        [Frame.GateClosedTop] =
        [
            "................",
            "................",
            "................",
            ".....kkkkkk.....",
            "...kksSSSSskk...",
            "..ksSsnnnnsSsk..",
            ".ksSnnnnnnnnSsk.",
            ".kSnnnnnnnnnnSk.",
            "ksSnnnnkknnnnSsk",
            "kSsnnnkkkknnnsSk",
            "kSnnnnkkkknnnnSk",
            "kSnnnnnkknnnnnSk",
            "kSsnnnnnnnnnnsSk",
            "kSnnnnnnnnnnnnSk",
            "kSnnnnnnnnnnnnSk",
            "kSsnnnnnnnnnnsSk",
        ],
        [Frame.GateClosedBottom] =
        [
            "kSnnnnnnnnnnnnSk",
            "kSnnnnnnnnnnnnSk",
            "kSsnnnnnnnnnnsSk",
            "kSnnnnnnnnnnnnSk",
            "kSnnnnnnnnnnnnSk",
            "kSsnnnnnnnnnnsSk",
            "kSnnnnnnnnnnnnSk",
            "kSnnnnnnnnnnnnSk",
            "kSsnnnnnnnnnnsSk",
            "kSnnnnnnnnnnnnSk",
            "kSnnnnnnnnnnnnSk",
            "kSsnnnnnnnnnnsSk",
            "kSnnnnnnnnnnnnSk",
            "kSnnnnnnnnnnnnSk",
            "kSSssssssssssSSk",
            "kkkkkkkkkkkkkkkk",
        ],
        [Frame.GateOpenTop] =
        [
            "................",
            "................",
            "................",
            ".....kkkkkk.....",
            "...kksSSSSskk...",
            "..ksSsCCCCsSsk..",
            ".ksSCCccccCCSsk.",
            ".kSCcccwwcccCSk.",
            "ksSCcwwwwwwcCSsk",
            "kSsCcwwwwwwcCsSk",
            "kSCccwwwwwwccCSk",
            "kSCcwwwwwwwwcCSk",
            "kSsCcwwwwwwcCsSk",
            "kSCccwwwwwwccCSk",
            "kSCcwwwwwwwwcCSk",
            "kSsCcwwwwwwcCsSk",
        ],
        [Frame.GateOpenBottom] =
        [
            "kSCccwwwwwwccCSk",
            "kSCcwwwwwwwwcCSk",
            "kSsCcwwwwwwcCsSk",
            "kSCccwwwwwwccCSk",
            "kSCcwwwwwwwwcCSk",
            "kSsCcwwwwwwcCsSk",
            "kSCccwwwwwwccCSk",
            "kSCcwwwwwwwwcCSk",
            "kSsCcwwwwwwcCsSk",
            "kSCccwwwwwwccCSk",
            "kSCCcccccccccCSk",
            "kSsCCccccccCCsSk",
            "kSCCCCCCCCCCCCSk",
            "kSCCCCCCCCCCCCSk",
            "kSSssssssssssSSk",
            "kkkkkkkkkkkkkkkk",
        ],
    };

    /// <summary>Atlas width in pixels (<see cref="Columns"/> cells).</summary>
    public static int Width => Columns * Cell;

    /// <summary>Atlas height in pixels.</summary>
    public static int Height => ((Enum.GetValues<Frame>().Length + Columns - 1) / Columns) * Cell;

    /// <summary>The packed atlas: RGBA8 in a uint (R in the low byte), alpha as described in the class remarks.</summary>
    public static uint[] BuildAtlas()
    {
        uint[] atlas = new uint[Width * Height];

        foreach ((Frame frame, string[] rows) in s_sprites)
        {
            if (rows.Length != Cell)
            {
                throw new InvalidOperationException($"Sprite {frame} has {rows.Length} rows, expected {Cell}.");
            }

            int originX = ((int)frame % Columns) * Cell;
            int originY = ((int)frame / Columns) * Cell;

            for (int y = 0; y < Cell; y++)
            {
                if (rows[y].Length != Cell)
                {
                    throw new InvalidOperationException($"Sprite {frame} row {y} has {rows[y].Length} columns, expected {Cell}.");
                }

                for (int x = 0; x < Cell; x++)
                {
                    char c = rows[y][x];

                    if (c == '.')
                    {
                        continue;
                    }

                    if (!s_palette.TryGetValue(c, out (uint Rgb, bool Glows) entry))
                    {
                        throw new InvalidOperationException($"Sprite {frame} uses '{c}', which is not in the palette.");
                    }

                    uint r = (entry.Rgb >> 16) & 0xFF, g = (entry.Rgb >> 8) & 0xFF, b = entry.Rgb & 0xFF;
                    uint a = entry.Glows ? 128u : 255u;
                    atlas[((originY + y) * Width) + originX + x] = r | (g << 8) | (b << 16) | (a << 24);
                }
            }
        }

        foreach (Frame frame in Enum.GetValues<Frame>())
        {
            if (!s_sprites.ContainsKey(frame))
            {
                throw new InvalidOperationException($"Sprite {frame} has no art.");
            }
        }

        return atlas;
    }
}
