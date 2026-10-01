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
            public readonly List<Vector2> PlacedLocations = new List<Vector2>();
            public readonly Dictionary<string, int> ActiveCounts =
                new Dictionary<string, int>();
            public readonly Dictionary<string, int> TotalCounts =
                new Dictionary<string, int>();
            public readonly Dictionary<string, int> WorkerCountsByProject =
                new Dictionary<string, int>();

            public readonly Dictionary<string, List<SessionInfo>> Ghosts =
                new Dictionary<string, List<SessionInfo>>();
            public readonly List<SessionInfo> TopGhosts = new List<SessionInfo>();
            public readonly Dictionary<string, List<SessionInfo>> WorkersByParentSession =
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
            float _width = -1f, _screenHeight = -1f, _bodyHeight = -1f, _textHeight = -1f;
            int _workspaceRevision = -1;
            string _pager, _editor;
            SidebarTab _tab;
            AgentStatusFilter _status;

            public bool Matches(List<ColonistBar.Entry> entries, int count,
                                SidebarTab tab, long sessionsVersion, int projectsRevision,
                                int projectStateRevision, AgentStatusFilter status,
                                float width, float screenHeight, float bodyHeight,
                                float textHeight, int workspaceRevision, string pager, string editor)
            {
                if (_sessionsVersion != sessionsVersion || _projectsRevision != projectsRevision ||
                    _projectStateRevision != projectStateRevision || _entryCount != count ||
                    _tab != tab || _status != status ||
                    _width != width || _screenHeight != screenHeight ||
                    _bodyHeight != bodyHeight || _textHeight != textHeight ||
                    _workspaceRevision != workspaceRevision || _pager != pager || _editor != editor ||
                    EntryPawns.Count != count)
                    return false;

                for (int i = 0; i < count; i++)
                    if (EntryPawns[i] != entries[i].pawn) return false;
                return true;
            }

            // Called after placement: retain sidebar coordinates for the next cache hit.
            public void RememberInputs(List<ColonistBar.Entry> entries, int count,
                                       SidebarTab tab, long sessionsVersion,
                                       int projectsRevision, int projectStateRevision,
                                       AgentStatusFilter status, float width,
                                       float screenHeight, float bodyHeight, float textHeight,
                                       List<Vector2> locs, int workspaceRevision, string pager, string editor)
            {
                EntryPawns.Clear();
                PlacedLocations.Clear();
                for (int i = 0; i < count; i++)
                {
                    EntryPawns.Add(entries[i].pawn);
                    PlacedLocations.Add(locs[i]);
                }
                _sessionsVersion = sessionsVersion;
                _projectsRevision = projectsRevision;
                _projectStateRevision = projectStateRevision;
                _entryCount = count;
                _tab = tab;
                _status = status;
                _width = width;
                _screenHeight = screenHeight;
                _bodyHeight = bodyHeight;
                _textHeight = textHeight;
                _workspaceRevision = workspaceRevision;
                _pager = pager;
                _editor = editor;
            }

            public void RestoreLocations(List<Vector2> locs, int count)
            {
                for (int i = 0; i < count && i < locs.Count; i++) locs[i] = PlacedLocations[i];
            }

            public void BeginFrame()
            {
                Rows.Clear();
                Heads.Clear();
                Faces.Clear();
                ViewRows.Clear();
                // Routed membership has its own session/filter/reader revision cache.

                foreach (var list in Buckets.Values) list.Clear();
                foreach (var list in Ghosts.Values) list.Clear();
                foreach (var list in WorkersByParentSession.Values) list.Clear();
                ActiveCounts.Clear();
                TotalCounts.Clear();
                WorkerCountsByProject.Clear();
                Order.Clear();
                Named.Clear();
                TopGhosts.Clear();
                TopWorkers.Clear();
                EntryPawns.Clear();
                PlacedLocations.Clear();

                AgentContentH = 0f;
            }
        }
    }
}
