using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopWorld
{
    // Shared project/agent preset checkboxes group inherited and local contributions; the implicit global
    // preset is settings-only.
    public static class PresetList
    {
        // The pitch of a row here, off the font like every other height in this mod.
        public static float RowH => UiTheme.RowH;

        // Ticked and refused: what a preset is handed anyway, by its command or its project.
        // Drawn rather than hidden - "why is ~/.claude bound" is the question this answers.
        public static void Draw(Rect outer, List<string> chosen, SmoothScroll scroll,
                                ICollection<string> implied = null,
                                IEnumerable<PresetInfo> catalog = null, ICollection<string> projectPresets = null,
                                string additionsLabel = "Added by this agent")
        {
            // The machine-wide base is implicit for every sandbox, so it is edited on the
            // Settings > Sandbox page rather than offered as a project checkbox.
            var allPresets = (catalog ?? SessionHub.Instance.Presets).ToList();
            var roots = new List<string>(chosen);
            if (implied != null) roots.AddRange(implied);
            var required = RequiredBy(roots, allPresets);
            var known = new HashSet<string>(allPresets.Select(p => p.Name));
            // Persisted references can outlive catalog definitions. Keep those rows visible
            // so direct references can be removed and inherited ones can be traced upstream.
            foreach (var name in roots.Concat(required).Distinct())
                if (name != "global" && !known.Contains(name))
                    allPresets.Add(new PresetInfo { Name = name, Source = "missing" });
            var presets = allPresets.Where(p => p.Name != "global").ToList();

            if (presets.Count == 0)
            {
                UiChoiceList<PresetInfo>.Draw(outer,
                    new List<UiChoice<PresetInfo>>(), scroll,
                    "No optional presets are available.");
                return;
            }

            presets = presets
                .OrderBy(p => p.Source == "system" ? 0 : 1)
                .ThenBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            var choices = new List<UiChoice<PresetInfo>>();
            foreach (var pr in presets)
            {
                bool inherited = implied != null && implied.Contains(pr.Name);
                bool dependency = required.Contains(pr.Name) && !chosen.Contains(pr.Name);
                if (inherited || dependency)
                {
                    choices.Add(new UiChoice<PresetInfo>
                    {
                        Value = pr,
                        Group = implied == null ? "Required by selected presets"
                            : projectPresets != null && projectPresets.Contains(pr.Name) ? "From project"
                            : inherited ? "From command" : "Required by selected presets",
                        Label = pr.Source == "missing" ? pr.Name + " (missing)" : pr.Name,
                        Tip = Tip(pr, true),
                        On = true,
                        Locked = true,
                        Warn = pr.IsEscape || pr.Source == "missing",
                    });
                    // An explicit selection may overlap with a project contribution. Keep its
                    // own row editable so removing it does not discard the inherited entry.
                    if (!chosen.Contains(pr.Name)) continue;
                }
                choices.Add(new UiChoice<PresetInfo>
                {
                    Value = pr,
                    Group = implied == null ? null : chosen.Contains(pr.Name) ? additionsLabel : "Available additions",
                    Label = pr.Source == "missing" ? pr.Name + " (missing)" : pr.Name,
                    Tip = Tip(pr, false),
                    On = chosen.Contains(pr.Name),
                    Warn = pr.IsEscape || pr.Source == "missing",
                    Changed = next =>
                    {
                        if (next) chosen.Add(pr.Name);
                        else chosen.Remove(pr.Name);
                    },
                });
            }
            choices = choices.OrderBy(c => c.Group == "From project" ? 0 : c.Group == "From command" ? 1
                : c.Group == "Required by selected presets" ? 2 : c.Group == "Available additions" ? 4 : 3).ToList();

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
            if (p.Source == "missing")
                return "This sandbox preset is unavailable and has no effect." +
                    (forced ? " Remove the reference from the project, command, or preset that requires it."
                        : " Uncheck it to remove this reference.");
            string gives = string.Join("\n", p.Gives.ToArray());
            string why = forced ? "\n\nRequired by another selected preset or inherited by this entry." : "";
            // First, not last: what it costs is read before what it gives, because by the time
            // the eye reaches a list of paths the decision has usually been made.
            string out_ = p.IsEscape ? $"Way out of the sandbox: {p.Escapes}.\n\n" : "";
            return $"{out_}{p.Description}\n\n{gives}{why}";
        }
    }
}
