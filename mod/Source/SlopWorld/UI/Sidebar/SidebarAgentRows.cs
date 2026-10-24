using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Project keys and ordering shared by row geometry and navigation without geometry.
    internal static class SidebarAgentRows
    {
        internal const string Loose = "no project";

        internal static string ProjectKey(SessionInfo info) =>
            string.IsNullOrEmpty(info.Project) ? Loose : info.Project;

        internal static void CountWorkersByProject(
            Dictionary<string, List<SessionInfo>> workersByParentSession,
            Func<string, SessionInfo> parentSession,
            Dictionary<string, int> counts
        )
        {
            counts.Clear();
            foreach (var pair in workersByParentSession)
            {
                var parent = parentSession(pair.Key);
                if (parent == null || pair.Value.Count == 0)
                    continue;
                string key = ProjectKey(parent);
                counts.TryGetValue(key, out int before);
                counts[key] = before + pair.Value.Count;
            }
        }

        // Reuse live bucket storage, but release identities no longer present in a rebuild.
        internal static Dictionary<string, List<T>> PruneEmpty<T>(
            Dictionary<string, List<T>> buckets,
            List<string> stale
        )
        {
            stale.Clear();
            foreach (var pair in buckets)
                if (pair.Value.Count == 0)
                    stale.Add(pair.Key);
            foreach (string key in stale)
                buckets.Remove(key);
            bool compact = stale.Count > 32 && stale.Count > buckets.Count;
            stale.Clear();
            // Dictionary enumeration traverses removed slots too. Rebuild after substantial
            // churn so the cost and retained capacity follow the live inventory.
            return compact ? new Dictionary<string, List<T>>(buckets, buckets.Comparer) : buckets;
        }

        internal static List<string> OrderAgents(
            List<SessionInfo> agents,
            Comparison<string> compareNames
        )
        {
            agents.Sort(
                (a, b) =>
                {
                    string left = ProjectKey(a),
                        right = ProjectKey(b);
                    int project =
                        left == Loose ? (right == Loose ? 0 : 1)
                        : right == Loose ? -1
                        : string.CompareOrdinal(left, right);
                    return project != 0 ? project : compareNames(a.Name, b.Name);
                }
            );
            var order = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var agent in agents)
                if (seen.Add(agent.Name))
                    order.Add(agent.Name);
            return order;
        }
    }
}
