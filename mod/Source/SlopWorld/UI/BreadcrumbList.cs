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
            Slab.Box(outer, SlopWidgets.Well, SlopWidgets.Edge);
            var pad = outer.ContractedBy(4f);
            if (all.Count == 0)
            {
                GUI.color = SlopWidgets.Dim;
                SlopWidgets.RowLabel(new Rect(pad.x, pad.y, pad.width, SlopWidgets.LineH),
                    "No breadcrumbs yet. Add one from Library.");
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
                bool forced = !b.Instructions && implied != null && implied.Contains(b.Name);
                bool was = b.Instructions ? b.On : forced || chosen.Contains(b.Name);
                string tip = b.Instructions
                    ? "Used when this agent mounts SLOPWORLD.md and discovery is enabled in " +
                      "Settings > Integrations > Instructions."
                    : (b.Text ?? "").Replace("\n", " ");
                bool on = SlopWidgets.Checkbox(cell, b.Name, was, tip, forced);
                if (on != was)
                {
                    if (b.Instructions)
                        onInstructionsChanged?.Invoke(on);
                    else if (on) chosen.Add(b.Name);
                    else chosen.Remove(b.Name);
                }
            }
            scroll.End();
        }
    }
}
