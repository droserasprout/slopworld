using System.Collections.Generic;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class ReaderLaunchTraceTests
    {
        [Test]
        public void TransportSplitsQueueAndBatchTimeWithoutMovingMilestoneClock()
        {
            long now = 10_000;
            var records = new List<string>();
            var trace = new ReaderLaunchTrace(() => now, records.Add);
            const string session = "launch-trace-transport";
            var screen = new Wire.ScreenView
            {
                Name = session,
                Seq = 4,
                AltScreen = true,
            };
            try
            {
                now = 100_000;
                trace.Attach(session);
                now = 150_000;
                // Reception may precede run callback delivery. These are observations,
                // not another ordered milestone and must not change step_ms.
                ReaderLaunchTrace.TransportFrame(screen, 90_000, 130_000);
                ReaderLaunchTrace.Received(session, true);
                Assert.That(records[1], Does.Contain("stage=pager-transport seq=4"));
                Assert.That(records[1], Does.Contain("received_elapsed_ms=80"));
                Assert.That(records[1], Does.Contain("queue_ms=40 decode_batch_ms=20"));
                Assert.That(
                    records[2],
                    Does.Contain("stage=first-screen elapsed_ms=140 step_ms=50")
                );
                int completed = records.Count;
                ReaderLaunchTrace.TransportFrame(screen, 90_000, 130_000);
                Assert.That(records.Count, Is.EqualTo(completed));
                ReaderLaunchTrace.Cancel(session, "unsubscribed");
                ReaderLaunchTrace.TransportFrame(screen, 90_000, 130_000);
                Assert.That(records.Count, Is.EqualTo(completed + 1));
            }
            finally
            {
                ReaderLaunchTrace.Cancel(session, "test-cleanup");
            }
        }

        [Test]
        public void ColdStatusWaitIsIncludedBeforeRunRequest()
        {
            var records = new List<string>();
            var trace = new ReaderLaunchTrace(() => 350_000, records.Add, 2_000);
            trace.Mark("run-request-path");
            Assert.That(records[0], Does.Contain("elapsed_ms=348 step_ms=348"));
        }

        [Test]
        public void FirstRepaintRequiresAReceivedScreenAndCompletesOnlyOnce()
        {
            long now = 10_000;
            var records = new List<string>();
            var trace = new ReaderLaunchTrace(() => now, records.Add);
            const string session = "launch-trace-repaint";
            try
            {
                now += 100_000;
                trace.Attach(session);
                ReaderLaunchTrace.Drawn(session, true);
                Assert.That(records.Count, Is.EqualTo(1));
                now += 50_000;
                trace.Mark("sessions-ready");
                now += 25_000;
                ReaderLaunchTrace.Received(session, false);
                ReaderLaunchTrace.Received(session, false);
                now += 16_000;
                ReaderLaunchTrace.Drawn(session, false);
                ReaderLaunchTrace.Drawn(session, false);
                Assert.That(records.Count, Is.EqualTo(4));
                Assert.That(records[0], Does.Contain("elapsed_ms=100 step_ms=100"));
                Assert.That(
                    records[3],
                    Does.Contain("stage=first-repaint elapsed_ms=191 step_ms=16")
                );
                // A blank startup snapshot must not hide the later wait for less to open.
                now += 500_000;
                ReaderLaunchTrace.Received(session, true);
                now += 16_000;
                ReaderLaunchTrace.Drawn(session, true);
                ReaderLaunchTrace.Drawn(session, true);
                Assert.That(records.Count, Is.EqualTo(6));
                Assert.That(
                    records[5],
                    Does.Contain("stage=pager-repaint elapsed_ms=707 step_ms=16")
                );
            }
            finally
            {
                ReaderLaunchTrace.Cancel(session, "test-cleanup");
            }
        }

        [Test]
        public void CancelledLaunchCannotCompleteOrAffectAnotherSession()
        {
            var first = new List<string>();
            var second = new List<string>();
            var a = new ReaderLaunchTrace(() => 0, first.Add);
            var b = new ReaderLaunchTrace(() => 0, second.Add);
            const string sessionA = "launch-trace-a";
            const string sessionB = "launch-trace-b";
            try
            {
                a.Attach(sessionA);
                b.Attach(sessionB);
                ReaderLaunchTrace.Cancel(sessionA, "unsubscribed");
                ReaderLaunchTrace.Received(sessionA, true);
                ReaderLaunchTrace.Drawn(sessionA, true);
                ReaderLaunchTrace.Received(sessionB, true);
                ReaderLaunchTrace.Drawn(sessionB, true);
                Assert.That(first.Count, Is.EqualTo(2));
                Assert.That(first[1], Does.Contain("stage=unsubscribed"));
                Assert.That(second.Count, Is.EqualTo(5));
                Assert.That(second[4], Does.Contain("stage=pager-repaint"));
            }
            finally
            {
                ReaderLaunchTrace.Cancel(sessionA, "test-cleanup");
                ReaderLaunchTrace.Cancel(sessionB, "test-cleanup");
            }
        }

        [Test]
        public void HiddenLaunchExpiresAndDisconnectClearsRemainingLaunches()
        {
            long now = 0;
            var first = new List<string>();
            var second = new List<string>();
            var a = new ReaderLaunchTrace(() => now, first.Add);
            const string sessionA = "launch-trace-expired";
            const string sessionB = "launch-trace-disconnected";
            try
            {
                a.Attach(sessionA);
                now = 31_000_000;
                var b = new ReaderLaunchTrace(() => now, second.Add);
                b.Attach(sessionB);
                Assert.That(first[1], Does.Contain("stage=expired"));
                ReaderLaunchTrace.CancelAll("disconnected");
                ReaderLaunchTrace.Received(sessionA, true);
                ReaderLaunchTrace.Drawn(sessionA, true);
                ReaderLaunchTrace.Received(sessionB, true);
                ReaderLaunchTrace.Drawn(sessionB, true);
                Assert.That(first.Count, Is.EqualTo(2));
                Assert.That(second.Count, Is.EqualTo(2));
                Assert.That(second[1], Does.Contain("stage=disconnected"));
            }
            finally
            {
                ReaderLaunchTrace.Cancel(sessionA, "test-cleanup");
                ReaderLaunchTrace.Cancel(sessionB, "test-cleanup");
            }
        }
    }
}
