using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    // Use -popupwindow -screen-fullscreen 0 to avoid the Unity Linux Alt+Tab freeze.
    // Request fullscreen through the X11 window manager so geometry and input coordinates change together.
    // Resize the Unity surface before the initial fullscreen request.
    // Keep window decorations disabled because Mutter can restore the title bar after fullscreen ends.
    public static class LinuxGameWindow
    {
        const float RetrySeconds = 1f;
        const float StartupSettleSeconds = 1.5f;
        // Check frequently after resizing so the intermediate window state remains brief.
        const float ResyncPollSeconds = 0.05f;
        const float ResyncTimeoutSeconds = 0.5f;
        const int ClientMessage = 33;
        const long SubstructureNotifyMask = 1L << 19;
        const long SubstructureRedirectMask = 1L << 20;

        enum WindowChange { Retry, WaitingForResize, Applied, Unsupported }
        enum ResizeStage { Retry, Waiting, Ready }

        static bool? _applied;
        static float _nextTry;
        static float _readySince = -1f;
        // Store the time of the initial surface resize request, or a negative value before that request.
        // Wait for the resize or its timeout before requesting fullscreen.
        static float _resyncedAt = -1f;
        static bool _warned;

        public static void Follow()
        {
            if (Application.platform != RuntimePlatform.LinuxPlayer) return;

            bool want = Settings.Fullscreen;
            if (_applied == want) return;

            float now = Time.realtimeSinceStartup;
            if (_applied == null && !Ready(now)) return;
            if (now < _nextTry) return;

            var result = TrySet(want);
            if (result == WindowChange.Applied || result == WindowChange.Unsupported) _applied = want;
            _nextTry = Time.realtimeSinceStartup
                + (result == WindowChange.WaitingForResize ? ResyncPollSeconds : RetrySeconds);
        }

        public static void Set(bool fullscreen)
        {
            if (Settings.S.fullscreen == fullscreen && _applied == fullscreen) return;

            Settings.S.fullscreen = fullscreen;
            Settings.S.MarkDirty();
            _nextTry = 0f;
            Follow();
        }

        public static void Toggle() => Set(!Settings.Fullscreen);

        // Wait briefly after the first frame for initial window creation and sizing.
        // FindGameWindow retries if the window is still unavailable. ResyncSurface permits fullscreen setup during loading.
        static bool Ready(float now)
        {
            if (_readySince < 0f) _readySince = now;
            return now - _readySince >= StartupSettleSeconds;
        }

        // Resize the Unity render surface to the monitor dimensions before requesting EWMH fullscreen.
        // Keep Unity in windowed mode to avoid its fullscreen freeze.
        static bool ResyncSurface()
        {
            int w = Display.main.systemWidth;
            int h = Display.main.systemHeight;
            if (w <= 0 || h <= 0) return false;
            Screen.SetResolution(w, h, FullScreenMode.Windowed);
            return true;
        }

        // Continue when screen dimensions reach the monitor dimensions or the resize timeout expires.
        static bool ResyncLanded(float now)
        {
            bool sized = Screen.width >= Display.main.systemWidth
                && Screen.height >= Display.main.systemHeight;
            return sized || now - _resyncedAt >= ResyncTimeoutSeconds;
        }

        // Keep retryable discovery and resize waits distinct from completed or unsupported requests.
        static WindowChange TrySet(bool fullscreen)
        {
            IntPtr display = IntPtr.Zero;
            try
            {
                display = XOpenDisplay(IntPtr.Zero);
                if (display == IntPtr.Zero)
                {
                    Warn("X11 display is unavailable. Could not change the game window state.");
                    // Retry because X11 display access can become available after startup, particularly in the debug sandbox.
                    return WindowChange.Retry;
                }

                IntPtr root = XDefaultRootWindow(display);
                IntPtr window = FindGameWindow(display, root);
                if (window == IntPtr.Zero) return WindowChange.Retry;

                IntPtr state = XInternAtom(display, "_NET_WM_STATE", 0);
                IntPtr fullscreenAtom = XInternAtom(display, "_NET_WM_STATE_FULLSCREEN", 0);
                IntPtr horizontal = XInternAtom(display, "_NET_WM_STATE_MAXIMIZED_HORZ", 0);
                IntPtr vertical = XInternAtom(display, "_NET_WM_STATE_MAXIMIZED_VERT", 0);
                if (state == IntPtr.Zero || fullscreenAtom == IntPtr.Zero
                    || horizontal == IntPtr.Zero || vertical == IntPtr.Zero)
                {
                    Warn("the window manager does not expose EWMH window-state atoms");
                    return WindowChange.Unsupported;
                }

                SetUndecorated(display, window);

                if (fullscreen && !_applied.HasValue)
                {
                    var resize = PrepareInitialFullscreen(display, root, window, state, horizontal, vertical);
                    if (resize == ResizeStage.Retry) return WindowChange.Retry;
                    if (resize == ResizeStage.Waiting) return WindowChange.WaitingForResize;
                }

                return ApplyWindowState(display, root, window, state, fullscreenAtom,
                    horizontal, vertical, fullscreen);
            }
            catch (Exception e)
            {
                Warn("could not change the game window state: " + e.Message);
                return WindowChange.Unsupported;
            }
            finally
            {
                if (display != IntPtr.Zero) _ = XCloseDisplay(display);
            }
        }

        // First resize Unity and maximize the window, then wait for the surface or its timeout.
        // Retain maximization so leaving fullscreen restores the desktop work area.
        static ResizeStage PrepareInitialFullscreen(IntPtr display, IntPtr root, IntPtr window,
            IntPtr state, IntPtr horizontal, IntPtr vertical)
        {
            if (_resyncedAt >= 0f)
                return ResyncLanded(Time.realtimeSinceStartup) ? ResizeStage.Ready : ResizeStage.Waiting;
            if (!ResyncSurface()) return ResizeStage.Retry;
            bool maximized = SendState(display, root, window, state, 1, horizontal, vertical);
            RaiseAndActivate(display, root, window);
            _ = XFlush(display);
            if (!maximized) return ResizeStage.Retry;
            _resyncedAt = Time.realtimeSinceStartup;
            return ResizeStage.Waiting;
        }

        static WindowChange ApplyWindowState(IntPtr display, IntPtr root, IntPtr window,
            IntPtr state, IntPtr fullscreenAtom, IntPtr horizontal, IntPtr vertical, bool fullscreen)
        {
            bool sent = fullscreen
                ? SendState(display, root, window, state, 1, fullscreenAtom, IntPtr.Zero)
                : _applied == true
                    ? SendState(display, root, window, state, 0, fullscreenAtom, IntPtr.Zero)
                    : SendState(display, root, window, state, 1, horizontal, vertical);
            RaiseAndActivate(display, root, window);
            _ = XFlush(display);
            return sent ? WindowChange.Applied : WindowChange.Retry;
        }

        // Use the same process ID window lookup for X11 titles and fullscreen changes.
        // The Unity Linux player has no managed window title setter.
        public static bool TrySetTitle(string title)
        {
            if (Application.platform != RuntimePlatform.LinuxPlayer) return true;

            IntPtr display = IntPtr.Zero;
            try
            {
                display = XOpenDisplay(IntPtr.Zero);
                if (display == IntPtr.Zero) return false;

                IntPtr root = XDefaultRootWindow(display);
                IntPtr window = FindGameWindow(display, root);
                if (window == IntPtr.Zero) return false;

                IntPtr netWmName = XInternAtom(display, "_NET_WM_NAME", 0);
                IntPtr utf8String = XInternAtom(display, "UTF8_STRING", 0);
                IntPtr wmName = XInternAtom(display, "WM_NAME", 0);
                IntPtr stringType = XInternAtom(display, "STRING", 0);
                if (netWmName == IntPtr.Zero || utf8String == IntPtr.Zero
                    || wmName == IntPtr.Zero || stringType == IntPtr.Zero)
                    return false;

                byte[] bytes = Encoding.UTF8.GetBytes(title ?? "");
                GCHandle pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    IntPtr data = bytes.Length == 0
                        ? IntPtr.Zero : pinned.AddrOfPinnedObject();
                    bool modern = XChangeProperty(display, window, netWmName, utf8String,
                        8, 0, data, bytes.Length) != 0;
                    bool legacy = XChangeProperty(display, window, wmName, stringType,
                        8, 0, data, bytes.Length) != 0;
                    _ = XFlush(display);
                    return modern && legacy;
                }
                finally
                {
                    pinned.Free();
                }
            }
            catch (Exception e)
            {
                Warn("could not set the game window title: " + e.Message);
                return true;
            }
            finally
            {
                if (display != IntPtr.Zero) _ = XCloseDisplay(display);
            }
        }

        static bool SendState(IntPtr display, IntPtr root, IntPtr window, IntPtr state,
                              int action, IntPtr first, IntPtr second)
        {
            var message = new XClientMessageEvent
            {
                type = ClientMessage,
                sendEvent = 1,
                display = display,
                window = window,
                messageType = state,
                format = 32,
                data0 = new IntPtr(action), // _NET_WM_STATE_ADD/REMOVE
                data1 = first,
                data2 = second,
            };

            IntPtr mask = new IntPtr(SubstructureNotifyMask | SubstructureRedirectMask);
            if (XSendEvent(display, root, 0, mask, ref message) != 0) return true;

            Warn("the window manager rejected the fullscreen geometry request");
            return false;
        }

        static void RaiseAndActivate(IntPtr display, IntPtr root, IntPtr window)
        {
            // Xlib reports raise/flush errors asynchronously; activation is best-effort.
            _ = XRaiseWindow(display, window);

            IntPtr active = XInternAtom(display, "_NET_ACTIVE_WINDOW", 0);
            if (active == IntPtr.Zero) return;

            var message = new XClientMessageEvent
            {
                type = ClientMessage,
                sendEvent = 1,
                display = display,
                window = window,
                messageType = active,
                format = 32,
                data0 = new IntPtr(1), // source indication: application
            };
            IntPtr mask = new IntPtr(SubstructureNotifyMask | SubstructureRedirectMask);
            if (XSendEvent(display, root, 0, mask, ref message) == 0)
                Warn("the window manager rejected the activation request");
        }

        static void SetUndecorated(IntPtr display, IntPtr window)
        {
            IntPtr hints = XInternAtom(display, "_MOTIF_WM_HINTS", 0);
            if (hints == IntPtr.Zero)
            {
                Warn("the window manager does not expose Motif decoration hints");
                return;
            }

            // Set MWM_HINTS_DECORATIONS with decorations=0 to remove the frame and title bar.
            // Xlib's format=32 consumes native C longs, including on 64-bit Linux.
            IntPtr[] value = { new IntPtr(2), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero };
            GCHandle pinned = GCHandle.Alloc(value, GCHandleType.Pinned);
            try
            {
                if (XChangeProperty(display, window, hints, hints, 32, 0,
                        pinned.AddrOfPinnedObject(), value.Length) == 0)
                    Warn("the window manager rejected the undecorated window hint");
            }
            finally
            {
                pinned.Free();
            }
        }

        static IntPtr FindGameWindow(IntPtr display, IntPtr root)
        {
            IntPtr listAtom = XInternAtom(display, "_NET_CLIENT_LIST", 0);
            IntPtr pidAtom = XInternAtom(display, "_NET_WM_PID", 0);
            IntPtr cardinalAtom = XInternAtom(display, "CARDINAL", 0);
            if (listAtom == IntPtr.Zero || pidAtom == IntPtr.Zero || cardinalAtom == IntPtr.Zero)
                return IntPtr.Zero;

            IntPtr data;
            int format;
            IntPtr count;
            if (!GetProperty(display, root, listAtom, IntPtr.Zero, out data, out format, out count))
                return IntPtr.Zero;

            try
            {
                if (format != 32) return IntPtr.Zero;

                int pid = Process.GetCurrentProcess().Id;
                int n = count.ToInt32();
                for (int i = 0; i < n; i++)
                {
                    IntPtr window = Marshal.ReadIntPtr(data, i * IntPtr.Size);
                    if (WindowHasPid(display, window, pidAtom, cardinalAtom, pid)) return window;
                }
            }
            finally
            {
                _ = XFree(data);
            }

            return IntPtr.Zero;
        }

        static bool WindowHasPid(IntPtr display, IntPtr window, IntPtr pidAtom,
                                 IntPtr cardinalAtom, int pid)
        {
            IntPtr data;
            int format;
            IntPtr count;
            if (!GetProperty(display, window, pidAtom, cardinalAtom,
                    IntPtr.Zero, new IntPtr(1), out data, out format, out count))
                return false;

            try
            {
                return format == 32 && count.ToInt64() > 0
                    && Marshal.ReadIntPtr(data).ToInt64() == pid;
            }
            finally
            {
                _ = XFree(data);
            }
        }

        static bool GetProperty(IntPtr display, IntPtr window, IntPtr property,
                                IntPtr requestType, IntPtr offset, IntPtr length,
                                out IntPtr data, out int format, out IntPtr count)
        {
            IntPtr actualType;
            IntPtr bytesAfter;
            data = IntPtr.Zero;
            format = 0;
            count = IntPtr.Zero;
            return XGetWindowProperty(display, window, property, offset, length, 0,
                requestType, out actualType, out format, out count, out bytesAfter, out data) == 0
                && data != IntPtr.Zero && count.ToInt64() > 0;
        }

        static bool GetProperty(IntPtr display, IntPtr window, IntPtr property,
                                IntPtr offset, out IntPtr data, out int format, out IntPtr count)
        {
            return GetProperty(display, window, property, IntPtr.Zero, offset,
                new IntPtr(4096), out data, out format, out count);
        }

        static void Warn(string message)
        {
            if (_warned) return;
            _warned = true;
            Log.Warning("[SlopWorld] " + message);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct XClientMessageEvent
        {
            public int type;
            public IntPtr serial;
            public int sendEvent;
            public IntPtr display;
            public IntPtr window;
            public IntPtr messageType;
            public int format;
            public IntPtr data0;
            public IntPtr data1;
            public IntPtr data2;
            public IntPtr data3;
            public IntPtr data4;
        }

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr XOpenDisplay(IntPtr displayName);

        // Cleanup and flush return values are not synchronous X server status codes.
        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern int XCloseDisplay(IntPtr display);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr XDefaultRootWindow(IntPtr display);

        // Xlib atom names are narrow byte strings. Supply a terminated UTF-8 buffer
        // explicitly instead of relying on Mono's default string marshaling.
        static IntPtr XInternAtom(IntPtr display, string atomName, int onlyIfExists) =>
            XInternAtomUtf8(display, Encoding.UTF8.GetBytes(atomName + "\0"), onlyIfExists);

        [DllImport("libX11.so.6", EntryPoint = "XInternAtom", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr XInternAtomUtf8(IntPtr display, byte[] atomName, int onlyIfExists);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern int XGetWindowProperty(
            IntPtr display, IntPtr window, IntPtr property, IntPtr longOffset,
            IntPtr longLength, int deleteProperty, IntPtr requestedType,
            out IntPtr actualType, out int actualFormat, out IntPtr itemCount,
            out IntPtr bytesAfter, out IntPtr propertyData);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern int XChangeProperty(
            IntPtr display, IntPtr window, IntPtr property, IntPtr type,
            int format, int mode, IntPtr data, int elementCount);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern int XFree(IntPtr data);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern int XSendEvent(IntPtr display, IntPtr window, int propagate,
            IntPtr eventMask, ref XClientMessageEvent eventSend);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern int XFlush(IntPtr display);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern int XRaiseWindow(IntPtr display, IntPtr window);
    }
}
