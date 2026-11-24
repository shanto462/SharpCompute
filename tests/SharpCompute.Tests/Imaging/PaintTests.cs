using System;
using System.Collections.Generic;
using System.Linq;
using SharpCompute.Presentation;

namespace SharpCompute.Tests.Imaging;

/// <summary>The Paint sample without a window: undo-ring bookkeeping, stroke interpolation, and brush coverage on the GPU.</summary>
public class PaintTests
{
    private const string Sample = SampleAssembly.Paint;
    private const int Width = 128;
    private const int Height = 96;

    [SampleFact(Sample)]
    public void UndoRing_UndoesAndRedoesInOrder_AndPushDiscardsRedo()
    {
        dynamic ring = SampleAssembly.Create(Sample, "UndoRing", 20);

        Assert.True(ring.IsEmpty);
        Assert.False(ring.CanUndo);
        Assert.Equal(-1, (int)ring.CurrentSlot);

        int[] slots = [.. Enumerable.Range(0, 4).Select(_ => (int)ring.Push())];
        Assert.Equal([0, 1, 2, 3], slots);
        Assert.Equal(3, (int)ring.UndoCount);

        Assert.Equal(2, Undo(ring));
        Assert.Equal(1, Undo(ring));
        Assert.Equal(2, (int)ring.RedoCount);
        Assert.Equal(2, Redo(ring));
        Assert.Equal(1, (int)ring.RedoCount);

        // A new edit after an undo replaces the redo branch, reusing its slot.
        Assert.Equal(3, (int)ring.Push());
        Assert.False(ring.CanRedo);
        Assert.Equal(3, (int)ring.UndoCount);
        Assert.Equal(-1, Redo(ring));

        Assert.Equal(2, Undo(ring));
        Assert.Equal(1, Undo(ring));
        Assert.Equal(0, Undo(ring));
        Assert.Equal(-1, Undo(ring));
        Assert.Equal(0, (int)ring.CurrentSlot);
    }

    [SampleFact(Sample)]
    public void UndoRing_WhenFull_KeepsTheNewest20Steps_AndNeverHandsOutALiveSlot()
    {
        dynamic ring = SampleAssembly.Create(Sample, "UndoRing", 20);
        Assert.Equal(21, (int)ring.SlotCount);

        // Simulate the slot contents: each push stores its edit number.
        int[] contents = new int[21];

        for (int edit = 0; edit < 50; edit++)
        {
            contents[(int)ring.Push()] = edit;
        }

        Assert.Equal(20, (int)ring.UndoCount);
        Assert.Equal(49, contents[(int)ring.CurrentSlot]);

        // Undo walks back through edits 48..29 exactly, then stops.
        for (int expected = 48; expected >= 29; expected--)
        {
            Assert.Equal(expected, contents[Undo(ring)]);
        }

        Assert.Equal(-1, Undo(ring));

        // ... and redo walks forward again to 49.
        for (int expected = 30; expected <= 49; expected++)
        {
            Assert.Equal(expected, contents[Redo(ring)]);
        }

        Assert.Equal(-1, Redo(ring));
    }

    [SampleFact(Sample)]
    public void StrokePath_SpacesStampsEvenly_AcrossFastSegments()
    {
        dynamic path = SampleAssembly.Create(Sample, "StrokePath");
        List<Float2> stamps = [];
        const float spacing = 4f;

        path.Begin(new Float2(10f, 10f), stamps);

        // Three fast "mouse events" far apart, then a tiny one that doesn't reach the next stamp.
        path.MoveTo(new Float2(310f, 10f), spacing, stamps);
        path.MoveTo(new Float2(310f, 210f), spacing, stamps);
        path.MoveTo(new Float2(10f, 210f), spacing, stamps);
        int before = stamps.Count;
        path.MoveTo(new Float2(8f, 210f), spacing, stamps);
        Assert.Equal(before, stamps.Count);

        // 800 px of path at 4 px spacing: a stamp every 4 px of arc length (straight-line gaps
        // shrink only where a stamp straddles a corner).
        Assert.Equal(201, stamps.Count);

        for (int i = 1; i < stamps.Count; i++)
        {
            float gap = Distance(stamps[i - 1], stamps[i]);
            Assert.InRange(gap, 2.8f, spacing + 1e-3f);
        }
    }

