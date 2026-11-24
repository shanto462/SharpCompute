// Original SharpCompute sample (MIT).
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using SharpCompute.Presentation;
using SharpCompute.Samples.Platformer.Game;
using SharpCompute.Samples.Platformer.Rendering;
using SharpCompute.Samples.Shaders;
using static System.FormattableString;

namespace SharpCompute.Samples.Platformer;

/// <summary>
/// Glowfall: a lantern spirit relights the lanterns of three dark caverns. The game logic is plain
/// C# on the CPU at a fixed 120 Hz; every pixel, light, shadow and particle is computed on the GPU
/// by SharpCompute shaders.
/// </summary>
public static class Program
{
    private const string Usage = """
        Glowfall: a SharpCompute platformer

          SharpCompute.Samples.Platformer [--level <1-3>] [--demo] [--mute]
          SharpCompute.Samples.Platformer --headless --frames <dir> [--seconds <s>] [--fps <n>] [--level <1-3>]
          SharpCompute.Samples.Platformer --check-levels [--map]

        Keys: arrows or A/D move · Z, Space, Up or W jump (hold for higher) · Down or S drops
              through ledges · R restarts the level · M mutes · Esc quits
        --demo      a simple pilot plays (attract mode)
        --headless  no window: the pilot plays and PNG frames are written to <dir>, two per second
                    (or <n> per second with --fps, e.g. 30 to make a clip)
        """;

