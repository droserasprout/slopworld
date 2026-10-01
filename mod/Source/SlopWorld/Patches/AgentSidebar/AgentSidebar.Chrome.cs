using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Fixed sidebar chrome: tabs, add strip, actions, headings, and overflow shadow.
    public static partial class AgentSidebar
    {
        // Fade the scrolling body into the shadow above the fixed add strip.
        static void DrawAgentShadow()
        {
            if (Layout.AgentContentH <= Body.height) return;

            const int Steps = 4;
            const float Height = 12f;
            float band = Height / Steps;
            var add = AddBar;
            for (int i = 0; i < Steps; i++)
            {
                float strength = 0.12f + 0.12f * i;
                Slab.Fill(new Rect(add.x, add.y - Height + i * band, Width, band),
                    UiTheme.Fade(UiTheme.Scrim, strength));
            }
        }

        static void DrawAdd()
        {
            var r = AddBar;
            // Keep the button lit while its menu is stacked over the pane. The menu owns the
            // press, but the pointer is still visibly over the control that opened it.
            bool over = ColonistBarStrip.MouseOver(AddHitBar);

            Slab.Fill(r, over ? UiTheme.Hover : UiTheme.Panel);
            TooltipHandler.TipRegion(r,
                "Add projects, agents, workers, tasks, library items, presets, " +
                "commands, and host shells.");
            Slab.Hairline(new Rect(r.x, r.y, r.width, 1f), UiTheme.Edge);

            float d = AddIcon;
            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = over ? Color.white : UiTheme.Lead;
                GUI.DrawTexture(
                    new Rect(r.center.x - d / 2f, r.center.y - d / 2f, d, d), Icons.Add);
                GUI.color = Color.white;
            }
        }

        static void Tabs()
        {
            var strip = new Rect(Panel.x, 0f, Width, TabH);
            Slab.Hairline(new Rect(Panel.x + CellX, TabH - 1f, Width - CellX * 2f, 1f),
                UiTheme.Edge);

            float y = (TabRowH - TabIcon) / 2f;

            const float Gap = 3f;
            float x = Panel.x + CellX;
            foreach (var definition in TabRegistry.Definitions)
            {
                var tab = definition.Tab;
                Tab(new Rect(x, y, TabIcon, TabIcon), IconFor(definition),
                    CurrentTab == tab, definition.Tooltip,
                    () => Show(tab));
                x += TabIcon + Gap;
            }

            FilterButton();

            if (HasActions)
                Actions(new Rect(FilterRect.x, TabRowH + y, TabIcon, TabIcon));

            if (ColonistBarStrip.MouseOver(strip) && Event.current.rawType == EventType.MouseDown
                && ColonistBarStrip.Interactive
                && Event.current.mousePosition.x < Panel.x + Width - GripW)
                Event.current.Use();
        }

        static Texture2D IconFor(SidebarTabDefinition definition)
        {
            switch (definition.IconKey)
            {
                case "files": return Icons.Files;
                case "search": return Icons.Search;
                case "git": return Icons.Git;
                case "tasks": return Icons.Tasks;
                case "library": return Icons.Library;
                default: return Icons.Agents;
            }
        }

        // Each definition owns the second-row controls for its view.
        static void Actions(Rect r)
        {
            CurrentDefinition.DrawActions(new SidebarTabActionContext(
                r.x, r.y, r.width, r.height));
        }

        static bool AllFolded()
        {
            return CurrentDefinition.AllFolded();
        }

        static void ToggleAllFolds()
        {
            SetAllFolds(!AllFolded());
        }

        // This rect puts the menu below the filter button for pointer and keyboard actions.
        // Its fixed position also keeps a menu in place when each selection opens it again.
        static Rect FilterRect =>
            new Rect(Panel.x + Width - CellX - TabIcon,
                (TabRowH - TabIcon) / 2f, TabIcon, TabIcon);

        static void FilterButton()
        {
            Tab(FilterRect, Icons.Filter, Filtering,
                Filtering
                    ? $"Showing {FilterLabel}. Click to tick another, or all of them."
                    : "Every project. Click to show only some of them.",
                OpenFilterMenu);
        }

        // Ticks, not a pick: a tick closes the menu the way every option in one does, and opens it
        // again where it was. Therefore, several can be set without hunting the button back down
        // between them.

        static void Tab(Rect r, Texture2D icon, bool on, string tip, System.Action go)
        {
            TooltipHandler.TipRegion(r, tip);
            if (UiLayout.IconButton(r, icon, on ? UiTheme.Lead : UiTheme.Off)
                && ColonistBarStrip.Interactive)
                go();

            // The selected tab is marked by the same blue signal used for the current row,
            // at the foot of whichever of the strip's rows the button sits in.
            if (on)
                Slab.Fill(new Rect(r.x, (r.y < TabRowH ? TabRowH : TabH) - 2f, r.width, 2f),
                    UiTheme.Accent);
        }

        static void DrawHead(Head head)
        {
            using (WidgetState.Save())
            {
                var r = head.Rect;
                RowChrome.Hover(r, false, true, RowHoverPolicy.OverlayAware);

                GUI.color = UiTheme.Faint;
                var arrow = new Rect(CellX, r.y + (HeadH - ArrowW) / 2f, ArrowW, ArrowW);
                if (Event.current.type == EventType.Repaint)
                    GUI.DrawTexture(arrow, head.Folded ? TexButton.Reveal : TexButton.Collapse);

                Text.Font = GameFont.Tiny;

                float lx = arrow.xMax + UiTheme.GapXS;
                string count = head.Count;
                float countW = UiTheme.Wide(count);
                var countRect = new Rect(r.xMax - CellX - countW, r.y, countW, HeadH);
                GUI.color = UiTheme.Faint;
                UiText.RowLabel(countRect, count, TextAnchor.MiddleRight);

                var p = SessionHub.Instance.Project(head.Label);

                Text.Font = GameFont.Small;
                GUI.color = UiTheme.Faint;
                var label = new Rect(lx, r.y, Mathf.Max(0f, countRect.x - Pad - lx), HeadH);
                UiText.RowLabel(label, head.Label);

                Slab.Hairline(new Rect(CellX, r.yMax - 1f, r.width - CellX * 2f, 1f),
                    UiTheme.Edge);

                TooltipHandler.TipRegion(r, p == null
                    ? "Agents whose project has gone, and anyone here who is not an agent.\n\n" +
                      "Click to fold."
                    : $"{p.Dir}\n({ProjectsView.Summary(p)})\n\n" +
                      "Click to fold, right-click for the project.");
            }
        }

    }
}
