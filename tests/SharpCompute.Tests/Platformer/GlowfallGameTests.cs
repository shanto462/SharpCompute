using System;
using System.Linq;
using System.Numerics;
using SharpCompute.Samples.Platformer.Game;

namespace SharpCompute.Tests.Platformer;

/// <summary>Glowfall's simulation: plain C#, no GPU, driven step by step.</summary>
public sealed class GlowfallGameTests
{
    private static readonly InputState Idle = InputState.None;
    private static readonly InputState Jump = new(false, false, true, false);

    [Fact]
    public void EveryLevel_CanBeFinished_WithTheRealMovementCode()
    {
        foreach (Level level in Levels.All)
        {
            Reachability.Result result = Reachability.Check(level);

            Assert.True(result.Finishable, $"{level.Name}: gate reached {result.GateReached}, unreached lanterns {string.Join(", ", result.UnreachedLanterns)}");
            Assert.Empty(result.UnreachedMotes);
        }
    }

    [Fact]
    public void Parse_RejectsUnevenRowsUnknownTilesAndMissingStartOrGate()
    {
        Assert.Throws<FormatException>(() => Level.Parse("x", ["#P#", "##"]));
        Assert.Throws<FormatException>(() => Level.Parse("x", ["#P?G#"]));
        Assert.Throws<FormatException>(() => Level.Parse("x", ["#..G#"]));
        Assert.Throws<FormatException>(() => Level.Parse("x", ["#P..#"]));

        Level level = Level.Parse("ok", ["#####", "#P.G#", "#####"]);
        Assert.Equal((5, 3), (level.Width, level.Height));
        Assert.Equal(TileKind.Rock, level[-1, 1]);
        Assert.Equal(TileKind.Empty, level[2, -5]);
    }

    [Fact]
    public void Jump_ClearsAboutThreeTiles_AndATapIsLowerThanAHold()
    {
        float held = PeakHeight(holdSteps: 200);
        float tapped = PeakHeight(holdSteps: 6);

        Assert.InRange(held, 3f * Level.TileSize, 3.6f * Level.TileSize);
        Assert.True(tapped < held * 0.6f, $"tap {tapped} vs hold {held}");
    }

    [Fact]
    public void CoyoteTime_AllowsAJumpJustAfterWalkingOffALedge_ButNotLater()
    {
        Assert.True(JumpsAfterLeavingLedge(delaySteps: 6));    // 0.05 s: within coyote time
        Assert.False(JumpsAfterLeavingLedge(delaySteps: 24));  // 0.2 s: too late
    }

    [Fact]
    public void JumpBuffer_APressJustBeforeLandingStillJumps()
    {
        World world = Flat();
        world.Teleport(new Vector2(48, 4 * Level.TileSize));   // in the air, falling onto the floor

        // Press jump while still falling, release, and keep the button up.
        for (int i = 0; i < 3; i++)
        {
            world.Step(Jump);
        }

        bool jumped = false;

        for (int i = 0; i < 60; i++)
        {
            world.Step(Idle);
            jumped |= world.Events.Any(e => e.Event == GameEvent.Jump);
        }

        Assert.False(jumped, "Pressed far too early: not buffered.");

        world.Teleport(new Vector2(48, (7 * Level.TileSize) - 2));
        world.Step(Idle);
        world.Step(Jump);
        world.Step(Idle);
        jumped = false;

        for (int i = 0; i < 20; i++)
        {
            world.Step(Idle);
            jumped |= world.Events.Any(e => e.Event == GameEvent.Jump) || world.Player.Velocity.Y < -100;
        }

        Assert.True(jumped, "A press a few frames before landing should still jump.");
    }

    [Fact]
    public void Ledges_AreSolidFromAbove_PassableFromBelow_AndDownDropsThrough()
    {
        Level level = Level.Parse("ledges",
        [
            "##########",
            "#........#",
            "#..====..#",
            "#........#",
            "#.P....G.#",
            "##########",
        ]);
        World world = new(level, spawnCreatures: false);
        Settle(world);
        float floorY = world.Player.Body.Bottom;

        // Jump from under the ledge: up through it, then land on top of it.
        world.Teleport(new Vector2(4 * Level.TileSize, floorY));
        Settle(world);

        for (int i = 0; i < 200; i++)
        {
            world.Step(i < 40 ? Jump : Idle);
        }

        Assert.Equal(2 * Level.TileSize, world.Player.Body.Bottom, 0.01f);
        Assert.True(world.Player.OnGround);

        // Holding down drops through it again.
        for (int i = 0; i < 120; i++)
        {
            world.Step(new InputState(false, false, false, true));
        }

        Assert.Equal(floorY, world.Player.Body.Bottom, 0.01f);
    }

    [Fact]
    public void Spikes_SendThePlayerBackToTheLastLitLantern()
    {
        Level level = Level.Parse("spikes",
        [
            "##########",
            "#........#",
            "#P.L..^^G#",
            "##########",
        ]);
        World world = new(level, spawnCreatures: false);
        Vector2 lantern = world.Lanterns[0].Position;
        bool hurt = false;

        for (int i = 0; i < 600 && !hurt; i++)
        {
            world.Step(new InputState(false, true, false, false));
            hurt = world.Events.Any(e => e.Event == GameEvent.Hurt);
        }

        Assert.True(world.Lanterns[0].Lit);
        Assert.True(hurt);

        for (int i = 0; i < 120; i++)
        {
            world.Step(Idle);
        }

        Assert.Equal(lantern.X, world.Player.Body.Center.X, 1f);
        Assert.Equal(0, world.Player.HurtTimer);
    }

