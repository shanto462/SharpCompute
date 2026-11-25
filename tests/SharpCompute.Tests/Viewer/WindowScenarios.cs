using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SharpCompute.Interop;
using SharpCompute.Presentation;

namespace SharpCompute.Tests.Viewer;

/// <summary>
/// The test assembly's entry point. Test runners never call it; it exists so a window test can
/// launch this assembly as a child process (<c>dotnet SharpCompute.Tests.dll &lt;scenario&gt;</c>)
/// and drive a <see cref="MetalWindow"/> on that process's main thread, which AppKit requires and a
/// test runner never provides. Replaces the empty entry point Microsoft.NET.Test.Sdk would generate
/// (see <c>GenerateProgramFile</c> in the project).
/// </summary>
public static class TestProgram
{
    /// <summary>Exit code: the scenario's window never received keyboard focus, so nothing was checked.</summary>
    internal const int NoFocus = 3;

    /// <summary>Exit code: this macOS wouldn't let the scenario build the input event it needs, so nothing was checked.</summary>
    internal const int CannotSynthesize = 4;

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "key-state" => WindowScenarios.KeyState(),
                "mouse-moved" => WindowScenarios.MouseMoved(),
                "mouse-wheel" => WindowScenarios.MouseWheel(),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    private static int Unknown(string scenario)
    {
        Console.Error.WriteLine($"Unknown scenario '{scenario}'.");
        return 2;
    }
}

