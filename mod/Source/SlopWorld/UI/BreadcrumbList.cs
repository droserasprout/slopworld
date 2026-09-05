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
                                Action<bool> onInstructionsChanged = null)
        {
            var all = SessionHub.Instance.Library
                .Where(s => s.Kind == LibraryItemKind.Breadcrumb)
                .OrderBy(s => s.Name, System.StringComparer.OrdinalIgnoreCase)
                .Select(s => new Entry { Name = s.Name, Text = s.Text })
                .ToList();
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
                bool was = b.Instructions ? b.On : forced || chosen.Contains(b.Name);
                string tip = b.Instructions
                    ? "Used when this agent mounts SLOPWORLD.md and discovery is enabled in " +
                      "Settings > Integrations > Instructions."
                    : (b.Text ?? "").Replace("\n", " ");
                choices.Add(new UiChoice<Entry>
                {
                    Value = b,
                    Label = b.Name,
                    Tip = tip,
                    On = was,
                    Locked = forced,
                    Changed = next =>
                    {
                        if (b.Instructions)
                            onInstructionsChanged?.Invoke(next);
                        else if (next) chosen.Add(b.Name);
                        else chosen.Remove(b.Name);
                    },
                });
            }

            UiChoiceList<Entry>.Draw(outer, choices, scroll,
                "No breadcrumbs yet. Add one from Library.");
        }
    }
}
