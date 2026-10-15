using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace SlopWorld.Tests
{
    static class LinuxIdleInhibitorTests
    {
        public static void LegacyActivityNotifiesBothServicesAndToleratesMissingGnome()
        {
            using (var desktop = new IdleDesktop(IdleDesktop.Mode.Missing, gnome: true))
            {
                LinuxIdleInhibitor.Tickle(desktop.Address);
                AssertEx.Equal(2, desktop.ActivityPaths.Count, "both legacy services notified");
                AssertEx.True(
                    desktop.ActivityPaths.Contains("/org/gnome/ScreenSaver"),
                    "GNOME path"
                );
                AssertEx.True(
                    desktop.ActivityPaths.Contains("/org/freedesktop/ScreenSaver"),
                    "freedesktop path"
                );
            }
            using (var desktop = new IdleDesktop(IdleDesktop.Mode.Missing))
            {
                LinuxIdleInhibitor.Tickle(desktop.Address);
                AssertEx.Equal(
                    "/org/freedesktop/ScreenSaver",
                    desktop.ActivityPaths.Single(),
                    "missing GNOME service does not block freedesktop activity"
                );
            }
            LinuxIdleInhibitor.Tickle("unix:path=/nonexistent/slopworld-idle-test-bus");
        }

        public static void PortalGrantHoldsConnectionAndRepeatedCleanupReleasesOnce()
        {
            using var desktop = new IdleDesktop(IdleDesktop.Mode.Grant);
            for (int i = 1; i <= 3; i++)
            {
                using var lease = LinuxIdleInhibitor.Acquire(desktop.Address);
                string sender = desktop.PortalSenders.Last();
                lease.Refresh();
                AssertEx.True(desktop.Owns(sender), "portal lease keeps its connection alive");
                AssertEx.True(
                    desktop.SaverSenders.IsEmpty,
                    "portal success does not request fallback"
                );
                lease.Dispose();
                lease.Dispose();
                AssertEx.Equal(i, desktop.Closes, "one Close per lease");
                AssertEx.True(
                    SpinWait.SpinUntil(() => !desktop.Owns(sender), 2000),
                    "lease disconnects"
                );
            }
        }

        public static void MissingRejectedSilentOrInvalidPortalDisconnectsBeforeFallback()
        {
            foreach (
                var mode in new[]
                {
                    IdleDesktop.Mode.Missing,
                    IdleDesktop.Mode.Reject,
                    IdleDesktop.Mode.Silent,
                    IdleDesktop.Mode.WrongHandle,
                    IdleDesktop.Mode.Malformed,
                }
            )
            {
                using var desktop = new IdleDesktop(mode);
                using var lease = LinuxIdleInhibitor.Acquire(desktop.Address, 300);
                foreach (string sender in desktop.PortalSenders)
                    AssertEx.False(
                        desktop.Owns(sender),
                        "failed portal connection is gone before fallback returns"
                    );
                string fallback = desktop.SaverSenders.Single();
                AssertEx.True(desktop.Owns(fallback), "fallback holds its own connection");
                lease.Dispose();
                AssertEx.Equal(1, desktop.Uninhibits, "explicitly releases the fallback cookie");
                AssertEx.True(
                    SpinWait.SpinUntil(() => !desktop.Owns(fallback), 2000),
                    "fallback disconnects"
                );
            }
        }

        public static void MissingServicesBusAndMalformedCookieFailWithoutLeakingConnections()
        {
            using var desktop = new IdleDesktop(IdleDesktop.Mode.Missing, saver: false);
            AssertEx.Throws<AggregateException>(
                () => LinuxIdleInhibitor.Acquire(desktop.Address, 300),
                "no desktop service"
            );
            AssertEx.Throws<AggregateException>(
                () =>
                    LinuxIdleInhibitor.Acquire(
                        "unix:path=/nonexistent/slopworld-idle-test-bus",
                        300
                    ),
                "unavailable bus"
            );
            using var bad = new IdleDesktop(IdleDesktop.Mode.Missing, badSaver: true);
            AssertEx.Throws<AggregateException>(
                () => LinuxIdleInhibitor.Acquire(bad.Address, 300),
                "invalid cookie rejected"
            );
            AssertEx.True(
                SpinWait.SpinUntil(() => !bad.Owns(bad.SaverSenders.Single()), 2000),
                "invalid reply does not retain inhibition"
            );
        }

        public static void StalledReleaseStillDisconnectsWithinBoundedCleanup()
        {
            using var desktop = new IdleDesktop(IdleDesktop.Mode.Grant, stallRelease: true);
            using var lease = LinuxIdleInhibitor.Acquire(desktop.Address);
            string sender = desktop.PortalSenders.Single();
            var clock = Stopwatch.StartNew();
            lease.Dispose();
            AssertEx.True(
                clock.ElapsedMilliseconds < 1500,
                "stalled service does not block cleanup indefinitely"
            );
            AssertEx.True(
                SpinWait.SpinUntil(() => !desktop.Owns(sender), 2000),
                "stalled release disconnects"
            );
        }

        public static void ServiceRestartInvalidatesLeaseAndCleanupCannotReleaseTheNewOwnersCookie()
        {
            foreach (var mode in new[] { IdleDesktop.Mode.Grant, IdleDesktop.Mode.Missing })
            {
                using var desktop = new IdleDesktop(mode);
                using var old = LinuxIdleInhibitor.Acquire(desktop.Address);
                old.Refresh();
                desktop.StopService();
                desktop.StartService();
                AssertEx.Throws<InvalidOperationException>(
                    () => old.Refresh(),
                    "changed service owner invalidates lease"
                );
                using var current = LinuxIdleInhibitor.Acquire(desktop.Address);
                current.Refresh();
                old.Dispose();
                AssertEx.Equal(
                    0,
                    desktop.Closes + desktop.Uninhibits,
                    "old release never targets the new owner"
                );
                current.Dispose();
                AssertEx.Equal(
                    1,
                    desktop.Closes + desktop.Uninhibits,
                    "new lease releases normally"
                );
            }
        }

        public static void ProcessExitDisconnectsWithoutRunningLeaseCleanup()
        {
            const string marker = "SLOPWORLD_TEST_IDLE_BUS";
            string address = Environment.GetEnvironmentVariable(marker);
            if (address != null)
            {
                _ = LinuxIdleInhibitor.Acquire(address);
                Environment.Exit(0);
            }
            using var desktop = new IdleDesktop(IdleDesktop.Mode.Grant);
            var info = new ProcessStartInfo("dotnet")
            {
                ArgumentList =
                {
                    typeof(LinuxIdleInhibitorTests).Assembly.Location,
                    "--where=name == 'LinuxIdleInhibitor: ProcessExitDisconnectsWithoutRunningLeaseCleanup'",
                    "--noresult",
                    "--quiet",
                },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.Environment[marker] = desktop.Address;
            using var child = Process.Start(info);
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            try
            {
                AssertEx.True(child.WaitForExit(10000), "child test process exits");
                AssertEx.Equal(
                    0,
                    child.ExitCode,
                    stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult()
                );
                AssertEx.Equal(0, desktop.Closes, "child bypasses lease.Dispose");
                string sender = desktop.PortalSenders.Single();
                AssertEx.True(
                    SpinWait.SpinUntil(() => !desktop.Owns(sender), 2000),
                    "process exit closes the bus connection"
                );
            }
            finally
            {
                if (!child.HasExited)
                {
                    child.Kill();
                    child.WaitForExit();
                }
            }
        }
    }
}
