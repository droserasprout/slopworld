using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class ProjectInfoTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("uses daemon metadata for temporary paths", BuildsTemporaryPaths);
            yield return ("ignores stale temporary previews", IgnoresStaleTemporaryPreviews);
            yield return ("coalesces and bounds temporary preview retries", CoalescesPreviewRequests);
            yield return ("temporary preview resumes after mode is disabled", ResumesAfterDisable);
            yield return ("round trips and copies project settings", RoundTripsAndCopiesSettings);
            yield return ("uses daemon expansion for client paths", ExpandsClientPaths);
        }

        static void ExpandsClientPaths()
        {
            var project = ProjectInfo.FromJson(JVal.Parse(
                "{\"name\":\"repo\",\"dir\":\"~/repo\",\"expanded_dir\":\"/daemon/home/repo\"}"));
            AssertEx.Equal("/daemon/home/repo", project.ExpandedDir, "daemon home wins");
            AssertEx.Equal("~/repo", project.Dir, "editable shorthand is retained");
            AssertEx.Equal(project.ExpandedDir, project.Copy().ExpandedDir, "copy retains metadata");
            AssertEx.True(JVal.Parse(project.ToJson())["expanded_dir"].IsNull,
                          "response metadata is not sent in edits");
            var literal = ProjectInfo.FromJson(JVal.Parse("{\"dir\":\"/work/repo\"}"));
            AssertEx.Equal("/work/repo", literal.ExpandedDir, "older daemon literal path");
            var unavailable = ProjectInfo.FromJson(JVal.Parse(
                "{\"dir\":\"$UNSET/repo\",\"expanded_dir\":\"\"}"));
            AssertEx.Equal("", unavailable.ExpandedDir, "empty expansion does not fall back");
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
                Mounts = new List<MountEntry>
                {
                    new MountEntry { From = "shared", To = "/mnt/shared", Mode = MountMode.Ro },
                    new MountEntry { From = "tools", To = "/mnt/tools", Mode = MountMode.Rw },
                },
            };

            var wire = JVal.Parse(project.ToJson());
            var parsed = ProjectInfo.FromJson(wire);
            AssertEx.Equal("repo", parsed.Name, "project name");
            AssertEx.Equal("/work/repo", parsed.Dir, "project directory");
            AssertEx.True(parsed.Temp, "temporary flag");
            AssertEx.Equal(2, parsed.Mounts.Count, "project mounts");
            AssertEx.Equal("shared", parsed.Mounts[0].From, "first mount project");
            AssertEx.Equal(MountMode.Ro, parsed.Mounts[0].Mode, "first mount mode");
            AssertEx.Equal(project.ToJson(), parsed.ToJson(), "project JSON round trip");

            var copy = project.Copy();
            copy.Mounts[0].From = "changed";
            copy.Mounts.Add(new MountEntry { From = "another", To = "/mnt/another", Mode = MountMode.Ro });
            AssertEx.Equal("shared", project.Mounts[0].From, "copy owns mount entries");
            AssertEx.Equal(2, project.Mounts.Count, "copy owns mount list");
        }
    }
}
