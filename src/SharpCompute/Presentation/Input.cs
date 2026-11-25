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

/// <summary>
/// Data for <see cref="MetalWindow.MouseWheel"/>: one step of a scroll wheel, or one sample of a
/// two-finger trackpad or Magic Mouse scroll.
/// </summary>
/// <remarks>
/// <para>
/// <b>Direction.</b> (<see cref="DeltaX"/>, <see cref="DeltaY"/>) is how far the content under the
/// pointer should move, in the same top-left-origin space as <see cref="X"/> and <see cref="Y"/>:
/// a positive <see cref="DeltaY"/> moves content down (the view scrolls toward its top) and a
/// positive <see cref="DeltaX"/> moves content right, so panning is <c>offset += delta</c>. Apps
/// conventionally also treat a positive <see cref="DeltaY"/> as "zoom in" or "bigger".
/// </para>
/// <para>
/// <b>Natural scrolling is already applied.</b> Which physical motion gives a positive
/// <see cref="DeltaY"/> depends on "Natural scrolling" in System Settings, and macOS applies that
/// setting before the event is delivered: with it on, two fingers moving down (or a wheel rotated
/// toward you) are positive; with it off, a wheel rotated away from you (or two fingers moving up)
/// is. Either way the deltas mean "move the content this way", so never invert them again to
/// honor the setting.
/// </para>
/// <para>
/// <b>Units.</b> When <see cref="IsPrecise"/> is true the deltas are drawable pixels (AppKit's
/// points × the display's scale factor, like the pointer position) and many small events arrive
/// per gesture. When it is false they are lines: a notched wheel reports about one event per
/// notch, but macOS scales the value by how fast the wheel turns, so a single slow notch can be as
/// small as 0.1 and a fast spin several lines. An app that wants one step per notch should
/// act on the sign (or round away from zero) rather than truncate.
/// </para>
/// <para>
/// <b>Shift.</b> For a wheel that only scrolls vertically, macOS turns Shift + wheel into
/// horizontal scrolling: the delta arrives in <see cref="DeltaX"/> and <see cref="DeltaY"/> is 0.
/// Trackpads are not remapped. A Shift + wheel shortcut should therefore read whichever delta is
/// larger in magnitude.
/// </para>
/// </remarks>
public sealed class MouseWheelEventArgs(double x, double y, float deltaX, float deltaY, bool isPrecise, bool isMomentum, KeyModifiers modifiers) : EventArgs
{
    /// <summary>The pointer's horizontal position in drawable pixels, as in <see cref="MouseEventArgs"/>.</summary>
    public double X { get; } = x;

    /// <summary>The pointer's vertical position in drawable pixels, counted from the top of the content area.</summary>
    public double Y { get; } = y;

    /// <summary>Horizontal scroll amount; positive moves content right. See the class remarks for units and direction.</summary>
    public float DeltaX { get; } = deltaX;

    /// <summary>Vertical scroll amount; positive moves content down. See the class remarks for units and direction.</summary>
    public float DeltaY { get; } = deltaY;

    /// <summary>
    /// True when the deltas are pixel-precise (a trackpad or Magic Mouse; AppKit's
    /// <c>hasPreciseScrollingDeltas</c>), false when they are line counts from a notched wheel.
    /// </summary>
    public bool IsPrecise { get; } = isPrecise;

    /// <summary>
    /// True when the event is inertia: the fingers have left the trackpad and macOS keeps sending
    /// decaying deltas. Apps that want scrolling to stop the moment the fingers lift (a value
    /// adjusted with the wheel, a camera dolly) skip these. Always false for a notched wheel.
    /// </summary>
    public bool IsMomentum { get; } = isMomentum;

    /// <summary>The modifier keys held when the event occurred.</summary>
    public KeyModifiers Modifiers { get; } = modifiers;
}
