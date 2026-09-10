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
                    UiWidgets.Fade(UiWidgets.Scrim, strength));
            }
        }

        static void DrawAdd()
        {
            var r = AddBar;
            // Keep the button lit while its menu is stacked over the pane. The menu owns the
            // press, but the pointer is still visibly over the control that opened it.
            bool over = ColonistBarStrip.MouseOver(AddHitBar);

            Slab.Fill(r, over ? UiWidgets.Hover : UiWidgets.Panel);
            TooltipHandler.TipRegion(r,
                "Add a project, an agent, a library item, a sandbox preset, a command or a host shell");
            Slab.Hairline(new Rect(r.x, r.y, r.width, 1f), UiWidgets.Edge);

            float d = AddIcon;
            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = over ? Color.white : UiWidgets.Lead;
                GUI.DrawTexture(
                    new Rect(r.center.x - d / 2f, r.center.y - d / 2f, d, d), Icons.Add);
                GUI.color = Color.white;
            }
        }

        static void Tabs()
        {
            var strip = new Rect(Panel.x, 0f, Width, TabH);
            Slab.Hairline(new Rect(Panel.x + CellX, TabH - 1f, Width - CellX * 2f, 1f),
                UiWidgets.Edge);

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

        // Where the filter button is. One rect, so the menu comes out under the button
        // whether the button or the command palette opened it - and, being fixed rather
        // than taken from the mouse, so a menu that reopens itself after each tick reopens
        // in the place it was.
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

        // Ticks, not a pick: a tick closes the menu the way every option in one does, and
        // opens it again where it was, so several can be set without hunting the button
        // back down between them.

        static void Tab(Rect r, Texture2D icon, bool on, string tip, System.Action go)
        {
            TooltipHandler.TipRegion(r, tip);
            if (UiWidgets.IconButton(r, icon, on ? UiWidgets.Lead : UiWidgets.Off)
                && ColonistBarStrip.Interactive)
                go();

            // The selected tab is marked by the same blue signal used for the current row,
            // at the foot of whichever of the strip's rows the button sits in.
            if (on)
                Slab.Fill(new Rect(r.x, (r.y < TabRowH ? TabRowH : TabH) - 2f, r.width, 2f),
                    UiWidgets.Accent);
        }

        static void DrawHead(Head head)
        {
            var r = head.Rect;
            RowChrome.Hover(r, false, true, RowHoverPolicy.OverlayAware);

            GUI.color = UiWidgets.Faint;
            var arrow = new Rect(CellX, r.y + (HeadH - ArrowW) / 2f, ArrowW, ArrowW);
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(arrow, head.Folded ? TexButton.Reveal : TexButton.Collapse);

            Text.Font = GameFont.Tiny;

            float lx = arrow.xMax + UiWidgets.GapXS;
            string count = $"{head.Active}/{head.Total}";
            float countW = UiWidgets.Wide(count);
            var countRect = new Rect(r.xMax - CellX - countW, r.y, countW, HeadH);
            GUI.color = UiWidgets.Faint;
            UiWidgets.RowLabel(countRect, count, TextAnchor.MiddleRight);

            var p = SessionHub.Instance.Project(head.Label);

            Text.Font = GameFont.Small;
            GUI.color = UiWidgets.Faint;
            var label = new Rect(lx, r.y, Mathf.Max(0f, countRect.x - Pad - lx), HeadH);
            UiWidgets.RowLabel(label, head.Label);

            Slab.Hairline(new Rect(CellX, r.yMax - 1f, r.width - CellX * 2f, 1f),
                UiWidgets.Edge);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            TooltipHandler.TipRegion(r, p == null
                ? "Agents whose project has gone, and anyone here who is not an agent.\n\n" +
                  "Click to fold."
                : $"{p.Dir}\n({ProjectsView.Summary(p)})\n\n" +
                  "Click to fold, right-click for the project.");
        }

        // A soft edge marks the fixed add strip over the scrolling body without reserving a scrollbar gutter.
    }
}
