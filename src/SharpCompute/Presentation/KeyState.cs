using System.Collections.Generic;

namespace SharpCompute.Presentation;

/// <summary>
/// Which keys and modifiers are held, kept separately from AppKit so the bookkeeping can be tested
/// without a window. <see cref="MetalWindow"/> feeds it key events and focus changes and raises its
/// events from the results.
/// </summary>
/// <remarks>
/// <para>
/// Held keys are tracked by physical key code, not by <see cref="Key"/>: letters and digits are
/// resolved from the character the layout produces, and a key-up can report a different character
/// than its key-down (Shift pressed in between turns <c>1</c> into <c>!</c>). Recording the
/// <see cref="Key"/> at key-down and looking it up by code on key-up guarantees the release matches
/// the press. Several codes can map to one <see cref="Key"/> (Return and keypad Enter are both
/// <see cref="Key.Enter"/>); the key counts as down while any of them is held.
/// </para>
/// <para>
/// Not thread-safe: <see cref="MetalWindow"/> only touches it on the main thread.
/// </para>
/// </remarks>
internal sealed class KeyState
{
    // A handful of keys at most (keyboards roll over at ~6-10), so a list beats a dictionary and
    // keeps press order, which ReleaseAll reports in.
    private readonly List<HeldKey> _held = [];

    /// <summary>The modifier keys held right now.</summary>
    public KeyModifiers Modifiers { get; set; }

    /// <summary>The number of physical keys held.</summary>
    public int Count => _held.Count;

    /// <summary>
    /// True while any held key code was recorded as <paramref name="key"/>. Always false for
    /// <see cref="Key.Unknown"/>, which is "no key in the enum" rather than a key.
    /// </summary>
    public bool IsDown(Key key)
    {
        if (key == Key.Unknown)
        {
            return false;
        }

        foreach (HeldKey held in _held)
        {
            if (held.Key == key)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Records a key-down and returns the <see cref="Key"/> to report for it.
    /// </summary>
    /// <remarks>
    /// A repeat of a held code reports the <see cref="Key"/> recorded at its first key-down, so the
    /// key-down, its repeats and its key-up all agree even if the layout's character changed. A
    /// repeat of a code that isn't held (the key went down while another window had focus) records
    /// it: the key is evidently down and its key-up will follow. A fresh (non-repeat) key-down of a
    /// code that is already held means its key-up was lost; the code is re-recorded, and when that
    /// changes its <see cref="Key"/>, <paramref name="replaced"/> receives the old entry so the
    /// caller can raise the missing key-up and keep key-down/key-up pairs balanced.
    /// </remarks>
    public Key Press(ushort keyCode, Key key, char character, bool isRepeat, out HeldKey? replaced)
    {
        replaced = null;
        int index = IndexOf(keyCode);

        if (index >= 0)
        {
            HeldKey held = _held[index];

            if (isRepeat || held.Key == key)
            {
                return held.Key;
            }

            replaced = held;
            _held.RemoveAt(index);
        }

        _held.Add(new HeldKey(keyCode, key, character));
        return key;
    }

    /// <summary>
    /// Records a key-up. Returns false when <paramref name="keyCode"/> isn't held (pressed before
    /// the window had focus, or already released by <see cref="ReleaseAll"/>); otherwise
    /// <paramref name="released"/> is the entry recorded at key-down.
    /// </summary>
    public bool Release(ushort keyCode, out HeldKey released)
    {
        int index = IndexOf(keyCode);

        if (index < 0)
        {
            released = default;
            return false;
        }

        released = _held[index];
        _held.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Forgets every held key and modifier (focus loss: their key-ups will go to another app) and
    /// returns the keys that were held, in press order. The state is already clear when this returns,
    /// so handlers of the resulting key-up events see nothing held.
    /// </summary>
    public HeldKey[] ReleaseAll()
    {
        Modifiers = KeyModifiers.None;

        if (_held.Count == 0)
        {
            return [];
        }

        HeldKey[] released = [.. _held];
        _held.Clear();
        return released;
    }

    private int IndexOf(ushort keyCode)
    {
        for (int i = 0; i < _held.Count; i++)
        {
            if (_held[i].KeyCode == keyCode)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>A held key: its physical code and the <see cref="Key"/> and character recorded at key-down.</summary>
internal readonly record struct HeldKey(ushort KeyCode, Key Key, char Character);