/// <summary>Window scenarios that must run on the process main thread; see <see cref="TestProgram"/>.</summary>
internal static partial class WindowScenarios
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const double FocusTimeoutSeconds = 5;

    private const nuint EventKeyDown = 10;
    private const nuint EventKeyUp = 11;
    private const nuint EventFlagsChanged = 12;
    private const nuint ShiftFlag = 1 << 17;

    // kVK_ANSI_D, kVK_LeftArrow, kVK_Shift.
    private const ushort CodeD = 0x02;
    private const ushort CodeLeft = 0x7B;
    private const ushort CodeShift = 0x38;

    // CGScrollEventUnit, kCGScrollWheelEventMomentumPhase / kCGMomentumScrollPhaseContinue, and the
    // event record's window number (not in the public headers; see PostWheel).
    private const uint ScrollUnitPixel = 0;
    private const uint ScrollUnitLine = 1;
    private const uint FieldScrollWheelMomentumPhase = 123;
    private const long MomentumPhaseContinue = 2;
    private const uint FieldWindowNumber = 51;

    /// <summary>
    /// Feeds a real window synthetic key events through the AppKit event queue and checks the
    /// window's events and held state after each, then hides the app with a key held and checks the
    /// key is released. Returns 0, or <see cref="TestProgram.NoFocus"/> when the window never
    /// became key (macOS may refuse to activate an app launched in the background).
    /// </summary>
    internal static int KeyState()
    {
        using MetalWindow window = new(GraphicsDevice.GetDefault(), "SharpCompute key-state test", 320, 200);

        List<KeyEventArgs> downs = [];
        List<KeyEventArgs> ups = [];
        window.KeyDown += (_, e) => downs.Add(e);
        window.KeyUp += (_, e) => ups.Add(e);

        nint app = ObjC.SendIntPtr(ObjC.GetClass("NSApplication"), Sel("sharedApplication"));
        int step = 0;
        double stepStart = 0;
        bool focused = false;

        window.Run(frame =>
        {
            void Next()
            {
                step++;
                stepStart = frame.Time;
            }

            switch (step)
            {
                case 0:
                    if (ObjC.SendIntPtr(app, Sel("keyWindow")) != 0 && SendBool(app, Sel("isActive")))
                    {
                        focused = true;
                        Next();
                    }
                    else if (frame.Time > FocusTimeoutSeconds)
                    {
                        window.Close();
                    }

                    return;

                case 1:
                    // One focused frame has been polled; held modifiers are seeded from the hardware.
                    Check(Task.Run(() => Throws(() => window.IsKeyDown(Key.D))).Result, "IsKeyDown must throw off the main thread.");
                    Check(Task.Run(() => Throws(() => _ = window.HeldModifiers)).Result, "HeldModifiers must throw off the main thread.");
                    Check(!window.IsKeyDown(Key.D) && !window.IsKeyDown(Key.Unknown), "Nothing should be held yet.");
                    Post(app, window, EventKeyDown, 0, "d", isRepeat: false, CodeD);
                    Next();
                    return;

                case 2:
                    Check(window.IsKeyDown(Key.D), "D should be held after its key-down.");
                    Check(downs is [{ Key: Key.D, Character: 'd', IsRepeat: false }], $"Expected one KeyDown(D), got {Describe(downs)}.");
                    Post(app, window, EventKeyDown, 0, "d", isRepeat: true, CodeD);
                    Next();
                    return;

                case 3:
                    Check(window.IsKeyDown(Key.D), "A repeat must not change the held state.");
                    Check(downs is [_, { Key: Key.D, IsRepeat: true }], $"Expected a repeat KeyDown(D), got {Describe(downs)}.");
                    Check(ups.Count == 0, "A repeat must not raise KeyUp.");
                    Post(app, window, EventFlagsChanged, ShiftFlag, "", isRepeat: false, CodeShift);
                    Next();
                    return;

                case 4:
                    Check(window.HeldModifiers == KeyModifiers.Shift, $"Expected Shift held, got {window.HeldModifiers}.");

                    // The key-up's characters differ from the key-down's; the release must still be D.
                    Post(app, window, EventKeyUp, ShiftFlag, "X", isRepeat: false, CodeD);
                    Next();
                    return;

                case 5:
                    Check(!window.IsKeyDown(Key.D), "D should be released after its key-up.");
                    Check(ups is [{ Key: Key.D, Character: 'd', Modifiers: KeyModifiers.Shift, IsRepeat: false }], $"Expected KeyUp(D) with Shift, got {Describe(ups)}.");
                    Check(window.HeldModifiers == KeyModifiers.Shift, "Shift is still held.");
                    Post(app, window, EventFlagsChanged, 0, "", isRepeat: false, CodeShift);
                    Next();
                    return;

                case 6:
                    Check(window.HeldModifiers == KeyModifiers.None, $"Expected no modifiers, got {window.HeldModifiers}.");
                    Post(app, window, EventKeyDown, 0, "", isRepeat: false, CodeLeft);
                    Next();
                    return;

                case 7:
                    Check(window.IsKeyDown(Key.Left), "Left should be held.");
                    ups.Clear();

                    // The key-up for Left would go to whichever app is focused next; hiding must release it.
                    ObjC.SendVoid(app, Sel("hide:"), (nint)0);
                    Next();
                    return;

                case 8:
                    if (ups.Count > 0 || frame.Time - stepStart > FocusTimeoutSeconds)
                    {
                        Check(ups is [{ Key: Key.Left, Modifiers: KeyModifiers.None, IsRepeat: false }], $"Hiding the app should raise KeyUp(Left), got {Describe(ups)}.");
                        Check(!window.IsKeyDown(Key.Left), "Left should be released once the app is hidden.");
                        ObjC.SendVoid(app, Sel("unhide:"), (nint)0);
                        window.Close();
                    }

                    return;
            }
        });

        if (!focused)
        {
            Console.WriteLine("The window never became key; nothing was checked.");
            return TestProgram.NoFocus;
        }

        Check(step == 8, $"The scenario stopped at step {step}.");
        Console.WriteLine("Key state scenario passed.");
        return 0;
    }

    /// <summary>
    /// Posts synthetic mouse-moved events to a real window: one over the content area must raise
    /// <see cref="MetalWindow.MouseMoved"/> (no button, inside the drawable), one outside it must not.
    /// </summary>
    internal static int MouseMoved()
    {
        using MetalWindow window = new(GraphicsDevice.GetDefault(), "SharpCompute mouse-moved test", 320, 200);

        List<MouseEventArgs> moves = [];
        window.MouseMoved += (_, e) => moves.Add(e);

        nint app = ObjC.SendIntPtr(ObjC.GetClass("NSApplication"), Sel("sharedApplication"));
        int step = 0;
        bool focused = false;

        window.Run(frame =>
        {
            switch (step)
            {
                case 0:
                    if (ObjC.SendIntPtr(app, Sel("keyWindow")) != 0 && SendBool(app, Sel("isActive")))
                    {
                        focused = true;
                        step++;
                    }
                    else if (frame.Time > FocusTimeoutSeconds)
                    {
                        window.Close();
                    }

                    return;

                case 1:
                    // Window coordinates have a bottom-left origin; (100, 50) is inside the 320 × 200 content.
                    PostMouse(app, window, 100, 50);
                    step++;
                    return;

                case 2:
                    Check(moves.Count == 1, $"Expected one MouseMoved, got {moves.Count}.");
                    MouseEventArgs m = moves[0];
                    Check(m.Button == MouseButton.None, $"Expected no button, got {m.Button}.");
                    Check(m.X >= 0 && m.X < window.Width && m.Y >= 0 && m.Y < window.Height, $"({m.X}, {m.Y}) should be inside the drawable.");

                    // The point is 50 points above the bottom of the content: Y counts from the top.
                    double expectedY = (200 - 50) * window.ScaleFactor;
                    Check(Math.Abs(m.Y - expectedY) <= window.ScaleFactor, $"Expected Y ≈ {expectedY}, got {m.Y}.");

                    // Outside the content area: nothing is raised.
                    PostMouse(app, window, -40, -40);
                    step++;
                    return;

                case 3:
                    Check(moves.Count == 1, $"A move outside the content area must not raise MouseMoved (got {moves.Count}).");
                    step++;
                    window.Close();
                    return;
            }
        });

        if (!focused)
        {
            Console.WriteLine("The window never became key; nothing was checked.");
            return TestProgram.NoFocus;
        }

        Check(step == 4, $"The scenario stopped at step {step}.");
        Console.WriteLine("Mouse-moved scenario passed.");
        return 0;
    }

    private static void PostMouse(nint app, MetalWindow window, double x, double y)
    {
        nint key = ObjC.SendIntPtr(app, Sel("keyWindow"));
        Check(key != 0, $"The window lost focus before the scenario finished (window '{window.Title}').");
        nint windowNumber = ObjC.SendIntPtr(key, Sel("windowNumber"));
        nint e = MouseEvent(
            ObjC.GetClass("NSEvent"),
            Sel("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:"),
            5, x, y, 0, 0, windowNumber, 0, 0, 0, 0f);

        Check(e != 0, "NSEvent refused to create the mouse event.");
        PostEvent(app, Sel("postEvent:atStart:"), e, 0);
    }

    /// <summary>
    /// Feeds a real window synthetic scroll-wheel events (see <see cref="PostWheel"/>) and checks
    /// <see cref="MetalWindow.MouseWheel"/>: a line delta from a notched wheel, a precise delta scaled
    /// to drawable pixels, macOS's Shift remapping of a wheel, the momentum flag, and that nothing is
    /// raised over the title bar or for an event without movement. Returns 0,
    /// <see cref="TestProgram.NoFocus"/>, or <see cref="TestProgram.CannotSynthesize"/> when this
    /// macOS doesn't let a scroll event be aimed at a window.
    /// </summary>
    internal static int MouseWheel()
    {
        using MetalWindow window = new(GraphicsDevice.GetDefault(), "SharpCompute mouse-wheel test", 320, 200);

        List<MouseWheelEventArgs> wheels = [];
        window.MouseWheel += (_, e) => wheels.Add(e);

        nint app = ObjC.SendIntPtr(ObjC.GetClass("NSApplication"), Sel("sharedApplication"));
        int step = 0;
        bool focused = false;

        try
        {
            window.Run(frame =>
            {
                double scale = window.ScaleFactor;

                switch (step)
                {
                    case 0:
                        if (ObjC.SendIntPtr(app, Sel("keyWindow")) != 0 && SendBool(app, Sel("isActive")))
                        {
                            focused = true;
                            step++;
                        }
                        else if (frame.Time > FocusTimeoutSeconds)
                        {
                            window.Close();
                        }

                        return;

                    case 1:
                        // A notched wheel: three lines up, at (100, 50) from the bottom-left of the 320 × 200 content.
                        PostWheel(app, window, 100, 50, ScrollUnitLine, wheelY: 3, wheelX: 0, flags: 0);
                        step++;
                        return;

                    case 2:
                        Check(wheels.Count == 1, $"Expected one MouseWheel, got {wheels.Count}.");
                        MouseWheelEventArgs line = wheels[0];
                        Check(line is { IsPrecise: false, IsMomentum: false, Modifiers: KeyModifiers.None }, $"Expected a plain line event, got {Describe(line)}.");
                        Check(line is { DeltaX: 0f, DeltaY: 3f }, $"Line deltas must pass through unscaled, got {Describe(line)}.");
                        Check(Math.Abs(line.X - (100 * scale)) <= scale, $"Expected X ≈ {100 * scale}, got {line.X}.");

                        // 50 points above the bottom of the content: Y counts from the top.
                        Check(Math.Abs(line.Y - ((200 - 50) * scale)) <= scale, $"Expected Y ≈ {(200 - 50) * scale}, got {line.Y}.");

                        // A trackpad: pixel deltas on both axes, with Shift held (which leaves them alone).
                        PostWheel(app, window, 100, 50, ScrollUnitPixel, wheelY: -10, wheelX: 4, ShiftFlag);
                        step++;
                        return;

                    case 3:
                        Check(wheels.Count == 2, $"Expected a second MouseWheel, got {wheels.Count}.");
                        MouseWheelEventArgs precise = wheels[1];
                        Check(precise is { IsPrecise: true, IsMomentum: false, Modifiers: KeyModifiers.Shift }, $"Expected a precise event with Shift, got {Describe(precise)}.");
                        Check(
                            Math.Abs(precise.DeltaX - (4 * scale)) < 1e-3 && Math.Abs(precise.DeltaY - (-10 * scale)) < 1e-3,
                            $"Precise deltas are points × the scale factor ({scale}): expected ({4 * scale}, {-10 * scale}), got {Describe(precise)}.");

                        // Shift + a notched wheel: macOS itself turns the vertical lines into horizontal ones.
                        PostWheel(app, window, 100, 50, ScrollUnitLine, wheelY: 2, wheelX: 0, ShiftFlag);

                        // Inertia after a trackpad flick.
                        PostWheel(app, window, 100, 50, ScrollUnitPixel, wheelY: 5, wheelX: 0, flags: 0, MomentumPhaseContinue);

                        // Over the title bar (above the 200-point content), and an event without movement: neither is raised.
                        PostWheel(app, window, 100, 210, ScrollUnitPixel, wheelY: 5, wheelX: 0, flags: 0);
                        PostWheel(app, window, 100, 50, ScrollUnitPixel, wheelY: 0, wheelX: 0, flags: 0);
                        step++;
                        return;

                    case 4:
                        Check(wheels.Count == 4, $"Expected four MouseWheel events in total (title bar and zero-delta events are not raised), got {wheels.Count}.");
                        MouseWheelEventArgs shifted = wheels[2];
                        Check(shifted is { IsPrecise: false, Modifiers: KeyModifiers.Shift, DeltaX: 2f, DeltaY: 0f }, $"Shift + wheel should arrive as a horizontal line delta, got {Describe(shifted)}.");
                        MouseWheelEventArgs momentum = wheels[3];
                        Check(momentum is { IsPrecise: true, IsMomentum: true, DeltaX: 0f }, $"Expected a momentum event, got {Describe(momentum)}.");
                        Check(Math.Abs(momentum.DeltaY - (5 * scale)) < 1e-3, $"Expected a momentum delta of {5 * scale}, got {Describe(momentum)}.");
                        step++;
                        window.Close();
                        return;
                }
            });
        }
        catch (CannotSynthesizeException e)
        {
            Console.WriteLine($"Could not aim a synthetic scroll event at the window; nothing more was checked. {e.Message}");
            return TestProgram.CannotSynthesize;
        }

        if (!focused)
        {
            Console.WriteLine("The window never became key; nothing was checked.");
            return TestProgram.NoFocus;
        }

        Check(step == 5, $"The scenario stopped at step {step}.");
        Console.WriteLine("Mouse-wheel scenario passed.");
        return 0;
    }

    /// <summary>
    /// Queues a scroll-wheel event for the key window at (<paramref name="x"/>, <paramref name="y"/>)
    /// in window coordinates (points, origin bottom-left).
    /// </summary>
    /// <remarks>
    /// <c>NSEvent</c> has no factory for scroll events, so one is built as a <c>CGEvent</c>
    /// (<c>CGEventCreateScrollWheelEvent2</c>, public) and wrapped with <c>+eventWithCGEvent:</c>.
    /// A CGEvent only knows which window it is for once the window server has routed it, and posting
    /// it there (<c>CGEventPost</c>) needs the Accessibility permission and would scroll whatever is
    /// under the real pointer. So the target is written into the event directly, through two things
    /// that are not in the public headers: integer field 51 (the window number) and
    /// <c>CGEventSetWindowLocation</c>. If either stops working, the wrapped event no longer names
    /// the window and this throws <see cref="CannotSynthesizeException"/>; the translation itself is
    /// covered without a window by <c>MouseWheelTests</c>.
    /// </remarks>
    private static void PostWheel(nint app, MetalWindow window, double x, double y, uint units, int wheelY, int wheelX, nuint flags, long momentumPhase = 0)
    {
        nint key = ObjC.SendIntPtr(app, Sel("keyWindow"));
        Check(key != 0, $"The window lost focus before the scenario finished (window '{window.Title}').");
        nint windowNumber = ObjC.SendIntPtr(key, Sel("windowNumber"));
        CGRect frame = ObjC.SendCGRect(key, Sel("frame"));

        nint cg = CGEventCreateScrollWheelEvent2(0, units, 2, wheelY, wheelX, 0);
        Check(cg != 0, "CoreGraphics refused to create the scroll event.");

        try
        {
            CGEventSetFlags(cg, flags);
            CGEventSetIntegerValueField(cg, FieldScrollWheelMomentumPhase, momentumPhase);
            CGEventSetIntegerValueField(cg, FieldWindowNumber, windowNumber);

            try
            {
                // Window-relative with a top-left origin, measured over the whole frame (title bar included).
                CGEventSetWindowLocation(cg, x, frame.Size.Height - y);
            }
            catch (EntryPointNotFoundException)
            {
                throw new CannotSynthesizeException("CGEventSetWindowLocation is not available.");
            }

            nint e = ObjC.SendIntPtr(ObjC.GetClass("NSEvent"), Sel("eventWithCGEvent:"), cg);
            Check(e != 0, "NSEvent refused to wrap the scroll event.");

            CGPoint location = ObjC.SendCGPoint(e, Sel("locationInWindow"));

            if (ObjC.SendIntPtr(e, Sel("window")) != key || Math.Abs(location.X - x) > 0.5 || Math.Abs(location.Y - y) > 0.5)
            {
                throw new CannotSynthesizeException(
                    $"The event came out for window number {ObjC.SendIntPtr(e, Sel("windowNumber"))} at ({location.X}, {location.Y}), not {windowNumber} at ({x}, {y}).");
            }

            PostEvent(app, Sel("postEvent:atStart:"), e, 0);
        }
        finally
        {
            CFRelease(cg);
        }
    }

    private static void Post(nint app, MetalWindow window, nuint type, nuint flags, string characters, bool isRepeat, ushort keyCode)
    {
        nint key = ObjC.SendIntPtr(app, Sel("keyWindow"));
        Check(key != 0, $"The window lost focus before the scenario finished (window '{window.Title}').");
        nint windowNumber = ObjC.SendIntPtr(key, Sel("windowNumber"));
        nint text = NSString.Create(characters);

        try
        {
            nint e = KeyEvent(
                ObjC.GetClass("NSEvent"),
                Sel("keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:charactersIgnoringModifiers:isARepeat:keyCode:"),
                type, 0, 0, flags, 0, windowNumber, 0, text, text, isRepeat ? (byte)1 : (byte)0, keyCode);

            Check(e != 0, "NSEvent refused to create the key event.");
            PostEvent(app, Sel("postEvent:atStart:"), e, 0);
        }
        finally
        {
            ObjC.Release(text);
        }
    }

    /// <summary>A scroll event could not be aimed at the window on this macOS; not a failure of the code under test.</summary>
    private sealed class CannotSynthesizeException(string message) : Exception(message);

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string Describe(MouseWheelEventArgs e) =>
        $"({e.DeltaX}, {e.DeltaY}) at ({e.X}, {e.Y}){(e.IsPrecise ? " precise" : " lines")}{(e.IsMomentum ? " momentum" : "")} {e.Modifiers}";

    private static string Describe(List<KeyEventArgs> events) =>
        "[" + string.Join(", ", events.ConvertAll(static e => $"{e.Key} '{e.Character}' {e.Modifiers}{(e.IsRepeat ? " repeat" : "")}")) + "]";

    private static nint Sel(string name) => ObjC.GetSelector(name);

    private static bool SendBool(nint receiver, nint selector) => ObjC.SendByte(receiver, selector) != 0;

    // +[NSEvent keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:charactersIgnoringModifiers:isARepeat:keyCode:]
    // The NSPoint location is passed as its two doubles: on arm64 a homogeneous double pair goes in
    // two consecutive FP registers either way, and this assembly keeps runtime marshalling enabled,
    // which rules out struct parameters in LibraryImport.
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial nint KeyEvent(
        nint receiver, nint selector, nuint type, double locationX, double locationY, nuint flags, double timestamp,
        nint windowNumber, nint context, nint characters, nint charactersIgnoringModifiers, byte isARepeat, ushort keyCode);

    // +[NSEvent mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:]
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial nint MouseEvent(
        nint receiver, nint selector, nuint type, double locationX, double locationY, nuint flags, double timestamp,
        nint windowNumber, nint context, nint eventNumber, nint clickCount, float pressure);

    // The non-variadic form of CGEventCreateScrollWheelEvent: wheel1 is vertical, wheel2 horizontal.
    [LibraryImport(CoreGraphics)]
    private static partial nint CGEventCreateScrollWheelEvent2(nint source, uint units, uint wheelCount, int wheel1, int wheel2, int wheel3);

    [LibraryImport(CoreGraphics)]
    private static partial void CGEventSetFlags(nint e, ulong flags);

    // Not in the public headers (see PostWheel). The CGPoint is passed as its two doubles, as in KeyEvent.
    [LibraryImport(CoreGraphics)]
    private static partial void CGEventSetWindowLocation(nint e, double x, double y);

    [LibraryImport(CoreGraphics)]
    private static partial void CGEventSetIntegerValueField(nint e, uint field, long value);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial void CFRelease(nint obj);

    // -[NSApplication postEvent:atStart:]
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void PostEvent(nint receiver, nint selector, nint e, byte atStart);
}
