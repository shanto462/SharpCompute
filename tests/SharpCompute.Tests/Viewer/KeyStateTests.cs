using SharpCompute.Presentation;

namespace SharpCompute.Tests.Viewer;

/// <summary>The held-key bookkeeping behind <see cref="MetalWindow.IsKeyDown"/>, without AppKit.</summary>
public class KeyStateTests
{
    // kVK_ANSI_D, kVK_ANSI_1, kVK_Return, kVK_ANSI_KeypadEnter, kVK_LeftArrow, kVK_F1.
    private const ushort CodeD = 0x02;
    private const ushort Code1 = 0x12;
    private const ushort CodeReturn = 0x24;
    private const ushort CodeKeypadEnter = 0x4C;
    private const ushort CodeLeft = 0x7B;
    private const ushort CodeF1 = 0x7A;

    [Fact]
    public void NothingIsHeldInitially()
    {
        KeyState state = new();

        Assert.Equal(0, state.Count);
        Assert.Equal(KeyModifiers.None, state.Modifiers);
        Assert.False(state.IsDown(Key.D));
        Assert.Empty(state.ReleaseAll());
    }

    [Fact]
    public void PressThenRelease_TogglesIsDown()
    {
        KeyState state = new();

        Assert.Equal(Key.D, state.Press(CodeD, Key.D, 'd', isRepeat: false, out HeldKey? replaced));
        Assert.Null(replaced);
        Assert.True(state.IsDown(Key.D));
        Assert.False(state.IsDown(Key.A));

        Assert.True(state.Release(CodeD, out HeldKey released));
        Assert.Equal(new HeldKey(CodeD, Key.D, 'd'), released);
        Assert.False(state.IsDown(Key.D));
        Assert.Equal(0, state.Count);
    }

    [Fact]
    public void Repeats_DontChangeState_AndReportTheKeyFromKeyDown()
    {
        KeyState state = new();
        state.Press(Code1, Key.D1, '1', isRepeat: false, out _);

        // Shift pressed while 1 is held: the repeats now carry '!', which resolves to Unknown.
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(Key.D1, state.Press(Code1, Key.Unknown, '!', isRepeat: true, out HeldKey? replaced));
            Assert.Null(replaced);
        }

        Assert.Equal(1, state.Count);
        Assert.True(state.IsDown(Key.D1));
        Assert.True(state.Release(Code1, out HeldKey released));
        Assert.Equal(Key.D1, released.Key);
        Assert.Equal('1', released.Character);
        Assert.False(state.IsDown(Key.D1));
    }

    [Fact]
    public void Release_UsesTheKeyRecordedAtKeyDown_NotTheKeyUpsCharacter()
    {
        // The window resolves a key-up only by code; whatever characters the key-up carries are ignored.
        KeyState state = new();
        state.Press(CodeD, Key.D, 'd', isRepeat: false, out _);

        Assert.True(state.Release(CodeD, out HeldKey released));
        Assert.Equal(Key.D, released.Key);
    }

    [Fact]
    public void Release_OfAKeyThatIsntHeld_ReturnsFalse()
    {
        KeyState state = new();
        Assert.False(state.Release(CodeD, out _));

        state.Press(CodeD, Key.D, 'd', isRepeat: false, out _);
        Assert.True(state.Release(CodeD, out _));
        Assert.False(state.Release(CodeD, out _));
    }

    [Fact]
    public void RepeatOfAnUntrackedKey_RecordsIt()
    {
        // Held down before the window had focus: the first event we see is a repeat.
        KeyState state = new();

        Assert.Equal(Key.Left, state.Press(CodeLeft, Key.Left, '\0', isRepeat: true, out HeldKey? replaced));
        Assert.Null(replaced);
        Assert.True(state.IsDown(Key.Left));
        Assert.True(state.Release(CodeLeft, out _));
    }

    [Fact]
    public void FreshKeyDownOfAHeldCode_WithTheSameKey_ChangesNothing()
    {
        KeyState state = new();
        state.Press(CodeD, Key.D, 'd', isRepeat: false, out _);

        Assert.Equal(Key.D, state.Press(CodeD, Key.D, 'd', isRepeat: false, out HeldKey? replaced));
        Assert.Null(replaced);
        Assert.Equal(1, state.Count);
    }

    [Fact]
    public void FreshKeyDownOfAHeldCode_WithADifferentKey_ReplacesItAndReportsTheLostKey()
    {
        // The key-up for the first press was lost and the layout changed before the next press.
        KeyState state = new();
        state.Press(CodeD, Key.D, 'd', isRepeat: false, out _);

        Assert.Equal(Key.E, state.Press(CodeD, Key.E, 'e', isRepeat: false, out HeldKey? replaced));
        Assert.Equal(new HeldKey(CodeD, Key.D, 'd'), replaced);
        Assert.False(state.IsDown(Key.D));
        Assert.True(state.IsDown(Key.E));
        Assert.Equal(1, state.Count);
    }

    [Fact]
    public void TwoCodesForOneKey_KeyStaysDownUntilBothAreReleased()
    {
        KeyState state = new();
        state.Press(CodeReturn, Key.Enter, '\r', isRepeat: false, out _);
        state.Press(CodeKeypadEnter, Key.Enter, '\u0003', isRepeat: false, out _);

        Assert.True(state.Release(CodeReturn, out _));
        Assert.True(state.IsDown(Key.Enter));
        Assert.True(state.Release(CodeKeypadEnter, out _));
        Assert.False(state.IsDown(Key.Enter));
    }

    [Fact]
    public void UnknownKeys_AreTrackedForRelease_ButNeverReportedDown()
    {
        KeyState state = new();
        state.Press(CodeF1, Key.Unknown, '\0', isRepeat: false, out _);

        Assert.Equal(1, state.Count);
        Assert.False(state.IsDown(Key.Unknown));
        Assert.True(state.Release(CodeF1, out HeldKey released));
        Assert.Equal(Key.Unknown, released.Key);
    }

    [Fact]
    public void ReleaseAll_ReturnsHeldKeysInPressOrder_AndClearsKeysAndModifiers()
    {
        KeyState state = new();
        state.Press(CodeLeft, Key.Left, '\0', isRepeat: false, out _);
        state.Press(CodeD, Key.D, 'd', isRepeat: false, out _);
        state.Press(Code1, Key.D1, '1', isRepeat: false, out _);
        state.Release(CodeD, out _);
        state.Modifiers = KeyModifiers.Shift | KeyModifiers.Option;

        HeldKey[] released = state.ReleaseAll();

        Assert.Equal([new HeldKey(CodeLeft, Key.Left, '\0'), new HeldKey(Code1, Key.D1, '1')], released);
        Assert.Equal(0, state.Count);
        Assert.Equal(KeyModifiers.None, state.Modifiers);
        Assert.False(state.IsDown(Key.Left));
        Assert.False(state.IsDown(Key.D1));

        // The key-ups that arrive later (if focus comes back first) match nothing.
        Assert.False(state.Release(CodeLeft, out _));
        Assert.Empty(state.ReleaseAll());
    }

    [Fact]
    public void Modifiers_AreIndependentOfKeys()
    {
        KeyState state = new();
        state.Modifiers = KeyModifiers.Shift;
        state.Press(CodeD, Key.D, 'D', isRepeat: false, out _);
        state.Release(CodeD, out _);

        Assert.Equal(KeyModifiers.Shift, state.Modifiers);

        state.Modifiers = KeyModifiers.None;
        Assert.Equal(KeyModifiers.None, state.Modifiers);
    }
}
