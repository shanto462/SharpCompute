using System;
using System.Runtime.InteropServices;

namespace SharpCompute.Interop;

/// <summary>
/// The extra <c>objc_msgSend</c> call shapes used by the AppKit / QuartzCore presentation layer.
/// As in <see cref="ObjC"/>, each overload is one exact native signature: the arm64 ABI passes the
/// CoreGraphics geometry structs (homogeneous aggregates of <c>double</c>) in SIMD registers, which
/// the marshaller does for us as long as the struct is declared by value.
/// </summary>
internal static unsafe partial class ObjC
{
    // CGFloat f(id, SEL) — backingScaleFactor.
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial double SendDouble(nint receiver, nint selector);

    // BOOL f(id, SEL) — isVisible, isMiniaturized, isARepeat. BOOL is a one-byte bool on arm64.
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial byte SendByte(nint receiver, nint selector);

    // unsigned short f(id, SEL) — keyCode.
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial ushort SendUInt16(nint receiver, nint selector);

    // NSPoint f(id, SEL) — locationInWindow.
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial CGPoint SendCGPoint(nint receiver, nint selector);

    // NSPoint f(id, SEL, NSPoint, id) — convertPoint:fromView:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial CGPoint SendCGPoint(nint receiver, nint selector, CGPoint arg0, nint arg1);

    // NSRect f(id, SEL) — bounds.
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial CGRect SendCGRect(nint receiver, nint selector);

    // id f(id, SEL, double) — dateWithTimeIntervalSinceNow:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint SendIntPtr(nint receiver, nint selector, double arg0);

    // id f(id, SEL, NSRect, NSUInteger, NSUInteger, BOOL) — initWithContentRect:styleMask:backing:defer:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint SendIntPtr(nint receiver, nint selector, CGRect arg0, nuint arg1, nuint arg2, byte arg3);

    // id f(id, SEL, NSUInteger, id, id, BOOL) — nextEventMatchingMask:untilDate:inMode:dequeue:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial nint SendIntPtr(nint receiver, nint selector, nuint arg0, nint arg1, nint arg2, byte arg3);

    // void f(id, SEL, BOOL) — setFramebufferOnly:, setOpaque:, setWantsLayer:, setReleasedWhenClosed:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void SendVoid(nint receiver, nint selector, byte arg0);

    // void f(id, SEL, CGFloat) — setContentsScale:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void SendVoid(nint receiver, nint selector, double arg0);

    // void f(id, SEL, CGSize) — setDrawableSize:, setContentMinSize:
    [LibraryImport(LibObjC, EntryPoint = MsgSend)]
    internal static partial void SendVoid(nint receiver, nint selector, CGSize arg0);
}

/// <summary>CoreGraphics <c>CGPoint</c> / AppKit <c>NSPoint</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct CGPoint(double x, double y)
{
    public readonly double X = x;
    public readonly double Y = y;
}

/// <summary>CoreGraphics <c>CGSize</c> / AppKit <c>NSSize</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct CGSize(double width, double height)
{
    public readonly double Width = width;
    public readonly double Height = height;
}

/// <summary>CoreGraphics <c>CGRect</c> / AppKit <c>NSRect</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct CGRect(CGPoint origin, CGSize size)
{
    public readonly CGPoint Origin = origin;
    public readonly CGSize Size = size;
}

/// <summary>Framework loading, process checks and the AppKit / QuartzCore / CoreGraphics constants the presentation layer uses.</summary>
internal static partial class AppKit
{
    private const string AppKitFramework = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string QuartzCoreFramework = "/System/Library/Frameworks/QuartzCore.framework/QuartzCore";
    private const string CoreGraphicsFramework = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundationFramework = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string FoundationFramework = "/System/Library/Frameworks/Foundation.framework/Foundation";
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";

    private static readonly Lazy<nint> s_appKit = new(static () => NativeLibrary.Load(AppKitFramework));
    private static readonly Lazy<nint> s_quartzCore = new(static () => NativeLibrary.Load(QuartzCoreFramework));

    /// <summary>Loads AppKit and QuartzCore so their classes (NSApplication, CAMetalLayer, ...) are registered.</summary>
    internal static void EnsureLoaded()
    {
        _ = s_appKit.Value;
        _ = s_quartzCore.Value;
    }

