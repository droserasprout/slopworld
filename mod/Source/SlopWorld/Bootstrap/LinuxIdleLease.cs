using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Combines desktop inhibition with X11's independent idle timer. All resources
    // belong to IdleInhibitionWorker; neither the renderer's display nor its thread is used.
    internal sealed class LinuxIdleLease : IIdleInhibitionLease
    {
        readonly IDisposable _desktop;
        readonly IIdleInhibitionLease _x11;
        readonly Action _tickle;
        bool _disposed;

        LinuxIdleLease(IDisposable desktop, IIdleInhibitionLease x11, Action tickle)
        {
            _desktop = desktop;
            _x11 = x11;
            _tickle = tickle;
        }

        internal static LinuxIdleLease Acquire() =>
            Acquire(
                () => LinuxIdleInhibitor.Acquire(),
                suspend => new X11IdleInhibitor(suspend),
                () => LinuxIdleInhibitor.Tickle()
            );

        internal static LinuxIdleLease Acquire(
            Func<IDisposable> acquireDesktop,
            Func<bool, IIdleInhibitionLease> acquireX11,
            Action tickle
        )
        {
            var failures = new List<Exception>();
            IDisposable desktop = null;
            IIdleInhibitionLease x11 = null;
            try
            {
                desktop = acquireDesktop();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            try
            {
                // Reset the X idle timer even with a desktop lease. Suspend the X
                // screensaver only when desktop inhibition failed, as SDL does.
                x11 = acquireX11(desktop == null);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            if (desktop == null && x11 == null)
                throw new AggregateException(
                    "desktop and X11 idle inhibition are unavailable",
                    failures
                );
            return new LinuxIdleLease(desktop, x11, desktop == null ? tickle : null);
        }

        public void Refresh()
        {
            if (_disposed)
                return;
            _x11?.Refresh();
            _tickle?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try
            {
                _desktop?.Dispose();
            }
            finally
            {
                _x11?.Dispose();
            }
        }
    }
}
