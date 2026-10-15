using System;

namespace SlopWorld.Tests
{
    static class LinuxIdleLeaseTests
    {
        sealed class Lease : IIdleInhibitionLease
        {
            public int Refreshes,
                Releases;
            public bool FailRelease;

            public void Refresh() => Refreshes++;

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
