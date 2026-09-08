using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Colonist-bar bucketing and row geometry for agents, ghosts, and workers.
    public static partial class AgentSidebar
    {
        static void Bucket(List<ColonistBar.Entry> entries, List<Vector2> locs, int count)
        {
            CountAgents();
            BucketEntries(entries, locs, count);
            BucketRuntimeSessions();
            OrderBuckets();
        }

        static void CountAgents()
        {
            // Build the header totals and worker counts once for this layout instead of
            // rescanning every session and worker list once per project below.
            foreach (var info in SessionHub.Instance.Sessions)
            {
                if (info == null || info.Worker || info.Ephemeral || info.Host || IsRouted(info))
                    continue;
                string key = string.IsNullOrEmpty(info.Project) ? Loose : info.Project;
                Increment(Layout.TotalCounts, key);
                if (IsActive(info.State)) Increment(Layout.ActiveCounts, key);
            }
        }

        static void BucketEntries(List<ColonistBar.Entry> entries, List<Vector2> locs, int count)
        {
            for (int i = 0; i < count && i < entries.Count; i++)
            {
                var pawn = entries[i].pawn;
                if (pawn == null)
                {
                    locs[i] = Parked;
                    continue;
                }

                var session = Session(pawn);
                var info = session == null ? null : SessionHub.Instance.Get(session);
                if (info == null)
                {
                    // The daemon's list can drop a removed session before the colony sweep
                    // unbinds its pawn. Do not mistake that stale pawn for a real no-project
                    // agent while the two views catch up.
                    locs[i] = Parked;
                    continue;
                }
                if (!PassesStatus(info.State))
                {
                    locs[i] = Parked;
                    continue;
                }
                if (IsRouted(info))
                {
                    // Reconciliation may leave a routed permanent session's pawn for one tick.
                    locs[i] = Parked;
                    continue;
                }
                // Parked rather than skipped, for the reason the routed ones are: the
                // colonist bar hit-tests the same table it is drawn from.
                if (!Passes(info?.Project))
                {
                    locs[i] = Parked;
                    continue;
                }
                string key = string.IsNullOrEmpty(info?.Project) ? Loose : info.Project;

                if (!Layout.Buckets.TryGetValue(key, out var list))
                    Layout.Buckets[key] = list = new List<int>();
                list.Add(i);
                Layout.Named[i] = session;
            }

            // Vanilla's entry order is only refreshed when the colony reconciles. A rename,
            // project move, or add can therefore leave the visible rows in the old order for
            // several seconds. Sort the indices here rather than waiting for displayOrder;
            // the indices still point at the original entries, so vanilla draws the right
            // pawn at each new location and hit-testing remains aligned.
            foreach (var list in Layout.Buckets.Values)
                list.Sort((a, b) => AgentColony.CompareNames(Layout.Named[a], Layout.Named[b]));
        }

        static void BucketRuntimeSessions()
        {
            foreach (var s in SessionHub.Instance.Sessions)
            {
                if (s.Worker)
                {
                    if (!Passes(s.Project) || !PassesStatus(s.State)) continue;
                    // A child is nested only when its explicit parent has a visible normal row.
                    // Missing parents are surfaced at the top instead of being silently lost.
                    var parent = SessionHub.Instance.Get(s.Parent);
                    bool parentVisible = parent != null && !parent.Worker && Passes(parent.Project)
                        && Layout.Named.ContainsValue(parent.Name);
                    if (!parentVisible)
                    {
                        Layout.TopWorkers.Add(s);
                        continue;
                    }
                    if (!Layout.Workers.TryGetValue(s.Parent, out var children))
                        Layout.Workers[s.Parent] = children = new List<SessionInfo>();
                    children.Add(s);
                    Increment(Layout.WorkerCounts, s.Project);
                    continue;
                }
                if ((!s.Ephemeral && !s.Host) || IsRouted(s) || !Passes(s.Project) ||
                    !PassesStatus(s.State)) continue;
                if (string.IsNullOrEmpty(s.Project)) { Layout.TopGhosts.Add(s); continue; }

                if (!Layout.Ghosts.TryGetValue(s.Project, out var list))
                    Layout.Ghosts[s.Project] = list = new List<SessionInfo>();
                list.Add(s);
            }
        }

        static void OrderBuckets()
        {
            foreach (var kv in Layout.Buckets)
                if (kv.Value.Count > 0) Layout.Order.Add(kv.Key);
            foreach (var kv in Layout.Ghosts)
                if (kv.Value.Count > 0 && !Layout.Order.Contains(kv.Key)) Layout.Order.Add(kv.Key);
            foreach (var p in SessionHub.Instance.Projects)
                if (!Layout.Order.Contains(p.Name) && Passes(p.Name)) Layout.Order.Add(p.Name);

            Layout.Order.Sort((a, b) =>
                a == Loose ? (b == Loose ? 0 : 1)
                : b == Loose ? -1
                : string.CompareOrdinal(a, b));

            Layout.TopGhosts.Sort(ByName);
            Layout.TopWorkers.Sort(ByName);
            foreach (var list in Layout.Ghosts.Values) list.Sort(ByName);
            foreach (var list in Layout.Workers.Values) list.Sort(ByName);
        }

        static readonly List<int> Empty = new List<int>();
        static readonly List<SessionInfo> EmptyGhosts = new List<SessionInfo>();

        static float GhostRow(SessionInfo s, float width, float y)
        {
            // Agent ghost rows draw their own leading mark, if any. The routed view owns the
            // separate action-slot layout; reserving it here needlessly shortens host paths.
            float tx = CellX;
            Layout.Rows.Add(new Row
            {
                Session = s.Name,
                Pawn = null,
                Ghost = true,
                Line = new Rect(0f, y, width, GhostH),
                Text = new Rect(tx, y + 1f, width - tx - Pad, NameH),
                Face = Rect.zero,
            });
            return y + GhostH;
        }

        static float WorkerRow(SessionInfo s, float width, float y, int depth)
        {
            float tx = CellX + Mathf.Min(depth, 8) * 12f;
            Layout.Rows.Add(new Row
            {
                Session = s.Name,
                Pawn = null,
                Ghost = false,
                Worker = true,
                Line = new Rect(0f, y, width, WorkerH),
                Text = new Rect(tx + GhostMarkW + TextGap, y + 1f,
                    width - tx - GhostMarkW - TextGap - Pad, WorkerH - 1f),
                Face = Rect.zero,
            });
            return y + WorkerH;
        }

        static int ByName(SessionInfo a, SessionInfo b) =>
            string.CompareOrdinal(a?.Name ?? "", b?.Name ?? "");

        static void Increment(Dictionary<string, int> counts, string key)
        {
            if (counts.TryGetValue(key, out int current)) counts[key] = current + 1;
            else counts[key] = 1;
        }


        static string Session(Pawn pawn) =>
            pawn == null ? null : AgentColony.Current?.SessionOf(pawn);
    }
}
