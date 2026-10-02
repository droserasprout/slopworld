using System;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

namespace SlopWorld.Tests
{
    static class LatestSampleTests
    {
        static bool ResultPending(LatestSample<int> sample)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var gate = typeof(LatestSample<int>).GetField("_gate", flags).GetValue(sample);
            lock (gate) return (bool)typeof(LatestSample<int>).GetField("_resultPending", flags).GetValue(sample);
        }

        public static void BlockedNativeReadDoesNotBlockOrQueueWheelFlood()
        {
            using var firstEntered = new ManualResetEventSlim();
            using var secondEntered = new ManualResetEventSlim();
            using var releaseFirst = new ManualResetEventSlim();
            using var releaseSecond = new ManualResetEventSlim();
            int calls = 0;
            using var sample = new LatestSample<int>(() =>
            {
                int invocation = Interlocked.Increment(ref calls);
                if (invocation == 1) { firstEntered.Set(); releaseFirst.Wait(); }
                else { secondEntered.Set(); releaseSecond.Wait(); }
                return 41 + invocation;
            });
            Task flood = null;
            try
            {
                AssertEx.False(sample.TryRead(out _, out _), "first call schedules only");
                AssertEx.True(firstEntered.Wait(2000), "native read started on worker");
                flood = Task.Run(() =>
                {
                    for (int i = 0; i < 10000; i++)
                        AssertEx.False(sample.TryRead(out _, out _), "input never waits for native read");
                });
                AssertEx.True(flood.Wait(2000), "wheel caller finishes while native read remains blocked");
                AssertEx.Equal(1, Volatile.Read(ref calls), "flood cannot queue native requests");
                releaseFirst.Set();
                AssertEx.True(SpinWait.SpinUntil(() => ResultPending(sample), 2000), "first result published without more polls");
                AssertEx.True(sample.TryRead(out int value, out _), "consume first result and request one successor");
                AssertEx.Equal(42, value, "first result");
                AssertEx.True(secondEntered.Wait(2000), "one successor starts");
                releaseSecond.Set();
                AssertEx.True(SpinWait.SpinUntil(() => ResultPending(sample), 2000), "second result published without more polls");
                var worker = (Thread)typeof(LatestSample<int>).GetField("_worker", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sample);
                AssertEx.True(SpinWait.SpinUntil(() => (worker.ThreadState & ThreadState.WaitSleepJoin) != 0, 2000), "worker returns to idle wait");
                AssertEx.Equal(2, Volatile.Read(ref calls), "no preexisting wheel request starts a third read");
            }
            finally
            {
                sample.Dispose();
                releaseFirst.Set();
                releaseSecond.Set();
                flood?.Wait(2000);
            }
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
