using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld.Tests
{
    static class ModBugfixTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("pending rename preserves one pawn across either event order",
                          PendingRenameOrders);
            yield return ("pending rename settles on success and failure",
                          SessionRenameSettles);
            yield return ("closed terminal rename emits no subscription",
                          ClosedTerminalRename);
            yield return ("open terminal rename transfers one subscription",
                          OpenTerminalRename);
            yield return ("reconnect restores only renamed subscriptions",
                          ReconnectSubscriptions);
            yield return ("disabled connect results release attempts",
                          DisabledConnectResults);
            yield return ("stale connect results preserve newer attempts",
                          StaleConnectResults);
        }

        sealed class PawnState
        {
            public readonly string Id;
            public AgentState State;

            public PawnState(string id, AgentState state)
            {
                Id = id;
                State = state;
            }
        }

        static void PendingRenameOrders()
        {
            foreach (bool pushFirst in new[] { true, false })
            {
                var store = NewStore("old");
                var original = new PawnState("pawn-1", AgentState.Working);
                var bindings = new Dictionary<string, PawnState> { ["old"] = original };
                var seen = new Dictionary<string, AgentState> { ["old"] = AgentState.Working };
                bool callbackRan = false;
                store.Save(new SessionInfo { Name = "new" }, false, "old", () =>
                {
                    callbackRan = true;
                    AssertEx.False(store.TryPendingRename("old", out _),
                                   "success settles alias before callback");
                    AssertEx.True(store.Get("new") != null && store.Get("old") == null,
                                  "success updates local membership before callback");
                    MoveBinding(bindings, seen, "old", "new");
                }, error => throw new Exception(error));

                Reconcile(store, bindings, seen);
                if (pushFirst)
                {
                    store.ApplySessions(ProtobufFixtures.Read<Wire.SessionsReply>(Snapshot("new")));
                    Reconcile(store, bindings, seen);
                    AssertEx.True(ReferenceEquals(original, bindings["old"]),
                                  "push preserves original pawn before callback");
                    AssertEx.Equal(1, bindings.Count, "push does not spawn a duplicate");
                    AssertEx.Equal(AgentState.Working, seen["old"], "push retains state");
                }

                FindRequest("PUT").Ok(JVal.Null);
                AssertEx.True(callbackRan, "real save callback ran");
                Reconcile(store, bindings, seen);
                AssertEx.True(ReferenceEquals(original, bindings["new"]),
                              "callback preserves original pawn for pushFirst=" + pushFirst);
                AssertEx.Equal(1, bindings.Count, "callback leaves one binding");
                AssertEx.Equal(AgentState.Working, seen["new"], "callback retains history");
                AssertEx.Equal(AgentState.Working, original.State, "callback retains pawn state");

                if (!pushFirst) store.ApplySessions(ProtobufFixtures.Read<Wire.SessionsReply>(Snapshot("new")));
                Reconcile(store, bindings, seen);
                AssertEx.True(ReferenceEquals(original, bindings["new"]),
                              "settled snapshot preserves original pawn");
                AssertEx.Equal(1, bindings.Count, "settled snapshot leaves one binding");

                store.ApplySessions(ProtobufFixtures.Read<Wire.SessionsReply>(Snapshot()));
                Reconcile(store, bindings, seen);
                AssertEx.Equal(0, bindings.Count, "ordinary deletion retires pawn");
            }

            var failedStore = NewStore("old");
            var failedBindings = new Dictionary<string, PawnState>
            {
                ["old"] = new PawnState("pawn-1", AgentState.Working),
            };
            var failedSeen = new Dictionary<string, AgentState> { ["old"] = AgentState.Working };
            bool failed = false;
            failedStore.Save(new SessionInfo { Name = "new" }, false, "old",
                             () => throw new Exception("unexpected success"), _ => failed = true);
            failedStore.ApplySessions(ProtobufFixtures.Read<Wire.SessionsReply>(Snapshot("new")));
            Reconcile(failedStore, failedBindings, failedSeen);
            FindRequest("PUT").Fail("rejected");
            Reconcile(failedStore, failedBindings, failedSeen);
            AssertEx.True(failed, "real failure callback ran");
            AssertEx.Equal(1, failedBindings.Count, "failure leaves one replacement binding");
            AssertEx.True(failedBindings["new"].Id != "pawn-1", "failure removes exemption");
        }

        static void Reconcile(SessionStore store, Dictionary<string, PawnState> bindings,
                              Dictionary<string, AgentState> seen)
        {
            var members = new ColonySessionIndex();
            members.Refresh(store.Sessions, store.Version);
            string Destination(string name) =>
                store.TryPendingRename(name, out var destination) ? destination : null;
            string Source(string name) =>
                store.TryPendingRenameSource(name, out var source) ? source : null;

            foreach (var name in bindings.Keys.ToList())
                if (!members.Contains(name) &&
                    !AgentRenamePolicy.Keeps(name, members.Contains, Destination, Source))
                {
                    bindings.Remove(name);
                    seen.Remove(name);
                }

            foreach (var session in store.Sessions)
                if (members.Contains(session.Name) && !bindings.ContainsKey(session.Name) &&
                    !AgentRenamePolicy.Covers(session.Name, bindings.ContainsKey, Destination, Source))
                    bindings[session.Name] = new PawnState("spawned-" + session.Name, AgentState.Down);

            foreach (var name in bindings.Keys.ToList())
            {
                string session = AgentRenamePolicy.SessionName(name, members.Contains,
                                                                Destination, Source);
                var state = store.Get(session)?.State ?? AgentState.Down;
                seen[name] = state;
                bindings[name].State = state;
            }
        }

        static void MoveBinding(Dictionary<string, PawnState> bindings,
                                Dictionary<string, AgentState> seen,
                                string oldName, string newName)
        {
            var pawn = bindings[oldName];
            bindings.Remove(oldName);
            bindings[newName] = pawn;
            var state = seen[oldName];
            seen.Remove(oldName);
            seen[newName] = state;
        }

        static void SessionRenameSettles()
        {
            var store = NewStore("old");
            var edited = new SessionInfo { Name = "new" };
            bool failed = false;
            store.Save(edited, false, "old", () => { }, _ => failed = true);
            AssertEx.True(store.TryPendingRename("old", out var destination) && destination == "new",
                           "rename is pending before response");

            // Pushed list first: the reverse lookup keeps the colony aware of the source.
            store.ApplySessions(ProtobufFixtures.Read<Wire.SessionsReply>(Snapshot("new")));
            AssertEx.True(store.TryPendingRenameSource("new", out var source) && source == "old",
                           "pushed destination exposes pending source");
            FindRequest("PUT").Ok(JVal.Null);
            AssertEx.False(store.TryPendingRename("old", out _), "success settles pending rename");
            AssertEx.False(failed, "success did not call failure");
            AssertEx.True(store.Get("new") != null, "destination remains in store");

            // Callback first: the local rename settles the mapping before the next push.
            store = NewStore("old");
            store.Save(edited, false, "old", () => { }, _ => failed = true);
            FindRequest("PUT").Ok(JVal.Null);
            AssertEx.False(store.TryPendingRename("old", out _), "callback-first success settles");
            store.ApplySessions(ProtobufFixtures.Read<Wire.SessionsReply>(Snapshot("new")));
            AssertEx.True(store.Get("new") != null, "callback-first push keeps destination");

            // A failed request removes the exemption, and deleting a session remains ordinary.
            store = NewStore("old");
            store.Save(edited, false, "old", () => { }, _ => failed = true);
            FindRequest("PUT").Fail("rejected");
            AssertEx.True(failed, "failure callback ran");
            AssertEx.False(store.TryPendingRename("old", out _), "failure settles pending rename");
            store.ApplySessions(ProtobufFixtures.Read<Wire.SessionsReply>(Snapshot()));
            AssertEx.True(store.Get("old") == null, "deleted session is gone");
        }

        static void ClosedTerminalRename()
        {
            var sent = new List<string>();
            var terminal = new TerminalIO(message => sent.Add(JVal.ToJson(ProtobufFixtures.Json(message))));
            terminal.Rename("closed", "renamed");
            terminal.Resubscribe();
            AssertEx.Equal(0, sent.Count, "closed rename has no socket commands");
        }

        static void OpenTerminalRename()
        {
            var sent = new List<string>();
            var terminal = new TerminalIO(message => sent.Add(JVal.ToJson(ProtobufFixtures.Json(message))));
            terminal.Subscribe("old");
            sent.Clear();
            terminal.Rename("old", "new");
            AssertEx.Equal(2, sent.Count, "open rename transfers subscription");
            AssertEx.True(sent[0].Contains("\"unsub\"") && sent[0].Contains("old"),
                           "old subscription removed");
            AssertEx.True(sent[1].Contains("\"sub\"") && sent[1].Contains("new"),
                           "new subscription added");
        }

        static void ReconnectSubscriptions()
        {
            var sent = new List<string>();
            var terminal = new TerminalIO(message => sent.Add(JVal.ToJson(ProtobufFixtures.Json(message))));
            terminal.Subscribe("old");
            terminal.Subscribe("already");
            sent.Clear();
            terminal.Rename("old", "already");
            sent.Clear();
            terminal.Resubscribe();
            AssertEx.Equal(1, sent.Count, "reconnect restores deduplicated set");
            AssertEx.True(sent[0].Contains("already") && !sent[0].Contains("old"),
                           "only destination remains subscribed");
        }

        sealed class FakeSocket : IHubSocket
        {
            readonly bool _result;
            public readonly string Failure;
            public bool Disposed;
            public bool Connected { get; private set; }
            public string LastError => Failure;
            public IncomingMessageQueue Incoming { get; } = new IncomingMessageQueue();

            public FakeSocket(bool result, string failure = "refused")
            {
                _result = result;
                Failure = failure;
            }

            public bool Connect(string host, int port, string path, string token, int timeoutMs = 3000)
            {
                Connected = _result;
                return _result;
            }

            public void SendBinary(byte[] text) { }

            public void Dispose()
            {
                Disposed = true;
                Connected = false;
                Incoming.Close();
            }
        }

        sealed class Scheduler
        {
            public readonly List<Action> Pending = new List<Action>();
            public void Queue(Action action) => Pending.Add(action);
            public void Run(int index)
            {
                var action = Pending[index];
                Pending.RemoveAt(index);
                action();
            }
        }

        static void DisabledConnectResults()
        {
            bool previous = Settings.S.autoConnect;
            try
            {
                foreach (bool result in new[] { true, false })
                {
                    var scheduler = new Scheduler();
                    var socket = new FakeSocket(result);
                    var retry = new FakeSocket(true);
                    var sockets = new Queue<FakeSocket>(new[] { socket, retry });
                    var hub = new HubTransport(() => sockets.Dequeue(), scheduler.Queue);
                    Settings.S.autoConnect = true;
                    hub.Connect();
                    Settings.S.autoConnect = false;
                    hub.Update(); // invalidate while no socket is installed
                    scheduler.Run(0); // complete the discarded attempt
                    hub.Update(); // consume its result while disabled
                    AssertEx.True(socket.Disposed, "discarded socket disposed for " + result);

                    Settings.S.autoConnect = true;
                    hub.Update();
                    AssertEx.Equal(1, scheduler.Pending.Count, "same transport schedules retry");
                    scheduler.Run(0);
                    hub.Update();
                    AssertEx.True(hub.Connected && retry.Connected && !retry.Disposed,
                                  "same transport installs retry socket");
                    AssertEx.Equal("connected", hub.Status,
                                   "auto-connect retries after discarded " + result);
                }
            }
            finally { Settings.S.autoConnect = previous; }
        }

        static void StaleConnectResults()
        {
            bool previous = Settings.S.autoConnect;
            try
            {
                Settings.S.autoConnect = true;
                var scheduler = new Scheduler();
                var stale = new FakeSocket(true);
                var current = new FakeSocket(true);
                var sockets = new Queue<FakeSocket>(new[] { stale, current });
                var hub = new HubTransport(() => sockets.Dequeue(), scheduler.Queue);
                hub.Connect();
                hub.Connect(); // invalidates the first serial, leaves the second in flight
                scheduler.Run(0);
                hub.Update();
                AssertEx.Equal(1, scheduler.Pending.Count,
                               "stale result does not release newer in-flight attempt");
                AssertEx.True(stale.Disposed, "stale socket disposed");
                scheduler.Run(0);
                hub.Update();
                AssertEx.Equal("connected", hub.Status, "newer attempt still connects");
            }
            finally { Settings.S.autoConnect = previous; }
        }

        static SessionStore NewStore(string name)
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore();
            store.ApplySessions(ProtobufFixtures.Read<Wire.SessionsReply>(Snapshot(name)));
            return store;
        }

        static DaemonClient.Request FindRequest(string method) =>
            DaemonClient.Requests.Last(r => r.Method == method);

        static JVal Snapshot(string name = null) =>
            JVal.Parse("{\"sessions\":[" +
                       (name == null ? "" : "{\"name\":\"" + name +
                        "\",\"alive\":true,\"state\":\"working\"}") + "]}");

    }
}
