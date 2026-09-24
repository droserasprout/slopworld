using System;
using Google.Protobuf;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace SlopWorld.Tests
{
    static class IdleWorkTests
    {
        static byte[] Screen(string name, ulong seq, uint off = 0, ulong request = 0) =>
            new Wire.Event { Screen = new Wire.ScreenView { Name = name, Seq = seq, Off = off, RequestId = request } }.ToByteArray();
        static byte[] Control(ulong seq) => new Wire.Event { Sessions = new Wire.SessionsReply {
            Sessions = { new Wire.SessionView { Seq = seq } } } }.ToByteArray();
        static ulong Identity(Wire.Event value) => value.Screen?.Seq ?? value.Sessions.Sessions[0].Seq;
        public static void Messages()
        {
            var queue = new IncomingMessageQueue();
            queue.Enqueue(Screen("a", 1)); queue.Enqueue(Control(2)); queue.Enqueue(Screen("a", 3, 2));
            queue.Enqueue(Screen("b", 4)); queue.Enqueue(Screen("a", 5, 0, 99)); queue.Enqueue(Screen("a", 6));
            queue.Enqueue(Control(7));
            var batch = new HubEventBatch(); int errors = 0;
            AssertEx.Equal(6, batch.Read(queue, _ => errors++), "live coalescing preserves controls and replies");
            var delivered = new List<ulong>();
            for (int i = 0; i < batch.Count; i++) if (batch.ShouldDispatch(i)) delivered.Add(Identity(batch[i]));
            AssertEx.Equal("2,3,4,5,6,7", string.Join(",", delivered), "history/control arrival order");
            AssertEx.Equal(0, errors, "valid binary messages");
            batch.Clear(); AssertEx.Equal(0, batch.Count, "release payload references");
            for (ulong i = 0; i < 35; i++) queue.Enqueue(Control(i));
            AssertEx.Equal(32, batch.Read(queue, _ => errors++), "frame budget");
            AssertEx.Equal(3, queue.Count, "backlog retained");
            AssertEx.Equal(3, batch.Read(queue, _ => errors++), "next frame");
            AssertEx.Equal(0, batch.Read(queue, _ => errors++), "idle");
            queue.Enqueue(Screen("a", 10)); queue.Enqueue(new byte[] { 0x80 });
            AssertEx.Equal(2, queue.Count, "malformed message cannot replace valid live screen");
            batch.Read(queue, _ => errors++);
            AssertEx.Equal(1, errors, "malformed binary reaches error callback");
            AssertEx.Equal(10UL, batch[0].Screen.Seq, "valid screen survives");
            AssertEx.False(batch.ShouldDispatch(1), "malformed payload never dispatched");
            AssertEx.Equal(IncomingEnqueueResult.Oversized, queue.Enqueue(new byte[IncomingMessageQueue.MaxMessageBytes + 1]), "size bound");
            var unknown = Screen("future", 11);
            var extended = new byte[unknown.Length + 3]; Array.Copy(unknown, extended, unknown.Length);
            extended[unknown.Length] = 0xa0; extended[unknown.Length + 1] = 0x06; extended[unknown.Length + 2] = 1;
            var parsed = new ReceivedEvent(extended);
            AssertEx.Equal("future", parsed.LiveName, "unknown protobuf field remains forward compatible");
            AssertEx.True(new ReceivedEvent(new byte[0]).Error != null, "missing oneof payload rejected");
            var overloaded = new IncomingMessageQueue();
            for (int i = 0; i < IncomingMessageQueue.MaxMessages; i++) overloaded.Enqueue(Control((ulong)i));
            IncomingEnqueueResult blocked = IncomingEnqueueResult.Accepted;
            using (var started = new ManualResetEvent(false))
            {
                var producer = new Thread(() => { started.Set(); blocked = overloaded.Enqueue(Control(999)); });
                producer.Start(); AssertEx.True(started.WaitOne(1000), "producer started");
                overloaded.Close(); AssertEx.True(producer.Join(1000), "close releases blocked reader");
            }
            AssertEx.Equal(IncomingEnqueueResult.Closed, blocked, "backpressure wakes on disconnect");
            AssertEx.Equal(0, overloaded.Count, "close drops queue references");
        }

        public static void DeferredScreensValidateBeforeReplacement()
        {
            var frame = new Wire.Event { Screen = new Wire.ScreenView {
                Name = "agent", Seq = ulong.MaxValue, Cols = 120, Rows = 34,
                Title = "title 🦀", Lines = { "hello", "世界", "\x1b[31mred" } } };
            var binary = frame.ToByteArray();
            AssertEx.True(ValidatedLiveScreen.TryName(binary, out var name), "ordinary screen uses deferred decode");
            AssertEx.Equal("agent", name, "validated identity");
            AssertEx.True(frame.Equals(new ReceivedEvent(binary, true).Value), "lazy decode retains every field");
            // Mutation differential: anything accepted by the fast validator must also parse
            // as precisely the same unsolicited live screen under the generated parser.
            var random = new Random(1934);
            for (int i = 0; i < 5000; i++)
            {
                var changed = (byte[])binary.Clone();
                changed[random.Next(changed.Length)] = (byte)random.Next(256);
                if (!ValidatedLiveScreen.TryName(changed, out var live)) continue;
                var eager = new ReceivedEvent(changed);
                AssertEx.True(eager.Error == null, "accepted subset must parse");
                AssertEx.Equal(eager.LiveName, live, "classification agrees");
                AssertEx.True(eager.Value.Equals(new ReceivedEvent(changed, true).Value), "decode agrees");
            }
            var queue = new IncomingMessageQueue();
            queue.Enqueue(binary);
            // Valid name followed by a truncated line, with a correct outer envelope length.
            queue.Enqueue(new byte[] { 42, 12, 10, 5, 97, 103, 101, 110, 116, 130, 1, 5, 120, 120 });
            AssertEx.Equal(2, queue.Count, "malformed row cannot evict a valid screen");
            queue.TryDequeue(out var retained);
            AssertEx.True(frame.Equals(retained.Value), "retained screen intact");
            queue.TryDequeue(out var invalid);
            AssertEx.True(invalid.Error != null, "malformed row delivered as error");
            var reply = frame.Clone(); reply.Screen.RequestId = 9;
            AssertEx.False(ValidatedLiveScreen.TryName(reply.ToByteArray(), out _), "reply uses eager path");
            reply.Screen.RequestId = 0; reply.Screen.Off = 1;
            AssertEx.False(ValidatedLiveScreen.TryName(reply.ToByteArray(), out _), "history uses eager path");
        }

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
