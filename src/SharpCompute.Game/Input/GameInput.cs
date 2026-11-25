using System;
using System.Numerics;
using SharpCompute.Presentation;

namespace SharpCompute.Game;

/// <summary>
/// Keyboard and mouse state as a fixed-timestep game wants it: sampled once per simulation step,
/// with pressed and released edges. Raw events arrive whenever the window delivers them;
/// <see cref="BeginStep"/> turns everything since the previous step into one consistent snapshot,
/// which the queries then report until the next step.
/// </summary>
/// <remarks>
/// <para>
/// <b>No lost taps.</b> A key pressed and released between two steps (a quick tap, or any tap
/// during a slow frame) is never seen by polling. Here the press is latched: the next step reports
/// the key as down and pressed, and the step after that reports it released.
/// </para>
/// <para>
/// <b>Sources.</b> <see cref="Attach"/> listens to a <see cref="MetalWindow"/>. The same state can
/// be driven by hand with <see cref="Press(Key)"/>, <see cref="Release(Key)"/> and
/// <see cref="MoveMouse"/>: that is how headless runs, replays and tests feed input. When the
/// window loses focus it reports every held key as released, so nothing sticks.
/// </para>
/// <para>
/// With a <see cref="GameLoop"/>, set <see cref="GameLoop.Input"/> and the loop calls
/// <see cref="BeginStep"/> for you. Not thread-safe: window events and steps both happen on the
/// main thread.
/// </para>
/// </remarks>
public sealed class GameInput
{
    private const int MouseButtonCount = 4;
    private static readonly int s_keyCount = KeyCount();

    private readonly ButtonState[] _keys = new ButtonState[s_keyCount];
    private readonly ButtonState[] _buttons = new ButtonState[MouseButtonCount];
    private Vector2 _liveMouse;
    private Vector2 _stepMouse;
    private bool _liveMouseSeen;

    /// <summary>Input with nothing held, fed by <see cref="Press(Key)"/> and friends.</summary>
    public GameInput() => Actions = new ActionMap(this);

    /// <summary>Input that follows <paramref name="window"/>.</summary>
    public GameInput(MetalWindow window)
        : this() => Attach(window);

    /// <summary>Named actions bound to keys and buttons.</summary>
    public ActionMap Actions { get; }

    /// <summary>The pointer position as of the current step, in the window's drawable pixels (top-left origin).</summary>
    public Vector2 MousePosition => _stepMouse;

    /// <summary>How far the pointer moved between the previous step and this one.</summary>
    public Vector2 MouseDelta { get; private set; }

    /// <summary>True once the pointer has reported a position at all (it has not until it first moves or clicks).</summary>
    public bool HasMousePosition { get; private set; }

    /// <summary>
    /// Subscribes to <paramref name="window"/>'s key and mouse events. Auto-repeat is ignored: a held
    /// key is one press.
    /// </summary>
    public void Attach(MetalWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.KeyDown += (_, e) =>
        {
            if (!e.IsRepeat)
            {
                Press(e.Key);
            }
        };
        window.KeyUp += (_, e) => Release(e.Key);
        window.MouseDown += (_, e) =>
        {
            MoveMouse((float)e.X, (float)e.Y);
            Press(e.Button);
        };
        window.MouseUp += (_, e) =>
        {
            MoveMouse((float)e.X, (float)e.Y);
            Release(e.Button);
        };
        window.MouseMoved += (_, e) => MoveMouse((float)e.X, (float)e.Y);
        window.MouseDragged += (_, e) => MoveMouse((float)e.X, (float)e.Y);
    }

    /// <summary>Records that <paramref name="key"/> went down. <see cref="Key.Unknown"/> is ignored.</summary>
    public void Press(Key key)
    {
        if (IsTracked(key))
        {
            _keys[(int)key].Press();
        }
    }

    /// <summary>Records that <paramref name="key"/> came up.</summary>
    public void Release(Key key)
    {
        if (IsTracked(key))
        {
            _keys[(int)key].Live = false;
        }
    }

