using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The project headings and their small status notes are shared by the file and git
    // trees. Keeping them here makes both views use the same filter, spacing and typography.
    static class ViewChrome
    {
        const float Indent = 11f;
        const float Pad = SlopWidgets.GapS;
        const float CellX = SlopWidgets.GapS;

        static float RowH => SlopWidgets.TinyRowH;

        public static void Empty(Rect body)
        {
            var r = new Rect(CellX, body.y + Pad, body.width - CellX * 2f, RowH * 3f);
            GUI.color = SlopWidgets.Faint;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(r, SessionHub.Instance.Online
                ? "No project has a directory yet."
                : $"daemon {SessionHub.Instance.Status}");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        public static List<string> Projects()
        {
            var names = new List<string>();
            foreach (var p in SessionHub.Instance.Projects)
                if (!string.IsNullOrEmpty(p.Dir) && AgentSidebar.Passes(p.Name))
                    names.Add(p.Name);
            names.Sort(System.StringComparer.Ordinal);
            return names;
        }

        public static float Note(float width, float y, int depth, string text, Color color)
        {
            float x = CellX + depth * Indent;
            GUI.color = color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            var r = new Rect(x, y, width - x - Pad, RowH);
            SlopWidgets.RowLabel(r, text);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            return y + RowH;
        }
    }
}
