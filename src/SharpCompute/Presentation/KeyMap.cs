namespace SharpCompute.Presentation;

/// <summary>Maps AppKit key codes and characters to <see cref="Key"/>.</summary>
internal static class KeyMap
{
    /// <summary>
    /// Named keys by virtual key code (<c>kVK_*</c> in HIToolbox's Events.h). These keys don't move
    /// between keyboard layouts, so the physical code is the right source.
    /// </summary>
    internal static Key FromKeyCode(ushort keyCode) => keyCode switch
    {
        0x24 => Key.Enter,
        0x4C => Key.Enter, // keypad Enter
        0x30 => Key.Tab,
        0x31 => Key.Space,
        0x33 => Key.Backspace,
        0x35 => Key.Escape,
        0x73 => Key.Home,
        0x74 => Key.PageUp,
        0x75 => Key.Delete,
        0x77 => Key.End,
        0x79 => Key.PageDown,
        0x7B => Key.Left,
        0x7C => Key.Right,
        0x7D => Key.Down,
        0x7E => Key.Up,
        _ => Key.Unknown,
    };

    /// <summary>Letters and digits by the character the layout produces (case-insensitive).</summary>
    internal static Key FromCharacter(char c) => c switch
    {
        >= 'a' and <= 'z' => Key.A + (c - 'a'),
        >= 'A' and <= 'Z' => Key.A + (c - 'A'),
        >= '0' and <= '9' => Key.D0 + (c - '0'),
        _ => Key.Unknown,
    };

    /// <summary>
    /// The character to report for a key event: the first UTF-16 unit, or <c>'\0'</c> when AppKit
    /// reports a function-key code point (the private-use block U+F700–U+F8FF) or nothing.
    /// </summary>
    internal static char ToCharacter(string? characters) =>
        string.IsNullOrEmpty(characters) || characters[0] is >= '' and <= '' ? '\0' : characters[0];

    internal static Key Resolve(ushort keyCode, char character)
    {
        Key named = FromKeyCode(keyCode);
        return named != Key.Unknown ? named : FromCharacter(character);
    }
}
