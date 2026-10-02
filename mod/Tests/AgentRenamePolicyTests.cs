using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld.Tests
{
    static class AgentRenamePolicyTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("rename policy preserves fake bindings across either event order",
                          PendingRenameOrders);
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
                    store.ApplySessions(Snapshot("new"));
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

                if (!pushFirst) store.ApplySessions(Snapshot("new"));
                Reconcile(store, bindings, seen);
                AssertEx.True(ReferenceEquals(original, bindings["new"]),
                              "settled snapshot preserves original pawn");
                AssertEx.Equal(1, bindings.Count, "settled snapshot leaves one binding");

                store.ApplySessions(Snapshot());
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
            failedStore.ApplySessions(Snapshot("new"));
            Reconcile(failedStore, failedBindings, failedSeen);
            FindRequest("PUT").Fail("rejected");
            Reconcile(failedStore, failedBindings, failedSeen);
            AssertEx.True(failed, "real failure callback ran");
            AssertEx.Equal(1, failedBindings.Count, "failure leaves one replacement binding");
            AssertEx.True(failedBindings["new"].Id != "pawn-1", "failure removes exemption");
        }

        // Simulate bindings only to exercise AgentRenamePolicy decisions. Pawn handoff is game-bound.
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
                    !AgentRenamePolicy.KeepsBinding(name, members.Contains, Destination, Source))
                {
                    bindings.Remove(name);
                    seen.Remove(name);
                }

            foreach (var session in store.Sessions)
                if (members.Contains(session.Name) && !bindings.ContainsKey(session.Name) &&
                    !AgentRenamePolicy.HasBindingForSession(session.Name, bindings.ContainsKey, Destination, Source))
                    bindings[session.Name] = new PawnState("spawned-" + session.Name, AgentState.Down);

            foreach (var name in bindings.Keys.ToList())
            {
                string session = AgentRenamePolicy.ResolveSessionName(name, members.Contains,
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

        static SessionStore NewStore(string name)
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore();
            store.ApplySessions(Snapshot(name));
            return store;
        }

        static DaemonClient.Request FindRequest(string method) =>
            DaemonClient.Requests.Last(r => r.Method == method);

        static Wire.SessionsReply Snapshot(string name = null)
        {
            var reply = new Wire.SessionsReply();
            if (name != null) reply.Sessions.Add(new Wire.SessionView {
                Name = name,
                Launch = new Wire.SessionLaunchView(),
                Worker = new Wire.SessionWorkerView(),
                Reader = new Wire.SessionReaderView(),
                Runtime = new Wire.SessionRuntimeView { Alive = true, State = "working" },
            });
            return reply;
        }

    }
}
