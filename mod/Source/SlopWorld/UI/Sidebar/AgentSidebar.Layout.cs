using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Panel geometry, placement sizing, and grouped row layout.
    public static partial class AgentSidebar
    {
        public static Rect Panel => WorkspaceLayout.Current.Navigation;

        public static Rect AddBar
        {
            get
            {
                float y = UI.screenHeight - AddH;
                return new Rect(Panel.x, y, Width, AddH);
            }
        }

        static Rect AddHitBar =>
            new Rect(AddBar.x, AddBar.y, Mathf.Max(0f, Width - GripW), AddBar.height);

        public static Rect Body =>
            new Rect(Panel.x, TabH, Width,
                Mathf.Max(0f, UI.screenHeight - TabH - AddH));


        public static bool FaceBox(Pawn pawn, out Rect box)
        {
            if (pawn != null && Layout.Faces.TryGetValue(pawn, out box)) return true;

            box = Rect.zero;
            return false;
        }

        struct PlacementMeasure
        {
            public float Scale;
            public float Pitch;
            public float Cell;
            public float Face;
            public float RowH;
        }

        public static float Place(
            List<ColonistBar.Entry> entries, List<Vector2> locs, int count)
        {
            BeginSessionSnapshot();
            Interaction.BeginFrame();

            if (CurrentTab != SidebarTab.Agents)
            {
                Layout.BeginFrame();
                // Skipping entries would leave invisible vanilla hit targets over the tree.
                for (int i = 0; i < count && i < locs.Count; i++) locs[i] = Parked;
                return Nominal;
            }

            var hub = SessionHub.Instance;
            var status = StatusFilter;
            int workspaceRevision = WorkspaceLayout.Revision;
            if (Layout.Matches(entries, count, CurrentTab, hub.SessionsVersion,
                               hub.ProjectsRevision, Projects.Revision, status, Width,
                               UI.screenHeight, Body.height, TextH, workspaceRevision, hub.Config.EffectivePager, hub.Config.Editor))
            {
                PerfTrace.Count("sidebar-layout-hits");
                Layout.RestoreLocations(locs, count);
                return Layout.LastScale;
            }

            Layout.BeginFrame();
            PerfTrace.Count("sidebar-layout-rebuilds");
            Bucket(entries, locs, count);

            // Measure.
            var measure = MeasurePlacement();

            // Layout.
            float y = LayoutAgents(entries, locs, measure);
            Layout.AgentContentH = Mathf.Max(Body.height, y + Pad);
            Layout.LastScale = measure.Scale;
            Layout.RememberInputs(entries, count, CurrentTab, hub.SessionsVersion,
                hub.ProjectsRevision, Projects.Revision, status, Width, UI.screenHeight,
                Body.height, TextH, locs, workspaceRevision, hub.Config.EffectivePager, hub.Config.Editor);

            return measure.Scale;
        }

        static PlacementMeasure MeasurePlacement()
        {
            // Agent rows are content coordinates: DrawBack/DrawFront put them inside the
            // shared scroll view, whose screen origin is Body. The chrome below is still
            // screen-fixed and never enters this coordinate space.
            float room = Body.height;
            int rows = 0;
            foreach (var key in Layout.Order)
                if (!Folded.Contains(key) && Layout.Buckets.TryGetValue(key, out var b))
                    rows += b.Count;

            // Add is outside this viewport now, so the body already accounts for its height.
            float scale = Fit(rows, Layout.Order.Count, room, GhostRoom());
            float face = ColonistBarColonistDrawer.PawnTextureSize.y * scale;
            return new PlacementMeasure
            {
                Scale = scale,
                Pitch = Pitch(scale),
                Cell = ColonistBar.BaseSize.y * scale,
                Face = face,
                RowH = Mathf.Max(face, TextH),
            };
        }

        static float LayoutAgents(
            List<ColonistBar.Entry> entries, List<Vector2> locs, PlacementMeasure measure)
        {
            float width = Width;
            float y = Pad;
            foreach (var w in Layout.TopWorkers) y = WorkerRow(w, width, y, 0);
            foreach (var g in Layout.TopGhosts) y = GhostRow(g, width, y);

            foreach (var key in Layout.Order)
                y = LayoutGroup(key, entries, locs, width, y, measure);

            return y;
        }

        static float LayoutGroup(
            string key, List<ColonistBar.Entry> entries, List<Vector2> locs,
            float width, float y, PlacementMeasure measure)
        {
            var bucket = Layout.Buckets.TryGetValue(key, out var b) ? b : Empty;
            bool folded = Folded.Contains(key);
            var ghosts = Layout.Ghosts.TryGetValue(key, out var gs) ? gs : EmptyGhosts;
            AgentCounts(key, out int active, out int total);

            Layout.Heads.Add(new Head
            {
                Label = key,
                Rect = new Rect(0f, y, width, HeadH),
                Active = active,
                Total = total,
                Count = active + "/" + total,
                Folded = folded,
            });
            y += HeadH;

            if (folded)
            {
                foreach (int i in bucket) locs[i] = Parked;
                return y;
            }

            foreach (var g in ghosts) y = GhostRow(g, width, y);
            foreach (int i in bucket)
            {
                LayoutAgent(entries, locs, i, width, y, measure);
                y += measure.Pitch;
                y = LayoutWorkers(Layout.Named[i], width, y, 0);
            }
            return y;
        }

        static bool IsActive(AgentState state) => state == AgentState.Working
            || state == AgentState.Waiting;

        static void AgentCounts(string project, out int active, out int total)
        {
            Layout.ActiveCounts.TryGetValue(project, out active);
            Layout.TotalCounts.TryGetValue(project, out total);
        }

        static float LayoutWorkers(string parent, float width, float y, int depth)
        {
            if (depth > 32 || !Layout.WorkersByParentSession.TryGetValue(parent, out var workers)) return y;
            foreach (var worker in workers)
            {
                y = WorkerRow(worker, width, y, depth + 1);
                y = LayoutWorkers(worker.Name, width, y, depth + 1);
            }
            return y;
        }

        static void LayoutAgent(
            List<ColonistBar.Entry> entries, List<Vector2> locs, int index,
            float width, float y, PlacementMeasure measure)
        {
            locs[index] = new Vector2(PortraitX + (measure.Face - measure.Cell) / 2f,
                y + (measure.RowH - measure.Cell) / 2f);

            // Keep the three text lines at the same gap from the portrait after the
            // portrait column moves to the screen edge. Headings and chrome retain
            // their CellX inset.
            float tx = PortraitX + measure.Face + TextGap;
            var line = new Rect(0f, y, width, measure.RowH);
            var face = new Rect(PortraitX, y + (measure.RowH - measure.Face) / 2f,
                measure.Face, measure.Face);
            Layout.Rows.Add(new Row
            {
                Session = Session(entries[index].pawn),
                Pawn = entries[index].pawn,
                Line = line,
                Text = new Rect(tx, y + (measure.RowH - TextH) / 2f,
                    width - tx - Pad, TextH),
                Face = face,
            });
            // Match FaceBox's old first-row behavior if vanilla ever supplies a duplicate
            // pawn entry.
            if (!Layout.Faces.ContainsKey(entries[index].pawn))
                Layout.Faces.Add(entries[index].pawn, face);
        }

        static float Pitch(float s) => Mathf.Max(
            (ColonistBar.BaseSize.y + ColonistBar.BaseSpaceBetweenColonistsVertical) * s,
            TextH + RowGap);

        static float Fit(int rows, int groups, float room, float ghosts)
        {
            if (rows <= 0) return Nominal;
            float headings = groups * HeadH;
            float edgePadding = 2f * Pad;
            float fixedH = headings + edgePadding + ghosts;
            float each = ColonistBar.BaseSize.y + ColonistBar.BaseSpaceBetweenColonistsVertical;
            float s = (room - fixedH) / (rows * each);
            // Fonts do not shrink with portraits, so once the labels set the pitch there is
            // no height left to save. Min rather than Clamp: the two bounds are both read
            // off the same font now and would otherwise cross.
            float useful = Mathf.Min(Nominal, Mathf.Max(Floor, (TextH + RowGap) / each));
            return Mathf.Clamp(s, useful, Nominal);
        }


        static float GhostRoom()
        {
            float h = Layout.TopGhosts.Count * GhostH + Layout.TopWorkers.Count * WorkerH;
            foreach (var kv in Layout.Ghosts)
                if (!Folded.Contains(kv.Key)) h += kv.Value.Count * GhostH;
            foreach (var kv in Layout.WorkerCountsByProject)
                if (!Folded.Contains(kv.Key)) h += kv.Value * WorkerH;
            return h;
        }

    }
}
