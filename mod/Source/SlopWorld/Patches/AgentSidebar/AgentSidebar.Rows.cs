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
            if (current) Slab.Fill(row.Line, SlopWidgets.RowOn);
            else SlopWidgets.HoverRow(row.Line);

            var text = row.Text;
            var act = RoutedAction(info);
            if (act != RowAct.None)
            {
                float d = Mathf.Min(GhostMarkW, text.height);
                GUI.color = SlopWidgets.Off;
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
                    if (info != null && info.Gone) SessionHub.Instance.Start(row.Session);
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
                GUI.color = SlopWidgets.Off;
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
