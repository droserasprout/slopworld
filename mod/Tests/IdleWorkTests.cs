using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace SlopWorld.Tests
{
    static class IdleWorkTests
    {
        public static void Messages()
        {
            var batch = new HubEventBatch();
            var queue = new ConcurrentQueue<string>();
            int errors = 0;
            Action<Exception> onError = _ => errors++;
            AssertEx.Equal(0, batch.Read(queue, onError), "empty queue");
            queue.Enqueue(Screen("a", 1));
            queue.Enqueue("{\"t\":\"status\",\"id\":2}");
            queue.Enqueue(Screen("a", 3, 2));
            queue.Enqueue(Screen("b", 4));
            queue.Enqueue(Screen("a", 5, 0, 99));
            queue.Enqueue(Screen("a", 6));
            queue.Enqueue("{\"t\":\"future-event\",\"id\":7}");
            AssertEx.Equal(7, batch.Read(queue, onError), "all events consume budget");
            AssertEx.Equal(0, errors, "valid messages accepted");
            var delivered = new List<int>();
            for (int i = 0; i < batch.Count; i++)
                if (batch.ShouldDispatch(i)) delivered.Add(batch[i]["id"].AsInt());
            AssertEx.Equal("2,3,4,5,6,7", string.Join(",", delivered),
                "retain history, request replies, other panes and control order");
            batch.Clear();
            AssertEx.Equal(0, batch.Count, "release payloads");
            for (int i = 0; i < 35; i++) queue.Enqueue(Screen("a", i));
            AssertEx.Equal(32, batch.Read(queue, onError), "bounded batch");
            AssertEx.Equal(3, queue.Count, "backlog retained");
            AssertEx.Equal(true, batch.ShouldDispatch(31), "newest in first batch");
            AssertEx.Equal(3, batch.Read(queue, onError), "next frame drains remainder");
            AssertEx.Equal(true, batch.ShouldDispatch(2), "coalescing indices reset");
            AssertEx.Equal(0, batch.Read(queue, onError), "return to idle");
            AssertEx.Equal(0, batch.Count, "no stale payload on idle frame");

            var bounded = new IncomingMessageQueue();
            AssertEx.Equal(IncomingEnqueueResult.Accepted,
                bounded.Enqueue(Screen("a", 1)), "first live screen accepted");
            AssertEx.Equal(IncomingEnqueueResult.Accepted,
                bounded.Enqueue("{\"t\":\"reply\",\"id\":2}"), "reply accepted");
            AssertEx.Equal(IncomingEnqueueResult.Accepted,
                bounded.Enqueue(Screen("a", 3)), "new live screen accepted");
            AssertEx.Equal(2, bounded.Count, "same-session live screen coalesced before parsing");

            var boundedBatch = new HubEventBatch();
            AssertEx.Equal(2, boundedBatch.Read(bounded, onError), "bounded queue drained");
            var boundedDelivered = new List<int>();
            for (int i = 0; i < boundedBatch.Count; i++)
                if (boundedBatch.ShouldDispatch(i)) boundedDelivered.Add(boundedBatch[i]["id"].AsInt());
            AssertEx.Equal("2,3", string.Join(",", boundedDelivered),
                "reply order survives live-screen replacement");

            AssertEx.Equal(IncomingEnqueueResult.Oversized,
                bounded.Enqueue(new string('x', IncomingMessageQueue.MaxMessageBytes + 1)),
                "oversized individual message rejected");

            var overloaded = new IncomingMessageQueue();
            for (int i = 0; i < IncomingMessageQueue.MaxMessages; i++)
                AssertEx.Equal(IncomingEnqueueResult.Accepted,
                    overloaded.Enqueue("{\"t\":\"status\",\"id\":" + i + "}"),
                    "lossless queue fill");
            IncomingEnqueueResult blockedResult = IncomingEnqueueResult.Accepted;
            using (var started = new ManualResetEvent(false))
            {
                var producer = new Thread(() =>
                {
                    started.Set();
                    blockedResult = overloaded.Enqueue("{\"t\":\"reply\",\"id\":999}");
                });
                producer.Start();
                AssertEx.True(started.WaitOne(1000), "overload producer started");
                overloaded.Close();
                AssertEx.True(producer.Join(1000), "disconnect releases blocked producer");
            }
            AssertEx.Equal(IncomingEnqueueResult.Closed, blockedResult,
                "blocked producer observes disconnect");
        }

        static string Screen(string name, int id, int off = 0, int request = 0) =>
            "{\"t\":\"screen\",\"id\":" + id + ",\"screen\":{\"name\":\"" + name +
            "\",\"off\":" + off + ",\"request_id\":" + request + "}}";

        public static void Scheduling()
        {
            foreach (int fps in new[] { 15, 60, 144, 360 })
            {
                var gate = new PeriodicWork();
                int runs = 0;
                for (int frame = 0; frame < fps * 10; frame++)
                    if (gate.Due(frame / (double)fps, 1.0)) runs++;
                AssertEx.Equal(10, runs, "solar sampling at " + fps + " FPS");
            }
            var delayed = new PeriodicWork();
            delayed.Delay(10, 0.25);
            AssertEx.Equal(false, delayed.Due(10.1, 0.25), "control send delays housekeeping");
            AssertEx.Equal(true, delayed.Due(10.25, 0.25), "deadline reached");
            AssertEx.Equal(true, delayed.Due(100, 0.25), "one update after stall");
            AssertEx.Equal(false, delayed.Due(100, 0.25), "no catch-up burst");
            AssertEx.Equal(true, delayed.Due(0, 0.25), "clock reset recovers");
        }

        public static void Titles()
        {
            var cache = new SidebarTitleCache();
            var info = new SessionInfo { Title = "initial" };
            object font = new object();
            int builds = 0;
            Func<SessionInfo, string> build = s => { builds++; return s.Title; };
            AssertEx.Equal("initial", cache.Get(info, font, 0, build), "initial text");
            for (int i = 0; i < 100; i++) cache.Get(info, font, 0, build);
            AssertEx.Equal(1, builds, "idle title retained");
            info.State = AgentState.Working;
            cache.Get(info, font, 0, build);
            AssertEx.Equal(1, builds, "activity does not rebuild title");
            info.Title = "changed";
            AssertEx.Equal("changed", cache.Get(info, font, 0, build), "mutable title invalidates");
            info.Label = "label";
            cache.Get(info, font, 0, build);
            info.Dir = "/new/path";
            cache.Get(info, font, 0, build);
            info.Host = true;
            cache.Get(info, font, 0, build);
            font = new object();
            cache.Get(info, font, 0, build);
            cache.Get(info, font, 1, build);
            AssertEx.Equal(7, builds, "label, path, host, font and atlas invalidate");
            try { cache.Get(info, font, 2, _ => throw new Exception("font unavailable")); }
            catch (Exception) { }
            cache.Get(info, font, 2, build);
            AssertEx.Equal(8, builds, "failed cleanup is retried");
            AssertEx.Equal("", cache.Get(null, font, 2, build), "missing session");
        }
    }
}
