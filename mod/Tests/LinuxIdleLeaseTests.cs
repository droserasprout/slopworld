using System;

namespace SlopWorld.Tests
{
    static class LinuxIdleLeaseTests
    {
        sealed class Lease : IIdleInhibitionLease
        {
            public int Refreshes,
                Releases;
            public bool FailRelease,
                FailRefresh;

            public void Refresh()
            {
                Refreshes++;
                if (FailRefresh)
                    throw new InvalidOperationException("service restarted");
            }

            public void Dispose()
            {
                Releases++;
                if (FailRelease)
                    throw new InvalidOperationException("release failed");
            }
        }

        public static void DesktopLeaseStillResetsX11WithoutSuspendingOrTickling()
        {
            var desktop = new Lease();
            var x11 = new Lease();
            int tickles = 0;
            using var lease = LinuxIdleLease.Acquire(
                () => desktop,
                suspend =>
                {
                    AssertEx.False(suspend, "desktop grant makes X11 suspension unnecessary");
                    return x11;
                },
                () => tickles++
            );
            lease.Refresh();
            lease.Refresh();
            AssertEx.Equal(2, x11.Refreshes, "X11 idle timer is independent of desktop grant");
            AssertEx.Equal(0, tickles, "desktop grant suppresses legacy notifications");
            lease.Dispose();
            lease.Dispose();
            lease.Refresh();
            AssertEx.Equal(2, x11.Refreshes, "disposed lease cannot reset activity");
            AssertEx.Equal(1, desktop.Releases, "desktop released once");
            AssertEx.Equal(1, x11.Releases, "X11 released once");
        }

        public static void MissingDesktopServicesFallBackToX11AndLegacyActivity()
        {
            var x11 = new Lease();
            int tickles = 0;
            using var lease = LinuxIdleLease.Acquire(
                () => throw new InvalidOperationException("no session bus"),
                suspend =>
                {
                    AssertEx.True(suspend, "missing desktop services require X11 suspension");
                    return x11;
                },
                () => tickles++
            );
            lease.Refresh();
            lease.Refresh();
            AssertEx.Equal(2, x11.Refreshes, "fallback continues resetting the idle timer");
            AssertEx.Equal(2, tickles, "fallback also notifies legacy screensavers");
            lease.Dispose();
            AssertEx.Equal(1, x11.Releases, "fallback suspension is released");
        }

        public static void MissingX11PreservesDesktopLeaseAndTotalFailureIsReported()
        {
            var desktop = new Lease();
            using var lease = LinuxIdleLease.Acquire(
                () => desktop,
                _ => throw new InvalidOperationException("no display"),
                () => throw new Exception("unexpected tickle")
            );
            lease.Refresh();
            AssertEx.Equal(0, desktop.Releases, "missing X11 does not release desktop inhibition");
            lease.Dispose();
            AssertEx.Equal(1, desktop.Releases, "desktop-only cleanup works");
            AssertEx.Throws<AggregateException>(
                () =>
                    LinuxIdleLease.Acquire(
                        () => throw new InvalidOperationException("no bus"),
                        _ => throw new InvalidOperationException("no display"),
                        () => { }
                    ),
                "report when neither protection can be acquired"
            );
        }

        public static void LostDesktopLeaseFallsBackAndLaterReacquires()
        {
            var original = new Lease();
            var recovered = new Lease();
            var displays = new System.Collections.Generic.List<Lease>();
            var suspensions = new System.Collections.Generic.List<bool>();
            int attempts = 0,
                tickles = 0;
            bool available = false;
            using var lease = LinuxIdleLease.Acquire(
                () =>
                    ++attempts == 1 ? original
                    : available ? recovered
                    : throw new InvalidOperationException("service unavailable"),
                suspend =>
                {
                    var display = new Lease();
                    displays.Add(display);
                    suspensions.Add(suspend);
                    return display;
                },
                () => tickles++
            );
            lease.Refresh();
            AssertEx.Equal(1, attempts, "healthy desktop lease is retained");
            original.FailRefresh = true;
            lease.Refresh();
            AssertEx.Equal(1, original.Releases, "lost lease is released");
            AssertEx.True(suspensions[1], "loss enables X11 suspension");
            AssertEx.Equal(1, displays[0].Releases, "old X11 connection is retired");
            AssertEx.Equal(1, tickles, "loss enables legacy activity");
            lease.Refresh();
            AssertEx.Equal(2, tickles, "fallback remains active during outage");
            AssertEx.Equal(2, displays.Count, "outage does not recreate an unchanged fallback");
            available = true;
            lease.Refresh();
            AssertEx.False(suspensions[2], "recovery removes fallback suspension");
            AssertEx.Equal(1, displays[1].Releases, "fallback display is released");
            AssertEx.Equal(2, tickles, "recovery stops legacy activity");
            lease.Refresh();
            AssertEx.Equal(1, recovered.Refreshes, "recovered lease is checked");
            lease.Dispose();
            AssertEx.Equal(1, recovered.Releases, "recovered lease is cleaned up");
            AssertEx.Equal(1, displays[2].Releases, "current display is cleaned up");
        }

        public static void DesktopReleaseFailureStillClosesX11()
        {
            var desktop = new Lease { FailRelease = true };
            var x11 = new Lease();
            var lease = LinuxIdleLease.Acquire(() => desktop, _ => x11, () => { });
            AssertEx.Throws<InvalidOperationException>(() => lease.Dispose(), "release failure");
            lease.Dispose();
            AssertEx.Equal(1, desktop.Releases, "failed release is not repeated");
            AssertEx.Equal(1, x11.Releases, "X11 cleanup survives desktop release failure");
        }
    }
}
