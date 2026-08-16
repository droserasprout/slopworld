using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    // `-popupwindow -screen-fullscreen 0` avoids Unity's Linux Alt+Tab freeze. Keep that
    // client window and ask the X11 window manager for EWMH fullscreen, which changes the
    // client geometry and input coordinates together without entering Unity's bad fullscreen
    // surface path. The first request waits until RimWorld's long load event and its final
    // Unity resize have both settled; sending it during loading leaves the top of the client
    // drawing only the background until a fullscreen toggle forces another resize. Keep the
    // client undecorated too: Mutter can restore Unity's titlebar when fullscreen is removed.
    public static class WindowMaximizer
    {
        const float RetrySeconds = 1f;
        const float StartupSettleSeconds = 1.5f;
        // Poll fast between the surface rebuild and the fullscreen request so the windowed
        // resize has landed but the intermediate state is on screen only for a beat.
        const float ResyncPollSeconds = 0.05f;
        const float ResyncTimeoutSeconds = 0.5f;
        const int ClientMessage = 33;
        const long SubstructureNotifyMask = 1L << 19;
        const long SubstructureRedirectMask = 1L << 20;

        static bool? _applied;
        static float _nextTry;
        static float _readySince = -1f;
        // When the initial surface rebuild was requested, or <0 before it has been. The
        // fullscreen request is held back until the windowed resize it triggers has landed,
        // so fullscreen is the last geometry change and the window never drops below it.
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

            bool done = TrySet(want);
            if (done) _applied = want;
            // While the surface rebuild is settling between the two initial phases, poll
            // fast so fullscreen lands the moment the windowed resize arrives rather than a
            // full RetrySeconds later; otherwise back off to the slow retry.
            bool resyncing = !done && _applied == null && _resyncedAt >= 0f;
            _nextTry = Time.realtimeSinceStartup + (resyncing ? ResyncPollSeconds : RetrySeconds);
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

        // Apply during the loading screen. The old gate waited for LongEventHandler to
        // drain plus a settle, because a fullscreen request sent mid-load left the top of
        // the client stale until a toggle forced another resize. ResyncSurface now induces
        // that resize itself, so the only wait left is a short settle from the first frame
        // to let the window get mapped and sized; FindGameWindow retries until it appears.
        static bool Ready(float now)
        {
            if (_readySince < 0f) _readySince = now;
            return now - _readySince >= StartupSettleSeconds;
        }

        // The game resetting its own geometry is what clears the stale upper client region
        // after an external resize: Screen.SetResolution rebuilds Unity's render surface at
        // the monitor size, so the EWMH fullscreen request lands on a correctly sized surface
        // even mid-load. Windowed mode keeps this off Unity's frozen fullscreen path.
        static void ResyncSurface()
        {
            int w = Display.main.systemWidth;
            int h = Display.main.systemHeight;
            if (w <= 0 || h <= 0) return;
            Screen.SetResolution(w, h, FullScreenMode.Windowed);
        }

        // True once the windowed resize ResyncSurface requested has taken effect, which
        // Unity applies a frame later. Screen reaching the monitor size is the signal; a
        // timeout covers the frame going unreported so fullscreen is never held back forever.
        static bool ResyncLanded(float now)
        {
            bool sized = Screen.width >= Display.main.systemWidth
                && Screen.height >= Display.main.systemHeight;
            return sized || now - _resyncedAt >= ResyncTimeoutSeconds;
        }

        // True means the request was sent or the platform cannot service it, false means the
        // game window has not been mapped yet and Follow should try again.
        static bool TrySet(bool fullscreen)
        {
            IntPtr display = IntPtr.Zero;
            try
            {
                display = XOpenDisplay(null);
                if (display == IntPtr.Zero)
                {
                    Warn("X11 display is unavailable; could not change the game window state");
                    return true;
                }

                IntPtr root = XDefaultRootWindow(display);
                IntPtr window = FindGameWindow(display, root);
                if (window == IntPtr.Zero) return false;

                IntPtr state = XInternAtom(display, "_NET_WM_STATE", 0);
                IntPtr fullscreenAtom = XInternAtom(display, "_NET_WM_STATE_FULLSCREEN", 0);
                IntPtr horizontal = XInternAtom(display, "_NET_WM_STATE_MAXIMIZED_HORZ", 0);
                IntPtr vertical = XInternAtom(display, "_NET_WM_STATE_MAXIMIZED_VERT", 0);
                if (state == IntPtr.Zero || fullscreenAtom == IntPtr.Zero
                    || horizontal == IntPtr.Zero || vertical == IntPtr.Zero)
                {
                    Warn("the window manager does not expose EWMH window-state atoms");
                    return true;
                }

                SetUndecorated(display, window);

                bool initial = !_applied.HasValue;
                bool wasFullscreen = _applied == true;
                bool ok;
                if (fullscreen)
                {
                    // Two phases on the first application. Phase one rebuilds Unity's surface
                    // at monitor size and establishes maximize underneath; Mutter retains that
                    // state under fullscreen, so later exit is one clean transition back to the
                    // complete workarea instead of restoring Unity's old size. Fullscreen is
                    // held for phase two, after the windowed resize from ResyncSurface has
                    // landed, so it is the last geometry change and the window never flickers
                    // down to the windowed size the resize passes through. RaiseAndActivate and
                    // XFlush below still run in phase one, so the client is raised meanwhile.
                    if (initial && _resyncedAt < 0f)
                    {
                        ResyncSurface();
                        _resyncedAt = Time.realtimeSinceStartup;
                        SendState(display, root, window, state, 1, horizontal, vertical);
                        RaiseAndActivate(display, root, window);
                        XFlush(display);
                        return false;
                    }
                    if (initial && !ResyncLanded(Time.realtimeSinceStartup)) return false;

                    ok = SendState(display, root, window, state, 1, fullscreenAtom, IntPtr.Zero);
                }
                else
                {
                    // Maximize is already underneath a fullscreen session. Removing only the
                    // fullscreen state restores the complete workarea, like double-clicking
                    // the titlebar, while leaving GNOME's top chrome visible.
                    ok = wasFullscreen
                        ? SendState(display, root, window, state, 0, fullscreenAtom, IntPtr.Zero)
                        : SendState(display, root, window, state, 1, horizontal, vertical);
                }

                RaiseAndActivate(display, root, window);
                XFlush(display);
                return ok;
            }
            catch (Exception e)
            {
                Warn("could not change the game window state: " + e.Message);
                return true;
            }
            finally
            {
                if (display != IntPtr.Zero) XCloseDisplay(display);
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
            XRaiseWindow(display, window);

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
            XSendEvent(display, root, 0, mask, ref message);
        }

        static void SetUndecorated(IntPtr display, IntPtr window)
        {
            IntPtr hints = XInternAtom(display, "_MOTIF_WM_HINTS", 0);
            if (hints == IntPtr.Zero)
            {
                Warn("the window manager does not expose Motif decoration hints");
                return;
            }

            // MWM_HINTS_DECORATIONS, followed by decorations=0: no frame or titlebar.
            int[] value = { 2, 0, 0, 0, 0 };
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
                XFree(data);
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
                XFree(data);
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
        static extern IntPtr XOpenDisplay(string displayName);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern int XCloseDisplay(IntPtr display);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr XDefaultRootWindow(IntPtr display);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr XInternAtom(IntPtr display, string atomName, int onlyIfExists);

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
