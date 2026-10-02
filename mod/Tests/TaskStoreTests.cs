using System;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class TaskStoreTests
    {
        public static void OverlappingCallersReceiveEveryDependencyOutcome()
        {
            foreach (bool cancel in new[] { false, true })
            foreach (bool failure in new[] { false, true })
            {
                DaemonClient.Requests.Clear();
                var store = new TaskStore();
                Action<string[], Action, Action<string>> run = (ids, ok, fail) =>
                {
                    if (cancel) store.CancelMany(ids, ok, fail);
                    else store.RemoveMany(ids, ok, fail);
                };
                int first = 0, duplicate = 0, overlap = 0, failed = 0;
                run(new[] { "a", "b" }, () => first++, _ => failed++);
                run(new[] { "b" }, () => duplicate++, _ => failed++);
                run(new[] { "b", "c" }, () => overlap++, _ => failed++);
                Assert.That(DaemonClient.Requests.Count, Is.EqualTo(1));
                if (failure) DaemonClient.Requests[0].Fail("denied");
                else DaemonClient.Requests[0].Ok(cancel ? Tasks() : ProtobufFixtures.Json(new Wire.Removed()));
                Assert.That(overlap, Is.Zero, "waits for its remaining IDs");
                Assert.That(duplicate, Is.EqualTo(failure ? 0 : 1));
                DaemonClient.Requests[1].Ok(cancel ? Tasks() : ProtobufFixtures.Json(new Wire.Removed()));
                Assert.That(first + duplicate + overlap, Is.EqualTo(failure ? 0 : 3));
                Assert.That(failed, Is.EqualTo(failure ? 3 : 0));
                DaemonClient.Requests[2].Ok(Tasks());
                run(new[] { "b" }, () => first++, _ => failed++);
                Assert.That(DaemonClient.Requests[3].Method, Is.EqualTo("POST"), "reservation released for retry");
            }
        }

        public static void AddedWorkerTaskSurvivesOlderRefresh()
        {
            DaemonClient.Requests.Clear();
            var store = new TaskStore();
            store.Refresh();
            store.Add(TaskInfo.FromWire(Task("worker-task", 10)));
            DaemonClient.Requests[0].Ok(Tasks(Task("stale", 1)));
            Assert.That(store.Tasks.Select(t => t.Id), Is.EqualTo(new[] { "worker-task" }));
            store.Refresh();
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(2), "refresh admission released");
            DaemonClient.Requests[1].Ok(Tasks(Task("worker-task", 11), Task("another", 12)));
            Assert.That(store.Tasks.Count, Is.EqualTo(2));
        }

        static Wire.Task Task(string id, ulong updated = 0, string status = "queued") =>
            new Wire.Task { Id = id, UpdatedMs = updated, Status = status };

        static JVal Tasks(params Wire.Task[] tasks)
        {
            var reply = new Wire.TasksReply();
            reply.Tasks.Add(tasks);
            return ProtobufFixtures.Json(reply);
        }

        public static void RefreshUsesOperatorBoardAndPollDeadline()
        {
            DaemonClient.Requests.Clear();
            UnityEngine.Time.realtimeSinceStartupAsDouble = 100;
            var store = new TaskStore();
            store.Update(false);
            Assert.That(DaemonClient.Requests, Is.Empty);

            store.Update(true);
            var request = DaemonClient.Requests.Single();
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Session, Is.EqualTo(TaskInfo.Host));
            Assert.That(request.Path, Is.EqualTo(WireProtocol.Routes.Tasks + "?all=true"));
            store.Update(true);
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(1), "one refresh in flight");
            request.Ok(Tasks(Task("old", 3, "done"), Task("new", 8)));
            Assert.That(store.Tasks.Select(t => t.Id), Is.EqualTo(new[] { "new", "old" }));
            Assert.That(store.OpenTasks, Is.EqualTo(1));
            UnityEngine.Time.realtimeSinceStartupAsDouble = 109;
            store.Update(true);
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(1));
            UnityEngine.Time.realtimeSinceStartupAsDouble = 110;
            store.Update(true);
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(2));
        }

        public static void MutationInvalidatesPendingRefreshAndPreservesNewerTask()
        {
            DaemonClient.Requests.Clear();
            var store = new TaskStore();
            bool refreshCompleted = false;
            store.Refresh(() => refreshCompleted = true);
            TaskInfo created = null;
            store.Create(null, null, task => created = task);
            var create = DaemonClient.Requests[1];
            Assert.That(create.Path, Is.EqualTo(WireProtocol.Routes.Tasks));
            var body = (Wire.CreateTaskReq)create.Body;
            Assert.That(body.To, Is.Empty);
            Assert.That(body.Body, Is.Empty);
            create.Ok(ProtobufFixtures.Json(new Wire.TaskResult { Task = Task("new", 9) }));
            DaemonClient.Requests[0].Ok(Tasks(Task("stale", 1)));
            Assert.That(created.Id, Is.EqualTo("new"));
            Assert.That(store.Tasks.Select(t => t.Id), Is.EqualTo(new[] { "new" }));
            Assert.That(refreshCompleted, Is.False);

            store.UpdateStatus("a/b c", DelegatedTaskStatus.Done, "finished", null);
            var update = DaemonClient.Requests[2];
            Assert.That(update.Path, Is.EqualTo(WireProtocol.Routes.Tasks + "/a%2Fb%20c"));
            var status = (Wire.UpdateTaskReq)update.Body;
            Assert.That(status.Status, Is.EqualTo("done"));
            Assert.That(status.HasNote, Is.True);
            Assert.That(status.Note, Is.EqualTo("finished"));
            update.Ok(ProtobufFixtures.Json(new Wire.TaskResult { Task = Task("new", 10, "done") }));
            Assert.That(store.OpenTasks, Is.Zero);
        }

        public static void CancellationBatchesSerializeAndRefreshAfterLastReply()
        {
            DaemonClient.Requests.Clear();
            var store = new TaskStore();
            store.Add(TaskInfo.FromWire(Task("a", 1)));
            int completed = 0;
            string failure = null;
            store.CancelMany(new[] { "a", "a", "", null, "b" }, () => completed++);
            store.CancelMany(new[] { "b", "c" }, null, error => failure = error);
            Assert.That(DaemonClient.Requests.Count, Is.EqualTo(1));
            Assert.That(((Wire.RemoveTasksReq)DaemonClient.Requests[0].Body).Ids,
                Is.EqualTo(new[] { "a", "b" }));
            DaemonClient.Requests[0].Ok(Tasks(Task("a", 4, "canceled")));
            Assert.That(completed, Is.EqualTo(1));
            Assert.That(store.Tasks.Single().Status, Is.EqualTo(DelegatedTaskStatus.Canceled));
            Assert.That(((Wire.RemoveTasksReq)DaemonClient.Requests[1].Body).Ids,
                Is.EqualTo(new[] { "c" }));
            DaemonClient.Requests[1].Fail("offline");
            Assert.That(failure, Is.EqualTo("offline"));
            Assert.That(DaemonClient.Requests[2].Path, Is.EqualTo(WireProtocol.Routes.Tasks + "?all=true"));
            DaemonClient.Requests[2].Ok(Tasks(Task("a", 4, "canceled"), Task("c", 5)));
            Assert.That(store.Tasks.Select(t => t.Id), Is.EqualTo(new[] { "c", "a" }));
        }

        public static void RemovalBatchesDeduplicateAndReconcileFailures()
        {
            DaemonClient.Requests.Clear();
            var store = new TaskStore();
            store.Add(TaskInfo.FromWire(Task("a", 1)));
            store.Add(TaskInfo.FromWire(Task("b", 2)));
            store.Add(TaskInfo.FromWire(Task("c", 3)));
            store.RemoveMany(new[] { "a", "a", "b" });
            store.RemoveMany(new[] { "b", "c" });
            Assert.That(((Wire.RemoveTasksReq)DaemonClient.Requests[0].Body).Ids,
                Is.EqualTo(new[] { "a", "b" }));
            DaemonClient.Requests[0].Ok(ProtobufFixtures.Json(new Wire.Removed()));
            Assert.That(store.Tasks.Select(t => t.Id), Is.EqualTo(new[] { "c" }));
            Assert.That(((Wire.RemoveTasksReq)DaemonClient.Requests[1].Body).Ids,
                Is.EqualTo(new[] { "c" }));
            string failure = null;
            // Failure leaves local state intact until the final reconciliation read.
            DaemonClient.Requests[1].Fail("denied");
            Assert.That(store.Tasks.Select(t => t.Id), Is.EqualTo(new[] { "c" }));
            Assert.That(DaemonClient.Requests[2].Method, Is.EqualTo("GET"));
            DaemonClient.Requests[2].Ok(Tasks(Task("c", 3)));
            store.Remove("c", null, error => failure = error);
            DaemonClient.Requests[3].Fail("again");
            Assert.That(failure, Is.EqualTo("again"));
        }

        public static void PruneKeepsOpenTasksAndReportsFailure()
        {
            DaemonClient.Requests.Clear();
            var store = new TaskStore();
            store.Add(TaskInfo.FromWire(Task("open", 1)));
            store.Add(TaskInfo.FromWire(Task("done", 2, "done")));
            bool completed = false;
            store.Prune(() => completed = true);
            Assert.That(DaemonClient.Requests[0].Method, Is.EqualTo("DELETE"));
            Assert.That(DaemonClient.Requests[0].Path, Is.EqualTo(WireProtocol.Routes.Tasks + "?all=true"));
            Assert.That(DaemonClient.Requests[0].Session, Is.EqualTo(TaskInfo.Host));
            DaemonClient.Requests[0].Ok(ProtobufFixtures.Json(new Wire.Removed()));
            Assert.That(completed, Is.True);
            Assert.That(store.Tasks.Select(t => t.Id), Is.EqualTo(new[] { "open" }));
            string failure = null;
            store.Prune(null, error => failure = error);
            DaemonClient.Requests[1].Fail("offline");
            Assert.That(failure, Is.EqualTo("offline"));
            Assert.That(store.Tasks.Select(t => t.Id), Is.EqualTo(new[] { "open" }));
        }
    }
}
