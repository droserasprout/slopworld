using System.Collections.Generic;
using RimWorld;
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
            public readonly List<Pawn> EntryPawns = new List<Pawn>();
            public readonly List<Vector2> EntryLocations = new List<Vector2>();
            public readonly Dictionary<string, int> ActiveCounts =
                new Dictionary<string, int>();
            public readonly Dictionary<string, int> TotalCounts =
                new Dictionary<string, int>();
            public readonly Dictionary<string, int> WorkerCounts =
                new Dictionary<string, int>();

            public readonly Dictionary<string, List<SessionInfo>> Ghosts =
                new Dictionary<string, List<SessionInfo>>();
            public readonly List<SessionInfo> TopGhosts = new List<SessionInfo>();
            public readonly Dictionary<string, List<SessionInfo>> Workers =
                new Dictionary<string, List<SessionInfo>>();
            public readonly List<SessionInfo> TopWorkers = new List<SessionInfo>();

            public readonly List<Row> ViewRows = new List<Row>();
            public readonly List<SessionInfo> Routed = new List<SessionInfo>();

            public float AgentContentH;
            public float LastScale;

            long _sessionsVersion = -1;
            int _projectsRevision = -1;
            int _projectStateRevision = -1;
            int _entryCount = -1;
            bool _plus;
            float _width = -1f, _screenHeight = -1f, _bodyHeight = -1f, _textHeight = -1f;
            int _workspaceRevision = -1;
            SidebarTab _tab;
            AgentStatusFilter _status;

            public bool Matches(List<ColonistBar.Entry> entries, int count, bool plus,
                                SidebarTab tab, long sessionsVersion, int projectsRevision,
                                int projectStateRevision, AgentStatusFilter status,
                                float width, float screenHeight, float bodyHeight,
                                float textHeight, int workspaceRevision)
            {
                if (_sessionsVersion != sessionsVersion || _projectsRevision != projectsRevision ||
                    _projectStateRevision != projectStateRevision || _entryCount != count ||
                    _plus != plus || _tab != tab || _status != status ||
                    _width != width || _screenHeight != screenHeight ||
                    _bodyHeight != bodyHeight || _textHeight != textHeight ||
                    _workspaceRevision != workspaceRevision ||
                    EntryPawns.Count != count)
                    return false;

                for (int i = 0; i < count; i++)
                    if (EntryPawns[i] != entries[i].pawn) return false;
                return true;
            }

            public void RememberInputs(List<ColonistBar.Entry> entries, int count, bool plus,
                                       SidebarTab tab, long sessionsVersion,
                                       int projectsRevision, int projectStateRevision,
                                       AgentStatusFilter status, float width,
                                       float screenHeight, float bodyHeight, float textHeight,
                                       List<Vector2> locs, int workspaceRevision)
            {
                EntryPawns.Clear();
                EntryLocations.Clear();
                for (int i = 0; i < count; i++)
                {
                    EntryPawns.Add(entries[i].pawn);
                    EntryLocations.Add(locs[i]);
                }
                _sessionsVersion = sessionsVersion;
                _projectsRevision = projectsRevision;
                _projectStateRevision = projectStateRevision;
                _entryCount = count;
                _plus = plus;
                _tab = tab;
                _status = status;
                _width = width;
                _screenHeight = screenHeight;
                _bodyHeight = bodyHeight;
                _textHeight = textHeight;
                _workspaceRevision = workspaceRevision;
            }

            public void RestoreLocations(List<Vector2> locs, int count)
            {
                for (int i = 0; i < count && i < locs.Count; i++) locs[i] = EntryLocations[i];
            }

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
                ActiveCounts.Clear();
                TotalCounts.Clear();
                WorkerCounts.Clear();
                Order.Clear();
                Named.Clear();
                TopGhosts.Clear();
                TopWorkers.Clear();
                EntryPawns.Clear();
                EntryLocations.Clear();

                AgentContentH = 0f;
            }
        }
    }
}
