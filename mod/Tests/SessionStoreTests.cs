using System;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class SessionStoreTests
    {
        public static void LaunchWaitsThroughAnOlderPushForItsOwnSession()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore(_ => { });
            int completed = 0;
            store.RefreshLaunched("worker", () => completed++);
            store.ApplySessions(Sessions("old"));
            Assert.That(completed, Is.Zero);
            DaemonClient.Requests[0].Reply(Sessions("worker"));
            Assert.That(completed, Is.EqualTo(1));
            Assert.That(store.Get("worker"), Is.Not.Null);
        }

        public static void ScreenSequencesRemainUnsignedAcrossIntegerBoundaries()
        {
            var store = new SessionStore(_ => { });
            ulong[] sequences =
            {
                0,
                int.MaxValue,
                (ulong)int.MaxValue + 1,
                uint.MaxValue,
                (ulong)uint.MaxValue + 1,
                ulong.MaxValue,
            };
            foreach (ulong seq in sequences)
            {
                store.ApplyScreen(new Wire.ScreenView { Name = "agent", Seq = seq });
                Assert.That(store.Screen("agent").Seq, Is.EqualTo(seq));
                if (seq == 0)
                    continue;
                store.ApplyScreen(new Wire.ScreenView { Name = "agent", Seq = seq - 1 });
                Assert.That(store.Screen("agent").Seq, Is.EqualTo(seq), "stale frame rejected");
            }
        }

        public static void MissingLaunchAndReaderPinSettleWithoutOpeningTerminal()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore(_ => { });
            Assert.That(store.Get(null), Is.Null);
            Assert.That(store.TryPendingRenameSource(null, out _), Is.False);
            string error = null;
            store.RefreshLaunched(
                "gone",
                () => Assert.Fail("missing launch opened"),
                e => error = e
            );
            DaemonClient.Requests.Single().Reply(Sessions("other"));
            Assert.That(error, Is.EqualTo("Launched session is no longer available."));
            store.ApplySessions(Sessions("later"));
            Assert.That(store.Get("later"), Is.Not.Null, "failed launch no longer blocks pushes");
            DaemonClient.Requests.Clear();
            store.SetReaderPinned("a/b", true);
            var request = DaemonClient.Requests.Single();
            Assert.That(
                request.Path,
                Is.EqualTo(WireProtocol.Routes.Sessions + "/a%2Fb/reader-pinned")
            );
            Assert.That(((Wire.ReaderPinnedReq)request.Body).Pinned, Is.True);
            request.Ok(JVal.Null);
            Assert.That(DaemonClient.Requests.Last().Method, Is.EqualTo("GET"));
            DaemonClient.Requests.Clear();
        }

        public static void DefaultRunUsesPanelSizeAndRejectsMissingReaderProject()
        {
            DaemonClient.Requests.Clear();
            WorkspaceWindow.TestCols = 100;
            WorkspaceWindow.TestRows = 30;
            try
            {
                var store = new SessionStore(_ => { });
                store.Run(null, null, null, null);
                var body = (Wire.RunReq)DaemonClient.Requests.Single().Body;
                Assert.That(body.Cols, Is.EqualTo(100));
                Assert.That(body.Rows, Is.EqualTo(30));
                Assert.That(body.Project, Is.Empty);
                Assert.That(body.Command, Is.Empty);
                Assert.That(body.Label, Is.Empty);
                DaemonClient.Requests.Clear();
                var missing = new BrowseScope { ProjectId = "missing", Worktree = "branch" };
                string error = null;
                store.Run(missing.Key, "", "", null, e => error = e);
                Assert.That(error, Is.EqualTo("The reader's project has gone."));
                error = null;
                store.RunHostShell(missing.Key, null, e => error = e);
                Assert.That(error, Is.EqualTo("The reader's project has gone."));
                Assert.That(DaemonClient.Requests, Is.Empty);
            }
            finally
            {
                WorkspaceWindow.TestCols = WorkspaceWindow.TestRows = 0;
                DaemonClient.Requests.Clear();
            }
        }

        static Wire.SessionsReply Sessions(string name) =>
            SessionFixtures.Snapshot(SessionFixtures.View(name));

        public static void CallbackFailuresAreReportedWithoutBlockingOtherWaiters()
        {
            DaemonClient.Requests.Clear();
            var expected = new InvalidOperationException("callback failed");
            Exception reported = null;
            var store = new SessionStore(error => reported = error);
            int completed = 0;
            store.Refresh(() => throw expected);
            store.Refresh(() => completed++);
            store.ApplySessions(Sessions("winner"));
            Assert.That(reported, Is.SameAs(expected));
            Assert.That(completed, Is.EqualTo(1));
            Assert.That(store.Get("winner"), Is.Not.Null);
            DaemonClient.Requests[0].Fail("stale");
            DaemonClient.Requests[1].Fail("stale");
            Assert.That(completed, Is.EqualTo(1));
        }

        public static void RefreshRejectsSupersededSnapshots()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore(_ => { });
            int completed = 0;
            store.Refresh(() => completed++);
            store.Refresh(() => completed++);
            DaemonClient.Requests[1].Reply(Sessions("new"));
            long version = store.Version;
            DaemonClient.Requests[0].Reply(Sessions("old"));
            Assert.That(store.Get("new"), Is.Not.Null);
            Assert.That(store.Get("old"), Is.Null);
            Assert.That(store.Version, Is.EqualTo(version));
            Assert.That(completed, Is.EqualTo(2), "stale reads still settle their callers");
        }

        public static void SupersededRefreshWaitsForWinningOutcome()
        {
            foreach (bool fail in new[] { false, true })
            {
                DaemonClient.Requests.Clear();
                var store = new SessionStore(_ => { });
                int completed = 0,
                    failed = 0;
                Action done = () =>
                {
                    Assert.That(store.Get("winner"), Is.Not.Null);
                    completed++;
                };
                Action<string> error = message =>
                {
                    Assert.That(message, Is.EqualTo("offline"));
                    failed++;
                };
                store.Refresh(done, error);
                store.Refresh(done, error);
                DaemonClient.Requests[0].Reply(Sessions("stale"));
                Assert.That(completed + failed, Is.Zero);
                if (fail)
                    DaemonClient.Requests[1].Fail("offline");
                else
                    DaemonClient.Requests[1].Reply(Sessions("winner"));
                Assert.That(completed, Is.EqualTo(fail ? 0 : 2));
                Assert.That(failed, Is.EqualTo(fail ? 2 : 0));
                DaemonClient.Requests[0].Fail("stale error");
                Assert.That(completed + failed, Is.EqualTo(2));
            }
        }

        public static void PushSettlesWaitersBeforeStaleHttpFailure()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore(_ => { });
            int completed = 0,
                failed = 0;
            store.Refresh(
                () =>
                {
                    Assert.That(store.Get("pushed"), Is.Not.Null);
                    completed++;
                    store.Refresh(() => completed++, _ => failed++);
                },
                _ => failed++
            );
            store.ApplySessions(Sessions("pushed"));
            Assert.That(completed, Is.EqualTo(1));
            DaemonClient.Requests[0].Fail("old failure");
            Assert.That(failed, Is.Zero);
            DaemonClient.Requests[1].Reply(Sessions("later"));
            Assert.That(completed, Is.EqualTo(2));
        }

        public static void PushSupersedesPendingRefresh()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore(_ => { });
            store.Refresh();
            store.ApplySessions(Sessions("pushed"));
            DaemonClient.Requests.Single().Reply(Sessions("stale"));
            Assert.That(store.Sessions.Select(s => s.Name), Is.EqualTo(new[] { "pushed" }));
        }

        public static void RefreshFailureRetainsSessions()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore(_ => { });
            store.ApplySessions(Sessions("retained"));
            long version = store.Version;
            string error = null;
            bool completed = false;
            store.Refresh(() => completed = true, message => error = message);
            DaemonClient.Requests.Single().Fail("offline");
            Assert.That(error, Is.EqualTo("offline"));
            Assert.That(completed, Is.False);
            Assert.That(store.Get("retained"), Is.Not.Null);
            Assert.That(store.Version, Is.EqualTo(version));
        }

        public static void RunWaitsForSessionRefresh()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore(_ => { });
            string started = null;
            store.Run(
                "project",
                "echo hello",
                "label",
                name =>
                {
                    Assert.That(
                        store.Get(name),
                        Is.Not.Null,
                        "session exists before opening its terminal"
                    );
                    started = name;
                },
                options: new SessionRunOptions
                {
                    Shell = false,
                    Text = "prompt",
                    Host = true,
                    Temp = true,
                    Path = "/work",
                    Hold = true,
                    Like = "seed",
                    AgentTemplate = "template",
                    Reader = new ReaderLaunchOptions
                    {
                        Path = "/source/file",
                        Key = "key",
                        Scope = "scope",
                        Line = 2147483648L,
                        Pinned = true,
                    },
                }
            );
            var request = DaemonClient.Requests.Single();
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(WireProtocol.Routes.Run));
            var body = (Wire.RunReq)request.Body;
            Assert.That(body.Project, Is.EqualTo("project"));
            Assert.That(body.Command, Is.EqualTo("echo hello"));
            Assert.That(body.Kind, Is.EqualTo("prompt"));
            Assert.That(body.Label, Is.EqualTo("label"));
            Assert.That(body.Text, Is.EqualTo("prompt"));
            Assert.That(body.Path, Is.EqualTo("/work"));
            Assert.That(body.Host && body.Temp && body.Hold, Is.True);
            Assert.That(body.Like, Is.EqualTo("seed"));
            Assert.That(body.AgentTemplate, Is.EqualTo("template"));
            Assert.That(body.Reader.Path, Is.EqualTo("/source/file"));
            Assert.That(body.Reader.Key, Is.EqualTo("key"));
            Assert.That(body.Reader.Scope, Is.EqualTo("scope"));
            Assert.That(body.Reader.Line, Is.EqualTo(2147483648U));
            Assert.That(body.Reader.Pinned, Is.True);
            request.Reply(new Wire.SessionResult { Session = "created" });
            Assert.That(started, Is.Null);
            Assert.That(DaemonClient.Requests[1].Method, Is.EqualTo("GET"));
            DaemonClient.Requests[1].Reply(Sessions("created"));
            Assert.That(started, Is.EqualTo("created"));
        }

        public static void LibraryRunForwardsOverridesAndRefreshFailure()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore(_ => { });
            string error = null;
            bool started = false;
            store.RunLibraryItem(
                "folder/item name",
                _ => started = true,
                e => error = e,
                "project",
                true,
                new System.Collections.Generic.List<string> { "tip" }
            );
            var request = DaemonClient.Requests.Single();
            Assert.That(
                request.Path,
                Is.EqualTo(WireProtocol.Routes.Library + "/folder%2Fitem%20name/run")
            );
            var body = (Wire.RunWhere)request.Body;
            Assert.That(body.Project, Is.EqualTo("project"));
            Assert.That(body.Temp, Is.True);
            Assert.That(body.RandomTips, Is.EqualTo(new[] { "tip" }));
            request.Reply(new Wire.SessionResult { Session = "created" });
            DaemonClient.Requests[1].Fail("refresh failed");
            Assert.That(error, Is.EqualTo("refresh failed"));
            Assert.That(started, Is.False);
        }

        public static void FailedRenameClearsBothPendingLookups()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore(_ => { });
            store.ApplySessions(Sessions("old"));
            string error = null;
            store.Save(new SessionInfo { Name = "new" }, false, "old", null, e => error = e);
            Assert.That(store.TryPendingRename("old", out var target), Is.True);
            Assert.That(target, Is.EqualTo("new"));
            Assert.That(store.TryPendingRenameSource("new", out var source), Is.True);
            Assert.That(source, Is.EqualTo("old"));
            DaemonClient.Requests.Single().Fail("rename rejected");
            Assert.That(error, Is.EqualTo("rename rejected"));
            Assert.That(store.TryPendingRename("old", out _), Is.False);
            Assert.That(store.TryPendingRenameSource("new", out _), Is.False);
            Assert.That(store.Get("old"), Is.Not.Null);
            Assert.That(store.Get("new"), Is.Null);
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(1), "failed save does not refresh");
        }

        public static void ReconnectDropsLiveAndQueuedHistoryForAllSessions()
        {
            var store = new SessionStore(_ => { });
            store.ApplySessions(Sessions("agent"));
            foreach (string name in new[] { "agent", "other" })
            {
                store.ApplyScreen(new Wire.ScreenView { Name = name, Seq = 20 });
                store.ApplyScreen(
                    new Wire.ScreenView
                    {
                        Name = name,
                        RequestId = 1,
                        Off = 5,
                    }
                );
            }
            long version = store.Version;
            store.ResetConnectionScreens();
            foreach (string name in new[] { "agent", "other" })
            {
                Assert.That(store.Screen(name), Is.Null);
                Assert.That(store.TryScrollScreen(name, out _), Is.False);
            }
            Assert.That(store.Get("agent"), Is.Not.Null);
            Assert.That(store.Version, Is.EqualTo(version));
            store.ApplyScreen(new Wire.ScreenView { Name = "agent", Seq = 1 });
            Assert.That(
                store.Screen("agent").Seq,
                Is.EqualTo(1),
                "new connection may restart sequence numbers"
            );
        }

        public static void CurrentPathUsesEscapedSessionAndForwardsFailures()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore(_ => { });
            string path = null,
                error = null;
            store.CurrentPath("a/b c", p => path = p, e => error = e);
            var request = DaemonClient.Requests.Single();
            Assert.That(request.Path, Is.EqualTo(WireProtocol.Routes.Sessions + "/a%2Fb%20c/cwd"));
            request.Reply(new Wire.PathResult { Path = "/current/directory" });
            Assert.That(path, Is.EqualTo("/current/directory"));
            store.CurrentPath("a/b c", null, e => error = e);
            DaemonClient.Requests.Last().Fail("gone");
            Assert.That(error, Is.EqualTo("gone"));
        }
    }
}
