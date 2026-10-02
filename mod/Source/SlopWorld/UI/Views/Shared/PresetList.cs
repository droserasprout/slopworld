using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopWorld
{
    // Shared preset list for commands and agents. It shows inherited presets and extra selections.
    // Edit the global preset in Settings, not in this list.
    public static class PresetList
    {
        // Use the shared row height for each preset.
        public static float RowH => UiTheme.RowH;

        // Show inherited or required presets as checked and locked. This explains why the sandbox mounts their paths.
        public static void Draw(Rect outer, List<string> chosen, SmoothScroll scroll,
                                ICollection<string> implied = null,
                                IEnumerable<PresetInfo> catalog = null,
                                string additionsLabel = "Added by this agent")
        {
            // The global preset applies to every sandbox. Edit it in Settings > Sandbox, not in this list.
            var allPresets = (catalog ?? SessionHub.Instance.Presets).ToList();
            var roots = new List<string>(chosen);
            if (implied != null) roots.AddRange(implied);
            var required = RequiredBy(roots, allPresets);
            var known = new HashSet<string>(allPresets.Select(p => p.Name));
            // Keep unavailable presets visible so users can remove direct references or find which preset requires them.
            foreach (var name in roots.Concat(required).Distinct())
                if (name != "global" && !known.Contains(name))
                    allPresets.Add(new PresetInfo { Name = name, Source = "missing" });
            var presets = allPresets.Where(p => p.Name != "global").ToList();

            if (presets.Count == 0)
            {
                UiChoiceList.Draw(outer,
                    new List<UiChoice>(), scroll,
                    "No optional presets are available.");
                return;
            }

            presets = presets
                .OrderBy(p => p.Source == "system" ? 0 : 1)
                .ThenBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            var choices = new List<UiChoice>();
            foreach (var pr in presets)
            {
                bool inherited = implied != null && implied.Contains(pr.Name);
                bool dependency = required.Contains(pr.Name) && !chosen.Contains(pr.Name);
                if (inherited || dependency)
                {
                    choices.Add(new UiChoice
                    {
                        Group = implied == null ? "Required by selected presets"
                            : inherited ? "From command" : "Required by selected presets",
                        Label = pr.Source == "missing" ? pr.Name + " (missing)" : pr.Name,
                        Tip = Tip(pr, true),
                        On = true,
                        Locked = true,
                        Warn = pr.IsEscape || pr.Source == "missing",
                    });
                    // A preset can be selected directly and inherited from the project. Keep the direct row editable so users can remove it separately.
                    if (!chosen.Contains(pr.Name)) continue;
                }
                choices.Add(new UiChoice
                {
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

            UiChoiceList.Draw(outer, choices, scroll,
                "No optional presets are available.");
        }

        static HashSet<string> RequiredBy(IEnumerable<string> chosen, List<PresetInfo> presets)
        {
            var required = new HashSet<string>();
            var todo = new Queue<string>(chosen);
            while (todo.Count > 0)
            {
                // Dequeue each name before searching. This changes the queue once per preset, not once per candidate.
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
            // Show host access first so users see its cost before they read preset details.
            string out_ = p.IsEscape ? $"Host access: {p.Escapes}.\n\n" : "";
            return $"{out_}{p.Description}\n\n{gives}{why}";
        }
    }
}
