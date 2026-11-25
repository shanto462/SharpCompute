using System;
using System.Collections.Generic;
using SharpCompute.Presentation;

namespace SharpCompute.Game;

/// <summary>
/// Names for what the player does ("jump", "fire") bound to the keys and mouse buttons that do it,
/// so game code asks <c>actions.WasPressed("jump")</c> and rebinding is a matter of data. Reached
/// through <see cref="GameInput.Actions"/>; the answers are those of the input's current step.
/// </summary>
/// <remarks>
/// An action is down while any of its bindings is down, pressed on a step where any binding was
/// pressed, and released on the step its last held binding came up. Asking about a name that was
/// never bound throws: a misspelt action should not quietly read as "not pressed". Names are
/// case-sensitive. Not thread-safe.
/// </remarks>
public sealed class ActionMap
{
    private readonly GameInput _input;
    private readonly Dictionary<string, Binding> _bindings = new(StringComparer.Ordinal);

    internal ActionMap(GameInput input) => _input = input;

    /// <summary>The bound action names.</summary>
    public IReadOnlyCollection<string> Names => _bindings.Keys;

    /// <summary>Adds <paramref name="keys"/> to <paramref name="action"/>, creating it if needed.</summary>
    /// <exception cref="ArgumentException"><paramref name="action"/> is empty, or a key is <see cref="Key.Unknown"/>.</exception>
    public void Bind(string action, params ReadOnlySpan<Key> keys)
    {
        Binding binding = GetOrAdd(action);

        foreach (Key key in keys)
        {
            if (key == Key.Unknown)
            {
                throw new ArgumentException("Key.Unknown cannot be bound to an action.", nameof(keys));
            }

            if (!binding.Keys.Contains(key))
            {
                binding.Keys.Add(key);
            }
        }
    }

    /// <summary>Adds mouse <paramref name="buttons"/> to <paramref name="action"/>, creating it if needed.</summary>
    /// <exception cref="ArgumentException"><paramref name="action"/> is empty, or a button is <see cref="MouseButton.None"/>.</exception>
    public void Bind(string action, params ReadOnlySpan<MouseButton> buttons)
    {
        Binding binding = GetOrAdd(action);

        foreach (MouseButton button in buttons)
        {
            if (button == MouseButton.None)
            {
                throw new ArgumentException("MouseButton.None cannot be bound to an action.", nameof(buttons));
            }

            if (!binding.Buttons.Contains(button))
            {
                binding.Buttons.Add(button);
            }
        }
    }

    /// <summary>Removes <paramref name="action"/> and its bindings. Returns false if it was not bound.</summary>
    public bool Unbind(string action) => action is not null && _bindings.Remove(action);

    /// <summary>True when <paramref name="action"/> has been bound.</summary>
    public bool IsBound(string action) => action is not null && _bindings.ContainsKey(action);

    /// <summary>True while any binding of <paramref name="action"/> is down this step.</summary>
    /// <exception cref="ArgumentException">No such action is bound.</exception>
    public bool IsDown(string action) => IsDown(Get(action));

    /// <summary>True on a step where a binding of <paramref name="action"/> went down.</summary>
    /// <exception cref="ArgumentException">No such action is bound.</exception>
    public bool WasPressed(string action)
    {
        Binding binding = Get(action);

        foreach (Key key in binding.Keys)
        {
            if (_input.WasPressed(key))
            {
                return true;
            }
        }

        foreach (MouseButton button in binding.Buttons)
        {
            if (_input.WasPressed(button))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True on the step the last held binding of <paramref name="action"/> came up.</summary>
    /// <exception cref="ArgumentException">No such action is bound.</exception>
    public bool WasReleased(string action)
    {
        Binding binding = Get(action);

        if (IsDown(binding))
        {
            return false;
        }

        foreach (Key key in binding.Keys)
        {
            if (_input.WasReleased(key))
            {
                return true;
            }
        }

        foreach (MouseButton button in binding.Buttons)
        {
            if (_input.WasReleased(button))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsDown(Binding binding)
    {
        foreach (Key key in binding.Keys)
        {
            if (_input.IsDown(key))
            {
                return true;
            }
        }

        foreach (MouseButton button in binding.Buttons)
        {
            if (_input.IsDown(button))
            {
                return true;
            }
        }

        return false;
    }

    private Binding GetOrAdd(string action)
    {
        ArgumentException.ThrowIfNullOrEmpty(action);

        if (!_bindings.TryGetValue(action, out Binding? binding))
        {
            _bindings[action] = binding = new Binding();
        }

        return binding;
    }

    private Binding Get(string action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _bindings.TryGetValue(action, out Binding? binding)
            ? binding
            : throw new ArgumentException($"No action named '{action}' is bound. Bound actions: {string.Join(", ", _bindings.Keys)}.", nameof(action));
    }

    private sealed class Binding
    {
        public List<Key> Keys { get; } = [];

        public List<MouseButton> Buttons { get; } = [];
    }
}