    /// <summary>1 when the calling thread is the process's main thread (the one AppKit requires).</summary>
    [LibraryImport(LibSystem, EntryPoint = "pthread_main_np")]
    internal static partial int pthread_main_np();

    [LibraryImport(CoreGraphicsFramework, EntryPoint = "CGSessionCopyCurrentDictionary")]
    private static partial nint CGSessionCopyCurrentDictionary();

    [LibraryImport(CoreGraphicsFramework, EntryPoint = "CGColorSpaceCreateWithName")]
    internal static partial nint CGColorSpaceCreateWithName(nint name);

    [LibraryImport(CoreGraphicsFramework, EntryPoint = "CGColorSpaceRelease")]
    internal static partial void CGColorSpaceRelease(nint colorSpace);

    [LibraryImport(CoreFoundationFramework, EntryPoint = "CFRelease")]
    private static partial void CFRelease(nint obj);

    /// <summary>True on the process main thread.</summary>
    internal static bool IsMainThread => pthread_main_np() == 1;

    /// <summary>
    /// True when this process can reach a window server: there is a logged-in GUI (Aqua) session
    /// and the process belongs to it. An SSH login or a launch daemon has no session dictionary.
    /// </summary>
    internal static bool HasWindowServer()
    {
        nint session;

        try
        {
            session = CGSessionCopyCurrentDictionary();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }

        if (session == 0)
        {
            return false;
        }

        CFRelease(session);
        return true;
    }

    /// <summary>Reads an exported <c>NSString *</c> / <c>CFStringRef</c> constant such as <c>NSDefaultRunLoopMode</c>.</summary>
    internal static unsafe nint GetStringConstant(string framework, string symbol) =>
        *(nint*)NativeLibrary.GetExport(NativeLibrary.Load(framework), symbol);

    internal static nint DefaultRunLoopMode => s_defaultRunLoopMode.Value;

    internal static nint SrgbColorSpaceName => s_srgbColorSpaceName.Value;

    private static readonly Lazy<nint> s_defaultRunLoopMode = new(static () => GetStringConstant(FoundationFramework, "NSDefaultRunLoopMode"));
    private static readonly Lazy<nint> s_srgbColorSpaceName = new(static () => GetStringConstant(CoreGraphicsFramework, "kCGColorSpaceSRGB"));

    /// <summary><c>NSApplicationActivationPolicyRegular</c>: a Dock icon, a menu bar, and it can become key.</summary>
    internal const nuint ActivationPolicyRegular = 0;

    /// <summary><c>NSWindowStyleMaskTitled | Closable | Miniaturizable | Resizable</c>.</summary>
    internal const nuint WindowStyleStandard = 1 | 2 | 4 | 8;

    /// <summary><c>NSBackingStoreBuffered</c>.</summary>
    internal const nuint BackingStoreBuffered = 2;

    /// <summary><c>NSEventMaskAny</c>.</summary>
    internal static readonly nuint EventMaskAny = nuint.MaxValue;

    /// <summary><c>NSWindowOcclusionStateVisible</c>.</summary>
    internal const nuint WindowOcclusionStateVisible = 1 << 1;

    /// <summary><c>MTLPixelFormatBGRA8Unorm</c>, the format a <c>CAMetalLayer</c> drawable uses here.</summary>
    internal const nuint PixelFormatBGRA8Unorm = 80;

    // NSEventType
    internal const nuint EventLeftMouseDown = 1;
    internal const nuint EventLeftMouseUp = 2;
    internal const nuint EventRightMouseDown = 3;
    internal const nuint EventRightMouseUp = 4;
    internal const nuint EventLeftMouseDragged = 6;
    internal const nuint EventRightMouseDragged = 7;
    internal const nuint EventKeyDown = 10;
    internal const nuint EventKeyUp = 11;
    internal const nuint EventOtherMouseDown = 25;
    internal const nuint EventOtherMouseUp = 26;
    internal const nuint EventOtherMouseDragged = 27;

    // NSEventModifierFlags
    internal const nuint ModifierShift = 1 << 17;
    internal const nuint ModifierControl = 1 << 18;
    internal const nuint ModifierOption = 1 << 19;
    internal const nuint ModifierCommand = 1 << 20;
}

