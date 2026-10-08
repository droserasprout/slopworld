using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class FilesStoreTests
    {
        sealed class Fixture : IDisposable
        {
            readonly SessionHub _hub = SessionHub.Instance;
            readonly float _time = Time.realtimeSinceStartup;
            readonly List<DaemonClient.Request> _requests = new List<DaemonClient.Request>(DaemonClient.Requests);
            public readonly FilesStore Store = new FilesStore();
            public readonly List<FileNode> Revealed = new List<FileNode>();

            public Fixture()
            {
                SessionHub.Instance = new SessionHub();
                Time.realtimeSinceStartup = 100f;
                DaemonClient.Requests.Clear();
                SidebarScopes.TestScopes.Clear();
                SidebarScopes.TestDisabled.Clear();
                Store.Revealed += Revealed.Add;
            }

            public FileNode Root(string name = "p")
            {
                var scope = new BrowseScope
                {
                    ProjectId = name,
                    Project = name,
                    Worktree = "main",
                    Path = "/" + name,
                    Phase = "ready"
                };
                SidebarScopes.TestScopes.Add(scope);
                return Store.Root(scope.Key);
            }

            public void Dispose()
            {
                SessionHub.Instance = _hub;
                Time.realtimeSinceStartup = _time;
                DaemonClient.Requests.Clear();
                DaemonClient.Requests.AddRange(_requests);
                SidebarScopes.TestScopes.Clear();
                SidebarScopes.TestDisabled.Clear();
            }
        }

        static Wire.BrowseResult Listing(string[] dirs = null, string[] files = null, bool capped = false)
        {
            var reply = new Wire.BrowseResult { Truncated = capped };
            if (dirs != null) reply.Dirs.Add(dirs);
            if (files != null) reply.Files.Add(files);
            return reply;
        }

        static FileNode Child(FileNode parent, string name, bool directory = false) => new FileNode
        {
            Name = name,
            Path = parent.Path + "/" + name,
            Root = parent.Root,
            Project = parent.Project,
            Depth = parent.Depth + 1,
            IsDir = directory,
            Expanded = directory,
            HasChildren = directory,
        };

        public static void ReloadRejectsLateRepliesAndPreservesExpandedNodes()
        {
            using (var f = new Fixture())
            {
                var root = f.Root();
                var child = Child(root, "src", true);
                child.Children = new List<FileNode> { Child(child, "old.cs") };
                root.Children = new List<FileNode> { child };
                f.Store.RefreshLoaded(root);
                var old = DaemonClient.Requests.Single();
                f.Store.Reload();
                f.Store.Reload();
                f.Store.RefreshLoaded(root, false);
                var fresh = DaemonClient.Requests[1];
                old.Reply(Listing(files: new[] { "stale.cs" }));
                AssertEx.True(root.Loading, "stale completion cannot end the replacement listing");
                AssertEx.True(ReferenceEquals(child, root.Children.Single()), "reload retains the directory skeleton");
                fresh.Reply(Listing(dirs: new[] { "src" }, files: new[] { "new.cs" }));
                AssertEx.False(root.Loading, "replacement completes normally");
                AssertEx.True(ReferenceEquals(child, root.Children[0]), "same directory retains identity");
                AssertEx.True(child.Expanded, "expanded branch survives refresh");
                AssertEx.Equal("new.cs", root.Children[1].Name, "only the fresh listing lands");
            }
        }

        public static void RelocatedScopeRejectsOldReplyAndMovesRetainedChildren()
        {
            using (var f = new Fixture())
            {
                var root = f.Root();
                var child = Child(root, "src", true);
                child.Children = new List<FileNode> { Child(child, "file.cs") };
                root.Children = new List<FileNode> { child };
                f.Store.RefreshLoaded(root, false);
                var old = DaemonClient.Requests.Single();
                SidebarScopes.TestScopes.Single().Path = "/renamed";
                AssertEx.True(ReferenceEquals(root, f.Store.Root(root.Project)), "checkout relocation retains root identity");
                AssertEx.Equal("/renamed/src/file.cs", child.Children.Single().Path, "descendant paths follow relocation");
                f.Store.RefreshLoaded(root, false);
                old.Fail("old path no longer exists");
                AssertEx.True(root.Loading, "old failure cannot end a new listing");
                AssertEx.Equal(null, root.Error, "old failure cannot mark the moved root failed");
                DaemonClient.Requests[1].Reply(Listing(dirs: new[] { "src" }));
                AssertEx.True(ReferenceEquals(child, root.Children.Single()), "relocated branch remains reusable");
            }
        }

        public static void FoldingCancelsBackgroundQueueButRetainsJoinedReveal()
        {
            using (var f = new Fixture())
            {
                var roots = Enumerable.Range(0, 6).Select(i => f.Root("p" + i)).ToList();
                foreach (var root in roots)
                {
                    root.Children = new List<FileNode>();
                    f.Store.RefreshLoaded(root);
                }
                AssertEx.Equal(4, DaemonClient.Requests.Count, "browse admission is bounded");
                f.Store.RevealPath(roots[4].Project, new List<string> { "target.cs" });
                f.Store.SetAllFolded(true);
                AssertEx.True(roots[4].Loading, "foreground waiter retains its queued listing");
                AssertEx.False(roots[5].Loading, "unused background listing is cancelled");
                DaemonClient.Requests[0].Reply(Listing());
                AssertEx.Equal(5, DaemonClient.Requests.Count, "completion admits the retained foreground listing");
                DaemonClient.Requests[4].Reply(Listing(files: new[] { "target.cs" }));
                AssertEx.Equal("/p4/target.cs", f.Revealed.Single().Path, "joined reveal completes after cancellation");
                foreach (var request in DaemonClient.Requests.Skip(1).Take(3).ToList()) request.Reply(Listing());
                AssertEx.Equal(5, DaemonClient.Requests.Count, "cancelled work never consumes admission");
            }
        }

        public static void FailedListingRetriesAfterManualToggle()
        {
            using (var f = new Fixture())
            {
                var root = f.Root();
                f.Store.EnsureLoaded(root);
                DaemonClient.Requests.Single().Fail("offline");
                AssertEx.False(root.Loading, "failure releases loading state");
                AssertEx.Equal("offline", root.Error, "failure is retained for display");
                f.Store.EnsureLoaded(root);
                AssertEx.Equal(1, DaemonClient.Requests.Count, "draw does not repeatedly retry failures");
                f.Store.ToggleNode(root);
                f.Store.ToggleNode(root);
                f.Store.EnsureLoaded(root);
                DaemonClient.Requests[1].Reply(Listing(files: new[] { "recovered.cs" }));
                AssertEx.Equal(null, root.Error, "manual retry clears failure");
                AssertEx.Equal("recovered.cs", root.Children.Single().Name, "retry can recover");
            }
        }

        public static void ParentRefreshDropsEmptiedBranchAndReplacesChangedType()
        {
            using (var f = new Fixture())
            {
                var root = f.Root();
                var emptied = Child(root, "empty", true);
                emptied.Children = new List<FileNode> { Child(emptied, "gone.cs") };
                emptied.More = true;
                var replaced = Child(root, "changed", true);
                root.Children = new List<FileNode> { emptied, replaced };
                var reply = Listing(dirs: new[] { "empty" }, files: new[] { "changed" });
                reply.EmptyDirs.Add("empty");
                reply.GitignoredFiles.Add("changed");
                f.Store.RefreshLoaded(root, false);
                DaemonClient.Requests.Single().Reply(reply);
                AssertEx.True(ReferenceEquals(emptied, root.Children[0]), "empty directory retains identity");
                AssertEx.Equal(0, emptied.Children.Count, "old rows disappear immediately");
                AssertEx.False(emptied.More || emptied.HasChildren, "empty directory loses disclosure state");
                AssertEx.False(ReferenceEquals(replaced, root.Children[1]), "type change creates a new node");
                AssertEx.True(root.Children[1].Gitignored, "new file retains listing metadata");
            }
        }

        public static void CappedRevealProbesFileThenLoadsOnlyMissingDirectory()
        {
            using (var f = new Fixture())
            {
                var root = f.Root();
                root.Children = new List<FileNode>();
                root.More = true;
                f.Store.RevealPath(root.Project, new List<string> { "src", "file.cs" });
                AssertEx.True(DaemonClient.Requests.Single().Path.StartsWith(WireProtocol.Routes.FileStat,
                    StringComparison.Ordinal), "capped listing cannot establish absence");
                DaemonClient.Requests[0].Reply(new Wire.FileStatResult { IsFile = false });
                DaemonClient.Requests[1].Reply(Listing(files: new[] { "file.cs" }));
                AssertEx.Equal("/p/src/file.cs", f.Revealed.Single().Path, "directory probe reveals its target");
                AssertEx.Equal(2, DaemonClient.Requests.Count, "no unrelated sibling listings are needed");
            }
        }

        public static void SupersededRevealIgnoresLateProbeAndFailure()
        {
            using (var f = new Fixture())
            {
                var root = f.Root();
                root.Children = new List<FileNode> { Child(root, "current.cs") };
                root.More = true;
                f.Store.RevealPath(root.Project, new List<string> { "old.cs" });
                var old = DaemonClient.Requests.Single();
                f.Store.RevealPath(root.Project, new List<string> { "current.cs" });
                old.Reply(new Wire.FileStatResult { IsFile = true });
                AssertEx.Equal(1, root.Children.Count, "late probe cannot insert an obsolete row");
                AssertEx.Equal("current.cs", f.Revealed.Single().Name, "latest reveal wins");
                f.Store.RevealPath(root.Project, new List<string> { "another.cs" });
                var obsoleteFailure = DaemonClient.Requests[1];
                f.Store.ScopesChanged();
                obsoleteFailure.Fail("late failure");
                AssertEx.Equal(1, f.Revealed.Count, "scope change invalidates pending reveal");
            }
        }

        public static void RemovedScopeCannotBeResurrectedByBrowseReply()
        {
            using (var f = new Fixture())
            {
                var root = f.Root();
                f.Store.EnsureLoaded(root);
                var old = DaemonClient.Requests.Single();
                SidebarScopes.TestScopes.Clear();
                f.Store.ScopesChanged();
                old.Reply(Listing(files: new[] { "late.cs" }));
                AssertEx.Equal(null, root.Children, "removed root cannot accept delayed data");
                AssertEx.Equal(0, f.Store.Controller.Groups().Count, "removed scope cannot reappear in groups");
            }
        }

        public static void IndependentStoresDoNotShareBrowseAdmission()
        {
            using (var f = new Fixture())
            {
                foreach (var i in Enumerable.Range(0, 4)) f.Store.EnsureLoaded(f.Root("p" + i));
                var other = new FilesStore();
                other.EnsureLoaded(other.Root(SidebarScopes.TestScopes[0].Key));
                AssertEx.Equal(5, DaemonClient.Requests.Count, "a new owner has its own request lifetime");
            }
        }
    }
}
