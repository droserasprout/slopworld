using System;
using System.Threading;

namespace SlopWorld.Tests
{
    static class LatestSampleTests
    {
        public static void BlockedNativeReadDoesNotBlockOrQueueWheelFlood()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            int calls = 0;
            using var sample = new LatestSample<int>(() =>
            {
                Interlocked.Increment(ref calls);
                entered.Set();
                release.Wait();
                return 42;
            });
            try
            {
                AssertEx.Equal(false, sample.TryRead(out _, out _), "first call schedules only");
                AssertEx.Equal(true, entered.Wait(2000), "native read started on worker");
                for (int i = 0; i < 10000; i++)
                    AssertEx.Equal(false, sample.TryRead(out _, out _), "input never waits for native read");
                AssertEx.Equal(1, Volatile.Read(ref calls), "no backlog of native requests");
                release.Set();
                int value = 0;
                AssertEx.Equal(true, SpinWait.SpinUntil(() => sample.TryRead(out value, out _), 2000),
                    "completed sample becomes available");
                AssertEx.Equal(42, value, "native result published");
            }
            finally { sample.Dispose(); release.Set(); }
        }

        public static void NativeFailureStopsSamplingAndIsReportedWithoutThrowingOnCaller()
        {
            using var sample = new LatestSample<int>(() => throw new InvalidOperationException("native failure"));
            Exception error = null;
            sample.TryRead(out _, out _);
            AssertEx.Equal(true, SpinWait.SpinUntil(() =>
            {
                sample.TryRead(out _, out error);
                return error != null;
            }, 2000), "failure is published");
            AssertEx.Equal("native failure", error.Message, "original error retained");
            for (int i = 0; i < 10000; i++)
                AssertEx.Equal(false, sample.TryRead(out _, out _), "failed sampler stays stopped");
        }
    }
}
