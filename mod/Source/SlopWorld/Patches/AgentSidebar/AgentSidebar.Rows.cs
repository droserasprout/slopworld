using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // AgentSidebar row rendering and routed-row interaction.
    public static partial class AgentSidebar
    {
        public static float DrawRouted(Rect body, SidebarTab tab)
        {
            DrawRoutedRows(body, tab, null, body.width, 0f);
            return RoutedHeight;
        }

        public static void DrawRouted(Rect body, SidebarTab tab, SmoothScroll scroll)
        {
            if (body.width <= 0f || body.height <= 0f || RoutedHeight <= 0f) return;
            var geometry = UiScrollBody.Measure(body, RoutedHeight,
                UiScrollbarReservation.WhenNeeded);
            using (scroll.Scope(body, geometry.View))
            {
                DrawRoutedRows(body, tab, scroll, geometry.View.width, scroll.Position.y);
            }
        }

        static void DrawRoutedRows(Rect body, SidebarTab tab, SmoothScroll scroll,
                                   float width, float scrollY)
        {
            bool clipped = scroll != null;
            float top = scrollY - GhostH;
            float bottom = scrollY + body.height + GhostH;
            float y = 0f;
            foreach (var info in Layout.Routed)
            {
                if (clipped && (y + GhostH <= top || y >= bottom))
                {
                    y += GhostH;
                    continue;
                }

                float rowY = clipped ? y : body.y + y;
                float rowX = clipped ? 0f : body.x;
                var row = new Row
                {
                    Session = info.Name,
                    Ghost = true,
                    Line = new Rect(rowX, rowY, width, GhostH),
                    Text = new Rect(rowX + CellX + ArrowW + UiTheme.GapXS,
                        rowY + 1f, width - CellX - ArrowW - UiTheme.GapXS - Pad, NameH),
                    Face = Rect.zero,
                };
                DrawRoutedRow(row, info, tab);
                // Drawing happens in the scroll-local group, while clicks happen after it has
                // ended. Clip screen-space hit rectangles to the viewport so partial rows
                // cannot intercept clicks in the tree or chrome outside the upper pane.
                if (!clipped || (y + GhostH > scrollY && y < scrollY + body.height))
                {
                    float hitTop = Mathf.Max(0f, y - scrollY);
                    float hitBottom = Mathf.Min(body.height, y + GhostH - scrollY);
                    Layout.ViewRows.Add(new Row
                    {
                        Session = row.Session,
                        Ghost = true,
                        Line = clipped
                            ? new Rect(body.x, body.y + hitTop, width, hitBottom - hitTop)
                            : row.Line,
                        Text = row.Text,
                        Face = Rect.zero,
                    });
                }
                y += GhostH;
            }
        }

        static void DrawRoutedRow(Row row, SessionInfo info, SidebarTab tab)
        {
            bool current = row.Session == TerminalWindow.CurrentName;
            RowChrome.Hover(row.Line, current, true, RowHoverPolicy.OverlayAware);

            var text = row.Text;
            var act = RoutedAction(info);
            if (act != RowAct.None)
            {
                float d = Mathf.Min(GhostMarkW, text.height);
                if (Event.current.type == EventType.Repaint)
                {
                    var was = GUI.color;
                    GUI.color = UiTheme.Off;
                    GUI.DrawTexture(new Rect(text.x, text.y + (text.height - d) / 2f, d, d),
                        RowActions.Tex(act));
                    GUI.color = was;
                }
                text.x += d + UiTheme.GapXS;
                text.width -= d + UiTheme.GapXS;
            }

            Text.Font = GameFont.Small;
            bool preview = tab == SidebarTab.Files
                ? FilesView.IsViewerSession(row.Session)
                : tab == SidebarTab.Git && GitView.IsViewerSession(row.Session);
            bool locked = tab == SidebarTab.Files
                ? FilesView.IsViewerLocked(row.Session)
                : tab == SidebarTab.Git && GitView.IsViewerLocked(row.Session);
            SidebarRowRenderer.DrawGhostLabel(text, info, row.Session, false, GhostMarkW,
                preview && !locked);
            if (preview)
                TooltipHandler.TipRegion(row.Line, locked
                    ? "Pinned preview tab. Click to show it. Middle-click to close."
                    : "Preview tab. Double-click its header to keep it open. Middle-click to close.");
            GUI.color = Color.white;
        }

        static readonly MouseClickSequence RoutedClicks = new MouseClickSequence();
        static string _routedClickSession;

        public static bool ClickRouted()
        {
            if (!ColonistBarStrip.Interactive) return false;
            var e = Event.current;
            if (e.rawType != EventType.MouseDown || (e.button != 0 && e.button != 1 && e.button != 2))
                return false;
            foreach (var row in Layout.ViewRows)
            {
                if (!ColonistBarStrip.MouseOver(row.Line)) continue;
                if (e.button == 2)
                {
                    RoutedClicks.Reset();
                    _routedClickSession = null;
                    // Only view-owned previews are closable here; editor rows own live work.
                    if (CurrentTab == SidebarTab.Files) FilesView.CloseViewerTab(row.Session);
                    else if (CurrentTab == SidebarTab.Git) GitView.CloseViewerTab(row.Session);
                }
                else if (e.button == 1)
                {
                    RoutedClicks.Reset();
                    _routedClickSession = null;
                    if (!(CurrentTab == SidebarTab.Files &&
                          SnapshotGet(row.Session) == null &&
                          FilesView.IsNativeViewerHeader(row.Session)))
                        RowMenu(row.Session);
                }
                else
                {
                    int clickCount;
                    if (_routedClickSession == row.Session)
                        clickCount = RoutedClicks.Observe(e, Time.realtimeSinceStartup);
                    else
                    {
                        RoutedClicks.Reset();
                        _routedClickSession = row.Session;
                        clickCount = RoutedClicks.Observe(e, Time.realtimeSinceStartup);
                    }

                    bool locked = clickCount >= 2 || e.clickCount >= 2;
                    if (locked && LockRouted(row.Session))
                    {
                        RoutedClicks.Reset();
                        SessionSelectable.Current = row.Session;
                        OpenRouted(row.Session);
                        e.Use();
                        return true;
                    }

                    SessionSelectable.Current = row.Session;
                    var info = SnapshotGet(row.Session);
                    if (info != null && info.Gone && !ColonistBarStrip.Drawing)
                        SessionHub.Instance.SessionStore.Start(row.Session);
                    else OpenRouted(row.Session);
                }
                e.Use();
                return true;
            }
            return false;
        }

        static bool LockRouted(string session)
        {
            switch (CurrentTab)
            {
                case SidebarTab.Files: return FilesView.LockViewer(session);
                case SidebarTab.Git: return GitView.LockViewer(session);
                default: return false;
            }
        }

        static void OpenRouted(string session)
        {
            if (CurrentTab == SidebarTab.Files && FilesView.OpenViewerHeader(session)) return;
            TerminalWindow.Open(session);
        }


        static void DrawRows()
        {
            var hub = SessionHub.Instance;
            foreach (var row in Layout.Rows)
            {
                if (SkipAgentPaint(row.Line)) continue;
                var info = row.Session == null ? null : hub.Get(row.Session);
                var state = info?.State ?? AgentState.Down;
                var tint = TerminalWindow.StateColor(state);

                if (row.Worker)
                {
                    DrawWorkerRow(row, info);
                    continue;
                }

                if (row.Ghost)
                {
                    DrawGhostRow(row, info);
                    continue;
                }

                DrawAgentRow(row, info, state, tint);
            }
        }

        // Draw: ghost rows emit their action mark, label and click target in that order.
        static void DrawGhostRow(Row row, SessionInfo info)
        {
            Text.Font = GameFont.Small;
            var text = row.Text;

            var act = RowActions.Of(info);
            if (act != RowAct.None)
            {
                float d = Mathf.Min(GhostMarkW, text.height);
                if (Event.current.type == EventType.Repaint)
                {
                    var was = GUI.color;
                    GUI.color = UiTheme.Off;
                    GUI.DrawTexture(
                        new Rect(text.x, text.y + (text.height - d) / 2f, d, d),
                        RowActions.Tex(act));
                    GUI.color = was;
                }
                text.x += d + UiTheme.GapXS;
                text.width -= d + UiTheme.GapXS;
            }

            SidebarRowRenderer.DrawGhostLabel(text, info, row.Session, true, GhostMarkW);
            GUI.color = Color.white;
            Click(row, info);
        }

        // Worker children are intentionally quieter than agents: the task owns their identity,
        // and the parent row already supplies the normal portrait/state presentation.
        static void DrawWorkerRow(Row row, SessionInfo info)
        {
            RowChrome.Hover(row.Line, row.Session == TerminalWindow.CurrentName, true,
                RowHoverPolicy.OverlayAware);

            float d = Mathf.Min(GhostMarkW, row.Text.height);
            Rect mark = new Rect(row.Text.x - TextGap - GhostMarkW,
                row.Text.y + (row.Text.height - d) / 2f, d, d);
            if (Event.current.type == EventType.Repaint)
            {
                var was = GUI.color;
                GUI.color = UiTheme.Off;
                GUI.DrawTexture(mark, Icons.Agents);
                GUI.color = was;
            }
            TooltipHandler.TipRegion(mark, "Task worker session");

            Text.Font = GameFont.Tiny;
            AgentState state = info?.State ?? AgentState.Down;
            Color tint = info == null ? UiTheme.Dim : TerminalWindow.StateColor(state);
            string ago = state == AgentState.Down ? "" : SidebarRowRenderer.Ago(info);
            float ageW = ago.Length == 0 ? 0f : UiTheme.Wide(ago);
            float nameW = Mathf.Max(0f, row.Text.width -
                (ageW > 0f ? ageW + UiTheme.GapXS : 0f));
            var name = new Rect(row.Text.x, row.Text.y, nameW, row.Text.height);
            GUI.color = tint;
            UiText.RowLabel(name, info?.Name ?? row.Session);
            if (ageW > 0f)
            {
                var time = new Rect(row.Text.xMax - ageW, row.Text.y, ageW, row.Text.height);
                UiText.RowLabel(time, ago, TextAnchor.MiddleRight);
                string stateName = SidebarRowRenderer.StateName(state);
                TooltipHandler.TipRegion(time, $"{stateName} for {ago}");
            }
            GUI.color = Color.white;
            if (info != null && !string.IsNullOrEmpty(info.TaskId))
                TooltipHandler.TipRegion(row.Line,
                    $"Worker task {info.TaskId}\nParent: {info.Parent}");
            Click(row, info);
        }

        // Draw the status badge at the portrait's right edge, vertically aligned with the
        // lower text band; the dark ring keeps it legible over hair and clothing.
        static void DrawStateBadge(Rect face, Rect text, AgentState state)
        {
            if (CompactView) return;
            if (face.width <= 0f) return;
            if (Event.current.type != EventType.Repaint) return;

            float d = Mathf.Max(BadgeMin,
                Mathf.Round(face.width * BadgeShare));
            var portrait = Patch_SidebarPortraitDraw.PortraitRect(face);
            var center = new Vector2(portrait.xMax - d / 2f - BadgeInset,
                text.y + NameH + SubH * 1.5f + 3f);

            GUI.color = UiTheme.ViewBg;
            GUI.DrawTexture(Icons.DotBox(center, d + BadgeRing * 2f), Icons.Dot);
            var stateColor = TerminalWindow.StateColor(state);
            GUI.color = new Color(stateColor.r, stateColor.g, stateColor.b, BadgeAlpha);
            GUI.DrawTexture(Icons.DotBox(center, d), Icons.Dot);
            GUI.color = Color.white;
        }

        // Draw: agent rows keep the badge, name/time, summary and click target ordered.
        static void DrawAgentRow(Row row, SessionInfo info, AgentState state, Color tint)
        {
            DrawStateBadge(row.Face, row.Text, state);
            SidebarRowRenderer.DrawAgentText(row.Text,
                row.Session ?? row.Pawn?.LabelShort ?? "?", info, state, tint,
                NameH, SubH, BellW);
            GUI.color = Color.white;
            Click(row, info);
        }

        public static void DrawFront()
        {
            if (!Drawing) return;
            try
            {
                if (Interaction.AgentScrollOpen)
                {
                    DrawRows();
                    Patch_SidebarPortraitDraw.DrawDeferredSelection();
                    EndAgentScroll();
                    DrawAgentShadow();
                    Absorb();
                    return;
                }

                DrawRows();

                Absorb();
            }
            finally
            {
                EndAgentScroll();
                // This cleanup must also happen when label drawing throws.
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                Drawing = false;
            }
        }

    }
}
