using System;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class SessionStoreTests
    {
        static Wire.SessionsReply Sessions(string name) =>
            new Wire.SessionsReply { Sessions = { new Wire.SessionView {
                Name = name, Launch = new Wire.SessionLaunchView(), Worker = new Wire.SessionWorkerView(), Reader = new Wire.SessionReaderView(), Runtime = new Wire.SessionRuntimeView()
            } } };

        public static void RefreshRejectsSupersededSnapshots()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore();
            int completed = 0;
            store.Refresh(() => completed++);
            store.Refresh(() => completed++);
            DaemonClient.Requests[1].Ok(ProtobufFixtures.Json(Sessions("new")));
            long version = store.Version;
            DaemonClient.Requests[0].Ok(ProtobufFixtures.Json(Sessions("old")));
            Assert.That(store.Get("new"), Is.Not.Null);
            Assert.That(store.Get("old"), Is.Null);
            Assert.That(store.Version, Is.EqualTo(version));
            Assert.That(completed, Is.EqualTo(2), "stale reads still settle their callers");
        }

        public static void PushSupersedesPendingRefresh()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore();
            store.Refresh();
            store.ApplySessions(Sessions("pushed"));
            DaemonClient.Requests.Single().Ok(ProtobufFixtures.Json(Sessions("stale")));
            Assert.That(store.Sessions.Select(s => s.Name), Is.EqualTo(new[] { "pushed" }));
        }

        public static void RefreshFailureRetainsSessions()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore();
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
            var store = new SessionStore();
            string started = null;
            store.Run("project", "echo hello", "label", name => {
                Assert.That(store.Get(name), Is.Not.Null, "session exists before opening its terminal");
                started = name;
            }, shell: false, text: "prompt", host: true, temp: true,
                path: "/work", hold: true, like: "seed", agentTemplate: "template",
                readerPath: "/source/file", readerKey: "key", readerScope: "scope", readerLine: 17, readerPinned: true);
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
            Assert.That(body.Reader.Line, Is.EqualTo(17));
            Assert.That(body.Reader.Pinned, Is.True);
            request.Ok(JVal.Parse("{\"session\":\"created\"}"));
            Assert.That(started, Is.Null);
            Assert.That(DaemonClient.Requests[1].Method, Is.EqualTo("GET"));
            DaemonClient.Requests[1].Ok(ProtobufFixtures.Json(Sessions("created")));
            Assert.That(started, Is.EqualTo("created"));
        }

        public static void LibraryRunForwardsOverridesAndRefreshFailure()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore();
            string error = null;
            bool started = false;
            store.RunLibraryItem("folder/item name", _ => started = true, e => error = e,
                "project", true, new System.Collections.Generic.List<string> { "tip" });
            var request = DaemonClient.Requests.Single();
            Assert.That(request.Path, Is.EqualTo(WireProtocol.Routes.Library + "/folder%2Fitem%20name/run"));
            var body = (Wire.RunWhere)request.Body;
            Assert.That(body.Project, Is.EqualTo("project"));
            Assert.That(body.Temp, Is.True);
            Assert.That(body.RandomTips, Is.EqualTo(new[] { "tip" }));
            request.Ok(JVal.Parse("{\"session\":\"created\"}"));
            DaemonClient.Requests[1].Fail("refresh failed");
            Assert.That(error, Is.EqualTo("refresh failed"));
            Assert.That(started, Is.False);
        }

        public static void FailedRenameClearsBothPendingLookups()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore();
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
            var store = new SessionStore();
            store.ApplySessions(Sessions("agent"));
            foreach (string name in new[] { "agent", "other" })
            {
                store.ApplyScreen(new Wire.ScreenView { Name = name, Seq = 20 });
                store.ApplyScreen(new Wire.ScreenView { Name = name, RequestId = 1, Off = 5 });
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
            Assert.That(store.Screen("agent").Seq, Is.EqualTo(1), "new connection may restart sequence numbers");
        }

        public static void CurrentPathUsesEscapedSessionAndForwardsFailures()
        {
            DaemonClient.Requests.Clear();
            var store = new SessionStore();
            string path = null, error = null;
            store.CurrentPath("a/b c", p => path = p, e => error = e);
            var request = DaemonClient.Requests.Single();
            Assert.That(request.Path, Is.EqualTo(WireProtocol.Routes.Sessions + "/a%2Fb%20c/cwd"));
            request.Ok(JVal.Parse("{\"path\":\"/current/directory\"}"));
            Assert.That(path, Is.EqualTo("/current/directory"));
            store.CurrentPath("a/b c", null, e => error = e);
            DaemonClient.Requests.Last().Fail("gone");
            Assert.That(error, Is.EqualTo("gone"));
        }
    }
}
