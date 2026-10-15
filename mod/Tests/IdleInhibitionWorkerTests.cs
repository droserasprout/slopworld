using System;
using System.Diagnostics;
using System.Threading;

namespace SlopWorld.Tests
{
    static class IdleInhibitionWorkerTests
    {
        sealed class Lease : IIdleInhibitionLease
        {
            readonly ManualResetEventSlim _released;
            public int Releases;
            public int Refreshes;
            public Action OnRefresh;

            public Lease(ManualResetEventSlim released) => _released = released;

            public void Refresh()
            {
                Interlocked.Increment(ref Refreshes);
                OnRefresh?.Invoke();
            }

            public void Dispose()
            {
                Interlocked.Increment(ref Releases);
                _released.Set();
            }
        }

        public static void LeaseLivesUntilStopAndRepeatedStopReleasesOnlyOnce()
        {
            using var acquired = new ManualResetEventSlim();
            using var released = new ManualResetEventSlim();
            var lease = new Lease(released);
            using var worker = new IdleInhibitionWorker(
                () =>
                {
                    acquired.Set();
                    return lease;
                },
                _ => throw new Exception("unexpected warning")
            );
            AssertEx.True(acquired.Wait(2000), "worker acquired the lease");
            AssertEx.False(released.IsSet, "lease stays alive while the client runs");
            worker.Dispose();
            worker.Dispose();
            AssertEx.True(released.Wait(2000), "stop releases the lease");
            AssertEx.Equal(1, lease.Releases, "repeated stop releases once");
        }

        public static void QuitDuringBlockedAcquisitionReturnsAndReleasesLateLease()
        {
            using var entered = new ManualResetEventSlim();
            using var unblock = new ManualResetEventSlim();
            using var released = new ManualResetEventSlim();
            var lease = new Lease(released);
            using var worker = new IdleInhibitionWorker(
                () =>
                {
                    entered.Set();
                    unblock.Wait();
                    return lease;
                },
                _ => throw new Exception("unexpected warning")
            );
            try
            {
                AssertEx.True(entered.Wait(2000), "acquisition is blocked");
                var clock = Stopwatch.StartNew();
                worker.Dispose();
                AssertEx.True(clock.ElapsedMilliseconds < 2000, "quit does not wait indefinitely");
                AssertEx.False(released.IsSet, "acquisition has not returned yet");
            }
            finally
            {
                unblock.Set();
                worker.Dispose();
            }
            AssertEx.True(released.Wait(2000), "late acquisition is cleaned up");
            AssertEx.Equal(1, lease.Releases, "late lease is released once");
            AssertEx.Equal(
                0,
                lease.Refreshes,
                "late lease does not reset desktop activity after quit"
            );
        }

        public static void UnavailableServiceWarnsOnceAndStopRemainsSafe()
        {
            using var warned = new ManualResetEventSlim();
            int attempts = 0,
                warnings = 0;
            using var worker = new IdleInhibitionWorker(
                () =>
                {
                    Interlocked.Increment(ref attempts);
                    throw new InvalidOperationException("service unavailable");
                },
                e =>
                {
                    AssertEx.Equal(
                        "service unavailable",
                        e.Message,
                        "error reaches the warning callback"
                    );
                    Interlocked.Increment(ref warnings);
                    warned.Set();
                }
            );
            AssertEx.True(warned.Wait(2000), "failure is reported");
            worker.Dispose();
            worker.Dispose();
            AssertEx.Equal(1, attempts, "failed acquisition does not flood the desktop");
            AssertEx.Equal(1, warnings, "one warning");
        }

        public static void RefreshRepeatsAndStopInterruptsTheInterval()
        {
            using var refreshed = new ManualResetEventSlim();
            using var released = new ManualResetEventSlim();
            var lease = new Lease(released);
            lease.OnRefresh = () =>
            {
                if (lease.Refreshes >= 2)
                    refreshed.Set();
            };
            using var worker = new IdleInhibitionWorker(
                () => lease,
                _ => throw new Exception("unexpected warning"),
                refreshMilliseconds: 10
            );
            AssertEx.True(refreshed.Wait(2000), "worker refreshes more than once");
            worker.Dispose();
            AssertEx.True(released.IsSet, "stop releases periodic lease");
            AssertEx.Equal(1, lease.Releases, "one release after refreshes");

            // A production-length interval must be interruptible without waiting 30 seconds.
            refreshed.Reset();
            released.Reset();
            var slowLease = new Lease(released) { OnRefresh = () => refreshed.Set() };
            using var slowWorker = new IdleInhibitionWorker(
                () => slowLease,
                _ => throw new Exception("unexpected warning")
            );
            AssertEx.True(refreshed.Wait(2000), "initial refresh is immediate");
            slowWorker.Dispose();
            AssertEx.True(released.IsSet, "stop interrupts production refresh interval");
        }

        public static void RefreshFailureReleasesLeaseAndWarns()
        {
            using var warned = new ManualResetEventSlim();
            using var released = new ManualResetEventSlim();
            var lease = new Lease(released)
            {
                OnRefresh = () => throw new InvalidOperationException("refresh failed"),
            };
            using var worker = new IdleInhibitionWorker(() => lease, _ => warned.Set());
            AssertEx.True(warned.Wait(2000), "refresh failure is reported");
            worker.Dispose();
            AssertEx.Equal(1, lease.Releases, "refresh failure releases inhibition");
        }

        public static void DisableDuringAcquisitionReleasesLateLeaseWithoutRefreshing()
        {
            using var entered = new ManualResetEventSlim();
            using var unblock = new ManualResetEventSlim();
            using var released = new ManualResetEventSlim();
            var lease = new Lease(released);
            using var worker = new IdleInhibitionWorker(
                () =>
                {
                    entered.Set();
                    unblock.Wait();
                    return lease;
                },
                _ => throw new Exception("unexpected warning")
            );
            try
            {
                AssertEx.True(entered.Wait(2000), "acquisition entered");
                worker.RequestStop();
                worker.RequestStop();
                AssertEx.False(
                    released.IsSet,
                    "disable returns before native acquisition completes"
                );
            }
            finally
            {
                unblock.Set();
                worker.Dispose();
            }
            AssertEx.True(released.Wait(2000), "disabled acquisition is eventually released");
            AssertEx.Equal(0, lease.Refreshes, "disabled lease never resets activity");
            AssertEx.Equal(1, lease.Releases, "repeated disable releases once");
        }
    }
}