/// <summary>Selectors for AppKit and QuartzCore, registered once per process.</summary>
internal static class AppKitSelectors
{
    // NSApplication
    public static readonly nint SharedApplication = ObjC.GetSelector("sharedApplication");
    public static readonly nint SetActivationPolicy = ObjC.GetSelector("setActivationPolicy:");
    public static readonly nint FinishLaunching = ObjC.GetSelector("finishLaunching");
    public static readonly nint ActivateIgnoringOtherApps = ObjC.GetSelector("activateIgnoringOtherApps:");
    public static readonly nint NextEventMatchingMask = ObjC.GetSelector("nextEventMatchingMask:untilDate:inMode:dequeue:");
    public static readonly nint SendEvent = ObjC.GetSelector("sendEvent:");
    public static readonly nint UpdateWindows = ObjC.GetSelector("updateWindows");

    // NSDate
    public static readonly nint DistantPast = ObjC.GetSelector("distantPast");
    public static readonly nint DateWithTimeIntervalSinceNow = ObjC.GetSelector("dateWithTimeIntervalSinceNow:");

    // NSWindow
    public static readonly nint InitWithContentRect = ObjC.GetSelector("initWithContentRect:styleMask:backing:defer:");
    public static readonly nint SetTitle = ObjC.GetSelector("setTitle:");
    public static readonly nint SetReleasedWhenClosed = ObjC.GetSelector("setReleasedWhenClosed:");
    public static readonly nint SetContentMinSize = ObjC.GetSelector("setContentMinSize:");
    public static readonly nint Center = ObjC.GetSelector("center");
    public static readonly nint MakeKeyAndOrderFront = ObjC.GetSelector("makeKeyAndOrderFront:");
    public static readonly nint ContentView = ObjC.GetSelector("contentView");
    public static readonly nint BackingScaleFactor = ObjC.GetSelector("backingScaleFactor");
    public static readonly nint IsVisible = ObjC.GetSelector("isVisible");
    public static readonly nint IsMiniaturized = ObjC.GetSelector("isMiniaturized");
    public static readonly nint IsHidden = ObjC.GetSelector("isHidden");
    public static readonly nint OcclusionState = ObjC.GetSelector("occlusionState");
    public static readonly nint Close = ObjC.GetSelector("close");

    // NSView
    public static readonly nint SetLayer = ObjC.GetSelector("setLayer:");
    public static readonly nint SetWantsLayer = ObjC.GetSelector("setWantsLayer:");
    public static readonly nint Bounds = ObjC.GetSelector("bounds");
    public static readonly nint ConvertPointFromView = ObjC.GetSelector("convertPoint:fromView:");

    // NSEvent
    public static readonly nint Type = ObjC.GetSelector("type");
    public static readonly nint Window = ObjC.GetSelector("window");
    public static readonly nint LocationInWindow = ObjC.GetSelector("locationInWindow");
    public static readonly nint ButtonNumber = ObjC.GetSelector("buttonNumber");
    public static readonly nint KeyCode = ObjC.GetSelector("keyCode");
    public static readonly nint ModifierFlags = ObjC.GetSelector("modifierFlags");
    public static readonly nint IsARepeat = ObjC.GetSelector("isARepeat");
    public static readonly nint CharactersIgnoringModifiers = ObjC.GetSelector("charactersIgnoringModifiers");

    // CAMetalLayer / CAMetalDrawable
    public static readonly nint Layer = ObjC.GetSelector("layer");
    public static readonly nint SetDevice = ObjC.GetSelector("setDevice:");
    public static readonly nint SetPixelFormat = ObjC.GetSelector("setPixelFormat:");
    public static readonly nint SetFramebufferOnly = ObjC.GetSelector("setFramebufferOnly:");
    public static readonly nint SetMaximumDrawableCount = ObjC.GetSelector("setMaximumDrawableCount:");
    public static readonly nint SetDrawableSize = ObjC.GetSelector("setDrawableSize:");
    public static readonly nint SetContentsScale = ObjC.GetSelector("setContentsScale:");
    public static readonly nint SetOpaque = ObjC.GetSelector("setOpaque:");
    public static readonly nint SetColorspace = ObjC.GetSelector("setColorspace:");
    public static readonly nint NextDrawable = ObjC.GetSelector("nextDrawable");
    public static readonly nint Texture = ObjC.GetSelector("texture");

    // MTLTexture
    public static readonly nint Width = ObjC.GetSelector("width");
    public static readonly nint Height = ObjC.GetSelector("height");

    // MTLCommandBuffer
    public static readonly nint PresentDrawable = ObjC.GetSelector("presentDrawable:");
}
