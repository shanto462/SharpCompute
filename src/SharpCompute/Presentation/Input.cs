using System;

namespace SharpCompute.Presentation;

/// <summary>
/// A keyboard key. Named keys (arrows, Escape, ...) come from the physical key code; letters and
/// digits come from the character the active keyboard layout produces, so <see cref="Q"/> is the
/// key labelled Q on an AZERTY keyboard too.
/// </summary>
public enum Key
{
    /// <summary>A key with no entry in this enum; check <see cref="KeyEventArgs.Character"/>.</summary>
    Unknown = 0,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    Left, Right, Up, Down,
    Escape, Space, Enter, Tab, Backspace, Delete,
    Home, End, PageUp, PageDown,
}

/// <summary>Modifier keys held during an input event.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Option = 4,
    Command = 8,
}

/// <summary>A mouse button.</summary>
public enum MouseButton
{
    Left,
    Right,
    Middle,

    /// <summary>Any button beyond the third.</summary>
    Other,

    /// <summary>No button: the pointer moved with no button held (<see cref="MetalWindow.MouseMoved"/>).</summary>
    None = -1,
}

/// <summary>Data for <see cref="MetalWindow.KeyDown"/> and <see cref="MetalWindow.KeyUp"/>.</summary>
public sealed class KeyEventArgs(Key key, char character, KeyModifiers modifiers, bool isRepeat) : EventArgs
{
    /// <summary>The key, or <see cref="Key.Unknown"/>.</summary>
    public Key Key { get; } = key;

    /// <summary>
    /// The character the key produces with Shift applied but other modifiers ignored, or <c>'\0'</c>
    /// for keys that produce none (arrows, function keys). For a key-up, the character recorded at
    /// the matching key-down.
    /// </summary>
    public char Character { get; } = character;

    /// <summary>The modifier keys held when the event occurred.</summary>
    public KeyModifiers Modifiers { get; } = modifiers;

    /// <summary>True when this event comes from the key being held down (auto-repeat). Always false for a key-up.</summary>
    public bool IsRepeat { get; } = isRepeat;
}

/// <summary>
/// Data for the mouse events of <see cref="MetalWindow"/>. Coordinates are in drawable pixels
/// (the same space as <see cref="MetalWindow.Width"/> and <see cref="MetalWindow.Height"/>), with
/// the origin at the top-left corner of the window's content area. A drag that leaves the window
/// reports positions outside <c>[0, Width) × [0, Height)</c>.
/// </summary>
public sealed class MouseEventArgs(double x, double y, MouseButton button, KeyModifiers modifiers) : EventArgs
{
    public double X { get; } = x;

    public double Y { get; } = y;

    public MouseButton Button { get; } = button;

    public KeyModifiers Modifiers { get; } = modifiers;
}
