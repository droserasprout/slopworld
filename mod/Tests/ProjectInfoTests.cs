using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class ProjectInfoTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("project edits preserve inherited agent resource caps", PreservesResourceCaps);
            yield return ("round trips host networking", RoundTripsHostNetworking);
            yield return ("uses daemon metadata for temporary paths", BuildsTemporaryPaths);
            yield return ("ignores stale temporary previews", IgnoresStaleTemporaryPreviews);
            yield return ("coalesces and bounds temporary preview retries", CoalescesPreviewRequests);
            yield return ("temporary preview resumes after mode is disabled", ResumesAfterDisable);
            yield return ("round trips and copies project settings", RoundTripsAndCopiesSettings);
        }

        static void PreservesResourceCaps()
        {
            var project = ProjectInfo.FromJson(JVal.Parse("{\"limits\":{\"memory_mb\":4096,\"pids\":128}}"));
            var copy = project.Copy();
            copy.Name = "renamed";
            var saved = JVal.Parse(copy.ToJson());
            AssertEx.Equal(4096, saved["limits"]["memory_mb"].AsInt(), "unrelated edits preserve project memory limit");
            AssertEx.Equal(128, saved["limits"]["pids"].AsInt(), "project copies preserve process limit");
        }

        static void RoundTripsHostNetworking()
        {
            var project = new ProjectInfo
            {
                Name = "repo",
                Dir = "/home/you/repo",
                Network = NetworkMode.Host,
            };

            var wire = JVal.Parse(project.ToJson());
            AssertEx.Equal("host", wire["network"].AsString(),
                           "host network survives project serialization");
            AssertEx.Equal(NetworkMode.Host, ProjectInfo.FromJson(wire).Network,
                           "host network survives project parsing");
        }

        static void BuildsTemporaryPaths()
        {
            AssertEx.Equal("", ProjectInfo.TempRoot, "missing temp metadata is unavailable");
        }

        static void IgnoresStaleTemporaryPreviews()
        {
            var state = new TempProjectPreviewState();
            int first = state.Begin("old-name");
            int second = state.Begin("new-name");
            AssertEx.False(state.Accept(first, true, "old-name", "/tmp/old-name"),
                           "late preview cannot replace newer name");
            AssertEx.False(state.Accept(second, false, "new-name", "/tmp/new-name"),
                           "preview cannot apply after temporary mode is disabled");
            int third = state.Begin("new-name");
            AssertEx.True(third > second, "enabling temporary mode starts a new request");
            AssertEx.True(state.Accept(third, true, "new-name", "/tmp/new-name"),
                          "current temporary preview applies");
            AssertEx.Equal("/tmp/new-name", state.Dir, "current preview path");
        }

        static void ResumesAfterDisable()
        {
            foreach (bool success in new[] { true, false })
            {
                var state = new TempProjectPreviewState();
                var now = new DateTime(2026, 1, 1);
                int first = state.Begin("same", now);
                if (success)
                    state.Accept(first, false, "same", "/scratch/same");
                else
                    state.Fail(first, false, "same", "offline", now);
                AssertEx.False(state.Pending, "reply after disabling releases the pending request");
                int next = state.Begin("same", now);
                AssertEx.True(next > first, "same-name request can resume without waiting");
                state.Cancel();
                AssertEx.False(state.Accept(next, true, "same", "/old/same"),
                               "mode change invalidates replies even after it is enabled again");
                int current = state.Begin("same", now);
                AssertEx.True(state.Accept(current, true, "same", "/scratch/same"),
                              "new request succeeds");
            }
        }

        static void CoalescesPreviewRequests()
        {
            var state = new TempProjectPreviewState();
            DateTime now = new DateTime(2026, 1, 1);
            int first = state.Begin("same-name", now);
            AssertEx.True(state.Pending, "first preview is pending");
            AssertEx.False(state.ShouldRequest("same-name", now),
                           "a redraw does not request while the reply is pending");
            AssertEx.Equal(0, state.Begin("same-name", now),
                           "a redraw does not advance the request generation");

            AssertEx.True(state.Fail(first, true, "same-name", "offline", now),
                           "failed preview records visible error");
            AssertEx.Equal("offline", state.Error, "preview error is visible");
            AssertEx.Equal(0, state.Begin("same-name", now.AddMilliseconds(500)),
                           "retry waits before asking again");
            int second = state.Begin("same-name", now.AddSeconds(1));
            AssertEx.True(second != 0, "first retry starts");
            state.Fail(second, true, "same-name", "offline", now.AddSeconds(1));
            int third = state.Begin("same-name", now.AddSeconds(3));
            AssertEx.True(third != 0, "second retry starts");
            state.Fail(third, true, "same-name", "offline", now.AddSeconds(3));
            AssertEx.False(state.ShouldRequest("same-name", now.AddSeconds(20)),
                           "preview retries are bounded");
            AssertEx.True(state.Status.Contains("three attempts"),
                          "bounded failure remains visible");
        }

        static void RoundTripsAndCopiesSettings()
        {
            var project = new ProjectInfo
            {
                Name = "repo",
                Dir = "/work/repo",
                Temp = true,
                Sandbox = new List<string> { "home", "docs" },
                Breadcrumbs = new List<string> { "read this", "then work" },
                Network = NetworkMode.None,
                Dns = new DnsConfig
                {
                    Mode = DnsMode.Servers,
                    Servers = new List<string> { "8.8.8.8", "1.1.1.1" },
                },
            };

            var wire = JVal.Parse(project.ToJson());
            var parsed = ProjectInfo.FromJson(wire);
            AssertEx.Equal("repo", parsed.Name, "project name");
            AssertEx.Equal("/work/repo", parsed.Dir, "project directory");
            AssertEx.True(parsed.Temp, "temporary flag");
            AssertEx.Sequence(project.Sandbox, parsed.Sandbox, "sandbox presets");
            AssertEx.Sequence(project.Breadcrumbs, parsed.Breadcrumbs, "breadcrumbs");
            AssertEx.Equal(NetworkMode.None, parsed.Network, "none network");
            AssertEx.Sequence(project.Dns.Servers, parsed.Dns.Servers, "custom DNS servers");
            AssertEx.Equal(project.ToJson(), parsed.ToJson(), "project JSON round trip");

            var copy = project.Copy();
            copy.Sandbox[0] = "changed";
            copy.Breadcrumbs.Add("another");
            copy.Dns.Servers[0] = "9.9.9.9";
            AssertEx.Equal("home", project.Sandbox[0], "copy owns sandbox list");
            AssertEx.Equal(2, project.Breadcrumbs.Count, "copy owns breadcrumb list");
            AssertEx.Equal("8.8.8.8", project.Dns.Servers[0], "copy owns DNS list");
        }
    }
}
