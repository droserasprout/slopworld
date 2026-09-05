using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopWorld
{
    // Shared project/agent preset checkboxes use one uncategorized column; the implicit global
    // preset is settings-only.
    public static class PresetList
    {
        // The pitch of a row here, off the font like every other height in this mod.
        public static float RowH => UiWidgets.RowH;

        // Ticked and refused: what a preset is handed anyway, by its command or its project.
        // Drawn rather than hidden - "why is ~/.claude bound" is the question this answers.
        public static void Draw(Rect outer, List<string> chosen, SmoothScroll scroll,
                                ICollection<string> implied = null)
        {
            // The machine-wide base is implicit for every sandbox, so it is edited on the
            // Settings > Sandbox page rather than offered as a project checkbox.
            var allPresets = SessionHub.Instance.Presets;
            var presets = allPresets
                .Where(p => p.Name != "global")
                .ToList();
            if (allPresets.Count == 0)
            {
                UiChoiceList<PresetInfo>.Draw(outer,
                    new List<UiChoice<PresetInfo>>(), scroll,
                    "The daemon has not sent its preset list yet.");
                return;
            }

            if (presets.Count == 0)
            {
                UiChoiceList<PresetInfo>.Draw(outer,
                    new List<UiChoice<PresetInfo>>(), scroll,
                    "No optional presets are available.");
                return;
            }

            presets = presets
                .OrderBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            var roots = new List<string>(chosen);
            if (implied != null) roots.AddRange(implied);
            var required = RequiredBy(roots, presets);
            var choices = new List<UiChoice<PresetInfo>>();
            foreach (var pr in presets)
            {
                bool forced = (implied != null && implied.Contains(pr.Name)) ||
                              (required.Contains(pr.Name) && !chosen.Contains(pr.Name));
                bool was = forced || chosen.Contains(pr.Name);
                choices.Add(new UiChoice<PresetInfo>
                {
                    Value = pr,
                    Label = pr.Name,
                    Tip = Tip(pr, forced),
                    On = was,
                    Locked = forced,
                    Warn = pr.IsEscape,
                    Changed = next =>
                    {
                        if (next) chosen.Add(pr.Name);
                        else chosen.Remove(pr.Name);
                    },
                });
            }

            UiChoiceList<PresetInfo>.Draw(outer, choices, scroll,
                "No optional presets are available.");
        }

        static HashSet<string> RequiredBy(IEnumerable<string> chosen, List<PresetInfo> presets)
        {
            var required = new HashSet<string>();
            var todo = new Queue<string>(chosen);
            while (todo.Count > 0)
            {
                // `FirstOrDefault` calls its predicate once per row: take the work item
                // first, rather than consuming the queue once per candidate.
                string wanted = todo.Dequeue();
                var p = presets.FirstOrDefault(x => x.Name == wanted);
                if (p == null) continue;
                foreach (var name in p.Requires)
                    if (required.Add(name)) todo.Enqueue(name);
            }
            return required;
        }

        static string Tip(PresetInfo p, bool forced)
        {
            string gives = string.Join("\n", p.Gives.ToArray());
            string why = forced ? "\n\nRequired by another selected preset or inherited by this entry." : "";
            // First, not last: what it costs is read before what it gives, because by the time
            // the eye reaches a list of paths the decision has usually been made.
            string out_ = p.IsEscape ? $"Way out of the sandbox: {p.Escapes}.\n\n" : "";
            return $"{out_}{p.Description}\n\n{gives}{why}";
        }
    }
}
