using System;
using System.Collections.Generic;
using NUnit.Framework;
using Google.Protobuf;

namespace SlopWorld.Tests
{
    static class LatencyTimelineTests
    {
        static Wire.ScreenView Screen(string id, ulong seq = 4) => new Wire.ScreenView
        {
            Name = "pane", Seq = seq,
            InputTimings = { new Wire.InputTiming {
                Id = id, RunId = 7, ReceivedUs = 1000, TmuxUs = 1010,
                FirstCaptureUs = 1020, FirstSeq = 2, CaptureUs = 1030, SendUs = 1040 } }
        };
        public static void PreciseMovementCompletesDespiteLegacyDuplicates()
        {
            long now = 100;
            var logs = new List<string>();
            var timeline = new LatencyTimeline(() => now, logs.Add);
            var owner = new object();
            timeline.Scroll(owner, now, true, "precise");
            now = 110;
            timeline.Scroll(owner, now, false, "duplicate");
            Assert.That(timeline.Count, Is.EqualTo(1));
            Assert.That(logs[logs.Count - 1], Does.Contain("status=deduplicated"));
            timeline.DrawScroll(owner, true, 1);
            now = 120;
            timeline.EndFrame(1);
            Assert.That(logs[logs.Count - 1], Does.Contain("status=frame_end").And
                .Contain("scroll_source=precise").And.Contain("elapsed_us=20"));
            Assert.That(timeline.Count, Is.Zero);
        }
        public static void ScrollRequiresReadyPaneAndPreservesSupersededOutcomes()
        {
            long now = 100;
            var logs = new List<string>();
            var timeline = new LatencyTimeline(() => now, logs.Add);
            var owner = new object();
            timeline.Scroll(owner, now, true);
            now = 120;
            timeline.Scroll(owner, now, true);
            Assert.That(logs.Exists(line => line.Contains("status=superseded")), Is.True);
            timeline.DrawScroll(new object(), true, 1);
            timeline.DrawScroll(owner, false, 1);
            timeline.EndFrame(1);
            Assert.That(timeline.Count, Is.EqualTo(1));
            now = 140;
            timeline.DrawScroll(owner, true, 2);
            now = 150;
            timeline.EndFrame(2);
            Assert.That(logs[logs.Count - 1], Does.Contain("kind=history_scroll").And
                .Contain("status=frame_end").And.Contain("elapsed_us=30").And.Contain("draw_us=20"));
            timeline.Scroll(owner, now, false);
            Assert.That(logs[logs.Count - 1], Does.Contain("status=no_motion"));
            timeline.Scroll(owner, now, true);
            timeline.CancelScroll(owner);
            Assert.That(logs[logs.Count - 1], Does.Contain("status=cancelled"));
            Assert.That(timeline.Count, Is.Zero);
        }
        public static void SupersedingFrameCompletesExactlyOnceAtFrameEnd()
        {
            long now = 100;
            var logs = new List<string>();
            var timeline = new LatencyTimeline(() => now, logs.Add);
            string id = timeline.Begin("pane", "keys");
            now = 160;
            timeline.Dispatch(Screen(id, 3), 150);
            now = 180;
            timeline.Dispatch(Screen(id), 170);
            timeline.Draw("pane", 3, Screen(id, 3).InputTimings, 10); // superseded
            timeline.EndFrame(10);
            Assert.That(timeline.Count, Is.EqualTo(1));
            timeline.Draw("pane", 4, Screen(id, 4).InputTimings, 11);
            now = 200;
            timeline.EndFrame(11);
            timeline.EndFrame(11);
            Assert.That(timeline.Count, Is.Zero);
            Assert.That(logs.Count, Is.EqualTo(2));
            Assert.That(logs[1], Does.Contain("status=frame_end").And.Contain("elapsed_us=100")
                .And.Contain("seq=4 first_seq=2").And.Contain("receive_us=70 dispatch_us=80"));
        }
        public static void WrongPaneHistoryStaleRunAndMalformedClocksDoNotComplete()
        {
            long now = 100;
            var logs = new List<string>();
            var timeline = new LatencyTimeline(() => now, logs.Add);
            string id = timeline.Begin("pane", "keys");
            now = 200;
            var screen = Screen(id);
            screen.Name = "other";
            timeline.Dispatch(screen, 190);
            screen.Name = "pane";
            screen.RequestId = 1;
            timeline.Dispatch(screen, 190);
            screen.RequestId = 0;
            screen.InputTimings[0].SendUs = 100;
            timeline.Dispatch(screen, 190);
            timeline.Draw("pane", 4, Screen(id, 4).InputTimings, 1);
            timeline.EndFrame(1);
            Assert.That(timeline.Count, Is.EqualTo(1));
            timeline.Dispatch(Screen(id), 190);
            screen = Screen(id, 5);
            screen.InputTimings[0].RunId = 8;
            timeline.Dispatch(screen, 195);
            timeline.Draw("pane", 5, Screen(id, 5).InputTimings, 2);
            timeline.EndFrame(2);
            Assert.That(timeline.Count, Is.EqualTo(1));
            // A replacement run may reuse the same sequence. Its metadata cannot
            // complete an observation dispatched from the preceding run.
            screen = Screen(id);
            screen.InputTimings[0].RunId = 8;
            timeline.Draw("pane", 4, screen.InputTimings, 3);
            timeline.EndFrame(3);
            Assert.That(timeline.Count, Is.EqualTo(1));
            timeline.Reset();
            Assert.That(logs[1], Does.Contain("status=disconnected"));
        }
        public static void BoundedExpiryAndMissingFrameEndAreExplicit()
        {
            long now = 100;
            var logs = new List<string>();
            var timeline = new LatencyTimeline(() => now, logs.Add);
            for (int i = 0; i < 129; i++) timeline.Begin("pane", "keys");
            Assert.That(timeline.Count, Is.EqualTo(128));
            Assert.That(logs.Exists(s => s.Contains("status=overflow")), Is.True);
            now += 10000000;
            timeline.Expire();
            Assert.That(timeline.Count, Is.Zero);
            string id = timeline.Begin("pane", "mouse");
            now += 100;
            timeline.Dispatch(Screen(id), now - 10);
            timeline.Draw("pane", 4, Screen(id, 4).InputTimings, 4);
            timeline.EndFrame(5);
            Assert.That(logs[logs.Count - 1], Does.Contain("status=missed_frame_end"));
        }
        public static void DiagnosticMetadataSurvivesProductionQueueCoalescing()
        {
            string id = "0123456789abcdef0123456789abcdef";
            var queue = new IncomingMessageQueue();
            var first = new Wire.Event { Screen = Screen(id, 3) };
            var latest = new Wire.Event { Screen = Screen(id, 4) };
            Assert.That(queue.Enqueue(first.ToByteArray()), Is.EqualTo(IncomingEnqueueResult.Accepted));
            Assert.That(queue.Enqueue(latest.ToByteArray()), Is.EqualTo(IncomingEnqueueResult.Accepted));
            Assert.That(queue.Count, Is.EqualTo(1));
            var batch = new HubEventBatch();
            batch.Read(queue, error => Assert.Fail(error.ToString()));
            Assert.That(batch[0].Screen.Seq, Is.EqualTo(4UL));
            Assert.That(batch[0].Screen.InputTimings[0].Id, Is.EqualTo(id));
            Assert.That(batch[0].Screen.InputTimings[0].FirstSeq, Is.EqualTo(2UL));
            batch.Clear();
            Assert.That(batch.Count, Is.Zero);
        }
    }
}
