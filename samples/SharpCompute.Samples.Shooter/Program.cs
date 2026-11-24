// Original SharpCompute sample (MIT).
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using SharpCompute.Presentation;
using SharpCompute.Samples.Shaders;
using SharpCompute.Samples.Shooter.Game;
using SharpCompute.Samples.Shooter.Rendering;
using static System.FormattableString;

namespace SharpCompute.Samples.Shooter;

/// <summary>
/// Pulsewake: a neon twin-stick arena shooter. The game is plain C# on the CPU at a fixed 120 Hz;
/// the vector shapes, tens of thousands of particles, bloom and shockwave distortion are SharpCompute
/// compute shaders, one batched submission per frame.
/// </summary>
public static class Program
{
    private const string Usage = """
        Pulsewake: a SharpCompute arena shooter

          SharpCompute.Samples.Shooter [--demo] [--mute] [--seed <n>]
          SharpCompute.Samples.Shooter --headless [--frames <dir>] [--seconds <s>] [--fps <n>] [--seed <n>] [--stress]

        Keys: WASD move · arrows fire in eight directions (or hold the left mouse button to fire at
              the pointer) · Space bomb · P pause · M mute · R restart · Esc quit
        --demo      an autopilot plays (attract mode)
        --headless  no window: the autopilot plays, frame times are printed, and with --frames a PNG
                    is written every half second
        --stress    (headless) also keeps the particle buffer full, to measure the worst case
        """;

    // Must stay synchronous: MetalWindow needs the process main thread, which only a synchronous Main keeps.
    public static int Main(string[] args)
    {
        bool demo = false, headless = false, mute = false, stress = false;
        string? frames = null;
        double seconds = 12;
        int fps = 2;
        uint seed = 1;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--demo":
                    demo = true;
                    break;
                case "--mute":
                    mute = true;
                    break;
                case "--headless":
                    headless = true;
                    break;
                case "--stress":
                    stress = true;
                    break;
                case "--fps" when i + 1 < args.Length && int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out int f) && f is >= 1 and <= 60:
                    fps = f;
                    i++;
                    break;
                case "--frames" when i + 1 < args.Length:
                    frames = args[++i];
                    break;
                case "--seconds" when i + 1 < args.Length && double.TryParse(args[i + 1], CultureInfo.InvariantCulture, out double s) && s > 0:
                    seconds = s;
                    i++;
                    break;
                case "--seed" when i + 1 < args.Length && uint.TryParse(args[i + 1], CultureInfo.InvariantCulture, out uint n):
                    seed = n;
                    i++;
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

        if (!GraphicsDevice.IsSupported)
        {
            Console.Error.WriteLine("No Metal device: SharpCompute needs macOS 15+ on Apple Silicon.");
            return 1;
        }

        if (headless)
        {
            return RunHeadless(seed, frames, seconds, stress, fps);
        }

        if (!MetalWindow.IsSupported)
        {
            Console.Error.WriteLine("No window server: run Pulsewake from a terminal in a desktop session (or use --headless).");
            return 1;
        }

