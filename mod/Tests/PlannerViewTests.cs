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
            var view = PlannerView.CreateTest(DaemonClient.BaseUrl);
            view.Opened();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDocument { Text = "Task", Revision = "r1" });
            return view;
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

        public static void ClosedSummarySettlesWithoutReloadingOrTouchingAnotherEndpoint()
        {
            var view = Loaded();
            view.RunTest("summarize");
            var action = DaemonClient.Requests.Last();
            view.Closed();
            DaemonClient.BaseUrl = "http://other-daemon";
            int requests = DaemonClient.Requests.Count;
            action.Reply(new Wire.PlannerActionReply { Output = "Saved summary" });
            Assert.That(view.OutputTest, Is.EqualTo("Saved summary"));
            Assert.That(view.TextTest, Is.EqualTo("Task"));
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(requests));
            DaemonClient.BaseUrl = "http://planner-test";
            view.Opened();
            Assert.That(view.ReadyTest, Is.True);
            view.Closed();
        }

        public static void FailedDispatchRequiresReconciliationAndRetainsStartupError()
        {
            var view = Loaded();
            view.RunTest("execute");
            DaemonClient.Requests.Last().Fail("Worker startup failed");
            Assert.That(view.ReadyTest, Is.False);
            view.Draw(new Rect(0, 0, 800, 600));
            var reload = DaemonClient.Requests.Last();
            Assert.That(reload.Method, Is.EqualTo("GET"));
            reload.Fail("Offline");
            Assert.That(view.ReadyTest, Is.False);
            view.LoadTest();
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDocument { Text = "Task sw-task:pending", Revision = "r2" });
            Assert.That(view.ReadyTest, Is.True);
            Assert.That(view.TextTest, Does.Contain("sw-task:pending"));
            view.Closed();
        }

        public static void SuccessfulReconciliationPreservesTheDispatchFailureExplanation()
        {
            var view = Loaded();
            view.RunTest("execute");
            DaemonClient.Requests.Last().Fail("Worker startup failed");
            view.Draw(new Rect(0, 0, 800, 600));
            DaemonClient
                .Requests.Last()
                .Reply(new Wire.PlannerDocument { Text = "Task sw-task:pending", Revision = "r2" });
            Assert.That(view.ErrorTest, Is.EqualTo("Worker startup failed"));
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
    }
}
