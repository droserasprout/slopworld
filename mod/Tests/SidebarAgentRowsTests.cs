using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class SidebarAgentRowsTests
    {
        public static void EmptyBucketsArePrunedAndLiveStorageIsRetained()
        {
            var live = new List<int> { 1 };
            var buckets = new Dictionary<string, List<int>>
            {
                ["live"] = live,
                ["gone"] = new List<int>(),
            };
            var scratch = new List<string>();
            buckets = SidebarAgentRows.PruneEmpty(buckets, scratch);
            buckets = SidebarAgentRows.PruneEmpty(buckets, scratch);
            Assert.That(buckets.Count, Is.EqualTo(1));
            Assert.That(buckets["live"], Is.SameAs(live));
            live.Clear();
            buckets = SidebarAgentRows.PruneEmpty(buckets, scratch);
            Assert.That(buckets, Is.Empty);
            Assert.That(scratch, Is.Empty);
        }

        public static void ChurnCompactsBucketsWithoutChangingLiveLists()
        {
            var live = new List<int> { 1 };
            var buckets = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 100; i++)
                buckets["gone" + i] = new List<int>();
            buckets["LIVE"] = live;
            var compacted = SidebarAgentRows.PruneEmpty(buckets, new List<string>());
            Assert.That(compacted, Is.Not.SameAs(buckets));
            Assert.That(compacted.Count, Is.EqualTo(1));
            Assert.That(compacted["live"], Is.SameAs(live));
        }

        public static void WorkerCountsUseParentProjectsAndActualLists()
        {
            var parents = new Dictionary<string, SessionInfo>
            {
                ["parent-a"] = new SessionInfo { Project = "project" },
                ["parent-b"] = new SessionInfo { Project = "project" },
                ["loose"] = new SessionInfo(),
            };
            var workers = new Dictionary<string, List<SessionInfo>>
            {
                ["parent-a"] = new List<SessionInfo>
                {
                    new SessionInfo { Project = "different" },
                    new SessionInfo(),
                },
                ["parent-b"] = new List<SessionInfo> { new SessionInfo() },
                ["loose"] = new List<SessionInfo> { new SessionInfo() },
                ["removed"] = new List<SessionInfo> { new SessionInfo() },
            };
            var counts = new Dictionary<string, int> { ["stale"] = 99 };
            SidebarAgentRows.CountWorkersByProject(
                workers,
                name => parents.TryGetValue(name, out var parent) ? parent : null,
                counts
            );
            Assert.That(counts.Count, Is.EqualTo(2));
            Assert.That(counts["project"], Is.EqualTo(3));
            Assert.That(counts[SidebarAgentRows.Loose], Is.EqualTo(1));
            workers["parent-a"].Clear();
            SidebarAgentRows.CountWorkersByProject(
                workers,
                name => parents.TryGetValue(name, out var parent) ? parent : null,
                counts
            );
            Assert.That(
                counts["project"],
                Is.EqualTo(1),
                "retained empty buckets cannot inflate the next layout"
            );
        }

        public static void AgentOrderNeedsNoPriorLayoutAndKeepsLooseAgentsLast()
        {
            var agents = new List<SessionInfo>
            {
                new SessionInfo { Name = "loose", Project = "" },
                new SessionInfo { Name = "z-agent", Project = "z" },
                new SessionInfo { Name = "last", Project = "a" },
                new SessionInfo { Name = "first", Project = "a" },
                new SessionInfo { Name = "last", Project = "a" },
            };
            Assert.That(
                SidebarAgentRows.OrderAgents(agents, StringComparer.Ordinal.Compare),
                Is.EqualTo(new[] { "first", "last", "z-agent", "loose" })
            );
            agents.Clear();
            agents.Add(new SessionInfo { Name = "new", Project = "a" });
            Assert.That(
                SidebarAgentRows.OrderAgents(agents, StringComparer.Ordinal.Compare),
                Is.EqualTo(new[] { "new" }),
                "navigation does not retain removed rows"
            );
        }
    }
}
