using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class DaemonRenderedBreadcrumbCacheTests
    {
        sealed class Request
        {
            public string Body;
            public Action<JVal> Ok;
            public Action<string> Fail;
            public void Reply(string value) => Ok(JVal.Parse("{\"breadcrumb\":" + JVal.Q(value) + "}"));
        }

        sealed class Fixture
        {
            public string Connection = "http://first\n1";
            public DateTime Now = new DateTime(2026, 1, 1);
            public readonly List<Request> Requests = new List<Request>();
            public readonly DaemonConfig Config = new DaemonConfig
            {
                MetadataAvailable = true, ExperimentalInstructions = true,
                ExperimentalBreadcrumbs = true, InstructionsBreadcrumbEnabled = true,
                InstructionsTemplate = "body", InstructionsMountPath = "context.md",
                InstructionsBreadcrumb = "Read {{ mount_path }}",
            };
            readonly ProjectInfo _project = new ProjectInfo { Name = "project" };
            readonly SessionInfo _agent = new SessionInfo
            {
                SlopworldMd = true, InstructionsBreadcrumb = true,
            };
            public readonly DaemonRenderedBreadcrumbCache Cache;

            public Fixture()
            {
                Cache = new DaemonRenderedBreadcrumbCache(() => Connection, () => Now,
                    (body, ok, fail) => Requests.Add(new Request { Body = body, Ok = ok, Fail = fail }));
            }

            public string Get() => Cache.GetOrRequest(_project, _agent, Config);
        }

        public static void CoalescesRequestsAndKeepsDraftIdentity()
        {
            var f = new Fixture();
            f.Get();
            f.Get();
            AssertEx.Equal(1, f.Requests.Count, "redraw reuses the pending request");
            f.Config.InstructionsBreadcrumb = "new template";
            f.Get();
            f.Requests[1].Reply("new rendering");
            f.Requests[0].Reply("old rendering");
            AssertEx.Equal("new rendering", f.Get(), "late response belongs only to the old draft");
            f.Config.InstructionsBreadcrumb = "Read {{ mount_path }}";
            AssertEx.Equal("old rendering", f.Get(), "old rendering retained under its request key");
            AssertEx.Equal("Read {{ mount_path }}",
                JVal.Parse(f.Requests[0].Body)["breadcrumb"].AsString(), "request captures the draft");
        }

        public static void ConnectionChangesRejectLateReplies()
        {
            var f = new Fixture();
            f.Get();
            f.Connection = "http://second\n2";
            // A reply must check the connection even before another draw notices the switch.
            f.Requests[0].Reply("first daemon");
            AssertEx.Equal(null, f.Get(), "another daemon cannot reuse the old cached response");
            f.Requests[1].Reply("second daemon");
            AssertEx.Equal("second daemon", f.Get(), "current daemon supplies its rendering");
            f.Connection = "http://second\n3";
            AssertEx.Equal(null, f.Get(), "reconnect to the same address invalidates cached policy");
            f.Requests[1].Fail("old error");
            f.Get();
            AssertEx.Equal(3, f.Requests.Count, "old error cannot release a new pending request");
        }

        public static void FailuresBackOffAndStopUntilExpiry()
        {
            var f = new Fixture();
            f.Get();
            f.Requests[0].Fail("offline");
            for (int i = 0; i < 10; i++) f.Get();
            AssertEx.Equal(1, f.Requests.Count, "failure does not retry on each draw");
            f.Now = f.Now.AddSeconds(1);
            f.Get();
            f.Requests[1].Fail("offline");
            f.Now = f.Now.AddSeconds(1);
            f.Get();
            AssertEx.Equal(2, f.Requests.Count, "second failure uses a longer delay");
            f.Now = f.Now.AddSeconds(1);
            f.Get();
            f.Requests[2].Fail("offline");
            f.Now = f.Now.AddSeconds(10);
            f.Get();
            AssertEx.Equal(DaemonRenderedBreadcrumbCache.MaxAttempts, f.Requests.Count,
                           "attempts are bounded within the cache lifetime");
            f.Now += DaemonRenderedBreadcrumbCache.Lifetime;
            f.Get();
            AssertEx.Equal(4, f.Requests.Count, "expired failure can recover");
        }

        public static void EvictionDoesNotResurrectPendingEntries()
        {
            var f = new Fixture();
            for (int i = 0; i <= DaemonRenderedBreadcrumbCache.MaxEntries; i++)
            {
                f.Config.InstructionsBreadcrumb = "draft " + i;
                f.Get();
                f.Now = f.Now.AddMilliseconds(1);
            }
            f.Requests[0].Reply("evicted");
            f.Config.InstructionsBreadcrumb = "draft 0";
            AssertEx.Equal(null, f.Get(), "late callback cannot restore an evicted entry");
            AssertEx.Equal(DaemonRenderedBreadcrumbCache.MaxEntries + 2, f.Requests.Count,
                           "evicted key starts a fresh request");
        }

        public static void EmptyBreadcrumbIsAValueButMissingFieldIsNot()
        {
            var f = new Fixture();
            f.Get();
            f.Requests[0].Ok(JVal.Parse("{}"));
            f.Get();
            AssertEx.Equal(1, f.Requests.Count, "missing field uses bounded error backoff");
            f.Now = f.Now.AddSeconds(1);
            f.Get();
            f.Requests[1].Reply("");
            AssertEx.Equal("", f.Get(), "explicitly empty daemon rendering is cached");
            f.Now += DaemonRenderedBreadcrumbCache.Lifetime;
            AssertEx.Equal(null, f.Get(), "successful cached values also expire");
        }
    }
}
