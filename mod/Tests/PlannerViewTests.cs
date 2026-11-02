using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class PlannerViewTests
    {
        static PlannerView Loaded()
        {
            DaemonClient.Requests.Clear();
            DaemonClient.BaseUrl = "http://planner-test";
            SessionHub.Instance = new SessionHub();
            TaskDetailView.Shown = null;
            TaskDetailView.ReturnTo = null;
            var view = PlannerView.CreateTest(DaemonClient.BaseUrl);
            SessionHub.Instance.Projects.Add(
                new ProjectInfo { Id = "stable-project-id", Name = "repo" }
            );
            view.Opened();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDocument { Text = "Task", Revision = "r1" });
            return view;
        }

        public static void UnchangedRefreshRetainsFieldsAndPublishesLinkChanges()
        {
            var view = Loaded();
            view.SelectTest(0);
            var draft = view.DraftIdentityTest;
            var fields = view.FieldsIdentityTest;
            var document = view.DocumentTest;
            view.LoadTest();
            Assert.That(view.BusyTest, Is.False);
            Assert.That(view.ReadyTest, Is.True);
            Assert.That(view.DocumentTest, Is.SameAs(document));
            Assert.That(view.DraftIdentityTest, Is.SameAs(draft));
            var refreshed = document.Clone();
            refreshed.Items.Add(new Wire.PlannerItem { Id = "linked" });
            DaemonClient.Requests.Last().Reply(refreshed);
            Assert.That(view.DraftIdentityTest, Is.SameAs(draft));
            Assert.That(view.FieldsIdentityTest, Is.SameAs(fields));
            Assert.That(view.DocumentTest.Items.Single().Id, Is.EqualTo("linked"));
            view.LoadTest();
            DaemonClient.Requests.Last().Fail("Offline");
            Assert.That(view.DraftIdentityTest, Is.SameAs(draft));
            Assert.That(view.FieldsIdentityTest, Is.SameAs(fields));
            view.LoadTest();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDocument { Text = "Changed", Revision = "r2" });
            Assert.That(view.DraftIdentityTest, Is.Not.SameAs(draft));
            Assert.That(view.TextTest, Is.EqualTo("Changed"));
            view.Closed();
        }

        public static void BackgroundRefreshDoesNotOverwriteNewEditsOrANewerSave()
        {
            var view = Loaded();
            view.SelectTest(0);
            view.LoadTest();
            var refresh = DaemonClient.Requests.Last();
            view.PriorityEditTest("A")();
            refresh.Reply(new Wire.PlannerDocument { Text = "External edit", Revision = "r2" });
            Assert.That(view.PriorityTest, Is.EqualTo("A"));
            Assert.That(view.DocumentTest.Revision, Is.EqualTo("r1"));
            view.DiscardTest();
            view.LoadTest();
            refresh = DaemonClient.Requests.Last();
            view.SaveTest("Local edit");
            var save = DaemonClient.Requests.Last();
            refresh.Reply(new Wire.PlannerDocument { Text = "External edit", Revision = "r2" });
            Assert.That(view.BusyTest, Is.True);
            Assert.That(view.TextTest, Is.EqualTo("Local edit"));
            save.Reply(new Wire.PlannerDocument { Text = "Local edit", Revision = "r3" });
            Assert.That(view.DocumentTest.Revision, Is.EqualTo("r3"));
            Assert.That(view.ReadyTest, Is.True);
            view.Closed();
        }

        public static void ClosingDuringSaveRetainsAcceptedRevisionAndPreventsAnotherLoad()
        {
            var view = Loaded();
            view.SaveTest("Edited task");
            var save = DaemonClient.Requests.Last();
            view.Closed();
            Assert.That(view.BusyTest, Is.True);
            save.Reply(
                new Wire.PlannerDocument { Text = "Edited task sw-id:saved", Revision = "r2" }
            );
            view.Opened();
            Assert.That(view.TextTest, Is.EqualTo("Edited task sw-id:saved"));
            Assert.That(view.DocumentTest.Revision, Is.EqualTo("r2"));
            Assert.That(view.ReadyTest, Is.True);
            view.Closed();
        }

        public static void SelectionMenusCannotEditAReplacementDraftOrAnotherEndpoint()
        {
            var view = Loaded();
            view.LoadTest();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDocument { Text = "First\nSecond", Revision = "r2" });
            view.SelectTest(0);
            var edit = view.PriorityEditTest("A");
            view.SelectTest(1);
            edit();
            Assert.That(view.PriorityTest, Is.Empty);
            edit = view.PriorityEditTest("B");
            DaemonClient.BaseUrl = "http://other-daemon";
            edit();
            Assert.That(view.PriorityTest, Is.Empty);
            DaemonClient.BaseUrl = "http://planner-test";
            view.Closed();
            view.Opened();
            edit();
            Assert.That(view.PriorityTest, Is.Empty);
            view.PriorityEditTest("C")();
            Assert.That(view.PriorityTest, Is.EqualTo("C"));
            view.Closed();
        }

        public static void OldReplyCannotReplaceNewerDocumentAndClosedTaskDoesNotOpenWindow()
        {
            var view = Loaded();
            view.LoadTest();
            var old = DaemonClient.Requests.Last();
            view.LoadTest();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDocument { Text = "Latest", Revision = "r3" });
            old.Reply(new Wire.PlannerDocument { Text = "Stale", Revision = "r2" });
            Assert.That(view.TextTest, Is.EqualTo("Latest"));
            view.OpenTaskTest("task");
            var task = DaemonClient.Requests.Last();
            view.Closed();
            view.Opened();
            task.Reply(new Wire.TaskResult { Task = new Wire.Task { Id = "task" } });
            Assert.That(TaskDetailView.Shown, Is.Null);
            Assert.That(view.BusyTest, Is.False);
            view.Closed();
        }

        public static void DescriptionRepliesAndDraftsRemainBoundToTheirTask()
        {
            var view = Loaded();
            view.LoadTest();
            DaemonClient
                .Requests.Last()
                .Reply(
                    new Wire.PlannerDocument
                    {
                        Text = "First sw-id:first\nSecond sw-id:second",
                        Revision = "r2",
                    }
                );
            view.SelectTest(0);
            var first = DaemonClient.Requests.Last();
            view.SelectTest(1);
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDescription { Text = "Second", Revision = "d2" });
            first.Reply(new Wire.PlannerDescription { Text = "First", Revision = "d1" });
            Assert.That(view.DescriptionTest, Is.EqualTo("Second"));
            view.DescriptionTest = "Unsaved second";
            view.SelectTest(0);
            Assert.That(view.DescriptionTest, Is.EqualTo("First"));
            view.SelectTest(1);
            Assert.That(view.DescriptionTest, Is.EqualTo("Unsaved second"));
            Assert.That(view.DescriptionDirtyTest, Is.True);
            view.Closed();
        }

        public static void RemovingLinksUsesTheirRevisionAndSettlesAfterClose()
        {
            var view = Loaded();
            view.LoadTest();
            var item = new Wire.PlannerItem { Id = "first", LinksRevision = "links-r1" };
            item.Links.Add(
                new Wire.PlannerLink
                {
                    Kind = "task",
                    Id = "failed-task",
                    Status = "failed",
                }
            );
            item.Links.Add(new Wire.PlannerLink { Kind = "worker", Id = "stable-worker" });
            var document = new Wire.PlannerDocument { Text = "Task sw-id:first", Revision = "r2" };
            document.Items.Add(item);
            DaemonClient.Requests.Last().Reply(document);
            view.SelectTest(0);
            view.RemoveLinkTest(0);
            var request = DaemonClient.Requests.Last();
            var body = (Wire.PlannerLinksReq)request.Body;
            Assert.That(body.Revision, Is.EqualTo("links-r1"));
            Assert.That(body.Links.Count, Is.EqualTo(1));
            Assert.That(body.Links[0].Id, Is.EqualTo("stable-worker"));
            view.Closed();
            var updated = document.Clone();
            updated.Items[0].Links.RemoveAt(0);
            updated.Items[0].LinksRevision = "links-r2";
            request.Reply(updated);
            view.Opened();
            Assert.That(view.DocumentTest.Items[0].Links.Count, Is.EqualTo(1));
            Assert.That(view.BusyTest, Is.False);
            view.AddLinkTest("worker", "stable-worker");
            Assert.That(DaemonClient.Requests.Last(), Is.SameAs(request));
            DaemonClient.BaseUrl = "http://another-endpoint";
            view.RemoveLinkTest(0);
            Assert.That(DaemonClient.Requests.Last(), Is.SameAs(request));
            view.Closed();
        }

        public static void WorkerLinksFollowIdentityAcrossRenameAndRejectNameReuse()
        {
            var renamed = new SessionInfo
            {
                Name = "renamed-worker",
                StateId = "original",
                Worker = true,
            };
            var replacement = new SessionInfo
            {
                Name = "worker",
                StateId = "replacement",
                Worker = true,
            };
            Assert.That(
                PlannerView.LinkedWorker(new[] { replacement, renamed }, "original"),
                Is.SameAs(renamed)
            );
            Assert.That(PlannerView.LinkedWorker(new[] { replacement }, "original"), Is.Null);
        }

        public static void OpeningALinkSeedsTheTaskReaderBeforeTheNextBoardPoll()
        {
            var view = Loaded();
            view.OpenTaskTest("agent-task");
            var request = DaemonClient.Requests.Last();
            Assert.That(request.Session, Is.EqualTo(TaskInfo.Host));
            request.Reply(
                new Wire.TaskResult
                {
                    Task = new Wire.Task
                    {
                        Id = "agent-task",
                        From = "alice",
                        To = "bob",
                        Body = "Work",
                    },
                }
            );
            Assert.That(TaskDetailView.Shown.Id, Is.EqualTo("agent-task"));
            Assert.That(TaskDetailView.ReturnTo, Is.SameAs(view));
            Assert.That(SessionHub.Instance.Tasks.Single().Id, Is.EqualTo("agent-task"));
            view.Closed();
        }

        public static void JointSaveRetainsFailedDescriptionAndDiscardRestoresBothFields()
        {
            var view = Loaded();
            view.LoadTest();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDocument { Text = "Task sw-id:first", Revision = "r2" });
            view.SelectTest(0);
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDescription { Text = "Original", Revision = "d1" });
            view.DescriptionTest = "Edited description";
            view.SaveTest("Edited task sw-id:first");
            var taskSave = DaemonClient.Requests.Last();
            taskSave.Reply(
                new Wire.PlannerDocument { Text = "Edited task sw-id:first", Revision = "r3" }
            );
            Assert.That(view.BusyTest, Is.True);
            var descriptionSave = DaemonClient.Requests.Last();
            Assert.That(
                ((Wire.PlannerSaveReq)descriptionSave.Body).Text,
                Is.EqualTo("Edited description")
            );
            descriptionSave.Fail("Conflict");
            Assert.That(view.DocumentTest.Revision, Is.EqualTo("r3"));
            Assert.That(view.DescriptionTest, Is.EqualTo("Edited description"));
            Assert.That(view.DirtyTest, Is.True);
            view.SaveDescriptionTest();
            DaemonClient
                .Requests.Last()
                .Reply(
                    new Wire.PlannerDescription { Text = "Edited description", Revision = "d2" }
                );
            Assert.That(view.DirtyTest, Is.False);
            view.PriorityEditTest("A")();
            view.DescriptionTest = "Discard this";
            view.DiscardTest();
            Assert.That(view.PriorityTest, Is.Empty);
            Assert.That(view.DescriptionTest, Is.EqualTo("Edited description"));
            Assert.That(view.DirtyTest, Is.False);
            view.Closed();
        }

        public static void DescriptionSaveSettlesAfterCloseAndErrorsPreserveDraft()
        {
            var view = Loaded();
            view.LoadTest();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDocument { Text = "Task sw-id:first", Revision = "r2" });
            view.SelectTest(0);
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDescription { Text = "Original", Revision = "d1" });
            view.DescriptionTest = "Edited";
            view.SaveDescriptionTest();
            var save = DaemonClient.Requests.Last();
            view.Closed();
            Assert.That(view.DescriptionBusyTest, Is.True);
            save.Reply(new Wire.PlannerDescription { Text = "Edited", Revision = "d2" });
            view.Opened();
            Assert.That(view.DescriptionTest, Is.EqualTo("Edited"));
            Assert.That(view.DescriptionDirtyTest, Is.False);
            view.DescriptionTest = "Conflict draft";
            view.SaveDescriptionTest();
            DaemonClient.Requests.Last().Fail("Description changed");
            Assert.That(view.DescriptionTest, Is.EqualTo("Conflict draft"));
            Assert.That(view.DescriptionErrorTest, Is.EqualTo("Description changed"));
            Assert.That(view.DescriptionBusyTest, Is.False);
            DaemonClient.BaseUrl = "http://another-endpoint";
            int requests = DaemonClient.Requests.Count;
            view.SaveDescriptionTest();
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(requests));
            view.Closed();
        }
    }
}
