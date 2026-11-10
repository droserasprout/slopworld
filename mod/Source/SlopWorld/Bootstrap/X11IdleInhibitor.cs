using System;
using System.Runtime.InteropServices;

namespace SlopWorld
{
    // A private, single-worker display connection owns screensaver suspension.
    // Closing it also releases suspension after normal cleanup or process death.
    internal sealed class X11IdleInhibitor : IIdleInhibitionLease
    {
        IntPtr _display;

        internal X11IdleInhibitor(bool suspend)
        {
            _display = XOpenDisplay(IntPtr.Zero);
            if (_display == IntPtr.Zero)
                throw new InvalidOperationException("X11 display is unavailable");
            try
            {
                if (suspend)
                    TrySuspend();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        void TrySuspend()
        {
            try
            {
                // Suspend requires MIT-SCREEN-SAVER 1.1. Timer resets still work
                // when the extension or optional system libXss is unavailable.
                if (
                    XScreenSaverQueryExtension(_display, out _, out _) != 0
                    && XScreenSaverQueryVersion(_display, out int major, out int minor) != 0
                    && (major > 1 || (major == 1 && minor >= 1))
                )
                {
                    XScreenSaverSuspend(_display, 1);
                    _ = XFlush(_display);
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        public void Refresh()
        {
            if (_display == IntPtr.Zero)
                return;
            _ = XResetScreenSaver(_display);
            _ = XFlush(_display);
        }

        public void Dispose()
        {
            if (_display == IntPtr.Zero)
                return;
            _ = XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr XOpenDisplay(IntPtr name);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern int XCloseDisplay(IntPtr display);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern int XResetScreenSaver(IntPtr display);

        [DllImport("libX11.so.6", CallingConvention = CallingConvention.Cdecl)]
        static extern int XFlush(IntPtr display);

        [DllImport("libXss.so.1", CallingConvention = CallingConvention.Cdecl)]
        static extern int XScreenSaverQueryExtension(
            IntPtr display,
            out int eventBase,
            out int errorBase
        );

        [DllImport("libXss.so.1", CallingConvention = CallingConvention.Cdecl)]
        static extern int XScreenSaverQueryVersion(IntPtr display, out int major, out int minor);

        [DllImport("libXss.so.1", CallingConvention = CallingConvention.Cdecl)]
        static extern void XScreenSaverSuspend(IntPtr display, int suspend);
    }
}
