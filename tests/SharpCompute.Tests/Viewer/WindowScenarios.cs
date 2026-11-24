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
    private const double FocusTimeoutSeconds = 5;

    private const nuint EventKeyDown = 10;
    private const nuint EventKeyUp = 11;
    private const nuint EventFlagsChanged = 12;
    private const nuint ShiftFlag = 1 << 17;

    // kVK_ANSI_D, kVK_LeftArrow, kVK_Shift.
    private const ushort CodeD = 0x02;
    private const ushort CodeLeft = 0x7B;
    private const ushort CodeShift = 0x38;

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

    // -[NSApplication postEvent:atStart:]
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static partial void PostEvent(nint receiver, nint selector, nint e, byte atStart);
}
