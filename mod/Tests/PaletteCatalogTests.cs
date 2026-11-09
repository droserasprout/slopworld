using System;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld
{
    public partial class CommandPalette
    {
        public sealed partial class PaletteExecutionTests
        {
            sealed class CatalogFixture : IDisposable
            {
                readonly BrowseScope[] scopes = SidebarScopes.TestScopes.ToArray();
                readonly string[] disabled = SidebarScopes.TestDisabled.ToArray();
                readonly DaemonClient.Request[] requests = DaemonClient.Requests.ToArray();
                readonly (string Action, string Scope)[] git = GitView.Calls.ToArray();

                public CatalogFixture()
                {
                    SidebarScopes.TestScopes.Clear();
                    SidebarScopes.TestDisabled.Clear();
                    DaemonClient.Requests.Clear();
                    GitView.Calls.Clear();
                }

                public void Dispose()
                {
                    SidebarScopes.TestScopes.Clear();
                    SidebarScopes.TestScopes.AddRange(scopes);
                    SidebarScopes.TestDisabled.Clear();
                    SidebarScopes.TestDisabled.UnionWith(disabled);
                    DaemonClient.Requests.Clear();
                    DaemonClient.Requests.AddRange(requests);
                    GitView.Calls.Clear();
                    GitView.Calls.AddRange(git);
                }
            }

            static BrowseScope Checkout(string worktree) =>
                new BrowseScope
                {
                    ProjectId = "project-id",
                    Project = "p",
                    Worktree = worktree,
                    Name = worktree,
                    Path = "/repo/" + worktree,
                    Phase = "ready",
                };

            [TestCase("removed")]
            [TestCase("hidden")]
            [TestCase("unready")]
            public void GitSelectionDoesNotFallBackToMainWhenCheckoutBecomesUnavailable(
                string change
            )
            {
                using var fixture = new CatalogFixture();
                var main = Checkout("main");
                var feature = Checkout("feature");
                SidebarScopes.TestScopes.AddRange(new[] { main, feature });
                Open(GitCommands().Single(c => c.Id == "git.stage-all"));
                palette.ExecuteSelected();
                palette._subIndex = palette._subShown.FindIndex(h => h.O.Value == feature.Key);
                if (change == "removed")
                    SidebarScopes.TestScopes.Remove(feature);
                else if (change == "hidden")
                    SidebarScopes.TestDisabled.Add(feature.Key);
                else
                    feature.Phase = "removing";
                Assert.That(
                    Assert.Throws<Exception>(() => palette.ExecuteSub()).Message,
                    Is.EqualTo("checkout is no longer available")
                );
                Assert.That(GitView.Calls, Is.Empty);
            }

            [Test]
            public void GitChoicesDistinguishMainAndWorktreeAndKeepIdentityAcrossRename()
            {
                using var fixture = new CatalogFixture();
                var main = Checkout("main");
                var feature = Checkout("feature");
                SidebarScopes.TestScopes.AddRange(new[] { main, feature });
                var command = GitCommands().Single(c => c.Id == "git.diff-all");
                var options = command.SubAction();
                Assert.That(
                    options.Select(o => o.Value),
                    Is.EqualTo(new[] { main.Key, feature.Key })
                );
                Assert.That(options[0].Label, Does.Contain("Main checkout"));
                feature.Project = "renamed";
                feature.Name = "renamed tree";
                command.Action(feature.Key);
                command.Action(main.Key);
                Assert.That(
                    GitView.Calls.Select(c => c.Scope),
                    Is.EqualTo(new[] { feature.Key, main.Key })
                );
                Assert.That(
                    Assert.Throws<Exception>(() => command.Action("p")).Message,
                    Is.EqualTo("checkout is no longer available")
                );
                Assert.That(GitView.Calls.Count, Is.EqualTo(2));
            }

            [Test]
            public void GitMenuShowsDisabledPlaceholderWhenNoCheckoutIsReady()
            {
                using var fixture = new CatalogFixture();
                SidebarScopes.TestScopes.Add(
                    new BrowseScope
                    {
                        ProjectId = "p",
                        Worktree = "broken",
                        Phase = "failed",
                    }
                );
                Open(GitCommands().First());
                palette.ExecuteSelected();
                Assert.That(palette._subShown.Single().O.Enabled, Is.False);
                palette.ExecuteSub();
                Assert.That(GitView.Calls, Is.Empty);
                Assert.That(palette.Closed, Is.False);
            }

            [Test]
            public void TaskCandidatesFollowSharedEligibilityAndIgnoreMalformedRows()
            {
                using var fixture = new CatalogFixture();
                SessionHub.Instance.Tasks.Add(null);
                SessionHub.Instance.Tasks.Add(new TaskInfo());
                foreach (DelegatedTaskStatus status in Enum.GetValues<DelegatedTaskStatus>())
                foreach (bool worker in new[] { false, true })
                    SessionHub.Instance.Tasks.Add(
                        new TaskInfo
                        {
                            Id = status + "-" + worker,
                            Status = status,
                            WorkerSession = worker ? "w" : "",
                            To = TaskInfo.Host,
                        }
                    );
                var valid = SessionHub
                    .Instance.Tasks.Where(t => t != null && t.Id.Length > 0)
                    .ToList();
                var commands = TaskCommands();
                Assert.That(
                    commands.Single(c => c.Id == "task.open").SubAction().Select(o => o.Value),
                    Is.EqualTo(valid.Select(t => t.Id))
                );
                Assert.That(
                    commands.Single(c => c.Id == "task.cancel").SubAction().Select(o => o.Value),
                    Is.EqualTo(valid.Where(t => t.CanCancel).Select(t => t.Id))
                );
                Assert.That(
                    commands.Single(c => c.Id == "task.remove").SubAction().Select(o => o.Value),
                    Is.EqualTo(valid.Where(t => t.Terminal).Select(t => t.Id))
                );
                Assert.That(
                    commands.Single(c => c.Id == "task.status").SubAction().Count,
                    Is.EqualTo(valid.Count(t => t.Incoming && !t.Terminal))
                );
                SessionHub.Instance.Tasks.Clear();
                foreach (var command in commands.Where(c => c.SubAction != null))
                    Assert.That(command.SubAction().Single().Enabled, Is.False, command.Id);
            }

            [Test]
            public void TaskRegistrationsDispatchCurrentRecordsToTheirOwners()
            {
                using var fixture = new CatalogFixture();
                var previousCanceled = TaskActions.Canceled;
                var previousRemoved = TaskActions.Removed;
                var previousShown = TaskDetailView.Shown;
                var previousReturn = TaskDetailView.ReturnTo;
                int previousFilters = TasksView.FiltersOpened;
                try
                {
                    var commands = TaskCommands();
                    SessionHub.Instance.Tasks.Add(new TaskInfo { Id = "t" });
                    var replacement = new TaskInfo { Id = "t", Status = DelegatedTaskStatus.Done };
                    SessionHub.Instance.Tasks[0] = replacement;
                    commands.Single(c => c.Id == "task.open").Action("t");
                    commands.Single(c => c.Id == "task.remove").Action("t");
                    Assert.That(TaskDetailView.Shown, Is.SameAs(replacement));
                    Assert.That(TaskActions.Removed, Is.SameAs(replacement));
                    replacement.Status = DelegatedTaskStatus.Accepted;
                    commands.Single(c => c.Id == "task.cancel").Action("t");
                    Assert.That(TaskActions.Canceled, Is.SameAs(replacement));
                    commands.Single(c => c.Id == "task.filter").Action(null);
                    Assert.That(TasksView.FiltersOpened, Is.EqualTo(previousFilters + 1));
                }
                finally
                {
                    TaskActions.Canceled = previousCanceled;
                    TaskActions.Removed = previousRemoved;
                    TaskDetailView.Shown = previousShown;
                    TaskDetailView.ReturnTo = previousReturn;
                    TasksView.FiltersOpened = previousFilters;
                }
            }

            [TestCase("removed")]
            [TestCase("reassigned")]
            [TestCase("done")]
            public void StatusSubmenuRechecksTargetBeforeLoadingChoicesAndBeforeSubmitting(
                string change
            )
            {
                using var fixture = new CatalogFixture();
                SessionHub.Instance.Tasks.Add(new TaskInfo { Id = "t", To = TaskInfo.Host });
                var row = TaskCommands().Single(c => c.Id == "task.status").SubAction().Single();
                var chosen = row.Children().Single(o => o.Label == "working");
                SessionHub.Instance.Tasks.Clear();
                if (change != "removed")
                    SessionHub.Instance.Tasks.Add(
                        new TaskInfo
                        {
                            Id = "t",
                            To = change == "reassigned" ? "agent" : TaskInfo.Host,
                            Status =
                                change == "done"
                                    ? DelegatedTaskStatus.Done
                                    : DelegatedTaskStatus.Queued,
                        }
                    );
                Assert.That(row.Children().Single().Enabled, Is.False);
                Assert.That(
                    Assert.Throws<Exception>(() => chosen.Select()).Message,
                    Is.EqualTo("task is no longer available for status updates")
                );
                Assert.That(DaemonClient.Requests, Is.Empty);
            }

            [Test]
            public void StatusSubmissionUsesLatestSnapshotSkipsRedundantUpdateAndReportsFailure()
            {
                using var fixture = new CatalogFixture();
                SessionHub.Instance.Tasks.Add(
                    new TaskInfo
                    {
                        Id = "t",
                        To = TaskInfo.Host,
                        Status = DelegatedTaskStatus.Accepted,
                    }
                );
                var root = TaskCommands().Single(c => c.Id == "task.status");
                var row = root.SubAction().Single();
                var options = row.Children();
                Assert.That(options.Single(o => o.Label == "accepted").Enabled, Is.False);
                var working = options.Single(o => o.Label == "working");
                SessionHub.Instance.Tasks[0] = new TaskInfo
                {
                    Id = "t",
                    To = TaskInfo.Host,
                    Status = DelegatedTaskStatus.Working,
                };
                working.Select();
                Assert.That(DaemonClient.Requests, Is.Empty);
                Assert.That(row.Children().Single(o => o.Label == "working").Enabled, Is.False);
                options.Single(o => o.Label == "done").Select();
                var request = DaemonClient.Requests.Single();
                Assert.That(request.Path, Is.EqualTo(WireProtocol.Routes.Tasks + "/t"));
                Assert.That(request.Session, Is.EqualTo(TaskInfo.Host));
                Assert.That(((Wire.UpdateTaskReq)request.Body).Status, Is.EqualTo("done"));
                Assert.That(((Wire.UpdateTaskReq)request.Body).HasNote, Is.False);
                Assert.That(
                    Assert.Throws<Exception>(() => request.Fail("offline")).Message,
                    Is.EqualTo("offline")
                );
            }

            [Test]
            public void ProjectChoicesKeepNameContractAndEmptyPlaceholder()
            {
                var options = ProjectsSub();
                Assert.That(options.Single().Value, Is.EqualTo("p"));
                SessionHub.Instance.Projects.Clear();
                Assert.That(ProjectsSub().Single().Enabled, Is.False);
            }

            [TestCase("git.diff-all", "diff")]
            [TestCase("git.stage-all", "stage")]
            [TestCase("git.unstage-all", "unstage")]
            [TestCase("git.commit-staged", "commit")]
            public void RegisteredGitCommandsPreserveSelectedCheckoutIdentity(
                string id,
                string action
            )
            {
                using var fixture = new CatalogFixture();
                var main = Checkout("main");
                var feature = Checkout("feature-id");
                SidebarScopes.TestScopes.AddRange(new[] { main, feature });
                SidebarScopes.TestDisabled.Add(main.Key);
                var command = GitCommands().Single(c => c.Id == id);
                Open(command);
                palette.ExecuteSelected();
                Assert.That(
                    palette._subShown.Select(h => h.O.Value),
                    Is.EqualTo(new[] { feature.Key })
                );
                Assert.That(
                    palette._subShown[0].O.Label,
                    Does.Contain("p").And.Contain("feature-id")
                );
                palette.ExecuteSub();
                Assert.That(GitView.Calls.Single(), Is.EqualTo((action, feature.Key)));
            }

            [Test]
            public void RegisteredCancelCommandIncludesWorkingWorkersButNotWorkingMail()
            {
                using var fixture = new CatalogFixture();
                var worker = new TaskInfo
                {
                    Id = "worker",
                    Status = DelegatedTaskStatus.Working,
                    WorkerSession = "w",
                };
                SessionHub.Instance.Tasks.AddRange(
                    new[]
                    {
                        worker,
                        new TaskInfo { Id = "mail", Status = DelegatedTaskStatus.Working },
                    }
                );
                Open(TaskCommands().Single(c => c.Id == "task.cancel"));
                palette.ExecuteSelected();
                Assert.That(
                    palette._subShown.Where(h => h.O.Enabled).Select(h => h.O.Value),
                    Is.EqualTo(new[] { "worker" })
                );
            }

            [TestCase(DelegatedTaskStatus.Done)]
            [TestCase(DelegatedTaskStatus.Failed)]
            [TestCase(DelegatedTaskStatus.Canceled)]
            public void StatusChoiceCannotReopenTaskCompletedAfterMenuOpened(
                DelegatedTaskStatus completed
            )
            {
                using var fixture = new CatalogFixture();
                SessionHub.Instance.Tasks.Add(
                    new TaskInfo
                    {
                        Id = "t",
                        To = TaskInfo.Host,
                        Status = DelegatedTaskStatus.Accepted,
                    }
                );
                Open(TaskCommands().Single(c => c.Id == "task.status"));
                palette.ExecuteSelected();
                palette.ExecuteSub();
                var working = palette._subShown.Single(h => h.O.Label == "working").O;
                SessionHub.Instance.Tasks[0] = new TaskInfo
                {
                    Id = "t",
                    To = TaskInfo.Host,
                    Status = completed,
                };
                Assert.That(
                    Assert.Throws<Exception>(() => working.Select()).Message,
                    Is.EqualTo("task is no longer available for status updates")
                );
                Assert.That(
                    DaemonClient.Requests,
                    Is.Empty,
                    "stale choice must not reopen completed work"
                );
            }
        }
    }
}
