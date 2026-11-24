// Original SharpCompute sample (MIT).
using System;

namespace SharpCompute.Samples.Paint;

/// <summary>
/// The bookkeeping for undo/redo over a fixed ring of snapshot slots; the caller owns the slot
/// storage. Every edit pushes the state <i>after</i> it, so the ring always holds the current state
/// plus up to <see cref="Steps"/> earlier ones (<see cref="SlotCount"/> = steps + 1 slots). Undo
/// and redo move through the ring and name the slot to restore. Pushing after an undo discards the
/// redo states; pushing into a full ring overwrites the oldest state.
/// </summary>
public sealed class UndoRing
{
    // Logical positions grow without bound; a position's slot is position % SlotCount.
    private long _oldest;
    private long _current = -1;
    private long _newest = -1;

    public UndoRing(int steps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(steps);
        Steps = steps;
    }

    /// <summary>How many edits can be undone when the ring is full.</summary>
    public int Steps { get; }

    public int SlotCount => Steps + 1;

    public bool IsEmpty => _current < 0;

    public int UndoCount => IsEmpty ? 0 : (int)(_current - _oldest);

    public int RedoCount => IsEmpty ? 0 : (int)(_newest - _current);

    public bool CanUndo => UndoCount > 0;

    public bool CanRedo => RedoCount > 0;

    /// <summary>The slot holding the current state, or -1 before the first <see cref="Push"/>.</summary>
    public int CurrentSlot => IsEmpty ? -1 : Slot(_current);

    /// <summary>Records a new current state and returns the slot to store it in.</summary>
    public int Push()
    {
        _current++;
        _newest = _current;
        _oldest = Math.Max(_oldest, _newest - Steps);
        return Slot(_current);
    }

    /// <summary>Steps back one state; <paramref name="slot"/> is the slot to restore.</summary>
    public bool TryUndo(out int slot)
    {
        if (!CanUndo)
        {
            slot = -1;
            return false;
        }

        _current--;
        slot = Slot(_current);
        return true;
    }

    /// <summary>Steps forward one state after an undo; <paramref name="slot"/> is the slot to restore.</summary>
    public bool TryRedo(out int slot)
    {
        if (!CanRedo)
        {
            slot = -1;
            return false;
        }

        _current++;
        slot = Slot(_current);
        return true;
    }

    /// <summary>Forgets every state.</summary>
    public void Clear() => (_oldest, _current, _newest) = (0, -1, -1);

    private int Slot(long position) => (int)(position % SlotCount);
}
