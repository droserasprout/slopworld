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

        public static void QuickAddKeepsFiltersAndRecoversFromRejectedSave()
        {
            var view = Loaded();
            try
            {
                view.AddTest("Follow up", "repo", "office");
                var save = DaemonClient.Requests.Last();
                string submitted = ((Wire.PlannerSaveReq)save.Body).Text;
                Assert.That(submitted, Does.Contain("Follow up +repo @office"));
                Assert.That(view.SelectedTitleTest, Is.EqualTo("Follow up"));
                Assert.That(view.QuickAddTest, Is.Empty);
                Assert.That(view.DetailOpenTest, Is.True);
                save.Fail("Offline");
                Assert.That(view.TextTest, Is.EqualTo(submitted));
                Assert.That(view.DirtyTest, Is.True);
                Assert.That(view.ErrorTest, Is.EqualTo("Offline"));
                view.SaveTest(view.TextTest);
                var retry = DaemonClient.Requests.Last();
                Assert.That(((Wire.PlannerSaveReq)retry.Body).Text, Is.EqualTo(submitted));
                retry.Reply(new Wire.PlannerDocument { Text = submitted, Revision = "r2" });
                Assert.That(view.DirtyTest, Is.False);
            }
            finally
            {
                view.Closed();
            }
        }

        public static void InvalidQuickAddPreservesInputWithoutSubmitting()
        {
            var view = Loaded();
            try
            {
                int requests = DaemonClient.Requests.Count;
                view.AddTest("\n");
                Assert.That(DaemonClient.Requests.Count, Is.EqualTo(requests));
                Assert.That(view.TextTest, Is.EqualTo("Task"));
                Assert.That(view.QuickAddTest, Is.EqualTo("\n"));
                Assert.That(view.ErrorTest, Is.Not.Empty);
            }
            finally
            {
                view.Closed();
            }
        }

        public static void DirtyRowBlocksSelectionAndCompletionUntilDiscarded()
        {
            var view = Loaded();
            try
            {
                view.LoadTest();
                DaemonClient
                    .Requests.Last()
                    .Reply(new Wire.PlannerDocument { Text = "First\nSecond", Revision = "r2" });
                view.SelectTest(0);
                view.PriorityEditTest("A")();
                var draft = view.DraftIdentityTest;
                int requests = DaemonClient.Requests.Count;
                view.SelectTest(1);
                view.ToggleTest(1);
                Assert.That(view.SelectedTitleTest, Is.EqualTo("First"));
                Assert.That(view.DraftIdentityTest, Is.SameAs(draft));
                Assert.That(view.TextTest, Is.EqualTo("First\nSecond"));
                Assert.That(view.ErrorTest, Does.Contain("Save or discard"));
                Assert.That(DaemonClient.Requests.Count, Is.EqualTo(requests));
                view.SelectTest(0);
                Assert.That(view.DraftIdentityTest, Is.SameAs(draft));
                view.DiscardTest();
                view.ToggleTest(1);
                var save = DaemonClient.Requests.Last();
                string text = ((Wire.PlannerSaveReq)save.Body).Text;
                var entries = TodoText.Parse(text);
                Assert.That(entries[0].Completed, Is.False);
                Assert.That(entries[1].Completed, Is.True);
                save.Reply(new Wire.PlannerDocument { Text = text, Revision = "r3" });
                view.ToggleTest(1);
                Assert.That(TodoText.Parse(view.TextTest)[1].Completed, Is.False);
            }
            finally
            {
                view.Closed();
            }
        }

        public static void DeleteClearsSelectionAndFailedSaveCanBeDiscarded()
        {
            var view = Loaded();
            try
            {
                view.SelectTest(0);
                view.DeleteTest();
                Assert.That(view.TextTest, Is.Empty);
                Assert.That(view.SelectedTitleTest, Is.Null);
                Assert.That(view.DetailOpenTest, Is.False);
                var save = DaemonClient.Requests.Last();
                Assert.That(((Wire.PlannerSaveReq)save.Body).Text, Is.Empty);
                save.Fail("Conflict");
                Assert.That(view.DirtyTest, Is.True);
                view.DiscardTest();
                Assert.That(view.TextTest, Is.EqualTo("Task"));
                Assert.That(view.DirtyTest, Is.False);
            }
            finally
            {
                view.Closed();
            }
        }

        public static void SourceToggleValidatesAndAppliesTheSelectedDraft()
        {
            var view = Loaded();
            try
            {
                view.SelectTest(0);
                view.PriorityEditTest("invalid")();
                view.ToggleSourceTest();
                Assert.That(view.SourceTest, Is.False);
                Assert.That(view.TextTest, Is.EqualTo("Task"));
                Assert.That(view.ErrorTest, Does.Contain("Priority"));
                view.PriorityEditTest("A")();
                int requests = DaemonClient.Requests.Count;
                view.ToggleSourceTest();
                Assert.That(view.SourceTest, Is.True);
                Assert.That(view.TextTest, Does.StartWith("(A) "));
                Assert.That(view.DirtyTest, Is.True);
                Assert.That(DaemonClient.Requests.Count, Is.EqualTo(requests));
                view.ToggleSourceTest();
                Assert.That(view.SourceTest, Is.False);
                Assert.That(view.PriorityTest, Is.EqualTo("A"));
            }
            finally
            {
                view.Closed();
            }
        }

        public static void DocumentOwnerKeepsSaveActiveWhilePublishingBacklogAndDescriptionStages()
        {
            DaemonClient.Requests.Clear();
            DaemonClient.BaseUrl = "http://planner-owner-test";
            var state = new PlannerDocumentState(DaemonClient.BaseUrl);
            state.Load(() => false);
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDocument { Text = "Task sw-id:first", Revision = "r1" });
            state.EnsureDescription("first");
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDescription { Text = "Original", Revision = "d1" });
            state.Description("first").Text = "Description draft";
            state.Text = "Edited task sw-id:first";
            state.Parse();
            state.Save();
            Assert.That(state.Stage, Is.EqualTo(PlannerDocumentState.RequestStage.SavingBacklog));
            // ID assignment or daemon normalization can rebuild rows at this boundary.
            // Subscribers must never observe an idle gap in the joint save.
            bool published = false;
            state.Rebuilt += () =>
            {
                published = true;
                Assert.That(state.Busy, Is.True);
                Assert.That(
                    state.Stage,
                    Is.EqualTo(PlannerDocumentState.RequestStage.SavingBacklog)
                );
                int requests = DaemonClient.Requests.Count;
                state.Save();
                Assert.That(DaemonClient.Requests.Count, Is.EqualTo(requests));
            };
            DaemonClient
                .Requests.Last()
                .Reply(
                    new Wire.PlannerDocument
                    {
                        Text = "Normalized task sw-id:first",
                        Revision = "r2",
                    }
                );
            Assert.That(published, Is.True);
            Assert.That(
                state.Stage,
                Is.EqualTo(PlannerDocumentState.RequestStage.SavingDescriptions)
            );
            DaemonClient.Requests.Last().Fail("Conflict");
            Assert.That(state.Stage, Is.EqualTo(PlannerDocumentState.RequestStage.Idle));
            Assert.That(state.Document.Revision, Is.EqualTo("r2"));
            Assert.That(state.Description("first").Revision, Is.EqualTo("d1"));
            Assert.That(state.Description("first").Dirty, Is.True);
            state.Save();
            Assert.That(
                state.Stage,
                Is.EqualTo(PlannerDocumentState.RequestStage.SavingDescriptions)
            );
            var retry = DaemonClient.Requests.Last();
            Assert.That(((Wire.PlannerSaveReq)retry.Body).Revision, Is.EqualTo("d1"));
            retry.Reply(
                new Wire.PlannerDescription { Text = "Description draft", Revision = "d2" }
            );
            Assert.That(state.Stage, Is.EqualTo(PlannerDocumentState.RequestStage.Idle));
            Assert.That(state.Dirty, Is.False);
        }

        public static void DescriptionBatchRetryKeepsEachAcceptedRevisionAndSkipsCommittedDrafts()
        {
            var view = DescriptionTask();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDescription { Text = "First", Revision = "d1" });
            view.LoadTest();
            DaemonClient
                .Requests.Last()
                .Reply(
                    new Wire.PlannerDocument
                    {
                        Text = "Task sw-id:first\nSecond sw-id:second",
                        Revision = "r3",
                    }
                );
            view.SelectTest(1);
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDescription { Text = "Second", Revision = "d2" });
            view.DescriptionTest = "Edited second";
            view.SelectTest(0);
            view.DescriptionTest = "Edited first";
            view.SaveDescriptionTest();
            var first = DaemonClient.Requests.Last();
            var firstBody = (Wire.PlannerSaveReq)first.Body;
            first.Reply(
                new Wire.PlannerDescription { Text = firstBody.Text, Revision = "accepted" }
            );
            var second = DaemonClient.Requests.Last();
            Assert.That(second, Is.Not.SameAs(first));
            var secondBody = (Wire.PlannerSaveReq)second.Body;
            second.Fail("Offline");
            // Select the committed draft while retrying the other task's description.
            int committed = firstBody.Text == "Edited first" ? 0 : 1;
            view.SelectTest(committed);
            Assert.That(view.DescriptionRevisionTest, Is.EqualTo("accepted"));
            Assert.That(view.DescriptionDirtyTest, Is.False);
            view.SaveDescriptionTest();
            var retry = DaemonClient.Requests.Last();
            Assert.That(((Wire.PlannerSaveReq)retry.Body).Text, Is.EqualTo(secondBody.Text));
            Assert.That(
                ((Wire.PlannerSaveReq)retry.Body).Revision,
                Is.EqualTo(secondBody.Revision)
            );
            int requests = DaemonClient.Requests.Count;
            view.Closed();
            retry.Reply(
                new Wire.PlannerDescription { Text = secondBody.Text, Revision = "recovered" }
            );
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(requests));
            Assert.That(view.BusyTest, Is.False);
            Assert.That(view.DirtyTest, Is.False);
            view.Opened();
            view.SelectTest(1 - committed);
            Assert.That(view.DescriptionRevisionTest, Is.EqualTo("recovered"));
            view.Closed();
        }

        public static void ImportedRowsCanSaveWithoutUnrelatedEdits()
        {
            var view = Loaded();
            Assert.That(view.DirtyTest, Is.False);
            Assert.That(view.CanSaveTest, Is.True);
            view.SaveTest(view.TextTest);
            var save = DaemonClient.Requests.Last();
            Assert.That(((Wire.PlannerSaveReq)save.Body).Text, Is.EqualTo("Task"));
            save.Reply(new Wire.PlannerDocument { Text = "Task sw-id:first", Revision = "r2" });
            Assert.That(view.CanSaveTest, Is.False);
            view.SelectTest(0);
            Assert.That(DaemonClient.Requests.Last(), Is.Not.SameAs(save));
            view.Closed();
        }

        public static void EndpointChangeStopsChainedSaveAndRetainsRemainingDraft()
        {
            var view = DescriptionTask();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDescription { Text = "Original", Revision = "d1" });
            view.DescriptionTest = "Private draft";
            view.SaveTest("Edited sw-id:first");
            var save = DaemonClient.Requests.Last();
            int requests = DaemonClient.Requests.Count;
            DaemonClient.BaseUrl = "http://other-daemon";
            save.Reply(new Wire.PlannerDocument { Text = "Edited sw-id:first", Revision = "r3" });
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(requests));
            Assert.That(view.BusyTest, Is.False);
            Assert.That(view.DescriptionTest, Is.EqualTo("Private draft"));
            Assert.That(view.DescriptionDirtyTest, Is.True);
            Assert.That(view.DocumentTest.Revision, Is.EqualTo("r3"));
            DaemonClient.BaseUrl = "http://planner-test";
            view.SaveDescriptionTest();
            view.Closed();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDescription { Text = "Private draft", Revision = "d2" });
            Assert.That(view.DirtyTest, Is.False);
            Assert.That(view.BusyTest, Is.False);
        }

        public static void EndpointChangeBetweenDescriptionsDoesNotSendTheNextDraft()
        {
            var view = DescriptionTask();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDescription { Text = "First", Revision = "d1" });
            view.LoadTest();
            DaemonClient
                .Requests.Last()
                .Reply(
                    new Wire.PlannerDocument
                    {
                        Text = "Task sw-id:first\nSecond sw-id:second",
                        Revision = "r3",
                    }
                );
            view.SelectTest(1);
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDescription { Text = "Second", Revision = "d2" });
            view.DescriptionTest = "Edited second";
            view.SelectTest(0);
            view.DescriptionTest = "Edited first";
            view.SaveDescriptionTest();
            var firstSave = DaemonClient.Requests.Last();
            int requests = DaemonClient.Requests.Count;
            DaemonClient.BaseUrl = "http://other-daemon";
            firstSave.Reply(
                new Wire.PlannerDescription
                {
                    Text = ((Wire.PlannerSaveReq)firstSave.Body).Text,
                    Revision = "d3",
                }
            );
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(requests));
            Assert.That(view.BusyTest, Is.False);
            Assert.That(view.DirtyTest, Is.True);
            view.Closed();
        }

        public static void DeletingARowWithADirtyDescriptionRejectsBeforeWriting()
        {
            var view = DescriptionTask();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDescription { Text = "Original", Revision = "d1" });
            view.DescriptionTest = "Keep this draft";
            int requests = DaemonClient.Requests.Count;
            view.SaveTest("");
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(requests));
            Assert.That(view.ErrorTest, Does.Contain("Restore"));
            Assert.That(view.BusyTest, Is.False);
            Assert.That(view.DocumentTest.Text, Is.EqualTo("Task sw-id:first"));
            Assert.That(view.DescriptionTest, Is.EqualTo("Keep this draft"));
            view.SaveTest("Task sw-id:first");
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDescription { Text = "Keep this draft", Revision = "d2" });
            view.SaveTest("");
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDocument { Text = "", Revision = "r3" });
            Assert.That(view.DirtyTest, Is.False);
            view.Closed();
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

        static PlannerView DescriptionTask()
        {
            var view = Loaded();
            view.LoadTest();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDocument { Text = "Task sw-id:first", Revision = "r2" });
            view.SelectTest(0);
            return view;
        }

        public static void FailedDescriptionReadCanRetryAndCleanReloadAdoptsRemoteText()
        {
            var view = DescriptionTask();
            DaemonClient.Requests.Last().Fail("Offline");
            Assert.That(view.DescriptionRevisionTest, Is.Null);
            Assert.That(view.DescriptionBusyTest, Is.False);
            view.ReloadDescriptionTest();
            var retry = DaemonClient.Requests.Last();
            int count = DaemonClient.Requests.Count;
            view.ReloadDescriptionTest();
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(count));
            Assert.That(view.DescriptionBusyTest, Is.True);
            retry.Reply(new Wire.PlannerDescription { Text = "Recovered", Revision = "d1" });
            Assert.That(view.DescriptionTest, Is.EqualTo("Recovered"));
            Assert.That(view.DescriptionErrorTest, Is.Null);
            view.ReloadDescriptionTest();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDescription { Text = "External edit", Revision = "d2" });
            Assert.That(view.DescriptionTest, Is.EqualTo("External edit"));
            Assert.That(view.DescriptionDirtyTest, Is.False);
            Assert.That(view.DescriptionRevisionTest, Is.EqualTo("d2"));
            view.Closed();
            count = DaemonClient.Requests.Count;
            view.ReloadDescriptionTest();
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(count));
            view.Opened();
            DaemonClient.BaseUrl = "http://another-endpoint";
            view.ReloadDescriptionTest();
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(count));
            view.Closed();
        }

        public static void ConflictedDescriptionReloadPreservesDraftAndResavesLatestRevision()
        {
            var view = DescriptionTask();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDescription { Text = "Original", Revision = "d1" });
            view.DescriptionTest = "My draft";
            view.SaveDescriptionTest();
            DaemonClient.Requests.Last().Fail("Description changed");
            view.ReloadDescriptionTest();
            DaemonClient.Requests.Last().Fail("Offline");
            Assert.That(view.DescriptionTest, Is.EqualTo("My draft"));
            Assert.That(view.DescriptionRevisionTest, Is.EqualTo("d1"));
            view.ReloadDescriptionTest();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDescription { Text = "External edit", Revision = "d2" });
            Assert.That(view.DescriptionTest, Is.EqualTo("My draft"));
            Assert.That(view.DescriptionDirtyTest, Is.True);
            Assert.That(view.DescriptionRevisionTest, Is.EqualTo("d2"));
            view.DiscardTest();
            Assert.That(view.DescriptionTest, Is.EqualTo("External edit"));
            view.DescriptionTest = "My draft";
            view.SaveDescriptionTest();
            var save = DaemonClient.Requests.Last();
            var body = (Wire.PlannerSaveReq)save.Body;
            Assert.That(body.Revision, Is.EqualTo("d2"));
            Assert.That(body.Text, Is.EqualTo("My draft"));
            int count = DaemonClient.Requests.Count;
            view.ReloadDescriptionTest();
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(count));
            save.Reply(new Wire.PlannerDescription { Text = "My draft", Revision = "d3" });
            Assert.That(view.DescriptionDirtyTest, Is.False);
            Assert.That(view.DescriptionRevisionTest, Is.EqualTo("d3"));
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
