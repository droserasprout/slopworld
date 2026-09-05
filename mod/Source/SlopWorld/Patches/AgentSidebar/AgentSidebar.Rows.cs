using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // AgentSidebar row rendering and routed-row interaction.
    public static partial class AgentSidebar
    {
        public static void DrawRouted(Rect body, SidebarTab tab)
        {
            Layout.ViewRows.Clear();
            float y = body.y;
            foreach (var info in RoutedFor(tab))
            {
                var row = new Row
                {
                    Session = info.Name,
                    Ghost = true,
                    Line = new Rect(body.x, y, body.width, GhostH),
                    Text = new Rect(body.x + CellX + ArrowW + 4f, y + 1f,
                        body.width - CellX - ArrowW - 4f - Pad, NameH),
                    Face = Rect.zero,
                };
                Layout.ViewRows.Add(row);
                DrawRoutedRow(row, info);
                y += GhostH;
            }
        }

        static void DrawRoutedRow(Row row, SessionInfo info)
        {
            bool current = row.Session == TerminalWindow.CurrentName;
            RowChrome.Hover(row.Line, current, true, RowHoverPolicy.OverlayAware);

            var text = row.Text;
            var act = RoutedAction(info);
            if (act != RowAct.None)
            {
                float d = Mathf.Min(GhostMarkW, text.height);
                GUI.color = UiWidgets.Off;
                GUI.DrawTexture(new Rect(text.x, text.y + (text.height - d) / 2f, d, d),
                    RowActions.Tex(act));
                text.x += d + 4f;
                text.width -= d + 4f;
            }

            Text.Font = GameFont.Small;
            SidebarRowRenderer.DrawGhostLabel(text, info, row.Session, false, GhostMarkW);
            GUI.color = Color.white;
        }

        public static bool ClickRouted()
        {
            if (!ColonistBarStrip.Interactive) return false;
            var e = Event.current;
            if (e.rawType != EventType.MouseDown || (e.button != 0 && e.button != 1))
                return false;
            foreach (var row in Layout.ViewRows)
            {
                if (!ColonistBarStrip.MouseOver(row.Line)) continue;
                if (e.button == 1)
                {
                    RowMenu(row.Session);
                }
                else
                {
                    SessionSelectable.Current = row.Session;
                    var info = SessionHub.Instance.Get(row.Session);
                    if (info != null && info.Gone && !ColonistBarStrip.Drawing)
                        SessionHub.Instance.Start(row.Session);
                    else TerminalWindow.Open(row.Session);
                }
                e.Use();
                return true;
            }
            return false;
        }


        static void DrawRows()
        {
            var hub = SessionHub.Instance;
            foreach (var row in Layout.Rows)
            {
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
                GUI.color = UiWidgets.Off;
                GUI.DrawTexture(
                    new Rect(text.x, text.y + (text.height - d) / 2f, d, d),
                    RowActions.Tex(act));
                text.x += d + 4f;
                text.width -= d + 4f;
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
            GUI.color = UiWidgets.Off;
            GUI.DrawTexture(mark, Icons.Agents);
            TooltipHandler.TipRegion(mark, "Task worker session");

            Text.Font = GameFont.Tiny;
            AgentState state = info?.State ?? AgentState.Down;
            Color tint = info == null ? UiWidgets.Dim : TerminalWindow.StateColor(state);
            string ago = state == AgentState.Down ? "" : SidebarRowRenderer.Ago(info);
            float ageW = ago.Length == 0 ? 0f : UiWidgets.Wide(ago);
            float nameW = Mathf.Max(0f, row.Text.width -
                (ageW > 0f ? ageW + UiWidgets.GapXS : 0f));
            var name = new Rect(row.Text.x, row.Text.y, nameW, row.Text.height);
            GUI.color = tint;
            UiWidgets.RowLabel(name, info?.Name ?? row.Session);
            if (ageW > 0f)
            {
                var time = new Rect(row.Text.xMax - ageW, row.Text.y, ageW, row.Text.height);
                UiWidgets.RowLabel(time, ago, TextAnchor.MiddleRight);
                string stateName = state == AgentState.Waiting
                    ? "waiting for input"
                    : state.ToString().ToLowerInvariant();
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

            float d = Mathf.Max(BadgeMin,
                Mathf.Round(face.width * BadgeShare));
            var portrait = Patch_SidebarPortraitDraw.PortraitRect(face);
            var center = new Vector2(portrait.xMax - d / 2f - BadgeInset,
                text.y + NameH + SubH * 1.5f + 3f);

            GUI.color = UiWidgets.ViewBg;
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
