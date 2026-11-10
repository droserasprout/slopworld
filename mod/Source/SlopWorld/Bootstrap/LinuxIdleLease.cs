using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // The worker owns both resources. Desktop service loss is repaired on the next
    // refresh; X11 protection and legacy activity cover unsuccessful reacquisition.
    internal sealed class LinuxIdleLease : IIdleInhibitionLease
    {
        readonly Func<IIdleInhibitionLease> _acquireDesktop;
        readonly Func<bool, IIdleInhibitionLease> _acquireX11;
        readonly Action _tickle;
        IIdleInhibitionLease _desktop,
            _x11;
        bool _suspended,
            _disposed;

        LinuxIdleLease(
            Func<IIdleInhibitionLease> desktop,
            Func<bool, IIdleInhibitionLease> x11,
            Action tickle
        )
        {
            _acquireDesktop = desktop;
            _acquireX11 = x11;
            _tickle = tickle;
        }

        internal static LinuxIdleLease Acquire() =>
            Acquire(
                () => LinuxIdleInhibitor.Acquire(),
                suspend => new X11IdleInhibitor(suspend),
                () => LinuxIdleInhibitor.Tickle()
            );

        internal static LinuxIdleLease Acquire(
            Func<IIdleInhibitionLease> acquireDesktop,
            Func<bool, IIdleInhibitionLease> acquireX11,
            Action tickle
        )
        {
            var lease = new LinuxIdleLease(acquireDesktop, acquireX11, tickle);
            var failures = new List<Exception>();
            lease.Reconnect(failures);
            if (lease._desktop == null && lease._x11 == null)
                throw new AggregateException(
                    "desktop and X11 idle inhibition are unavailable",
                    failures
                );
            return lease;
        }

        void Reconnect(List<Exception> failures = null)
        {
            if (_desktop == null)
            {
                try
                {
                    _desktop = _acquireDesktop();
                }
                catch (Exception error)
                {
                    failures?.Add(error);
                }
            }
            bool suspend = _desktop == null;
            if (_x11 == null || _suspended != suspend)
            {
                try
                {
                    // Acquire before releasing the old display so a failed replacement
                    // cannot remove the independent X11 idle-timer protection.
                    var next = _acquireX11(suspend);
                    var previous = _x11;
                    _x11 = next;
                    _suspended = suspend;
                    previous?.Dispose();
                }
                catch (Exception error)
                {
                    failures?.Add(error);
                }
            }
        }

        public void Refresh()
        {
            if (_disposed)
                return;
            try
            {
                _desktop?.Refresh();
            }
            catch (Exception)
            {
                _desktop.Dispose();
                _desktop = null;
            }
            Reconnect();
            _x11?.Refresh();
            if (_desktop == null)
                _tickle();
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
