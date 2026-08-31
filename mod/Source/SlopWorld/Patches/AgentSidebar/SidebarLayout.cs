using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static partial class AgentSidebar
    {
        // Geometry is rebuilt by Place for each colonist-bar frame. SmoothScroll and the
        // grip gesture remain here because their targets span the bar's back/front GUI passes.
        sealed class SidebarLayout
        {
            public readonly List<Row> Rows = new List<Row>();
            public readonly List<Head> Heads = new List<Head>();
            public readonly Dictionary<Pawn, Rect> Faces = new Dictionary<Pawn, Rect>();

            public readonly Dictionary<string, List<int>> Buckets =
                new Dictionary<string, List<int>>();
            public readonly List<string> Order = new List<string>();
            public readonly Dictionary<int, string> Named = new Dictionary<int, string>();

            public readonly Dictionary<string, List<SessionInfo>> Ghosts =
                new Dictionary<string, List<SessionInfo>>();
            public readonly List<SessionInfo> TopGhosts = new List<SessionInfo>();
            public readonly Dictionary<string, List<SessionInfo>> Workers =
                new Dictionary<string, List<SessionInfo>>();
            public readonly List<SessionInfo> TopWorkers = new List<SessionInfo>();

            public readonly List<Row> ViewRows = new List<Row>();
            public readonly List<SessionInfo> Routed = new List<SessionInfo>();

            public float AgentContentH;

            public void BeginFrame()
            {
                Rows.Clear();
                Heads.Clear();
                Faces.Clear();
                ViewRows.Clear();
                Routed.Clear();

                foreach (var list in Buckets.Values) list.Clear();
                foreach (var list in Ghosts.Values) list.Clear();
                foreach (var list in Workers.Values) list.Clear();
                Order.Clear();
                Named.Clear();
                TopGhosts.Clear();
                TopWorkers.Clear();

                AgentContentH = 0f;
            }
        }
    }
}
