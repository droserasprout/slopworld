using System;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class TaskFiltersTests
    {
        static readonly DelegatedTaskStatus[] Statuses = (DelegatedTaskStatus[])
            Enum.GetValues(typeof(DelegatedTaskStatus));
        static readonly TaskDirection[] Directions = (TaskDirection[])
            Enum.GetValues(typeof(TaskDirection));

        static TaskInfo Task(
            string from,
            string to,
            DelegatedTaskStatus status = DelegatedTaskStatus.Working
        ) =>
            new TaskInfo
            {
                From = from,
                To = to,
                Status = status,
            };

        public static void AllChecksEveryChildAndIncludesFutureAgents()
        {
            var filters = new TaskFilters();
            Assert.That(filters.Filtering, Is.False);
            foreach (var status in Statuses)
                Assert.That(filters.Status.Contains(status), Is.True);
            foreach (var direction in Directions)
                Assert.That(filters.Direction.Contains(direction), Is.True);
            Assert.That(filters.Agent.Contains("future"), Is.True);
            Assert.That(filters.Matches(Task("future", TaskInfo.Host)), Is.True);
        }

        public static void StatusAndDirectionSelectionsAllowMultipleValuesAndCanBeEmpty()
        {
            var filters = new TaskFilters();
            filters.Status.Toggle(DelegatedTaskStatus.Working, Statuses);
            Assert.That(filters.Filtering, Is.True);
            Assert.That(filters.Matches(Task("a", TaskInfo.Host)), Is.False);
            Assert.That(
                filters.Matches(Task("a", TaskInfo.Host, DelegatedTaskStatus.Done)),
                Is.True
            );
            Assert.That(
                filters.Matches(Task("a", TaskInfo.Host, DelegatedTaskStatus.Failed)),
                Is.True
            );
            filters.Status.SelectAll();

            filters.Direction.Toggle(TaskDirection.Incoming, Directions);
            Assert.That(filters.Matches(Task("a", TaskInfo.Host)), Is.False);
            Assert.That(filters.Matches(Task(TaskInfo.Host, "a")), Is.True);
            Assert.That(filters.Matches(Task("a", "b")), Is.True);
            filters.Direction.Toggle(TaskDirection.Outgoing, Directions);
            filters.Direction.Toggle(TaskDirection.AgentToAgent, Directions);
            Assert.That(filters.Matches(Task("a", "b")), Is.False);
            filters.Clear();
            foreach (var status in Statuses)
                filters.Status.Toggle(status, Statuses);
            foreach (var status in Statuses)
                Assert.That(filters.Matches(Task("a", "b", status)), Is.False);
        }

        public static void AgentSelectionMatchesEitherAgentAndNeverTreatsHostAsAnAgent()
        {
            var filters = new TaskFilters();
            var agents = new[] { "a", "b", "c" };
            filters.Agent.Toggle("a", agents);
            Assert.That(filters.Matches(Task("a", TaskInfo.Host)), Is.False);
            Assert.That(filters.Matches(Task(TaskInfo.Host, "a")), Is.False);
            Assert.That(filters.Matches(Task("a", "b")), Is.True);
            Assert.That(filters.Matches(Task("c", "a")), Is.True);
            Assert.That(filters.Matches(Task(TaskInfo.Host, "future")), Is.False);
            filters.Agent.Toggle("b", agents);
            filters.Agent.Toggle("c", agents);
            Assert.That(filters.Matches(Task(TaskInfo.Host, "a")), Is.False);
            Assert.That(filters.Matches(Task("a", "b")), Is.False);
            filters.Agent.Toggle("a", agents);
            Assert.That(filters.Matches(Task(TaskInfo.Host, "a")), Is.True);
            filters.Clear();
            Assert.That(filters.Filtering, Is.False);
            Assert.That(filters.Matches(Task(TaskInfo.Host, "future")), Is.True);
        }
    }
}
