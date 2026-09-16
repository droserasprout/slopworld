using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Named prompt guidance is edited in the same library table, but attached here like
    // sandbox presets: a project supplies defaults and an agent may add to them. The agent
    // editor may also append the generated instructions entry as a separate setting.
    public static class BreadcrumbList
    {
        const string InstructionsName = "Instructions discovery";

        sealed class Entry
        {
            public string Name;
            public string Text;
            public bool Instructions;
            public bool On;
        }

        public static void Draw(Rect outer, List<string> chosen, SmoothScroll scroll,
                                ICollection<string> implied = null,
                                string instructionsText = null, bool instructionsOn = false,
                                Action<bool> onInstructionsChanged = null, bool instructionsLocked = false,
                                bool breadcrumbsLocked = false, IEnumerable<LibraryItemInfo> catalog = null,
                                string additionsLabel = "Added by this agent")
        {
            var all = (catalog ?? SessionHub.Instance.Library)
                .Where(s => s.Kind == LibraryItemKind.Breadcrumb)
                .OrderBy(s => s.Name, System.StringComparer.OrdinalIgnoreCase)
                .Select(s => new Entry { Name = s.Name, Text = s.Text })
                .ToList();
            foreach (var name in chosen.Concat(implied ?? new List<string>()).Distinct())
                if (!all.Any(b => b.Name == name))
                    all.Add(new Entry { Name = name, Text = "Missing breadcrumb" });
            if (instructionsText != null)
            {
                all.Add(new Entry
                {
                    Name = InstructionsName,
                    Text = instructionsText,
                    Instructions = true,
                    On = instructionsOn,
                });
                all = all.OrderBy(s => s.Name, System.StringComparer.OrdinalIgnoreCase).ToList();
            }
            if (all.Count == 0)
            {
                UiChoiceList<Entry>.Draw(outer, new List<UiChoice<Entry>>(), scroll,
                    "No breadcrumbs yet. Add one from Library.");
                return;
            }

            var choices = new List<UiChoice<Entry>>();
            foreach (var b in all)
            {
                bool forced = !b.Instructions && implied != null && implied.Contains(b.Name);
                if (forced)
                {
                    choices.Add(new UiChoice<Entry>
                    {
                        Value = b,
                        Group = "From project",
                        Label = b.Name,
                        Tip = (b.Text ?? "") + "\nChange this contribution in the project editor.",
                        On = true,
                        Locked = true,
                    });
                    if (!chosen.Contains(b.Name)) continue;
                }
                bool was = b.Instructions ? b.On : chosen.Contains(b.Name);
                string tip = b.Instructions
                    ? "Used when this agent mounts SLOPWORLD.md and discovery is enabled in " +
                      "Settings > Agents > Instructions. Requires both gates in Settings > " +
                      "General > Experimental."
                    : (b.Text ?? "").Replace("\n", " ");
                if (!b.Instructions && breadcrumbsLocked)
                    tip += " Requires breadcrumbs in Settings > General > Experimental.";
                choices.Add(new UiChoice<Entry>
                {
                    Value = b,
                    Group = b.Instructions ? "Instructions" : implied == null ? null
                        : chosen.Contains(b.Name) ? additionsLabel : "Available additions",
                    Label = b.Name,
                    Tip = tip,
                    On = was,
                    Locked = breadcrumbsLocked || (b.Instructions && instructionsLocked),
                    Changed = next =>
                    {
                        if (b.Instructions)
                            onInstructionsChanged?.Invoke(next);
                        else if (next) chosen.Add(b.Name);
                        else chosen.Remove(b.Name);
                    },
                });
            }

            var projectOrder = implied?.ToList() ?? new List<string>();
            choices = choices.OrderBy(c => c.Group == "From project" ? 0 : c.Group == "Instructions" ? 2
                : c.Group == "Available additions" ? 3 : 1)
                .ThenBy(c => c.Group == "From project" ? projectOrder.IndexOf(c.Value.Name)
                    : c.On && !c.Value.Instructions ? chosen.IndexOf(c.Value.Name) : int.MaxValue).ToList();
            UiChoiceList<Entry>.Draw(outer, choices, scroll,
                "No breadcrumbs yet. Add one from Library.");
        }
    }
}
