using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The sandbox preset checkboxes, drawn the same way wherever they are ticked: for a
    // project, and for one agent in it. One column in a box of its own, because the list is
    // as long as whatever is in the daemon's preset directory and a form that grows with it
    // is one where everything below moves the day a file is added.
    //
    // Grouped by the category each preset states rather than by a list held here: a category
    // this build has never heard of is a heading, which is the whole point of the field.
    public static class PresetList
    {
        // The pitch of a row here, off the font like every other height in this mod.
        public static float RowH => SlopWidgets.RowH;

        // Ticked and refused: what a preset is handed anyway, by its command or its project.
        // Drawn rather than hidden - "why is ~/.claude bound" is the question this answers.
        public static void Draw(Rect outer, List<string> chosen, SmoothScroll scroll,
                                ICollection<string> implied = null)
        {
            var presets = SessionHub.Instance.Presets;
            Slab.Box(outer, SlopWidgets.Well, SlopWidgets.Edge);
            var pad = outer.ContractedBy(4f);

            if (presets.Count == 0)
            {
                GUI.color = SlopWidgets.Dim;
                Widgets.Label(new Rect(pad.x, pad.y, pad.width, SlopWidgets.LineH),
                    "The daemon has not sent its preset list yet.");
                GUI.color = Color.white;
                return;
            }

            var groups = presets
                .OrderBy(p => Category(p), System.StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase)
                .GroupBy(Category)
                .ToList();

            float h = (presets.Count + groups.Count) * RowH;
            var inner = new Rect(0f, 0f, pad.width - 18f, h);

            scroll.Begin(pad, inner);
            float y = 0f;
            var roots = new List<string>(chosen);
            if (implied != null) roots.AddRange(implied);
            var required = RequiredBy(roots, presets);
            foreach (var g in groups)
            {
                SlopWidgets.SectionHeading(new Rect(0f, y, inner.width, RowH), g.Key);
                y += RowH;

                foreach (var pr in g)
                {
                    var cell = new Rect(SlopWidgets.GapS, y,
                        inner.width - SlopWidgets.GapS, RowH);
                    y += RowH;

                    bool forced = (implied != null && implied.Contains(pr.Name)) ||
                                  (required.Contains(pr.Name) && !chosen.Contains(pr.Name));
                    bool was = forced || chosen.Contains(pr.Name);
                    bool on = SlopWidgets.Checkbox(cell, pr.Name, was, Tip(pr, forced), forced,
                                                   pr.IsEscape);

                    if (on == was) continue;
                    if (on) chosen.Add(pr.Name);
                    else chosen.Remove(pr.Name);
                }
            }
            scroll.End();
        }

        static string Category(PresetInfo p) =>
            string.IsNullOrEmpty(p.Category) ? "other" : p.Category;

        static HashSet<string> RequiredBy(IEnumerable<string> chosen, List<PresetInfo> presets)
        {
            var required = new HashSet<string>();
            var todo = new Queue<string>(chosen);
            while (todo.Count > 0)
            {
                var p = presets.FirstOrDefault(x => x.Name == todo.Dequeue());
                if (p == null) continue;
                foreach (var name in p.Requires)
                    if (required.Add(name)) todo.Enqueue(name);
            }
            return required;
        }

        static string Tip(PresetInfo p, bool forced)
        {
            string gives = string.Join("\n", p.Gives.ToArray());
            string why = forced ? "\n\nRequired by another selected preset or the command this agent runs." : "";
            // First, not last: what it costs is read before what it gives, because by the time
            // the eye reaches a list of paths the decision has usually been made.
            string out_ = p.IsEscape ? $"Way out of the sandbox: {p.Escapes}.\n\n" : "";
            return $"{out_}{p.Description}\n\n{gives}{why}";
        }
    }
}
