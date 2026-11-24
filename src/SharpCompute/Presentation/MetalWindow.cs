using System;
using System.Diagnostics;
using SharpCompute.Interop;

namespace SharpCompute.Presentation;

/// <summary>
/// A macOS window that shows GPU-rendered frames live: an <c>NSWindow</c> whose content view is
/// backed by a <c>CAMetalLayer</c>. This is SharpCompute's equivalent of ComputeSharp's swap chain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Threading.</b> AppKit only works on the process main thread, so a <see cref="MetalWindow"/>
/// must be created, run, presented to and disposed on that thread. In practice: construct it and
/// call <see cref="Run"/> directly from <c>Main</c>. A synchronous <c>Main</c> runs on the main
/// thread; an <c>async Main</c> does not after its first <c>await</c>, and neither do tasks,
/// thread-pool threads or test runners. Every member except <see cref="IsSupported"/> throws
/// <see cref="InvalidOperationException"/> when called from another thread.
/// </para>
/// <para>
/// <b>Loop.</b> <see cref="Run"/> repeats until the window closes: it drains pending AppKit events
/// without blocking (raising <see cref="KeyDown"/>, <see cref="KeyUp"/>, the mouse events and
/// <see cref="Resized"/>), then calls the frame callback. The callback renders into a <see cref="ReadWriteTexture2D{T}"/>
/// and calls <see cref="Present"/>, which waits for the layer's next drawable, so the loop runs at
/// the display's refresh rate. A frame that presents nothing waits for events for up to a
/// sixtieth of a second instead of spinning. While the user drags the window's edge, AppKit runs
/// its own resize loop and frames pause until the mouse is released.
/// </para>
/// <para>
/// <b>Sizes.</b> The constructor takes the content size in points; <see cref="Width"/> and
/// <see cref="Height"/> are the drawable size in pixels, which is twice that on a Retina display.
/// Frames of any size can be presented and are scaled according to <see cref="Scaling"/>.
/// Mouse coordinates are drawable pixels with a top-left origin; <see cref="TryMapToFrame"/>
/// converts them into a frame's pixel space.
/// </para>
/// <para>
/// <b>Keys.</b> Command-Q and Command-W close the window. Other keys are raised through
/// <see cref="KeyDown"/> (with auto-repeat) and <see cref="KeyUp"/>, and never reach AppKit (so
/// unhandled keys don't beep). For movement-style input, poll <see cref="IsKeyDown"/> and
/// <see cref="HeldModifiers"/> from the frame callback instead: they report what is held right
/// now, independent of the key-repeat rate. Held keys are tracked by physical key, so a key-up
/// always releases the <see cref="Key"/> its key-down reported even if the layout's character
/// changed in between. Key-ups go to whichever app has focus, so when the window stops being the
/// key window of the active application (another window or app is focused, the app is hidden),
/// the next loop iteration raises <see cref="KeyUp"/> for every held key and clears the state;
/// otherwise a key released elsewhere would stay held forever.
/// </para>
/// <para>
/// A window that is not disposed is not released: AppKit objects may only be released on the main
/// thread, which a finalizer can't guarantee.
/// </para>
/// </remarks>
public sealed class MetalWindow : IDisposable
{
    private const double IdleWaitSeconds = 1.0 / 60.0;
    private const int MaxDrawableCount = 3;

    private static bool s_applicationReady;
    private static nint s_application;

    private readonly Presenter _presenter;
    private readonly nint _window;
    private readonly nint _view;
    private readonly nint _layer;
    private string _title;
    private double _scale;
    private double _viewWidth;
    private double _viewHeight;
    private int _buttonsDown;
    private readonly KeyState _keys = new();
    private bool _focused;
    private bool _closed;
    private bool _disposed;
    private bool _running;
    private bool _disposeAfterRun;
    private bool _presentedThisFrame;

