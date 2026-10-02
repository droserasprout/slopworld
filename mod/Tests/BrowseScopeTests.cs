using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class BrowseScopeTests
    {
        sealed class Fixture
        {
            public float Now;
            public string Filter = "", Saved = "";
            public int ProjectsRevision;
            public readonly List<ProjectInfo> Projects = new List<ProjectInfo> {
                new ProjectInfo { Id = "p-id", Name = "p", Dir = "/p" } };
            public readonly List<(string Project, Action<List<BrowseScope>, string> Reply)> Requests = new List<(string, Action<List<BrowseScope>, string>)>();
            public readonly BrowseScopeCatalog Catalog;
            public Fixture() => Catalog = new BrowseScopeCatalog(p => Filter.Length == 0 || Filter == p,
                () => Now, (p, done) => Requests.Add((p, done)));
            public void Update(bool menu = false) => Catalog.Update(Projects, ProjectsRevision, Filter, Saved, true, menu);
            public void Toggle(BrowseScope scope) => Catalog.Toggle(scope, saved => Saved = saved);
            public void Reply(int index, params BrowseScope[] scopes) => Requests[index].Reply(scopes.ToList(), null);
            public void Refresh() { Now += 6; Update(); }
        }
        static BrowseScope Scope(string worktree = "main", string phase = "ready", string path = null) =>
            new BrowseScope { ProjectId = "p-id", Project = "p", Worktree = worktree, Name = worktree,
                Phase = phase, Path = path ?? "/p/" + worktree };

        public static void CatalogSnapshotsDoNotExposeRetainedState()
        {
            var f = new Fixture(); f.Update();
            var reply = Scope();
            string key = reply.Key;
            f.Reply(0, reply);
            reply.Path = "/changed-input";
            var all = f.Catalog.All("p-id");
            all[0].ProjectId = "changed";
            f.Catalog.Find(key).Path = "/changed-find";
            f.Catalog.EnabledScopes()[0].Path = "/changed-enabled";
            Assert.That(f.Catalog.Find(key).Path, Is.EqualTo("/p/main"));
            Assert.That(f.Catalog.Enabled(key), Is.True);
            Assert.That(((IList<BrowseScope>)all).IsReadOnly, Is.True);
        }

        public static void WorkLimitMustBePositive()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedWork(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedWork(-1));
        }

        public static void EmptyViewsExplainCatalogState()
        {
            var f = new Fixture();
            Assert.That(f.Catalog.EmptyReason(), Is.EqualTo("No projects are available."));
            f.Filter = "hidden";
            f.Update();
            Assert.That(f.Catalog.EmptyReason(), Does.Contain("filter"));
            f.Filter = "";
            f.Update();
            Assert.That(f.Catalog.EmptyReason(), Is.EqualTo("Loading checkouts…"));
            f.Requests[0].Reply(null, "offline");
            Assert.That(f.Catalog.EmptyReason(), Does.StartWith("Unable to load checkouts."));
            f.Refresh();
            f.Reply(1, Scope());
            f.Toggle(f.Catalog.All("p-id").Single());
            Assert.That(f.Catalog.EnabledScopes(), Is.Empty);
            Assert.That(f.Catalog.EmptyReason(), Does.StartWith("No checkouts are selected"));
        }

        public static void SidebarCatalogRequestsIdentifyTheHostCaller()
        {
            DaemonClient.Requests.Clear();
            try
            {
                List<BrowseScope> result = null;
                string error = null;
                SidebarScopes.Load("project name", (scopes, failure) => { result = scopes; error = failure; });
                var request = DaemonClient.Requests.Single();
                Assert.That(request.Session, Is.EqualTo(TaskInfo.Host));
                Assert.That(request.Path, Is.EqualTo(WireProtocol.Routes.Worktrees + "?project=project%20name"));
                request.Ok(JVal.Parse("{\"worktrees\":[{\"id\":\"main\",\"name\":\"main\",\"path\":\"/p\",\"phase\":\"ready\"}]}"));
                Assert.That(result.Single().Ready, Is.True);
                Assert.That(error, Is.Null);
                request.Fail("Unavailable");
                Assert.That(result, Is.Null);
                Assert.That(error, Is.EqualTo("Unavailable"));
            }
            finally { DaemonClient.Requests.Clear(); }
        }

        public static void OnlyProjectsWithWorktreesHaveNestedChoices()
        {
            var f = new Fixture(); f.Update();
            Assert.That(f.Catalog.HasWorktrees("p-id"), Is.False, "loading placeholders do not create a submenu");
            f.Reply(0, Scope());
            Assert.That(f.Catalog.HasWorktrees("p-id"), Is.False, "Main alone remains a project checkbox");
            f.Refresh(); f.Reply(1, Scope(), Scope("one"), Scope("missing", "error"));
            Assert.That(f.Catalog.HasWorktrees("p-id"), Is.True);
            f.Refresh(); f.Reply(2, Scope(), Scope("missing", "error"));
            Assert.That(f.Catalog.HasWorktrees("p-id"), Is.True, "unavailable worktrees remain discoverable");
            f.Refresh(); f.Reply(3, Scope());
            Assert.That(f.Catalog.HasWorktrees("p-id"), Is.False, "removing the last worktree restores the checkbox");
        }

        public static void OpenFilterDiscoversHiddenProjectsWithoutEnablingThem()
        {
            var f = new Fixture { Filter = "other" }; f.Update();
            Assert.That(f.Requests, Is.Empty);
            f.Update(menu: true);
            Assert.That(f.Requests.Count, Is.EqualTo(1));
            f.Reply(0, Scope(), Scope("one"));
            Assert.That(f.Catalog.HasWorktrees("p-id"), Is.True);
            Assert.That(f.Catalog.EnabledScopes(), Is.Empty);
            f.Now += 6; f.Update();
            Assert.That(f.Requests.Count, Is.EqualTo(1), "closed menu does not poll a hidden project");
        }

        public static void InitialCatalogFailureIsVisibleAndRetryClearsIt()
        {
            var f = new Fixture(); f.Update();
            int revision = f.Catalog.Revision;
            f.Requests[0].Reply(null, "Caller identity is missing");
            Assert.That(f.Catalog.Error("p-id"), Is.EqualTo("Caller identity is missing"));
            Assert.That(f.Catalog.Revision, Is.GreaterThan(revision));
            Assert.That(f.Catalog.EnabledScopes(), Is.Empty);
            f.Update();
            Assert.That(f.Requests.Count, Is.EqualTo(1), "failed requests retain the retry delay");
            f.Refresh(); f.Reply(1, Scope(), Scope("one"));
            Assert.That(f.Catalog.Error("p-id"), Is.Null);
            Assert.That(f.Catalog.HasWorktrees("p-id"), Is.True);
            f.Toggle(f.Catalog.All("p-id")[1]);
            string saved = f.Saved;
            f.Refresh(); f.Requests[2].Reply(null, "Timed out");
            Assert.That(f.Catalog.HasWorktrees("p-id"), Is.True);
            Assert.That(f.Catalog.EnabledScopes().Count, Is.EqualTo(2));
            Assert.That(f.Saved, Is.EqualTo(saved));
            revision = f.Catalog.Revision;
            f.Refresh(); f.Reply(3, Scope(), Scope("one"));
            Assert.That(f.Catalog.Error("p-id"), Is.Null);
            Assert.That(f.Catalog.Revision, Is.GreaterThan(revision), "clear the error even when catalog contents match");
        }

        public static void DefaultsAndProjectVisibility()
        {
            var f = new Fixture(); f.Update();
            f.Reply(0, Scope(), Scope("one"), Scope("two"));
            Assert.That(f.Catalog.EnabledScopes().Select(s => s.Worktree), Is.EqualTo(new[] { "main" }));
            f.Toggle(f.Catalog.All("p-id")[1]); f.Toggle(f.Catalog.All("p-id")[2]);
            Assert.That(f.Catalog.EnabledScopes().Count, Is.EqualTo(3));
            f.Filter = "other"; f.Update();
            Assert.That(f.Catalog.EnabledScopes(), Is.Empty);
            f.Filter = ""; f.Update();
            Assert.That(f.Catalog.EnabledScopes().Count, Is.EqualTo(3));
            foreach (var scope in f.Catalog.All("p-id")) f.Toggle(scope);
            Assert.That(f.Catalog.EnabledScopes(), Is.Empty, "project can show Agents with no checkout");
            var saved = new BrowseScopeChoices(f.Saved);
            Assert.That(saved.Chosen(Scope()), Is.False, "disabled Main persists");
            Assert.That(saved.Chosen(Scope("new")), Is.False, "new worktrees stay opt-in under All projects");
        }

        public static void UnassignedProjectChoiceExpiresWhenDaemonAssignsId()
        {
            var f = new Fixture(); f.Projects[0].Id = ""; f.Update();
            f.Reply(0, Scope());
            f.Toggle(f.Catalog.All("name:p")[0]);
            Assert.That(f.Catalog.EnabledScopes(), Is.Empty);
            f.Projects[0].Id = "p-id"; f.ProjectsRevision++; f.Update();
            f.Reply(1, Scope(), Scope("new"));
            Assert.That(f.Catalog.EnabledScopes().Select(s => s.Worktree), Is.EqualTo(new[] { "main" }));
            Assert.That(f.Saved, Does.Not.Contain(BrowseScope.Identity("p-id", "main")));
        }

        public static void RenameReadinessFailureAndRemoval()
        {
            var f = new Fixture(); f.Update();
            f.Reply(0, Scope(), Scope("one"));
            string key = f.Catalog.All("p-id")[1].Key;
            f.Toggle(f.Catalog.Find(key));
            f.Refresh(); f.Requests[1].Reply(null, "Request failed");
            Assert.That(f.Catalog.Enabled(key), Is.True, "transient catalog failure retains scope");
            f.Refresh(); f.Reply(2, Scope(), Scope("one", "error"));
            Assert.That(f.Catalog.Enabled(key), Is.False);
            Assert.That(f.Catalog.Chosen(f.Catalog.Find(key)), Is.True);
            string saved = f.Saved;
            f.Toggle(f.Catalog.Find(key));
            Assert.That(f.Saved, Is.EqualTo(saved), "unready choice is not toggleable");
            var renamed = Scope("one", path: "/p/renamed"); renamed.Name = "renamed";
            f.Refresh(); f.Reply(3, Scope(), renamed);
            Assert.That(f.Catalog.Enabled(key), Is.True);
            Assert.That(f.Catalog.Find(key).Path, Is.EqualTo("/p/renamed"));
            f.Refresh(); f.Reply(4, Scope());
            Assert.That(f.Catalog.Find(key), Is.Null, "removed worktree drops from visible catalog");
            Assert.That(f.Saved, Is.EqualTo(saved));
        }

        public static void CatalogConcurrencyAndLateReplies()
        {
            var f = new Fixture();
            f.Projects.Add(new ProjectInfo { Id = "q-id", Name = "q", Dir = "/q" });
            f.Projects.Add(new ProjectInfo { Id = "r-id", Name = "r", Dir = "/r" });
            f.Update(); f.Update();
            Assert.That(f.Requests.Count, Is.EqualTo(2));
            f.Projects[0].Name = "renamed"; f.ProjectsRevision++; f.Update();
            f.Reply(0, Scope("obsolete"));
            Assert.That(f.Catalog.All("p-id").Select(s => s.Worktree), Is.EqualTo(new[] { "main" }));
            f.Update();
            Assert.That(f.Requests.Last().Project, Is.EqualTo("renamed"));
            f.Projects.RemoveAt(0); f.ProjectsRevision++; f.Update();
            f.Reply(2, Scope("late"));
            Assert.That(f.Catalog.All("p-id"), Is.Empty);
            f.Reply(1, Scope()); f.Update();
            Assert.That(f.Requests.Last().Project, Is.EqualTo("r"));
        }

        public static void StableIdsResolveRenamedProjectsAndUnassignedProjects()
        {
            var p = new ProjectInfo { Id = "stable", Name = "old" };
            string key = BrowseScope.Identity(p.Id, "tree");
            p.Name = "renamed";
            Assert.That(BrowseScope.ProjectOf(key, new[] { p }), Is.SameAs(p));
            Assert.That(BrowseScope.WorktreeOf(key), Is.EqualTo("tree"));
            var legacy = new ProjectInfo { Name = "legacy", Dir = "/legacy" };
            Assert.That(BrowseScope.ProjectOf(BrowseScope.Identity(BrowseScope.ProjectIdOf(legacy), "main"), new[] { legacy }), Is.SameAs(legacy));
            Assert.That(new BrowseScopeChoices("").Chosen(Scope()), Is.True);
            var fromWire = ProjectInfo.FromWire(new Wire.Project { Id = "stable", Name = "renamed" });
            Assert.That(fromWire.Copy().ToWire().Id, Is.EqualTo("stable"));
        }

        public static void IndependentFoldsAndHistory()
        {
            var main = Scope(); var one = Scope("one"); var two = Scope("two");
            var state = new ContentTreeState();
            state.SetCollapsed(one.Key, true);
            state.SetCollapsed(main.ProjectKey, true);
            state.SetCollapsed(main.ProjectKey, false);
            Assert.That(state.IsCollapsed(one.Key), Is.True);
            Assert.That(state.IsCollapsed(two.Key), Is.False);
            state.SyncGroups(new[] { main.ProjectKey, main.Key, two.Key });
            Assert.That(state.IsCollapsed(one.Key), Is.False);
            var history = new SidebarViewHistory();
            history.Visit(SidebarViewLocation.Search(one.Key, "src/file.cs", 7));
            history.Visit(SidebarViewLocation.Search(two.Key, "src/file.cs", 7));
            Assert.That(history.Back(out var previous), Is.True);
            Assert.That(previous.Primary, Is.EqualTo(one.Key));
            Assert.That(previous.Secondary, Is.EqualTo("src/file.cs"));
        }

        public static void RequestsRemainBoundedAcrossQueryReplacement()
        {
            var work = new BoundedWork(2);
            var gate = new OperationGate();
            var pending = new List<Action>();
            long old = gate.Begin();
            for (int i = 0; i < 20; i++) work.Add(() => gate.IsCurrent(old), done => pending.Add(done));
            Assert.That(pending.Count, Is.EqualTo(2));
            long current = gate.Begin();
            for (int i = 0; i < 3; i++) work.Add(() => gate.IsCurrent(current), done => pending.Add(done));
            pending[0](); pending[0]();
            Assert.That(pending.Count, Is.EqualTo(3), "one completion releases one slot, skipping stale queued scopes");
            pending[1]();
            Assert.That(pending.Count, Is.EqualTo(4));
            pending[2]();
            Assert.That(pending.Count, Is.EqualTo(5));
            var query = new SearchSubmission(" submitted ", true, false, true, false);
            Assert.That(query.Query, Is.EqualTo("submitted"));
            Assert.That(query.Regex && query.Word && !query.Case && !query.IncludeIgnored, Is.True);
        }

        public static void ReaderScopeSurvivesFilteringAndRoutesRequests()
        {
            var oldHub = SessionHub.Instance;
            try
            {
                SessionHub.Instance = new SessionHub();
                SessionHub.Instance.Projects.Clear();
                SessionHub.Instance.Projects.Add(new ProjectInfo { Id = "p-id", Name = "renamed" });
                var one = Scope("one"); var two = Scope("two");
                var tabs = new PagerTabs();
                tabs.ForPreview().Open(one.Key, "cat src/file", "one", "diff:src/file");
                SessionHub.Instance.SessionStore.Complete("one-reader");
                tabs.Lock("one-reader");
                tabs.ForPreview().Open(two.Key, "cat src/file", "two", "diff:src/file");
                SessionHub.Instance.SessionStore.Complete("two-reader");
                tabs.ReleasePreview();
                Assert.That(tabs.Reopen(one.Key, "diff:src/file"), Is.True);
                Assert.That(tabs.Reopen(two.Key, "diff:src/file"), Is.False);
                DaemonClient.Requests.Clear();
                var store = new SessionStore();
                store.Run(one.Key, "pwd", "reader", _ => { }, options: new SessionRunOptions { Host = true });
                var request = (Wire.RunReq)DaemonClient.Requests.Single().Body;
                Assert.That(request.Project, Is.EqualTo("renamed"));
                Assert.That(request.Worktree, Is.EqualTo("one"));
                DaemonClient.Requests.Clear();
                store.RunHostShell(two.Key, _ => { });
                request = (Wire.RunReq)DaemonClient.Requests.Single().Body;
                Assert.That(request.Worktree, Is.EqualTo("two"));
            }
            finally { SessionHub.Instance = oldHub; DaemonClient.Requests.Clear(); }
        }
    }
}