        return RunWindow(seed, demo, mute);
    }

    private static int RunWindow(uint seed, bool demo, bool mute)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using Renderer renderer = new(device);
        using GameAudio audio = new(device, mute);
        Arena arena = new(seed);
        Pilot pilot = new();

        using MetalWindow window = new(device, "Pulsewake", 1280, 720);
        ReadWriteTexture2D<Rgba32> target = device.AllocateReadWriteTexture2D<Rgba32>(window.Width, window.Height);
        double accumulator = 0;
        bool paused = false;
        bool mouseFiring = false;
        Vector2 mouse = Vector2.Zero;

        // The window texture is only reallocated on resize, never per frame.
        window.Resized += (_, _) =>
        {
            target.Dispose();
            target = device.AllocateReadWriteTexture2D<Rgba32>(window.Width, window.Height);
        };

        window.KeyDown += (_, e) =>
        {
            if (e.IsRepeat)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Escape:
                    window.Close();
                    break;
                case Key.R:
                    arena.Reset();
                    renderer.ClearParticles();
                    break;
                case Key.M:
                    audio.Muted = !audio.Muted;
                    break;
                case Key.P:
                    paused = !paused;
                    break;
            }
        };

        // Mouse aim: while the left button is held, fire toward the pointer.
        window.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButton.Left)
            {
                mouseFiring = true;
                mouse = Renderer.WindowToArena(e.X, e.Y, window.Width, window.Height);
            }
        };
        window.MouseDragged += (_, e) => mouse = Renderer.WindowToArena(e.X, e.Y, window.Width, window.Height);
        window.MouseUp += (_, e) => mouseFiring &= e.Button != MouseButton.Left;

        Console.WriteLine(Usage);

        try
        {
            window.Run(frame =>
            {
                // Fixed 120 Hz simulation, independent of the display; input is sampled once per step.
                accumulator = paused ? 0 : accumulator + Math.Min(frame.DeltaTime, 0.1);
                int steps = 0;

                while (accumulator >= Tuning.StepSeconds && steps < 12)
                {
                    arena.Step(demo ? pilot.Next(arena) : ReadInput(window, arena, mouseFiring, mouse));
                    accumulator -= Tuning.StepSeconds;
                    steps++;
                }

                renderer.Render(arena, steps * Tuning.StepSeconds, target, paused);
                audio.Play(arena.Events, arena.GameOver);
                arena.EndFrame();
                window.Present(target);
            });
        }
        finally
        {
            target.Dispose();
        }

        return 0;
    }

    /// <summary>WASD moves; arrows aim and fire in eight directions; otherwise a held left button fires at the pointer.</summary>
    internal static InputState ReadInput(MetalWindow window, Arena arena, bool mouseFiring, Vector2 mouse)
    {
        Vector2 move = InputState.Direction(window.IsKeyDown(Key.A), window.IsKeyDown(Key.D), window.IsKeyDown(Key.W), window.IsKeyDown(Key.S));
        Vector2 aim = InputState.Direction(window.IsKeyDown(Key.Left), window.IsKeyDown(Key.Right), window.IsKeyDown(Key.Up), window.IsKeyDown(Key.Down));
        bool fire = aim != Vector2.Zero;

        if (!fire && mouseFiring)
        {
            aim = mouse - arena.Player.Position;
            fire = true;
        }

        return new InputState(move, aim, fire, window.IsKeyDown(Key.Space));
    }

    /// <summary>No window: the autopilot plays for <paramref name="seconds"/> at 60 frames per second of game time.</summary>
    private static int RunHeadless(uint seed, string? frames, double seconds, bool stress, int fps)
    {
        GraphicsDevice device = GraphicsDevice.GetDefault();
        using Renderer renderer = new(device);
        Arena arena = new(seed);
        Pilot pilot = new();
        Rng stressRng = new(seed + 7);

        if (frames is not null)
        {
            Directory.CreateDirectory(frames);
        }

        const int stepsPerFrame = 2;
        const int warmupFrames = 10;
        int totalFrames = (int)(seconds / (stepsPerFrame * Tuning.StepSeconds));
        int pngIndex = 0;
        double renderMs = 0, worstMs = 0;
        long particleSum = 0;
        int particleMax = 0, timed = 0, bombs = 0, deaths = 0;

        for (int frame = 0; frame < totalFrames; frame++)
        {
            for (int k = 0; k < stepsPerFrame; k++)
            {
                arena.Step(pilot.Next(arena));
            }

            foreach (GameEventAt e in arena.Events)
            {
                bombs += e.Event == GameEvent.Bomb ? 1 : 0;
                deaths += e.Event == GameEvent.Death ? 1 : 0;
            }

            if (stress)
            {
                // Two fireworks a frame keep the 65,536-slot ring buffer full.
                for (int b = 0; b < 2; b++)
                {
                    Vector2 at = new(stressRng.Range(40f, Tuning.Width - 40f), stressRng.Range(40f, Tuning.Height - 40f));
                    Vector3 color = Vector3.Lerp(Palette.Drifter, Palette.Weaver, stressRng.NextFloat());
                    renderer.Spawn(new ParticleBurst(at, 700, color, 220f, 1.8f, 1f, Streak: 0.03f, Drag: 1.5f));
                }
            }

            long start = Stopwatch.GetTimestamp();
            renderer.Render(arena, stepsPerFrame * Tuning.StepSeconds);
            double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            arena.EndFrame();

            if (frame >= warmupFrames)
            {
                int live = renderer.LiveParticles;
                renderMs += ms;
                worstMs = Math.Max(worstMs, ms);
                particleSum += live;
                particleMax = Math.Max(particleMax, live);
                timed++;
            }

            // 60 rendered frames per second of game time; write every (60 / fps)th as a PNG.
            if (frames is not null && frame % Math.Max(1, 60 / fps) == 0)
            {
                PngEncoder.Write(Path.Combine(frames, Invariant($"pulsewake-{pngIndex++:D4}.png")), renderer.FramePixels, Renderer.Width, Renderer.Height);
            }
        }

        timed = Math.Max(timed, 1);
        Console.WriteLine(Invariant(
            $"Pulsewake{(stress ? " (stress)" : "")}: {seconds:F1} s simulated, {timed} frames timed at {Renderer.Width}×{Renderer.Height}: {renderMs / timed:F2} ms per frame on average, {worstMs:F2} ms worst (wall time: CPU frame building + one GPU submission), particles alive {particleSum / timed} on average, {particleMax} at most. Wave {arena.Wave}, score {arena.Score}, {bombs} bombs, {deaths} ships lost{(frames is null ? "" : $", {pngIndex} PNGs in {Path.GetFullPath(frames)}")}"));
        return 0;
    }
}
