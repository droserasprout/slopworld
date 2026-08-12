using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Named prompt guidance is edited in the same shortcut table, but attached here like
    // sandbox presets: a project supplies defaults and an agent may add to them.
    public static class BreadcrumbList
    {
        public static void Draw(Rect outer, List<string> chosen, SmoothScroll scroll,
                                ICollection<string> implied = null)
        {
            var all = SessionHub.Instance.Shortcuts
                .Where(s => s.Kind == ShortcutKind.Breadcrumb)
                .OrderBy(s => s.Name, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            Slab.Box(outer, SlopWidgets.Well, SlopWidgets.Edge);
            var pad = outer.ContractedBy(4f);
            if (all.Count == 0)
            {
                GUI.color = SlopWidgets.Dim;
                SlopWidgets.RowLabel(new Rect(pad.x, pad.y, pad.width, SlopWidgets.LineH),
                    "No breadcrumbs yet. Add one from Shortcuts.");
                GUI.color = Color.white;
                return;
            }

            var inner = new Rect(0f, 0f, pad.width - SlopWidgets.ScrollbarW,
                all.Count * SlopWidgets.RowH);
            scroll.Begin(pad, inner);
            float y = 0f;
            foreach (var b in all)
            {
                var cell = new Rect(SlopWidgets.GapS, y, inner.width - SlopWidgets.GapS,
                    SlopWidgets.RowH);
                y += SlopWidgets.RowH;
                bool forced = implied != null && implied.Contains(b.Name);
                bool was = forced || chosen.Contains(b.Name);
                bool on = SlopWidgets.Checkbox(cell, b.Name, was,
                    b.Text.Replace("\n", " "), forced);
                if (on != was)
                {
                    if (on) chosen.Add(b.Name);
                    else chosen.Remove(b.Name);
                }
            }
            scroll.End();
        }
    }
}