    // Must stay synchronous: MetalWindow needs the process main thread, which only a synchronous Main keeps.
    public static int Main(string[] args)
    {
        int level = 0;
        bool demo = false, headless = false, map = false, check = false, mute = false;
        string? frames = null;
        double seconds = 8;
        int fps = 2;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--level" when i + 1 < args.Length && int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out int n) && n is >= 1 and <= 3:
                    level = n - 1;
                    i++;
                    break;
                case "--demo":
                    demo = true;
                    break;
                case "--mute":
                    mute = true;
                    break;
                case "--headless":
                    headless = true;
                    break;
                case "--frames" when i + 1 < args.Length:
                    frames = args[++i];
                    break;
                case "--seconds" when i + 1 < args.Length && double.TryParse(args[i + 1], CultureInfo.InvariantCulture, out double s) && s > 0:
                    seconds = s;
                    i++;
                    break;
                case "--fps" when i + 1 < args.Length && int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out int f) && f is >= 1 and <= 60:
                    fps = f;
                    i++;
                    break;
                case "--check-levels":
                    check = true;
                    break;
                case "--map":
                    map = true;
                    break;
                case "-h" or "--help":
                    Console.WriteLine(Usage);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown or incomplete argument '{args[i]}'.");
                    Console.Error.WriteLine(Usage);
                    return 1;
            }
        }

        if (check)
        {
            return CheckLevels(map);
        }

        if (!GraphicsDevice.IsSupported)
        {
            Console.Error.WriteLine("No Metal device: SharpCompute needs macOS 15+ on Apple Silicon.");
            return 1;
        }

        if (headless)
        {
            return RunHeadless(level, frames, seconds, fps);
        }

        if (!MetalWindow.IsSupported)
        {
            Console.Error.WriteLine("No window server: run Glowfall from a terminal in a desktop session (or use --headless).");
            return 1;
        }

        return RunWindow(level, demo, mute);
    }

    private static int RunWindow(int level, bool demo, bool mute)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using Renderer renderer = new(device);
        using GameAudio audio = new(device, mute);
        GameSession session = new(level);
        Pilot pilot = new();

        using MetalWindow window = new(device, "Glowfall", 960, 540);
        ReadWriteTexture2D<Rgba32> target = device.AllocateReadWriteTexture2D<Rgba32>(window.Width, window.Height);
        double accumulator = 0;
        Level? titledLevel = null;

        // The window texture is only reallocated on resize, never per frame.
        window.Resized += (_, _) =>
        {
            target.Dispose();
            target = device.AllocateReadWriteTexture2D<Rgba32>(window.Width, window.Height);
        };

        // A jump tapped and released between two polls (a short tap, or any tap during a hitch) would
        // never be seen by IsKeyDown; latch the press until the next simulation step consumes it.
        bool jumpLatched = false;

        window.KeyDown += (_, e) =>
        {
            if (!e.IsRepeat && IsJumpKey(e.Key))
            {
                jumpLatched = true;
            }

            if (e.Key == Key.Escape)
            {
                window.Close();
            }
            else if (e.Key == Key.R && !e.IsRepeat)
            {
                session.RestartLevel();
            }
            else if (e.Key == Key.M && !e.IsRepeat)
            {
                audio.Muted = !audio.Muted;
            }
        };

        Console.WriteLine(Usage);

        try
        {
            window.Run(frame =>
            {
                // Fixed 120 Hz simulation, independent of the display; input is sampled once per step.
                accumulator += Math.Min(frame.DeltaTime, 0.1);
                int steps = 0;

                while (accumulator >= Tuning.StepSeconds && steps < 12)
                {
                    InputState input = demo ? pilot.Next(session.World) : ReadInput(window);
                    session.Step(input with { Jump = input.Jump || jumpLatched });
                    jumpLatched = false;
                    accumulator -= Tuning.StepSeconds;
                    steps++;
                }

                renderer.Render(session, steps * Tuning.StepSeconds, target);
                audio.Play(session.Events, session.Camera.Position.X, session.Finished);
                session.TakeBursts();
                if (!ReferenceEquals(titledLevel, session.World.Level))
                {
                    titledLevel = session.World.Level;
                    window.Title = $"Glowfall · {titledLevel.Name}";
                }
                window.Present(target);
            });
        }
        finally
        {
            target.Dispose();
        }

        return 0;
    }

    private static bool IsJumpKey(Key key) => key is Key.Z or Key.Space or Key.Up or Key.W;

    private static InputState ReadInput(MetalWindow window) => new(
        Left: window.IsKeyDown(Key.Left) || window.IsKeyDown(Key.A),
        Right: window.IsKeyDown(Key.Right) || window.IsKeyDown(Key.D),
        Jump: window.IsKeyDown(Key.Z) || window.IsKeyDown(Key.Space) || window.IsKeyDown(Key.Up) || window.IsKeyDown(Key.W),
        Down: window.IsKeyDown(Key.Down) || window.IsKeyDown(Key.S));

    /// <summary>No window: the pilot plays for <paramref name="seconds"/>, and a PNG is written every half second.</summary>
    private static int RunHeadless(int level, string? frames, double seconds, int fps)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using Renderer renderer = new(device);
        GameSession session = new(level);
        Pilot pilot = new();

        if (frames is not null)
        {
            Directory.CreateDirectory(frames);
        }

        int totalSteps = (int)(seconds / Tuning.StepSeconds);
        const int stepsPerFrame = 2;
        int stepsPerImage = Math.Max(stepsPerFrame, (int)Math.Round(1.0 / (fps * Tuning.StepSeconds) / stepsPerFrame) * stepsPerFrame);
        int frameIndex = 0;
        double renderMs = 0;
        int rendered = 0;

        for (int step = 0; step < totalSteps; step += stepsPerFrame)
        {
            for (int k = 0; k < stepsPerFrame; k++)
            {
                session.Step(pilot.Next(session.World));
            }

            long start = Stopwatch.GetTimestamp();
            renderer.Render(session, stepsPerFrame * Tuning.StepSeconds);
            renderMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            rendered++;
            session.TakeBursts();

            if (frames is not null && step % stepsPerImage == 0)
            {
                string path = Path.Combine(frames, Invariant($"glowfall-{frameIndex++:D4}.png"));
                PngEncoder.Write(path, renderer.FramePixels, Renderer.Width, Renderer.Height);
            }
        }

        Console.WriteLine(Invariant(
            $"{session.World.Level.Name}: {seconds:F1} s simulated, {rendered} frames at {Renderer.Width}×{Renderer.Height}, {renderMs / rendered:F2} ms per frame (particles + scene, wall time), lanterns {session.World.LanternsLit}/{session.World.Lanterns.Count}, motes {session.World.MotesCollected}, {renderer.LiveParticles} particles alive{(frames is null ? "" : $", {frameIndex} PNGs in {Path.GetFullPath(frames)}")}"));
        return 0;
    }

    private static int CheckLevels(bool map)
    {
        int failures = 0;

        foreach (Level level in Levels.All)
        {
            Reachability.Result r = Reachability.Check(level);
            Console.WriteLine($"{level.Name}: {r.StandingSpots} standing spots, gate {(r.GateReached ? "reached" : "NOT reached")}, unreached lanterns {r.UnreachedLanterns.Count}, unreached motes {r.UnreachedMotes.Count}");

            if (map)
            {
                PrintMap(level, r);
            }

            failures += r.Finishable && r.UnreachedMotes.Count == 0 ? 0 : 1;
        }

        return failures == 0 ? 0 : 2;
    }

    private static void PrintMap(Level level, Reachability.Result r)
    {
        for (int y = 0; y < level.Height; y++)
        {
            char[] row = new char[level.Width];

            for (int x = 0; x < level.Width; x++)
            {
                row[x] = level[x, y] switch
                {
                    TileKind.Rock => '#',
                    TileKind.Ledge => '=',
                    TileKind.Spikes => '^',
                    _ => r.VisitedTiles.Contains((x, y)) ? '·' : ' ',
                };
            }

            foreach (Spawn s in level.Spawns)
            {
                if (s.TileY == y)
                {
                    row[s.TileX] = s.Kind switch { SpawnKind.Player => 'P', SpawnKind.Mote => 'o', SpawnKind.Lantern => 'L', SpawnKind.Gate => 'G', SpawnKind.Crawler => 'c', _ => 'm' };
                }
            }

            Console.WriteLine(new string(row));
        }
    }
}