    /// <summary>Records that a mouse button went down. <see cref="MouseButton.None"/> is ignored.</summary>
    public void Press(MouseButton button)
    {
        if (IsTracked(button))
        {
            _buttons[(int)button].Press();
        }
    }

    /// <summary>Records that a mouse button came up.</summary>
    public void Release(MouseButton button)
    {
        if (IsTracked(button))
        {
            _buttons[(int)button].Live = false;
        }
    }

    /// <summary>Records the pointer position, in drawable pixels.</summary>
    public void MoveMouse(float x, float y)
    {
        _liveMouse = new Vector2(x, y);
        _liveMouseSeen = true;
    }

    /// <summary>Releases every key and button (the game lost focus, a menu opened).</summary>
    public void ReleaseAll()
    {
        foreach (ref ButtonState key in _keys.AsSpan())
        {
            key.Live = false;
            key.Latched = false;
        }

        foreach (ref ButtonState button in _buttons.AsSpan())
        {
            button.Live = false;
            button.Latched = false;
        }
    }

    /// <summary>
    /// Starts a simulation step: everything that happened since the previous call becomes the state
    /// the queries report. Call it once per step, before the game reads input.
    /// </summary>
    public void BeginStep()
    {
        foreach (ref ButtonState key in _keys.AsSpan())
        {
            key.Step();
        }

        foreach (ref ButtonState button in _buttons.AsSpan())
        {
            button.Step();
        }

        // The first known position is not a movement.
        MouseDelta = HasMousePosition ? _liveMouse - _stepMouse : Vector2.Zero;
        _stepMouse = _liveMouse;
        HasMousePosition = _liveMouseSeen;
    }

    /// <summary>
    /// Forgets presses that no step has seen yet, keeping what is still held. A paused game calls
    /// this so a key tapped during the pause does not act on the first step after it.
    /// </summary>
    public void DiscardPresses()
    {
        foreach (ref ButtonState key in _keys.AsSpan())
        {
            key.Latched = false;
        }

        foreach (ref ButtonState button in _buttons.AsSpan())
        {
            button.Latched = false;
        }
    }

    /// <summary>True while <paramref name="key"/> is held this step, or was tapped since the previous one.</summary>
    public bool IsDown(Key key) => IsTracked(key) && _keys[(int)key].Down;

    /// <summary>True on the step <paramref name="key"/> went down.</summary>
    public bool WasPressed(Key key) => IsTracked(key) && _keys[(int)key].Pressed;

    /// <summary>True on the step <paramref name="key"/> came up.</summary>
    public bool WasReleased(Key key) => IsTracked(key) && _keys[(int)key].Released;

    /// <summary>True while <paramref name="button"/> is held this step, or was clicked since the previous one.</summary>
    public bool IsDown(MouseButton button) => IsTracked(button) && _buttons[(int)button].Down;

    /// <summary>True on the step <paramref name="button"/> went down.</summary>
    public bool WasPressed(MouseButton button) => IsTracked(button) && _buttons[(int)button].Pressed;

    /// <summary>True on the step <paramref name="button"/> came up.</summary>
    public bool WasReleased(MouseButton button) => IsTracked(button) && _buttons[(int)button].Released;

    private static bool IsTracked(Key key) => key > Key.Unknown && (int)key < s_keyCount;

    private static bool IsTracked(MouseButton button) => button >= 0 && (int)button < MouseButtonCount;

    private static int KeyCount()
    {
        int max = 0;

        foreach (Key key in Enum.GetValues<Key>())
        {
            max = Math.Max(max, (int)key);
        }

        return max + 1;
    }

    /// <summary>One key or button: the live state from events, and the snapshot of the current step.</summary>
    private struct ButtonState
    {
        /// <summary>Held right now, per the events.</summary>
        public bool Live;

        /// <summary>Went down since the last step (kept even if it came up again).</summary>
        public bool Latched;
        public bool Down;
        public bool Pressed;
        public bool Released;

        public void Press()
        {
            Live = true;
            Latched = true;
        }

        public void Step()
        {
            bool was = Down;
            Down = Live || Latched;
            Pressed = Latched || (Down && !was);
            Released = was && !Down;
            Latched = false;
        }
    }
}