    [SampleFact(Sample)]
    public void BrushCoverage_IsFullInside_ZeroOutside_AndMonotonic()
    {
        foreach (float hardness in new[] { 0f, 0.5f, 1f })
        {
            Assert.Equal(1f, Coverage(0f, 20f, hardness));
            Assert.Equal(0f, Coverage(20f, 20f, hardness));
            Assert.Equal(0f, Coverage(25f, 20f, hardness));

            float previous = 1f;

            for (float d = 0; d <= 20f; d += 0.25f)
            {
                float c = Coverage(d, 20f, hardness);
                Assert.True(c <= previous + 1e-6f, $"hardness {hardness}: coverage rises at {d}");
                previous = c;
            }
        }

        // Hardness sets where the falloff starts; a hard brush keeps a one-pixel anti-aliased rim.
        Assert.Equal(1f, Coverage(9.9f, 20f, 0.5f));
        Assert.True(Coverage(10.5f, 20f, 0.5f) < 1f);
        Assert.Equal(1f, Coverage(18.9f, 20f, 1f));
        Assert.InRange(Coverage(19.5f, 20f, 1f), 0.1f, 0.9f);
    }

    [SampleGpuFact(Sample)]
    public void Dab_OnTheGpu_MatchesTheCoverageCurve()
    {
        using IDisposable disposable = SampleAssembly.Create(Sample, "PaintCanvas", GraphicsDevice.GetDefault(), Width, Height, 20);
        dynamic canvas = disposable;
        dynamic brush = Brush(size: 40f, hardness: 0.3f, opacity: 0.8f, color: new Float3(1f, 0f, 0f));
        Float2 center = new(64.25f, 47.5f);

        Stroke(canvas, brush, center);
        Float4[] pixels = canvas.ToArray();

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                float alpha = Coverage(Distance(new Float2(x + 0.5f, y + 0.5f), center), 20f, 0.3f) * 0.8f;
                Float4 expected = (new Float4(1f, 0f, 0f, 1f) * alpha) + (new Float4(1f, 1f, 1f, 1f) * (1f - alpha));
                Float4 actual = pixels[(y * Width) + x];
                Float4 d = actual - expected;
                Assert.True(
                    Math.Abs(d.X) + Math.Abs(d.Y) + Math.Abs(d.Z) + Math.Abs(d.W) < 1e-5f,
                    $"Pixel ({x}, {y}): expected {expected}, got {actual}");
            }
        }
    }

    [SampleGpuFact(Sample)]
    public void FastStroke_IsContinuous_AndOpacityNeverBuildsUp()
    {
        using IDisposable disposable = SampleAssembly.Create(Sample, "PaintCanvas", GraphicsDevice.GetDefault(), Width, Height, 20);
        dynamic canvas = disposable;
        dynamic brush = Brush(size: 6f, hardness: 1f, opacity: 0.5f, color: new Float3(0f, 0f, 1f));

        // Two points 100 px apart (one fast drag), then back over itself: the line is solid and
        // exactly 50% blue everywhere along its center, including where it overlaps itself.
        Stroke(canvas, brush, new Float2(14f, 40.5f), new Float2(114f, 40.5f), new Float2(30f, 40.5f));
        Float4[] pixels = canvas.ToArray();

        for (int x = 14; x < 114; x++)
        {
            Float4 p = pixels[(40 * Width) + x];
            Assert.True(Math.Abs(p.X - 0.5f) < 1e-5f && Math.Abs(p.Z - 1f) < 1e-5f && p.W == 1f, $"x = {x}: {p}");
        }

        // Far from the line nothing changed.
        Assert.Equal(new Float4(1f, 1f, 1f, 1f), pixels[(80 * Width) + 64]);
    }

    [SampleGpuFact(Sample)]
    public void Eraser_ClearsToTransparent()
    {
        using IDisposable disposable = SampleAssembly.Create(Sample, "PaintCanvas", GraphicsDevice.GetDefault(), Width, Height, 20);
        dynamic canvas = disposable;
        dynamic brush = Brush(size: 30f, hardness: 1f, opacity: 1f, color: new Float3(0f, 0f, 0f));
        brush.Erase = true;

        Stroke(canvas, brush, new Float2(64f, 48f));
        Float4[] pixels = canvas.ToArray();

        Assert.Equal(new Float4(0f, 0f, 0f, 0f), pixels[(48 * Width) + 64]);
        Assert.Equal(new Float4(1f, 1f, 1f, 1f), pixels[(48 * Width) + 100]);
    }

    [SampleGpuFact(Sample)]
    public void Canvas_UndoAndRedo_RestoreSnapshotsOfStrokesAndFilters()
    {
        using IDisposable disposable = SampleAssembly.Create(Sample, "PaintCanvas", GraphicsDevice.GetDefault(), Width, Height, 3);
        dynamic canvas = disposable;
        dynamic brush = Brush(size: 24f, hardness: 0.5f, opacity: 1f, color: new Float3(0.2f, 0.6f, 0.9f));

        Float4[] blank = canvas.ToArray();
        Stroke(canvas, brush, new Float2(20f, 20f), new Float2(100f, 70f));
        Float4[] stroked = canvas.ToArray();
        canvas.Apply((dynamic)Filter("invert"));
        Float4[] inverted = canvas.ToArray();

        Assert.Equal(2, (int)canvas.History.UndoCount);

        Assert.True(canvas.Undo());
        AssertClose(stroked, canvas.ToArray());
        Assert.True(canvas.Undo());
        AssertClose(blank, canvas.ToArray());
        Assert.False(canvas.Undo());

        Assert.True(canvas.Redo());
        AssertClose(stroked, canvas.ToArray());
        Assert.True(canvas.Redo());
        AssertClose(inverted, canvas.ToArray());
        Assert.False(canvas.Redo());

        // With 3 steps, the 4th edit pushes the blank canvas out of the ring.
        canvas.Apply((dynamic)Filter("grayscale"));
        canvas.Apply((dynamic)Filter("invert"));
        Assert.Equal(3, (int)canvas.History.UndoCount);

        for (int i = 0; i < 3; i++)
        {
            Assert.True(canvas.Undo());
        }

        Assert.False(canvas.Undo());
        AssertClose(stroked, canvas.ToArray());
    }

    [SampleFact(Sample)]
    public void ScriptKeys_ParseToTheSameEventsAsTheWindow()
    {
        Assert.Equal((Key.Z, 'z', KeyModifiers.Command | KeyModifiers.Shift), ParseKey("cmd+shift+z"));
        Assert.Equal((Key.B, 'B', KeyModifiers.None), ParseKey("B"));
        Assert.Equal((Key.Unknown, ']', KeyModifiers.None), ParseKey("]"));
        Assert.Equal((Key.D3, '3', KeyModifiers.None), ParseKey("3"));
        Assert.Equal((Key.Unknown, '+', KeyModifiers.None), ParseKey("+"));
        Assert.Throws<FormatException>(() => ParseKey("hyper+z"));
    }

    private static (Key, char, KeyModifiers) ParseKey(string text) =>
        ((Key, char, KeyModifiers))SampleAssembly.Call(Sample, "PaintScript", "ParseKey", text)!;

    private static float Coverage(float distance, float radius, float hardness) =>
        (float)SampleAssembly.Call(Sample, "BrushStamp", "Coverage", distance, radius, hardness)!;

    private static float Distance(Float2 a, Float2 b) => MathF.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static dynamic Brush(float size, float hardness, float opacity, Float3 color)
    {
        dynamic brush = SampleAssembly.Create(Sample, "BrushSettings");
        brush.Size = size;
        brush.Hardness = hardness;
        brush.Opacity = opacity;
        brush.Color = color;
        return brush;
    }

    private static void Stroke(object canvas, object brush, params Float2[] points) =>
        SampleAssembly.Call(Sample, "StrokeExtensions", "StrokePolyline", canvas, (IReadOnlyList<Float2>)points, brush);

    private static object Filter(string name)
    {
        object?[] args = [name, (IReadOnlyList<string>)[], null, null];
        Assert.True((bool)SampleAssembly.Call(Sample, "SharpCompute.Samples.Imaging.FilterSpec", "TryParse", args)!);
        return args[2]!;
    }

    /// <summary>Undo restores 8-bit snapshots, so values come back rounded to the nearest 1/255.</summary>
    private static void AssertClose(Float4[] expected, Float4[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);

        for (int i = 0; i < expected.Length; i++)
        {
            Float4 d = actual[i] - expected[i];
            float max = MathF.Max(MathF.Max(MathF.Abs(d.X), MathF.Abs(d.Y)), MathF.Max(MathF.Abs(d.Z), MathF.Abs(d.W)));
            Assert.True(max <= (0.5f / 255f) + 1e-6f, $"Pixel {i}: expected {expected[i]}, got {actual[i]}");
        }
    }

    private static int Undo(dynamic ring) => ring.TryUndo(out int slot) ? slot : -1;

    private static int Redo(dynamic ring) => ring.TryRedo(out int slot) ? slot : -1;
}
