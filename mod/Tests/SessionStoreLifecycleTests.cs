using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class SessionStoreLifecycleTests
    {
        static Wire.SessionsReply Snapshot(params (string Name, bool Alive)[] sessions)
        {
            var reply = new Wire.SessionsReply();
            foreach (var (name, alive) in sessions)
                reply.Sessions.Add(new Wire.SessionView { Name = name, Launch = new Wire.SessionLaunchView(), Worker = new Wire.SessionWorkerView(), Reader = new Wire.SessionReaderView(), Runtime = new Wire.SessionRuntimeView { Alive = alive } });
            return reply;
        }

        static void Screens(SessionStore store, string name)
        {
            store.ApplyScreen(new Wire.ScreenView { Name = name, Seq = 20 });
            store.ApplyScreen(new Wire.ScreenView { Name = name, RequestId = 1, Off = 3 });
            store.ApplyScreen(new Wire.ScreenView { Name = name, RequestId = 2, Off = 0 });
        }

        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            foreach (bool begin in new[] { true, false })
                yield return ($"subscription {(begin ? "begin" : "end")} clears only its own screen stream", () => Subscription(begin));
            foreach (string operation in new[] { "start", "stop", "restart", "state/reset", "remove", "label", "create", "edit" })
                foreach (bool failure in new[] { true, false })
                    yield return ($"session {operation} {(failure ? "failure" : "success")} preserves mutation ordering", () => Mutation(operation, failure));
        }

        static void Subscription(bool begin)
        {
            var store = new SessionStore();
            store.ApplySessions(Snapshot(("agent", true), ("other", true)));
            Screens(store, "agent");
            Screens(store, "other");
            long version = store.Version;
            Action<string> change = begin ? store.BeginSubscription : store.EndSubscription;
            change(null);
            change("");
            Assert.That(store.Screen("agent").Seq, Is.EqualTo(20), "empty subscription has no effect");
            change("agent");
            Assert.That(store.Screen("agent"), Is.Null);
            Assert.That(store.TryScrollScreen("agent", out _), Is.False);
            Assert.That(store.Screen("other").Seq, Is.EqualTo(20));
            Assert.That(store.TryScrollScreen("other", out var first), Is.True);
            Assert.That(first.ScrollRequestId, Is.EqualTo(1));
            Assert.That(store.TryScrollScreen("other", out var second), Is.True);
            Assert.That(second.ScrollRequestId, Is.EqualTo(2), "off-zero history remains queued separately");
            Assert.That(first, Is.Not.SameAs(second), "history responses are immutable snapshots");
            Assert.That(store.TryScrollScreen("other", out _), Is.False);
            Assert.That(store.Version, Is.EqualTo(version));
            store.ApplyScreen(new Wire.ScreenView { Name = "agent", Seq = 1 });
            Assert.That(store.Screen("agent").Seq, Is.EqualTo(1), "new subscription accepts a fresh baseline");
        }

        public static void SessionSnapshotsPruneRemovedScreensAndResetChangedProcesses()
        {
            var store = new SessionStore();
            store.ApplySessions(Snapshot(("removed", true), ("stopped", true), ("started", false), ("kept", true)));
            foreach (string name in new[] { "removed", "stopped", "started", "kept" }) Screens(store, name);
            var kept = store.Screen("kept");
            store.ApplySessions(Snapshot(("stopped", false), ("started", true), ("kept", true)));
            foreach (string name in new[] { "removed", "stopped", "started" })
            {
                Assert.That(store.Screen(name), Is.Null, name);
                Assert.That(store.TryScrollScreen(name, out _), Is.False, name);
            }
            Assert.That(store.Get("removed"), Is.Null);
            Assert.That(store.Screen("kept"), Is.SameAs(kept));
            Assert.That(store.TryScrollScreen("kept", out _), Is.True);
            store.ApplyScreen(new Wire.ScreenView { Name = "started", Seq = 1 });
            Assert.That(store.Screen("started").Seq, Is.EqualTo(1));
        }

        public static void RenameMovesHistoryWithoutOverwritingDestinationScreens()
        {
            var store = new SessionStore();
            store.ApplySessions(Snapshot(("unrelated", true), ("old", true)));
            Screens(store, "old");
            long version = store.Version;
            store.Rename(null, "new");
            store.Rename("old", "");
            store.Rename("old", "old");
            Assert.That(store.Version, Is.EqualTo(version));
            store.Rename("old", "new");
            Assert.That(store.Get("old"), Is.Null);
            Assert.That(store.Get("new"), Is.Not.Null);
            Assert.That(store.Screen("old"), Is.Null);
            Assert.That(store.Screen("new").Seq, Is.EqualTo(20));
            Assert.That(store.TryScrollScreen("old", out _), Is.False);
            Assert.That(store.TryScrollScreen("new", out var history), Is.True);
            Assert.That(history.ScrollRequestId, Is.EqualTo(1));
            Assert.That(store.Version, Is.EqualTo(version + 1));
            Screens(store, "orphan");
            var destination = store.Screen("new");
            store.Rename("orphan", "new");
            Assert.That(store.Screen("new"), Is.SameAs(destination));
            Assert.That(store.Screen("orphan"), Is.Null);
            Assert.That(store.TryScrollScreen("orphan", out _), Is.False);
            Assert.That(store.TryScrollScreen("new", out history), Is.True);
            Assert.That(history.ScrollRequestId, Is.EqualTo(2), "destination history wins over stale source history");
            Assert.That(store.TryScrollScreen("new", out _), Is.False);
            Assert.That(store.Version, Is.EqualTo(version + 1), "moving orphan screens does not change session version");
        }

        static void Mutation(string operation, bool failure)
        {
            var requests = DaemonClient.Requests;
            requests.Clear();
            try
            {
                var store = new SessionStore();
                store.ApplySessions(Snapshot(("a/b c", true)));
                long version = store.Version;
                string error = null;
                int completed = 0;
                Action<string> fail = e => error = e;
                Action ok = () => completed++;
                switch (operation)
                {
                    case "start": store.Start("a/b c", fail); break;
                    case "stop": store.Stop("a/b c", fail); break;
                    case "restart": store.Restart("a/b c", fail); break;
                    case "state/reset": store.ResetState("a/b c", fail); break;
                    case "remove": store.Remove("a/b c", fail); break;
                    case "label": store.SetLabel("a/b c", null, ok, fail); break;
                    case "create": store.Save(new SessionInfo { Name = "a/b c", Cmd = "echo hello" }, true, "a/b c", ok, fail); break;
                    case "edit": store.Save(new SessionInfo { Name = "a/b c", Cmd = "echo hello" }, false, "a/b c", ok, fail); break;
                    default: throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation");
                }
                var request = requests.Single();
                string root = WireProtocol.Routes.Sessions;
                string path = root + "/a%2Fb%20c";
                if (operation == "create") path = root;
                else if (operation != "edit" && operation != "remove") path += "/" + operation;
                Assert.That(request.Path, Is.EqualTo(path));
                Assert.That(request.Method, Is.EqualTo(operation == "remove" ? "DELETE" : operation == "label" || operation == "edit" ? "PUT" : "POST"));
                if (operation == "label") Assert.That(((Wire.LabelReq)request.Body).Label, Is.Empty);
                if (operation == "create" || operation == "edit")
                    Assert.That(((Wire.SessionConfig)request.Body).Cmd, Is.EqualTo("echo hello"));
                Assert.That(store.TryPendingRename("a/b c", out _), Is.False, "ordinary save is not a rename");
                Assert.That(store.Version, Is.EqualTo(version), "request does not optimistically replace snapshot");
                if (failure)
                {
                    request.Fail("rejected");
                    Assert.That(error, Is.EqualTo("rejected"));
                    Assert.That(completed, Is.Zero);
                    Assert.That(requests.Count, Is.EqualTo(1));
                    Assert.That(store.Version, Is.EqualTo(version));
                }
                else
                {
                    request.Ok(JVal.Null);
                    Assert.That(requests.Count, Is.EqualTo(2));
                    Assert.That(requests[1].Method, Is.EqualTo("GET"));
                    Assert.That(requests[1].Path, Is.EqualTo(root));
                    Assert.That(completed, Is.EqualTo(operation == "label" || operation == "create" || operation == "edit" ? 1 : 0));
                    requests[1].Ok(ProtobufFixtures.Json(Snapshot(("fresh", true))));
                    Assert.That(store.Get("fresh"), Is.Not.Null);
                    Assert.That(error, Is.Null);
                }
            }
            finally { requests.Clear(); }
        }

        public static void HostShellUsesDaemonDefaultsAndReportsRunFailure()
        {
            DaemonClient.Requests.Clear();
            try
            {
                var store = new SessionStore();
                string error = null;
                store.RunHostShell("project", _ => Assert.Fail("failed run opened a terminal"), e => error = e);
                var request = DaemonClient.Requests.Single();
                var body = (Wire.RunReq)request.Body;
                Assert.That(body.Host, Is.True);
                Assert.That(body.Kind, Is.EqualTo("shell"));
                Assert.That(body.Command, Is.Empty);
                Assert.That(body.Label, Is.Empty);
                Assert.That(body.Project, Is.EqualTo("project"));
                request.Fail("offline");
                Assert.That(error, Is.EqualTo("offline"));
                Assert.That(DaemonClient.Requests.Count, Is.EqualTo(1));
            }
            finally { DaemonClient.Requests.Clear(); }
        }
        public static void SessionRenameSettles()
        {
            var store = RenameStore("old");
            var edited = new SessionInfo { Name = "new" };
            bool failed = false;
            store.Save(edited, false, "old", () => { }, _ => failed = true);
            AssertEx.True(store.TryPendingRename("old", out var destination) && destination == "new",
                           "rename is pending before response");

            // Pushed list first: the reverse lookup keeps the colony aware of the source.
            store.ApplySessions(RenameSnapshot("new"));
            AssertEx.True(store.TryPendingRenameSource("new", out var source) && source == "old",
                           "pushed destination exposes pending source");
            RenameRequest("PUT").Ok(JVal.Null);
            AssertEx.False(store.TryPendingRename("old", out _), "success settles pending rename");
            AssertEx.False(failed, "success did not call failure");
            AssertEx.True(store.Get("new") != null, "destination remains in store");

            // Callback first: the local rename settles the mapping before the next push.
            store = RenameStore("old");
            store.Save(edited, false, "old", () => { }, _ => failed = true);
            RenameRequest("PUT").Ok(JVal.Null);
            AssertEx.False(store.TryPendingRename("old", out _), "callback-first success settles");
            store.ApplySessions(RenameSnapshot("new"));
            AssertEx.True(store.Get("new") != null, "callback-first push keeps destination");

            // A failed request removes the exemption, and deleting a session remains ordinary.
            store = RenameStore("old");
            store.Save(edited, false, "old", () => { }, _ => failed = true);
            RenameRequest("PUT").Fail("rejected");
            AssertEx.True(failed, "failure callback ran");
            AssertEx.False(store.TryPendingRename("old", out _), "failure settles pending rename");
            store.ApplySessions(RenameSnapshot());
            AssertEx.True(store.Get("old") == null, "deleted session is gone");
        }

        static SessionStore RenameStore(string name)
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore();
            store.ApplySessions(RenameSnapshot(name));
            return store;
        }

        static DaemonClient.Request RenameRequest(string method) =>
            DaemonClient.Requests.Last(r => r.Method == method);

        static Wire.SessionsReply RenameSnapshot(string name = null)
        {
            var reply = new Wire.SessionsReply();
            if (name != null) reply.Sessions.Add(new Wire.SessionView
            {
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