    [Fact]
    public void LightingEveryLantern_OpensTheGate_AndEnteringItCompletesTheLevel()
    {
        Level level = Level.Parse("gate",
        [
            "############",
            "#..........#",
            "#P.L..L..G.#",
            "############",
        ]);
        World world = new(level, spawnCreatures: false);
        bool opened = false;

        for (int i = 0; i < 900 && !world.Complete; i++)
        {
            world.Step(new InputState(false, true, false, false));
            opened |= world.Events.Any(e => e.Event == GameEvent.GateOpen);
        }

        Assert.True(opened);
        Assert.Equal(2, world.LanternsLit);
        Assert.True(world.Complete);
    }

    [Fact]
    public void FallingOntoACrawler_Stomps_ItButWalkingIntoItHurts()
    {
        Level level = Level.Parse("crawler",
        [
            "##############",
            "#............#",
            "#............#",
            "#............#",
            "#P.....c....G#",
            "##############",
        ]);

        World walking = new(level);

        for (int i = 0; i < 400 && walking.Player.HurtTimer <= 0; i++)
        {
            walking.Step(new InputState(false, true, false, false));
        }

        Assert.True(walking.Player.HurtTimer > 0);

        World stomping = new(level);
        Creature crawler = stomping.Creatures.Single();
        stomping.Teleport(new Vector2(crawler.Body.Center.X, crawler.Body.Y - 30));
        bool stomped = false;

        for (int i = 0; i < 60 && !stomped; i++)
        {
            stomping.Step(Idle);
            stomped = stomping.Events.Any(e => e.Event == GameEvent.Stomp);
        }

        Assert.True(stomped);
        Assert.False(crawler.Alive);
        Assert.True(stomping.Player.Velocity.Y < 0, "A stomp bounces the player up.");
    }

    [Fact]
    public void SameInputs_GiveTheSameGame()
    {
        GameSession a = new(), b = new();
        Pilot pa = new(), pb = new();

        for (int i = 0; i < 2400; i++)
        {
            a.Step(pa.Next(a.World));
            b.Step(pb.Next(b.World));
        }

        Assert.Equal(a.World.Player.Body, b.World.Player.Body);
        Assert.Equal(a.World.MotesCollected, b.World.MotesCollected);
        Assert.Equal(a.Camera.Position, b.Camera.Position);
        Assert.Equal(a.Random(), b.Random());
    }

    [Fact]
    public void Camera_StaysInsideTheLevel()
    {
        GameSession session = new(1);
        World world = session.World;

        foreach (Vector2 feet in new[] { new Vector2(0, 0), new Vector2(world.Level.PixelWidth, world.Level.PixelHeight), new Vector2(world.Level.PixelWidth / 2f, 40) })
        {
            world.Teleport(feet);

            for (int i = 0; i < 600; i++)
            {
                session.Camera.Update(world, Tuning.StepSeconds);
            }

            Vector2 p = session.Camera.Position;
            Assert.InRange(p.X, 0, Math.Max(0, world.Level.PixelWidth - GameSession.ViewWidth));
            Assert.InRange(p.Y, 0, Math.Max(0, world.Level.PixelHeight - GameSession.ViewHeight));
        }
    }

    private static World Flat() => new(Level.Parse("flat",
    [
        "##########",
        "#........#",
        "#........#",
        "#........#",
        "#........#",
        "#........#",
        "#P......G#",
        "##########",
    ]), spawnCreatures: false);

    private static void Settle(World world)
    {
        for (int i = 0; i < 240 && !world.Player.OnGround; i++)
        {
            world.Step(Idle);
        }
    }

    private static float PeakHeight(int holdSteps)
    {
        World world = Flat();
        Settle(world);
        float floor = world.Player.Body.Bottom;
        float peak = 0;

        for (int i = 0; i < 240; i++)
        {
            world.Step(i < holdSteps ? Jump : Idle);
            peak = MathF.Max(peak, floor - world.Player.Body.Bottom);
        }

        return peak;
    }

    private static bool JumpsAfterLeavingLedge(int delaySteps)
    {
        Level level = Level.Parse("edge",
        [
            "##########",
            "#........#",
            "#........#",
            "#P.......#",
            "####.....#",
            "#........#",
            "#.......G#",
            "##########",
        ]);
        World world = new(level, spawnCreatures: false);
        Settle(world);

        // Walk right until the feet leave the ledge, then wait, then press jump.
        int guard = 0;

        while (world.Player.OnGround && guard++ < 600)
        {
            world.Step(new InputState(false, true, false, false));
        }

        for (int i = 0; i < delaySteps; i++)
        {
            world.Step(Idle);
        }

        world.Step(Jump);
        return world.Events.Any(e => e.Event == GameEvent.Jump);
    }
}
