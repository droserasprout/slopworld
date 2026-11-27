using System.Collections.Generic;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class TaskBoardRowsTests
    {
        static TaskInfo Task(
            string id,
            string from,
            string to,
            DelegatedTaskStatus status = DelegatedTaskStatus.Working
        ) =>
            new TaskInfo
            {
                Id = id,
                From = from,
                To = to,
                Status = status,
            };

        public static void FilterChangesRetainLiveSelectionMembershipAndRecountOpenDirections()
        {
            var rows = new TaskBoardRows();
            var filters = new TaskFilters();
            var tasks = new List<TaskInfo>
            {
                Task("in", "a", TaskInfo.Host),
                Task("out", TaskInfo.Host, "a"),
                Task("peer", "a", "b"),
                Task("done", "a", TaskInfo.Host, DelegatedTaskStatus.Done),
            };
            Assert.That(rows.Update(tasks, filters, false), Is.True);
            Assert.That(rows.Incoming, Is.EqualTo(1));
            Assert.That(rows.Outgoing, Is.EqualTo(1));
            Assert.That(rows.Peer, Is.EqualTo(1));
            Assert.That(rows.Update(tasks, filters, false), Is.False);
            filters.Direction.DeselectAll();
            Assert.That(rows.Update(tasks, filters, true), Is.False);
            Assert.That(rows.Visible, Is.Empty);
            Assert.That(rows.LiveIds, Is.EquivalentTo(new[] { "in", "out", "peer", "done" }));
            Assert.That(rows.Incoming + rows.Outgoing + rows.Peer, Is.Zero);
            filters.Clear();
            rows.Update(tasks, filters, true);
            Assert.That(rows.Visible, Is.EqualTo(tasks));
        }

        public static void ReplacementRecountsChangedStatusAndEmptySnapshotPrunesAllIds()
        {
            var rows = new TaskBoardRows();
            var filters = new TaskFilters();
            rows.Update(new List<TaskInfo> { Task("a", "worker", TaskInfo.Host) }, filters, false);
            var replacement = new List<TaskInfo>
            {
                Task("a", "worker", TaskInfo.Host, DelegatedTaskStatus.Done),
            };
            Assert.That(rows.Update(replacement, filters, false), Is.True);
            Assert.That(rows.Incoming, Is.Zero);
            Assert.That(rows.Visible[0], Is.SameAs(replacement[0]));
            var empty = new List<TaskInfo>();
            Assert.That(rows.Update(empty, filters, false), Is.True);
            Assert.That(rows.Visible, Is.Empty);
            Assert.That(rows.LiveIds, Is.Empty);
            Assert.That(rows.Update(empty, filters, false), Is.False);
        }
    }
}
