// Original SharpCompute sample (MIT).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SharpCompute.Presentation;

namespace SharpCompute.Samples.Paint;

/// <summary>
/// A scripted paint session for the screenshot hook (<c>SHARPCOMPUTE_PAINT_SCRIPT</c>): a JSON file
/// whose <c>actions</c> run through the same <see cref="PaintController"/> the window uses.
/// </summary>
/// <remarks>
/// <code>
/// {
///   "output": "paint.png",                          // the canvas; SHARPCOMPUTE_PAINT_OUTPUT overrides
///   "view": "paint-view.png",                       // optional: the window view (HUD, checkerboard)
///   "actions": [
///     { "keys": ["]", "]", "3", "'"] },             // key presses: a character, a letter, or "cmd+shift+z"
///     { "brush": { "size": 80, "hardness": 0.2, "opacity": 0.5, "color": "#3366ff", "erase": false } },
///     { "stroke": [[100, 200], [400, 260], [700, 240]] },  // a drag in canvas pixels
///     { "stroke": [[500, 500], [520, 700]], "button": "right" },
///     { "pick": [960, 400] }                        // press H, click the color wheel there
///   ]
/// }
/// </code>
/// </remarks>
public sealed class PaintScript
{
    private PaintScript(string output, string? view, List<Action<PaintController>> actions)
    {
        Output = output;
        View = view;
        Actions = actions;
    }

    /// <summary>The canvas PNG to write when the script finishes (full path).</summary>
    public string Output { get; }

    /// <summary>Where to also save the window view (canvas over the checkerboard, plus the HUD), or null.</summary>
    public string? View { get; }

    /// <summary>The steps, in order; the window replay runs one per frame.</summary>
    public IReadOnlyList<Action<PaintController>> Actions { get; }

    public static PaintScript Load(string path, string? outputOverride = null)
    {
        string fullPath = Path.GetFullPath(path);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(fullPath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement root = document.RootElement;

        // Relative paths are relative to the working directory, like command-line arguments.
        string output = Path.GetFullPath(outputOverride ?? (root.TryGetProperty("output", out JsonElement o) ? o.GetString()! : Path.GetFileNameWithoutExtension(fullPath) + ".png"));
        string? view = root.TryGetProperty("view", out JsonElement v) ? Path.GetFullPath(v.GetString()!) : null;

        if (outputOverride is not null && view is not null)
        {
            // Keep the view next to an overridden output.
            view = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + "-view.png");
        }

        List<Action<PaintController>> actions = [];

        foreach (JsonElement action in root.GetProperty("actions").EnumerateArray())
        {
            actions.Add(ParseAction(action));
        }

        return new PaintScript(output, view, actions);
    }

    /// <summary>Runs every action and saves the result.</summary>
    public string Run(PaintController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        foreach (Action<PaintController> action in Actions)
        {
            action(controller);
        }

        return Finish(controller);
    }

    /// <summary>Saves the canvas (and the view, if requested) and returns the canvas path.</summary>
    public string Finish(PaintController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        if (View is not null)
        {
            controller.SaveView(View);
        }

        return controller.Save(Output);
    }

    /// <summary>Parses "]", "b", "B", "cmd+z", "cmd+shift+z" into a key event.</summary>
    public static (Key Key, char Character, KeyModifiers Modifiers) ParseKey(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        KeyModifiers modifiers = KeyModifiers.None;
        string rest = text;

        while (rest.Length > 1 && rest.IndexOf('+', 1) is int plus and > 0)
        {
            modifiers |= rest[..plus].ToLowerInvariant() switch
            {
                "cmd" or "command" => KeyModifiers.Command,
                "shift" => KeyModifiers.Shift,
                "ctrl" or "control" => KeyModifiers.Control,
                "alt" or "option" => KeyModifiers.Option,
                string other => throw new FormatException($"Unknown modifier '{other}' in key '{text}'."),
            };
            rest = rest[(plus + 1)..];
        }

        if (rest.Length != 1)
        {
            throw new FormatException($"Expected a single key character in '{text}'.");
        }

        char c = rest[0];
        Key key = char.ToUpperInvariant(c) switch
        {
            >= 'A' and <= 'Z' and char letter => Key.A + (letter - 'A'),
            >= '0' and <= '9' and char digit => Key.D0 + (digit - '0'),
            _ => Key.Unknown,
        };

        return (key, c, modifiers);
    }

    private static Action<PaintController> ParseAction(JsonElement action)
    {
        if (action.TryGetProperty("keys", out JsonElement keys))
        {
            List<(Key, char, KeyModifiers)> parsed = [];

            foreach (JsonElement k in keys.EnumerateArray())
            {
                parsed.Add(ParseKey(k.GetString()!));
            }

            return controller =>
            {
                foreach ((Key key, char character, KeyModifiers modifiers) in parsed)
                {
                    string? message = controller.HandleKey(key, character, modifiers);

                    if (message is not null)
                    {
                        Console.WriteLine(message);
                    }
                }
            };
        }

        if (action.TryGetProperty("brush", out JsonElement brush))
        {
            float? size = brush.TryGetProperty("size", out JsonElement s) ? s.GetSingle() : null;
            float? hardness = brush.TryGetProperty("hardness", out JsonElement h) ? h.GetSingle() : null;
            float? opacity = brush.TryGetProperty("opacity", out JsonElement o) ? o.GetSingle() : null;
            bool? erase = brush.TryGetProperty("erase", out JsonElement e) ? e.GetBoolean() : null;
            Float3? color = null;

            if (brush.TryGetProperty("color", out JsonElement c))
            {
                color = BrushSettings.TryParseColor(c.GetString()!, out Float3 parsed) ? parsed : throw new FormatException($"Invalid color '{c.GetString()}': expected #rrggbb.");
            }

            return controller =>
            {
                BrushSettings b = controller.Brush;
                b.Size = size ?? b.Size;
                b.Hardness = hardness ?? b.Hardness;
                b.Opacity = opacity ?? b.Opacity;
                b.Color = color ?? b.Color;
                b.Erase = erase ?? b.Erase;
            };
        }

        if (action.TryGetProperty("stroke", out JsonElement stroke))
        {
            List<(double X, double Y)> points = [];

            foreach (JsonElement point in stroke.EnumerateArray())
            {
                points.Add((point[0].GetDouble(), point[1].GetDouble()));
            }

            bool right = action.TryGetProperty("button", out JsonElement button) && button.GetString() == "right";

            if (points.Count == 0)
            {
                throw new FormatException("A stroke needs at least one point.");
            }

            return controller =>
            {
                controller.PointerDown(points[0].X, points[0].Y, right);

                for (int i = 1; i < points.Count; i++)
                {
                    controller.PointerDrag(points[i].X, points[i].Y);
                }

                controller.PointerUp();
            };
        }

        if (action.TryGetProperty("pick", out JsonElement pick))
        {
            double x = pick[0].GetDouble();
            double y = pick[1].GetDouble();

            return controller =>
            {
                if (!controller.Picking)
                {
                    controller.HandleKey(Key.H, 'h', KeyModifiers.None);
                }

                controller.PointerDown(x, y);
                controller.PointerUp();
            };
        }

        throw new FormatException($"Unknown script action: {action.GetRawText()}");
    }
}
