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
            public string Filter = "",
                Saved = "";
            public int ProjectsRevision;
            public readonly List<ProjectInfo> Projects = new List<ProjectInfo>
            {
                new ProjectInfo
                {
                    Id = "p-id",
                    Name = "p",
                    Dir = "/p",
                },
            };

            public sealed class LoadRequest
            {
                public string Project;
                public int Attempt;
                public Action<List<BrowseScope>, string> Reply;
            }

            public readonly List<LoadRequest> Requests = new List<LoadRequest>();
            public readonly BrowseScopeCatalog Catalog;

            public Fixture() =>
                Catalog = new BrowseScopeCatalog(
                    p => Filter.Length == 0 || Filter == p,
                    () => Now,
                    (p, done) =>
                        Requests.Add(
                            new LoadRequest
                            {
                                Project = p,
                                Attempt = Requests.Count(request => request.Project == p) + 1,
                                Reply = done,
                            }
                        )
                );

            public void Update(bool menu = false) =>
                Catalog.Update(Projects, ProjectsRevision, Filter, Saved, true, menu);

            public void Toggle(BrowseScope scope) => Catalog.Toggle(scope, saved => Saved = saved);

            public LoadRequest Request(string project, int attempt) =>
                Requests.Single(request =>
                    request.Project == project && request.Attempt == attempt
                );

            public void Reply(LoadRequest request, params BrowseScope[] scopes) =>
                request.Reply(scopes.ToList(), null);

            public void Refresh()
            {
                Now += 6;
                Update();
            }
        }

        static BrowseScope Scope(
            string worktree = "main",
            string phase = "ready",
            string path = null
        ) =>
            new BrowseScope
            {
                ProjectId = "p-id",
                Project = "p",
                Worktree = worktree,
                Name = worktree,
                Phase = phase,
                Path = path ?? "/p/" + worktree,
            };

        public static void CatalogSnapshotsDoNotExposeRetainedState()
        {
            var f = new Fixture();
            f.Update();
            var reply = Scope();
            string key = reply.Key;
            f.Reply(f.Request("p", 1), reply);
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
            f.Request("p", 1).Reply(null, "offline");
            Assert.That(f.Catalog.EmptyReason(), Does.StartWith("Unable to load checkouts."));
            f.Refresh();
            f.Reply(f.Request("p", 2), Scope());
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
                SidebarScopes.Load(
                    "project name",
                    (scopes, failure) =>
                    {
                        result = scopes;
                        error = failure;
                    }
                );
                var request = DaemonClient.Requests.Single();
                Assert.That(request.Session, Is.EqualTo(TaskInfo.Host));
                Assert.That(
                    request.Path,
                    Is.EqualTo(WireProtocol.Routes.Worktrees + "?project=project%20name")
                );
                request.Ok(
                    JVal.Parse(
                        "{\"worktrees\":[{\"id\":\"main\",\"name\":\"main\",\"path\":\"/p\",\"phase\":\"ready\"}]}"
                    )
                );
                Assert.That(result.Single().Ready, Is.True);
                Assert.That(error, Is.Null);
                request.Fail("Unavailable");
                Assert.That(result, Is.Null);
                Assert.That(error, Is.EqualTo("Unavailable"));
            }
            finally
            {
                DaemonClient.Requests.Clear();
            }
        }

        public static void OnlyProjectsWithWorktreesHaveNestedChoices()
        {
            var f = new Fixture();
            f.Update();
            Assert.That(
                f.Catalog.HasWorktrees("p-id"),
                Is.False,
                "loading placeholders do not create a submenu"
            );
            f.Reply(f.Request("p", 1), Scope());
            Assert.That(
                f.Catalog.HasWorktrees("p-id"),
                Is.False,
                "Main alone remains a project checkbox"
            );
            f.Refresh();
            f.Reply(f.Request("p", 2), Scope(), Scope("one"), Scope("missing", "error"));
            Assert.That(f.Catalog.HasWorktrees("p-id"), Is.True);
            f.Refresh();
            f.Reply(f.Request("p", 3), Scope(), Scope("missing", "error"));
            Assert.That(
                f.Catalog.HasWorktrees("p-id"),
                Is.True,
                "unavailable worktrees remain discoverable"
            );
            f.Refresh();
            f.Reply(f.Request("p", 4), Scope());
            Assert.That(
                f.Catalog.HasWorktrees("p-id"),
                Is.False,
                "removing the last worktree restores the checkbox"
            );
        }

        public static void OpenFilterDiscoversHiddenProjectsWithoutEnablingThem()
        {
            var f = new Fixture { Filter = "other" };
            f.Update();
            Assert.That(f.Requests, Is.Empty);
            f.Update(menu: true);
            Assert.That(f.Requests.Count, Is.EqualTo(1));
            f.Reply(f.Request("p", 1), Scope(), Scope("one"));
            Assert.That(f.Catalog.HasWorktrees("p-id"), Is.True);
            Assert.That(f.Catalog.EnabledScopes(), Is.Empty);
            f.Now += 6;
            f.Update();
            Assert.That(
                f.Requests.Count,
                Is.EqualTo(1),
                "closed menu does not poll a hidden project"
            );
        }

        public static void InitialCatalogFailureIsVisibleAndRetryClearsIt()
        {
            var f = new Fixture();
            f.Update();
            int revision = f.Catalog.Revision;
            f.Request("p", 1).Reply(null, "Caller identity is missing");
            Assert.That(f.Catalog.Error("p-id"), Is.EqualTo("Caller identity is missing"));
            Assert.That(f.Catalog.Revision, Is.GreaterThan(revision));
            Assert.That(f.Catalog.EnabledScopes(), Is.Empty);
            f.Update();
            Assert.That(f.Requests.Count, Is.EqualTo(1), "failed requests retain the retry delay");
            f.Refresh();
            f.Reply(f.Request("p", 2), Scope(), Scope("one"));
            Assert.That(f.Catalog.Error("p-id"), Is.Null);
            Assert.That(f.Catalog.HasWorktrees("p-id"), Is.True);
            f.Toggle(f.Catalog.All("p-id")[1]);
            string saved = f.Saved;
            f.Refresh();
            f.Request("p", 3).Reply(null, "Timed out");
            Assert.That(f.Catalog.HasWorktrees("p-id"), Is.True);
            Assert.That(f.Catalog.EnabledScopes().Count, Is.EqualTo(2));
            Assert.That(f.Saved, Is.EqualTo(saved));
            revision = f.Catalog.Revision;
            f.Refresh();
            f.Reply(f.Request("p", 4), Scope(), Scope("one"));
            Assert.That(f.Catalog.Error("p-id"), Is.Null);
            Assert.That(
                f.Catalog.Revision,
                Is.GreaterThan(revision),
                "clear the error even when catalog contents match"
            );
        }

        public static void InvalidationRetainsActiveSlotsAndRejectsStaleSuccessAndFailure()
        {
            var f = new Fixture();
            f.Update();
            f.Reply(f.Request("p", 1), Scope());
            f.Refresh();
            int revision = f.Catalog.Revision;
            f.Catalog.Invalidate();
            f.Catalog.Invalidate();
            f.Update();
            Assert.That(f.Requests.Count, Is.EqualTo(2), "active load retains its slot");
            f.Reply(f.Request("p", 2), Scope("obsolete"));
            Assert.That(f.Catalog.Revision, Is.EqualTo(revision));
            Assert.That(f.Catalog.Find(Scope().Key), Is.Not.Null);
            Assert.That(f.Catalog.Find(Scope("obsolete").Key), Is.Null);
            f.Update();
            Assert.That(f.Requests.Count, Is.EqualTo(3), "stale success does not delay reload");
            f.Catalog.Invalidate();
            f.Update();
            Assert.That(f.Requests.Count, Is.EqualTo(3));
            f.Request("p", 3).Reply(null, "stale failure");
            Assert.That(f.Catalog.Error("p-id"), Is.Null);
            Assert.That(f.Catalog.Revision, Is.EqualTo(revision));
            f.Update();
            Assert.That(f.Requests.Count, Is.EqualTo(4), "stale failure releases its slot");
            f.Reply(f.Request("p", 4), Scope(), Scope("current"));
            Assert.That(f.Catalog.Find(Scope("current").Key), Is.Not.Null);
            Assert.That(f.Catalog.Revision, Is.GreaterThan(revision));
        }

        public static void RetryDelayStartsAtCompletionAndWaitsForOnlineUpdate()
        {
            var f = new Fixture();
            f.Update();
            f.Now = 3f;
            f.Request("p", 1).Reply(null, "offline");
            f.Now = 7.99f;
            f.Update();
            Assert.That(f.Requests.Count, Is.EqualTo(1));
            f.Now = 8f;
            f.Catalog.Update(f.Projects, f.ProjectsRevision, f.Filter, f.Saved, false);
            Assert.That(f.Requests.Count, Is.EqualTo(1), "offline updates do not admit requests");
            f.Update();
            Assert.That(f.Requests.Count, Is.EqualTo(2), "retry is admitted at the deadline");
            f.Reply(f.Request("p", 2), Scope());
            Assert.That(f.Catalog.Error("p-id"), Is.Null);
            f.Now = 12.99f;
            f.Update();
            Assert.That(f.Requests.Count, Is.EqualTo(2));
            f.Now = 13f;
            f.Update();
            Assert.That(f.Requests.Count, Is.EqualTo(3), "successful refresh uses the same delay");
            int revision = f.Catalog.Revision;
            f.Reply(f.Request("p", 3), Scope());
            Assert.That(
                f.Catalog.Revision,
                Is.EqualTo(revision),
                "unchanged reply does not publish"
            );
            f.Update();
            Assert.That(f.Requests.Count, Is.EqualTo(3));
        }

        public static void DefaultsAndProjectVisibility()
        {
            var f = new Fixture();
            f.Update();
            f.Reply(f.Request("p", 1), Scope(), Scope("one"), Scope("two"));
            Assert.That(
                f.Catalog.EnabledScopes().Select(s => s.Worktree),
                Is.EqualTo(new[] { "main" })
            );
            f.Toggle(f.Catalog.All("p-id")[1]);
            f.Toggle(f.Catalog.All("p-id")[2]);
            Assert.That(f.Catalog.EnabledScopes().Count, Is.EqualTo(3));
            f.Filter = "other";
            f.Update();
            Assert.That(f.Catalog.EnabledScopes(), Is.Empty);
            f.Filter = "";
            f.Update();
            Assert.That(f.Catalog.EnabledScopes().Count, Is.EqualTo(3));
            foreach (var scope in f.Catalog.All("p-id"))
                f.Toggle(scope);
            Assert.That(
                f.Catalog.EnabledScopes(),
                Is.Empty,
                "project can show Agents with no checkout"
            );
            var saved = new BrowseScopeChoices(f.Saved);
            Assert.That(saved.Chosen(Scope()), Is.False, "disabled Main persists");
            Assert.That(
                saved.Chosen(Scope("new")),
                Is.False,
                "new worktrees stay opt-in under All projects"
            );
        }

        public static void AddedCheckoutSelectionPersistsWithoutChangingOtherChoices()
        {
            var main = Scope();
            var existing = Scope("existing");
            var added = Scope("added");
            var choices = new BrowseScopeChoices("");
            choices.Toggle(main);
            choices.Select(existing);
            choices.Select(added);
            choices.Select(added);
            var saved = new BrowseScopeChoices(choices.Save());
            Assert.That(saved.Chosen(main), Is.False, "adding a worktree preserves disabled Main");
            Assert.That(saved.Chosen(existing), Is.True);
            Assert.That(
                saved.Chosen(added),
                Is.True,
                "select before the catalog has a ready descriptor"
            );
            Assert.That(saved.Chosen(Scope("other")), Is.False);
            Assert.That(
                saved.Chosen(new BrowseScope { ProjectId = "other-project", Worktree = "added" }),
                Is.False
            );
            saved.Select(main);
            Assert.That(new BrowseScopeChoices(saved.Save()).Chosen(main), Is.True);
        }

        public static void CatalogSelectsCommittedCheckoutBeforeDescriptorArrives()
        {
            var f = new Fixture();
            f.Update();
            f.Reply(f.Request("p", 1), Scope(), Scope("existing"));
            f.Toggle(Scope());
            f.Toggle(Scope("existing"));
            int writes = 0;
            Action<string> persist = saved =>
            {
                f.Saved = saved;
                writes++;
            };
            int revision = f.Catalog.Revision;
            f.Catalog.Select("p-id", "added", persist);
            Assert.That(f.Catalog.Revision, Is.GreaterThan(revision));
            Assert.That(f.Catalog.Find(Scope("added").Key), Is.Null);
            Assert.That(f.Catalog.Chosen(Scope("added")), Is.True);
            Assert.That(f.Catalog.Chosen(Scope()), Is.False);
            Assert.That(f.Catalog.Chosen(Scope("existing")), Is.True);
            Assert.That(f.Catalog.Chosen(Scope("other")), Is.False);
            revision = f.Catalog.Revision;
            f.Catalog.Select("p-id", "added", persist);
            f.Catalog.Select("unknown", "added", persist);
            f.Catalog.Select("p-id", "", persist);
            f.Update();
            Assert.That(writes, Is.EqualTo(1));
            Assert.That(
                f.Catalog.Revision,
                Is.EqualTo(revision),
                "no-op selection does not republish"
            );
            f.Refresh();
            f.Request("p", 2).Reply(null, "offline");
            Assert.That(new BrowseScopeChoices(f.Saved).Chosen(Scope("added")), Is.True);
            f.Refresh();
            f.Reply(f.Request("p", 3), Scope(), Scope("existing"), Scope("added"));
            Assert.That(f.Catalog.Enabled(Scope("added").Key), Is.True);
            Assert.That(f.Catalog.Enabled(Scope().Key), Is.False);
        }

        public static void CatalogCreationSelectionPreservesAllWorktrees()
        {
            var f = new Fixture();
            f.Update();
            int writes = 0;
            Action<string> persist = saved =>
            {
                f.Saved = saved;
                writes++;
            };
            f.Catalog.SelectAll("p-id", persist);
            string all = f.Saved;
            int revision = f.Catalog.Revision;
            f.Catalog.Select("p-id", "added", persist);
            f.Catalog.SelectAll("p-id", persist);
            Assert.That(writes, Is.EqualTo(1));
            Assert.That(f.Saved, Is.EqualTo(all));
            Assert.That(f.Catalog.Revision, Is.EqualTo(revision));
            f.Reply(f.Request("p", 1), Scope(), Scope("added"));
            Assert.That(f.Catalog.Enabled(Scope("added").Key), Is.True);
        }

        public static void AllWorktreesIncludesFutureCheckoutsAndNarrowsToOneChoice()
        {
            var main = Scope();
            var one = Scope("one");
            var two = Scope("two");
            var other = new BrowseScope
            {
                ProjectId = "p-id-other",
                Worktree = "one",
                Phase = "ready",
                Path = "/other",
            };
            var choices = new BrowseScopeChoices("");
            choices.Toggle(main);
            choices.Select(one);
            choices.Select(other);
            choices.SelectAll("p-id");
            choices = new BrowseScopeChoices(choices.Save());
            Assert.That(choices.AllChosen("p-id"), Is.True);
            Assert.That(choices.Chosen(main), Is.True);
            Assert.That(choices.Chosen(one), Is.True);
            Assert.That(choices.Chosen(two), Is.True, "future checkouts belong to All worktrees");
            Assert.That(
                choices.Chosen(other),
                Is.True,
                "other project choices survive clearing overrides"
            );
            string all = choices.Save();
            choices.Select(two);
            choices.SelectAll("p-id");
            Assert.That(
                choices.Save(),
                Is.EqualTo(all),
                "repeated All and creation inclusion preserve All mode"
            );
            choices.Toggle(Scope("unavailable", "error"));
            Assert.That(choices.Save(), Is.EqualTo(all));

            choices.Toggle(one);
            choices = new BrowseScopeChoices(choices.Save());
            Assert.That(choices.AllChosen("p-id"), Is.False);
            Assert.That(choices.Chosen(main), Is.False);
            Assert.That(choices.Chosen(one), Is.True);
            Assert.That(choices.Chosen(two), Is.False);
            Assert.That(choices.Chosen(other), Is.True);
            choices.Toggle(one);
            Assert.That(choices.Chosen(one), Is.False, "explicit selections still toggle off");
            choices.SelectAll("p-id");
            choices.Toggle(main);
            Assert.That(choices.Chosen(main), Is.True);
            Assert.That(choices.Chosen(one), Is.False, "Main can also narrow All to one checkout");
        }

        public static void ProjectCheckboxSummarizesCheckoutChoicesAndProjectVisibility()
        {
            var f = new Fixture();
            f.Update();
            f.Reply(f.Request("p", 1), Scope(), Scope("one"), Scope("two", "error"));
            Assert.That(f.Catalog.Selection("absent"), Is.EqualTo(BrowseScopeSelection.None));
            Assert.That(f.Catalog.Selection("p-id"), Is.EqualTo(BrowseScopeSelection.Some));
            f.Toggle(f.Catalog.Find(Scope().Key));
            Assert.That(f.Catalog.Selection("p-id"), Is.EqualTo(BrowseScopeSelection.None));
            f.Catalog.SelectAll("p-id", saved => f.Saved = saved);
            Assert.That(f.Catalog.Selection("p-id"), Is.EqualTo(BrowseScopeSelection.All));
            Assert.That(
                f.Catalog.EnabledScopes().Count,
                Is.EqualTo(2),
                "unready selections remain excluded from browsing"
            );
            f.Filter = "other";
            f.Update();
            Assert.That(f.Catalog.Selection("p-id"), Is.EqualTo(BrowseScopeSelection.None));
            Assert.That(
                f.Catalog.AllChosen("p-id"),
                Is.True,
                "hiding a project preserves All worktrees"
            );
            f.Filter = "";
            f.Update();
            f.Refresh();
            f.Reply(f.Request("p", 2), Scope(), Scope("one"), Scope("new"));
            Assert.That(f.Catalog.Selection("p-id"), Is.EqualTo(BrowseScopeSelection.All));
            Assert.That(f.Catalog.Enabled(Scope("new").Key), Is.True);
            f.Toggle(f.Catalog.Find(Scope("one").Key));
            Assert.That(f.Catalog.Selection("p-id"), Is.EqualTo(BrowseScopeSelection.Some));
            f.Toggle(f.Catalog.Find(Scope("one").Key));
            Assert.That(f.Catalog.Selection("p-id"), Is.EqualTo(BrowseScopeSelection.None));
        }

        public static void RenameReadinessFailureAndRemoval()
        {
            var f = new Fixture();
            f.Update();
            f.Reply(f.Request("p", 1), Scope(), Scope("one"));
            string key = f.Catalog.All("p-id")[1].Key;
            f.Toggle(f.Catalog.Find(key));
            f.Refresh();
            f.Request("p", 2).Reply(null, "Request failed");
            Assert.That(f.Catalog.Enabled(key), Is.True, "transient catalog failure retains scope");
            f.Refresh();
            f.Reply(f.Request("p", 3), Scope(), Scope("one", "error"));
            Assert.That(f.Catalog.Enabled(key), Is.False);
            Assert.That(f.Catalog.Chosen(f.Catalog.Find(key)), Is.True);
            string saved = f.Saved;
            f.Toggle(f.Catalog.Find(key));
            Assert.That(f.Saved, Is.EqualTo(saved), "unready choice is not toggleable");
            var renamed = Scope("one", path: "/p/renamed");
            renamed.Name = "renamed";
            f.Refresh();
            f.Reply(f.Request("p", 4), Scope(), renamed);
            Assert.That(f.Catalog.Enabled(key), Is.True);
            Assert.That(f.Catalog.Find(key).Path, Is.EqualTo("/p/renamed"));
            f.Refresh();
            f.Reply(f.Request("p", 5), Scope());
            Assert.That(
                f.Catalog.Find(key),
                Is.Null,
                "removed worktree drops from visible catalog"
            );
            Assert.That(f.Saved, Is.EqualTo(saved));
        }

        public static void CatalogConcurrencyAndLateReplies()
        {
            var f = new Fixture();
            f.Projects.Add(
                new ProjectInfo
                {
                    Id = "q-id",
                    Name = "q",
                    Dir = "/q",
                }
            );
            f.Projects.Add(
                new ProjectInfo
                {
                    Id = "r-id",
                    Name = "r",
                    Dir = "/r",
                }
            );
            f.Update();
            f.Update();
            Assert.That(f.Requests.Count, Is.EqualTo(2));
            f.Projects[0].Name = "renamed";
            f.ProjectsRevision++;
            f.Update();
            f.Reply(f.Request("p", 1), Scope("obsolete"));
            Assert.That(
                f.Catalog.All("p-id").Select(s => s.Worktree),
                Is.EqualTo(new[] { "main" })
            );
            f.Update();
            Assert.That(f.Requests.Last().Project, Is.EqualTo("renamed"));
            f.Projects.RemoveAt(0);
            f.ProjectsRevision++;
            f.Update();
            f.Reply(f.Request("renamed", 1), Scope("late"));
            Assert.That(f.Catalog.All("p-id"), Is.Empty);
            f.Reply(f.Request("q", 1), Scope());
            f.Update();
            Assert.That(f.Requests.Last().Project, Is.EqualTo("r"));
        }

        public static void StableIdsResolveRenamedProjects()
        {
            var p = new ProjectInfo { Id = "stable", Name = "old" };
            string key = BrowseScope.Identity(p.Id, "tree");
            p.Name = "renamed";
            Assert.That(BrowseScope.ProjectOf(key, new[] { p }), Is.SameAs(p));
            Assert.That(BrowseScope.WorktreeOf(key), Is.EqualTo("tree"));
            Assert.That(new BrowseScopeChoices("").Chosen(Scope()), Is.True);
            var fromWire = ProjectInfo.FromWire(
                new Wire.Project { Id = "stable", Name = "renamed" }
            );
            Assert.That(fromWire.Copy().ToWire().Id, Is.EqualTo("stable"));
        }

        public static void IndependentFoldsAndHistory()
        {
            var main = Scope();
            var one = Scope("one");
            var two = Scope("two");
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

        public static void ReaderScopeSurvivesFilteringAndRoutesRequests()
        {
            var oldHub = SessionHub.Instance;
            try
            {
                SessionHub.Instance = new SessionHub();
                SessionHub.Instance.Projects.Clear();
                SessionHub.Instance.Projects.Add(new ProjectInfo { Id = "p-id", Name = "renamed" });
                var one = Scope("one");
                var two = Scope("two");
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
                var store = new SessionStore(_ => { });
                store.Run(
                    one.Key,
                    "pwd",
                    "reader",
                    _ => { },
                    options: new SessionRunOptions { Host = true }
                );
                var request = (Wire.RunReq)DaemonClient.Requests.Single().Body;
                Assert.That(request.Project, Is.EqualTo("renamed"));
                Assert.That(request.Worktree, Is.EqualTo("one"));
                DaemonClient.Requests.Clear();
                store.RunHostShell(two.Key, _ => { });
                request = (Wire.RunReq)DaemonClient.Requests.Single().Body;
                Assert.That(request.Worktree, Is.EqualTo("two"));
            }
            finally
            {
                SessionHub.Instance = oldHub;
                DaemonClient.Requests.Clear();
            }
        }
    }
}