    /// <summary>Creates the window, shows it centered on screen, and brings the application to the front.</summary>
    /// <param name="device">The device whose command queue presents frames. Frames must be allocated from it.</param>
    /// <param name="title">The window title.</param>
    /// <param name="width">Content width in points.</param>
    /// <param name="height">Content height in points.</param>
    /// <exception cref="PlatformNotSupportedException">There is no Metal device, or no window server (for example over SSH).</exception>
    /// <exception cref="InvalidOperationException">Not called on the process main thread.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not in 1..16384.</exception>
    public MetalWindow(GraphicsDevice device, string title, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, Metal.MaxTexture2DDimension);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, Metal.MaxTexture2DDimension);
        ObjectDisposedException.ThrowIf(device.IsDisposed, device);

        if (!GraphicsDevice.IsSupported)
        {
            throw new PlatformNotSupportedException(
                "MetalWindow needs a Metal device, and none is available (GraphicsDevice.IsSupported is false). SharpCompute requires macOS 15+ on Apple Silicon.");
        }

        ThrowIfNotMainThread();

        if (!AppKit.HasWindowServer())
        {
            throw new PlatformNotSupportedException(
                "MetalWindow needs a window server, but this process is not part of a logged-in desktop session " +
                "(for example it runs over SSH, in a launch daemon, or in CI). Run it from a terminal on the Mac's desktop.");
        }

        AppKit.EnsureLoaded();

        using AutoreleasePool pool = AutoreleasePool.Push();

        EnsureApplication();

        Device = device;
        _title = title;
        _presenter = new Presenter(device);

        try
        {
            CGRect frame = new(new CGPoint(0, 0), new CGSize(width, height));
            nint window = ObjC.SendIntPtr(ObjC.GetClass("NSWindow"), Selectors.Alloc);
            _window = ObjC.SendIntPtr(window, AppKitSelectors.InitWithContentRect, frame, AppKit.WindowStyleStandard, AppKit.BackingStoreBuffered, (byte)0);

            if (_window == 0)
            {
                throw new InvalidOperationException("AppKit failed to create a window.");
            }

            // The window outlives -close so Run can observe that it closed; Dispose releases it.
            ObjC.SendVoid(_window, AppKitSelectors.SetReleasedWhenClosed, (byte)0);
            ObjC.SendVoid(_window, AppKitSelectors.SetContentMinSize, new CGSize(64, 64));
            SetNativeTitle(title);

            _layer = ObjC.SendIntPtr(ObjC.SendIntPtr(ObjC.GetClass("CAMetalLayer"), Selectors.Alloc), Selectors.Init);

            if (_layer == 0)
            {
                throw new InvalidOperationException("QuartzCore failed to create a CAMetalLayer.");
            }

            using (ObjCHandle.Lease deviceLease = device.DeviceHandle.Acquire(device))
            {
                ObjC.SendVoid(_layer, AppKitSelectors.SetDevice, deviceLease.Pointer);
            }

            ObjC.SendVoid(_layer, AppKitSelectors.SetPixelFormat, AppKit.PixelFormatBGRA8Unorm);

            // Drawables must allow ShaderWrite: the present kernel writes them as a compute texture.
            ObjC.SendVoid(_layer, AppKitSelectors.SetFramebufferOnly, (byte)0);
            ObjC.SendVoid(_layer, AppKitSelectors.SetMaximumDrawableCount, (nuint)MaxDrawableCount);
            ObjC.SendVoid(_layer, AppKitSelectors.SetOpaque, (byte)1);

            // Frames hold sRGB-encoded colors (what shaders and PNGs use); tag them so the compositor
            // color-matches them to the display instead of passing raw values through.
            nint srgb = AppKit.CGColorSpaceCreateWithName(AppKit.SrgbColorSpaceName);

            if (srgb != 0)
            {
                ObjC.SendVoid(_layer, AppKitSelectors.SetColorspace, srgb);
                AppKit.CGColorSpaceRelease(srgb);
            }

            // Layer-hosting view: assign the layer before turning wantsLayer on.
            _view = ObjC.SendIntPtr(_window, AppKitSelectors.ContentView);
            ObjC.SendVoid(_view, AppKitSelectors.SetLayer, _layer);
            ObjC.SendVoid(_view, AppKitSelectors.SetWantsLayer, (byte)1);

            UpdateDrawableSize(raiseResized: false);

            ObjC.SendVoid(_window, AppKitSelectors.Center);
            ObjC.SendVoid(_window, AppKitSelectors.MakeKeyAndOrderFront, (nint)0);
            ObjC.SendVoid(s_application, AppKitSelectors.ActivateIgnoringOtherApps, (byte)1);
        }
        catch
        {
            ReleaseNative();
            _presenter.Dispose();
            throw;
        }
    }

    /// <summary>
    /// True when a window can be created here: a Metal device is available and the process is part
    /// of a desktop session with a window server. It does not check the calling thread.
    /// </summary>
    public static bool IsSupported => GraphicsDevice.IsSupported && AppKit.HasWindowServer();

    /// <summary>The device used for presentation; frames passed to <see cref="Present"/> must come from it.</summary>
    public GraphicsDevice Device { get; }

    /// <summary>Drawable width in pixels (content width in points × the display's backing scale factor).</summary>
    public int Width { get; private set; }

    /// <summary>Drawable height in pixels (content height in points × the display's backing scale factor).</summary>
    public int Height { get; private set; }

    /// <summary>The backing scale factor of the window's display: 2 on Retina displays, 1 otherwise.</summary>
    public double ScaleFactor => _scale;

    /// <summary>How frames whose size differs from the drawable are scaled. Defaults to <see cref="PresentScaling.Fit"/>.</summary>
    public PresentScaling Scaling { get; set; } = PresentScaling.Fit;

    /// <summary>True once the window has been closed by the user, by Command-Q/W, or by <see cref="Close"/>.</summary>
    public bool IsClosed => _closed;

    /// <summary>The window title.</summary>
    public string Title
    {
        get => _title;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ThrowIfUnusable();

            if (value != _title)
            {
                _title = value;
                using AutoreleasePool pool = AutoreleasePool.Push();
                SetNativeTitle(value);
            }
        }
    }

    /// <summary>
    /// A key was pressed (repeats while held; see <see cref="KeyEventArgs.IsRepeat"/>). Repeats report
    /// the same <see cref="Key"/> as the first key-down.
    /// </summary>
    public event EventHandler<KeyEventArgs>? KeyDown;

    /// <summary>
    /// A key whose <see cref="KeyDown"/> was raised was released, or the window lost focus while it was
    /// held (see the class remarks). The arguments carry the <see cref="Key"/> and character recorded at
    /// key-down, the modifiers held at release (none after a focus loss) and
    /// <see cref="KeyEventArgs.IsRepeat"/> false. When a handler runs, <see cref="IsKeyDown"/> already
    /// reports the release. Not raised when the window closes: closing just clears the held state.
    /// </summary>
    public event EventHandler<KeyEventArgs>? KeyUp;

    /// <summary>
    /// The modifier keys held right now, updated from modifier-key changes and from every key event, so
    /// a modifier can serve as a button (Shift to run). <see cref="KeyModifiers.None"/> while the window
    /// doesn't have keyboard focus.
    /// </summary>
    /// <exception cref="InvalidOperationException">Not on the main thread.</exception>
    /// <exception cref="ObjectDisposedException">The window has been disposed.</exception>
    public KeyModifiers HeldModifiers
    {
        get
        {
            ThrowIfUnusable();
            return _keys.Modifiers;
        }
    }

    /// <summary>A mouse button was pressed inside the content area.</summary>
    public event EventHandler<MouseEventArgs>? MouseDown;

    /// <summary>The mouse moved while a button pressed inside the content area is held.</summary>
    public event EventHandler<MouseEventArgs>? MouseDragged;

    /// <summary>A button whose <see cref="MouseDown"/> was raised was released (possibly outside the window).</summary>
    public event EventHandler<MouseEventArgs>? MouseUp;

    /// <summary>
    /// The drawable size (<see cref="Width"/>, <see cref="Height"/>) changed: the window was resized
    /// or moved to a display with a different scale factor. Raised from <see cref="Run"/> before the
    /// next frame callback; reallocate size-dependent textures here or in that callback.
    /// </summary>
    public event EventHandler? Resized;

    /// <summary>
    /// Runs the frame loop until the window closes. Each iteration processes pending input, then
    /// calls <paramref name="onFrame"/>, which normally renders a frame and calls <see cref="Present"/>.
    /// Exceptions thrown by <paramref name="onFrame"/> or an event handler stop the loop and propagate.
    /// </summary>
    /// <exception cref="InvalidOperationException">Not on the main thread, or <see cref="Run"/> is already running.</exception>
    public void Run(Action<FrameInfo> onFrame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        ThrowIfUnusable();

        if (_running)
        {
            throw new InvalidOperationException("MetalWindow.Run is already running; it cannot be re-entered from a frame callback or event handler.");
        }

        _running = true;

        try
        {
            long start = Stopwatch.GetTimestamp();
            double previous = 0;
            bool idle = false;

            for (long frame = 0; !_closed; frame++)
            {
                // One pool per frame drains the events, drawables and command buffers it created.
                using AutoreleasePool pool = AutoreleasePool.Push();

                PumpEvents(idle ? IdleWaitSeconds : 0);

                if (_closed)
                {
                    break;
                }

                UpdateDrawableSize(raiseResized: true);

                double now = Stopwatch.GetElapsedTime(start).TotalSeconds;
                _presentedThisFrame = false;

                onFrame(new FrameInfo(now, frame == 0 ? 0 : now - previous, frame));

                previous = now;
                idle = !_presentedThisFrame;
            }
        }
        finally
        {
            _running = false;

            if (_disposeAfterRun)
            {
                Dispose();
            }
        }
    }

    /// <summary>
    /// Shows <paramref name="frame"/> in the window: waits for the next drawable (this is what paces
    /// the loop to the display), copies the frame into it scaled per <see cref="Scaling"/>, schedules
    /// it for display and waits for the copy to finish. When this returns, the frame may be written,
    /// resized or disposed freely.
    /// </summary>
    /// <returns>
    /// True when the frame was shown. False when the window is closed, minimized or fully hidden, or
    /// no drawable became available within a second; nothing is drawn then.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="frame"/> was allocated from a different device.</exception>
    /// <exception cref="ObjectDisposedException">The window, its device or <paramref name="frame"/> has been disposed.</exception>
    /// <exception cref="MetalException">The GPU failed to execute the copy.</exception>
    public bool Present(ReadWriteTexture2D<Rgba32> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ThrowIfUnusable();
        ObjectDisposedException.ThrowIf(frame.IsDisposed, frame);
        ObjectDisposedException.ThrowIf(Device.IsDisposed, Device);

        if (!ReferenceEquals(frame.Device, Device))
        {
            throw new ArgumentException("The frame was allocated from a different GraphicsDevice than the window presents with.", nameof(frame));
        }

        // A hidden app's window can still report itself visible for a moment; nextDrawable would then
        // block for its one-second timeout, so skip presenting while the app is hidden too.
        if (_closed ||
            ObjC.SendByte(s_application, AppKitSelectors.IsHidden) != 0 ||
            (ObjC.SendNUInt(_window, AppKitSelectors.OcclusionState) & AppKit.WindowOcclusionStateVisible) == 0)
        {
            return false;
        }

        using AutoreleasePool pool = AutoreleasePool.Push();
        using ObjCHandle.Lease queue = Device.QueueHandle.Acquire(Device);
        using ObjCHandle.Lease source = frame.Handle.Acquire(frame);
        using ObjCHandle.Lease storage = frame.BufferHandle.Acquire(frame);

        // Blocks until one of the layer's drawables is free; returns nil after a one-second timeout.
        nint drawable = ObjC.SendIntPtr(_layer, AppKitSelectors.NextDrawable);

        if (drawable == 0)
        {
            return false;
        }

        nint target = ObjC.SendIntPtr(drawable, AppKitSelectors.Texture);
        int targetWidth = (int)ObjC.SendNUInt(target, AppKitSelectors.Width);
        int targetHeight = (int)ObjC.SendNUInt(target, AppKitSelectors.Height);

        nint commandBuffer = ObjC.SendIntPtr(queue.Pointer, Selectors.CommandBuffer);

        if (commandBuffer == 0)
        {
            throw new InvalidOperationException("Metal failed to create a command buffer.");
        }

        _presenter.Encode(commandBuffer, source.Pointer, frame.Width, frame.Height, target, targetWidth, targetHeight, Scaling);

        ObjC.SendVoid(commandBuffer, AppKitSelectors.PresentDrawable, drawable);
        ObjC.SendVoid(commandBuffer, Selectors.Commit);
        ObjC.SendVoid(commandBuffer, Selectors.WaitUntilCompleted);

        if (ObjC.SendNUInt(commandBuffer, Selectors.Status) == Metal.CommandBufferStatusError)
        {
            string description = NSString.DescribeError(ObjC.SendIntPtr(commandBuffer, Selectors.Error));
            throw new MetalException($"The GPU failed to present a frame: {description}");
        }

        _presentedThisFrame = true;
        return true;
    }

    /// <summary>
    /// True while <paramref name="key"/> is physically held down in this window: from its first
    /// <see cref="KeyDown"/> until its <see cref="KeyUp"/>. Auto-repeat doesn't change it. Reflects the
    /// events processed so far, i.e. the input as of the start of the current frame. Always false for
    /// <see cref="Key.Unknown"/>, after the window lost focus, and once it is closed. Modifier keys are
    /// not <see cref="Key"/>s; see <see cref="HeldModifiers"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Not on the main thread.</exception>
    /// <exception cref="ObjectDisposedException">The window has been disposed.</exception>
    public bool IsKeyDown(Key key)
    {
        ThrowIfUnusable();
        return _keys.IsDown(key);
    }

    /// <summary>
    /// Converts a point in drawable pixels (as reported by the mouse events) into the pixel space of a
    /// <paramref name="frameWidth"/> × <paramref name="frameHeight"/> frame presented with the current
    /// <see cref="Scaling"/>. Returns false when the point is outside the frame (in a letterbox bar or
    /// outside the window); the converted coordinates are still set, for callers that clamp.
    /// </summary>
    public bool TryMapToFrame(double x, double y, int frameWidth, int frameHeight, out double frameX, out double frameY)
    {
        PixelRect destination = PresentLayout.Destination(frameWidth, frameHeight, Width, Height, Scaling);
        return PresentLayout.DrawableToFrame(x, y, destination, frameWidth, frameHeight, out frameX, out frameY);
    }

    /// <summary>Closes the window. <see cref="Run"/> returns before its next frame. Safe to call more than once.</summary>
    public void Close()
    {
        if (_disposed || _closed)
        {
            return;
        }

        ThrowIfNotMainThread();

        _closed = true;
        _keys.ReleaseAll();
        ObjC.SendVoid(_window, AppKitSelectors.Close);
    }

    /// <summary>
    /// Closes the window and releases it, its layer and the present pipeline. Called from inside
    /// <see cref="Run"/> (a frame callback or event handler), the release happens when Run returns.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ThrowIfNotMainThread();

        if (_running)
        {
            Close();
            _disposeAfterRun = true;
            return;
        }

        Close();
        _disposed = true;

        using AutoreleasePool pool = AutoreleasePool.Push();
        ReleaseNative();
        _presenter.Dispose();
    }

    private static void ThrowIfNotMainThread()
    {
        if (!AppKit.IsMainThread)
        {
            throw new InvalidOperationException(
                "MetalWindow must be used on the process main thread, which AppKit requires. Create it and call Run from a synchronous " +
                "Main method, not from a Task, a thread-pool thread, an async Main after an await, or a test runner.");
        }
    }

    private static void EnsureApplication()
    {
        if (s_applicationReady)
        {
            return;
        }

        s_application = ObjC.SendIntPtr(ObjC.GetClass("NSApplication"), AppKitSelectors.SharedApplication);

        if (s_application == 0)
        {
            throw new PlatformNotSupportedException("AppKit could not create the shared NSApplication; the window server may be unavailable.");
        }

        // Regular: a Dock icon, and the app can become active and take keyboard focus.
        ObjC.SendVoid(s_application, AppKitSelectors.SetActivationPolicy, AppKit.ActivationPolicyRegular);
        ObjC.SendVoid(s_application, AppKitSelectors.FinishLaunching);
        s_applicationReady = true;
    }

    private void ThrowIfUnusable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfNotMainThread();
    }

    private void SetNativeTitle(string title)
    {
        nint value = NSString.Create(title);

        try
        {
            ObjC.SendVoid(_window, AppKitSelectors.SetTitle, value);
        }
        finally
        {
            ObjC.Release(value);
        }
    }

    private void ReleaseNative()
    {
        // Detach the layer first so the view doesn't keep presenting a layer we're about to release.
        if (_view != 0)
        {
            ObjC.SendVoid(_view, AppKitSelectors.SetLayer, (nint)0);
        }

        ObjC.Release(_layer);
        ObjC.Release(_window);
    }

    /// <summary>
    /// Dispatches every queued event. With <paramref name="waitSeconds"/> &gt; 0 it first waits up to
    /// that long for one to arrive (used when the previous frame presented nothing).
    /// </summary>
    private void PumpEvents(double waitSeconds)
    {
        nint dateClass = ObjC.GetClass("NSDate");
        nint until = waitSeconds > 0
            ? ObjC.SendIntPtr(dateClass, AppKitSelectors.DateWithTimeIntervalSinceNow, waitSeconds)
            : ObjC.SendIntPtr(dateClass, AppKitSelectors.DistantPast);
        nint mode = AppKit.DefaultRunLoopMode;

        while (true)
        {
            nint e = ObjC.SendIntPtr(s_application, AppKitSelectors.NextEventMatchingMask, AppKit.EventMaskAny, until, mode, (byte)1);

            if (e == 0)
            {
                break;
            }

            if (HandleEvent(e))
            {
                ObjC.SendVoid(s_application, AppKitSelectors.SendEvent, e);
            }

            if (_closed)
            {
                break;
            }

            until = ObjC.SendIntPtr(dateClass, AppKitSelectors.DistantPast);
        }

        ObjC.SendVoid(s_application, AppKitSelectors.UpdateWindows);

        // The close button (and anything else that closes the window) orders it out. A minimized
        // window is not visible either, and neither are the windows of a hidden app (Dock "Hide",
        // "Hide Others" from another app), but neither of those is closed.
        if (!_closed &&
            ObjC.SendByte(_window, AppKitSelectors.IsVisible) == 0 &&
            ObjC.SendByte(_window, AppKitSelectors.IsMiniaturized) == 0 &&
            ObjC.SendByte(s_application, AppKitSelectors.IsHidden) == 0)
        {
            _closed = true;
            _keys.ReleaseAll();
        }

        if (!_closed)
        {
            UpdateFocus();
        }
    }

    /// <summary>
    /// Polls keyboard focus. Losing it releases every held key: their key-ups will be delivered to
    /// whichever app or window has focus now, never to us. A hidden or deactivated app is not active,
    /// so that covers Command-Tab, Hide and clicking another app as well as another window of this app
    /// becoming key. Focus is polled once per loop iteration: if it is lost and regained between two
    /// polls (for example while the frame callback blocks), a key released meanwhile stays held until
    /// it is pressed again.
    /// </summary>
    private void UpdateFocus()
    {
        bool focused =
            ObjC.SendByte(_window, AppKitSelectors.IsKeyWindow) != 0 &&
            ObjC.SendByte(s_application, AppKitSelectors.IsActive) != 0;

        if (focused)
        {
            if (!_focused)
            {
                // Modifiers already held when focus arrives produce no event until they change; read
                // their current state so Shift held through a Command-Tab back still counts.
                _keys.Modifiers = ToModifiers(ObjC.SendNUInt(ObjC.GetClass("NSEvent"), AppKitSelectors.ModifierFlags));
            }

            _focused = true;
            return;
        }

        _focused = false;

        // Checked every iteration rather than only on the transition: a key event can be handled
        // between two polls while the window is (or is about to be) unfocused.
        if (_keys.Count == 0 && _keys.Modifiers == KeyModifiers.None)
        {
            return;
        }

        foreach (HeldKey held in _keys.ReleaseAll())
        {
            KeyUp?.Invoke(this, new KeyEventArgs(held.Key, held.Character, KeyModifiers.None, isRepeat: false));

            if (_closed)
            {
                break;
            }
        }
    }

    /// <summary>Raises the window's events for <paramref name="e"/>. Returns whether AppKit should also see it.</summary>
    private bool HandleEvent(nint e)
    {
        nuint type = ObjC.SendNUInt(e, AppKitSelectors.Type);

        switch (type)
        {
            case AppKit.EventKeyDown:
                if (ObjC.SendIntPtr(e, AppKitSelectors.Window) == _window)
                {
                    HandleKeyDown(e);
                }

                return false;

            case AppKit.EventKeyUp:
                if (ObjC.SendIntPtr(e, AppKitSelectors.Window) == _window)
                {
                    HandleKeyUp(e);
                }

                return false;

            case AppKit.EventFlagsChanged:
                if (ObjC.SendIntPtr(e, AppKitSelectors.Window) == _window)
                {
                    _keys.Modifiers = ToModifiers(ObjC.SendNUInt(e, AppKitSelectors.ModifierFlags));
                }

                return true;

            case AppKit.EventLeftMouseDown or AppKit.EventRightMouseDown or AppKit.EventOtherMouseDown:
                HandleMouse(e, MouseEventKind.Down);
                return true;

            case AppKit.EventLeftMouseDragged or AppKit.EventRightMouseDragged or AppKit.EventOtherMouseDragged:
                HandleMouse(e, MouseEventKind.Dragged);
                return true;

            case AppKit.EventLeftMouseUp or AppKit.EventRightMouseUp or AppKit.EventOtherMouseUp:
                HandleMouse(e, MouseEventKind.Up);
                return true;

            default:
                return true;
        }
    }

    private void HandleKeyDown(nint e)
    {
        ushort keyCode = ObjC.SendUInt16(e, AppKitSelectors.KeyCode);
        KeyModifiers modifiers = ToModifiers(ObjC.SendNUInt(e, AppKitSelectors.ModifierFlags));
        bool isRepeat = ObjC.SendByte(e, AppKitSelectors.IsARepeat) != 0;
        char character = KeyMap.ToCharacter(NSString.ToManaged(ObjC.SendIntPtr(e, AppKitSelectors.CharactersIgnoringModifiers)));
        Key key = KeyMap.Resolve(keyCode, character);

        _keys.Modifiers = modifiers;

        // There is no menu bar, so provide the two standard shortcuts that would live in it.
        if ((modifiers & KeyModifiers.Command) != 0 && key is Key.Q or Key.W)
        {
            Close();
            return;
        }

        key = _keys.Press(keyCode, key, character, isRepeat, out HeldKey? replaced);

        if (replaced is { } lost)
        {
            KeyUp?.Invoke(this, new KeyEventArgs(lost.Key, lost.Character, modifiers, isRepeat: false));

            if (_closed)
            {
                return;
            }
        }

        KeyDown?.Invoke(this, new KeyEventArgs(key, character, modifiers, isRepeat));
    }

    private void HandleKeyUp(nint e)
    {
        ushort keyCode = ObjC.SendUInt16(e, AppKitSelectors.KeyCode);
        KeyModifiers modifiers = ToModifiers(ObjC.SendNUInt(e, AppKitSelectors.ModifierFlags));

        _keys.Modifiers = modifiers;

        // Look the key up by code: this event's characters may differ from the key-down's.
        if (_keys.Release(keyCode, out HeldKey released))
        {
            KeyUp?.Invoke(this, new KeyEventArgs(released.Key, released.Character, modifiers, isRepeat: false));
        }
    }

    private void HandleMouse(nint e, MouseEventKind kind)
    {
        if (ObjC.SendIntPtr(e, AppKitSelectors.Window) != _window)
        {
            return;
        }

        // Left and right come from the event type (always set); buttonNumber only distinguishes
        // the "other" buttons (2 = middle). Synthesized events can carry buttonNumber 0 for a right click.
        nuint type = ObjC.SendNUInt(e, AppKitSelectors.Type);
        MouseButton button = type switch
        {
            AppKit.EventLeftMouseDown or AppKit.EventLeftMouseUp or AppKit.EventLeftMouseDragged => MouseButton.Left,
            AppKit.EventRightMouseDown or AppKit.EventRightMouseUp or AppKit.EventRightMouseDragged => MouseButton.Right,
            _ => (nint)ObjC.SendNUInt(e, AppKitSelectors.ButtonNumber) == 2 ? MouseButton.Middle : MouseButton.Other,
        };
        int bit = 1 << (int)button;

        CGPoint inWindow = ObjC.SendCGPoint(e, AppKitSelectors.LocationInWindow);
        CGPoint inView = ObjC.SendCGPoint(_view, AppKitSelectors.ConvertPointFromView, inWindow, 0);
        (double x, double y) = PresentLayout.ViewToDrawable(inView.X, inView.Y, _viewWidth, _viewHeight, Width, Height);
        KeyModifiers modifiers = ToModifiers(ObjC.SendNUInt(e, AppKitSelectors.ModifierFlags));
        MouseEventArgs args = new(x, y, button, modifiers);

        switch (kind)
        {
            // Clicks on the title bar or the resize border are outside the drawable and belong to AppKit.
            case MouseEventKind.Down when x >= 0 && x < Width && y >= 0 && y < Height:
                _buttonsDown |= bit;
                MouseDown?.Invoke(this, args);
                break;

            case MouseEventKind.Dragged when (_buttonsDown & bit) != 0:
                MouseDragged?.Invoke(this, args);
                break;

            case MouseEventKind.Up when (_buttonsDown & bit) != 0:
                _buttonsDown &= ~bit;
                MouseUp?.Invoke(this, args);
                break;
        }
    }

    private void UpdateDrawableSize(bool raiseResized)
    {
        CGRect bounds = ObjC.SendCGRect(_view, AppKitSelectors.Bounds);
        double scale = ObjC.SendDouble(_window, AppKitSelectors.BackingScaleFactor);

        if (scale <= 0 || double.IsNaN(scale))
        {
            scale = 1;
        }

        _viewWidth = bounds.Size.Width;
        _viewHeight = bounds.Size.Height;

        (int width, int height) = PresentLayout.DrawableSize(bounds.Size.Width, bounds.Size.Height, scale);

        if (width == Width && height == Height && scale == _scale)
        {
            return;
        }

        bool sizeChanged = width != Width || height != Height;

        _scale = scale;
        Width = width;
        Height = height;

        // A layer-hosting view leaves both of these to us: without them the drawable keeps its old
        // size and the layer is composited at 1x.
        ObjC.SendVoid(_layer, AppKitSelectors.SetContentsScale, scale);
        ObjC.SendVoid(_layer, AppKitSelectors.SetDrawableSize, new CGSize(width, height));

        if (raiseResized && sizeChanged)
        {
            Resized?.Invoke(this, EventArgs.Empty);
        }
    }

    private static KeyModifiers ToModifiers(nuint flags)
    {
        KeyModifiers modifiers = KeyModifiers.None;

        if ((flags & AppKit.ModifierShift) != 0)
        {
            modifiers |= KeyModifiers.Shift;
        }

        if ((flags & AppKit.ModifierControl) != 0)
        {
            modifiers |= KeyModifiers.Control;
        }

        if ((flags & AppKit.ModifierOption) != 0)
        {
            modifiers |= KeyModifiers.Option;
        }

        if ((flags & AppKit.ModifierCommand) != 0)
        {
            modifiers |= KeyModifiers.Command;
        }

        return modifiers;
    }

    private enum MouseEventKind
    {
        Down,
        Dragged,
        Up,
    }
}
