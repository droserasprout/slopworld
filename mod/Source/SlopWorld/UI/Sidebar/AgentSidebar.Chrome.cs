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
            if (Layout.AgentContentH <= Body.height)
                return;

            const int Steps = 4;
            const float Height = 12f;
            float band = Height / Steps;
            var add = AddBar;
            for (int i = 0; i < Steps; i++)
            {
                float strength = 0.12f + 0.12f * i;
                Slab.Fill(
                    new Rect(add.x, add.y - Height + i * band, Width, band),
                    UiTheme.Fade(UiTheme.Scrim, strength)
                );
            }
        }

        static void DrawAdd()
        {
            var r = AddBar;
            // Keep the button lit while its menu is stacked over the pane. The menu owns the
            // press, but the pointer is still visibly over the control that opened it.
            bool over = ColonistBarStrip.MouseOver(AddHitBar);

            Slab.Fill(r, over ? UiTheme.Hover : UiTheme.Panel);
            TooltipHandler.TipRegion(
                r,
                "Add projects, agents, workers, tasks, library items, presets, "
                    + "commands, and host shells."
            );
            Slab.Hairline(new Rect(r.x, r.y, r.width, 1f), UiTheme.Edge);

            float d = AddIcon;
            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = over ? Color.white : UiTheme.Lead;
                GUI.DrawTexture(
                    new Rect(r.center.x - d / 2f, r.center.y - d / 2f, d, d),
                    Icons.Add
                );
                GUI.color = Color.white;
            }
        }

        static void Tabs()
        {
            var strip = new Rect(Panel.x, 0f, Width, TabRowH);
            var actions = ActionRow;
            if (CurrentSplit.HasUpper)
                Slab.Hairline(
                    new Rect(Panel.x + CellX, strip.yMax - 1f, Width - CellX * 2f, 1f),
                    UiTheme.Edge
                );
            Slab.Hairline(
                new Rect(Panel.x + CellX, actions.yMax - 1f, Width - CellX * 2f, 1f),
                UiTheme.Edge
            );

            float icon = NavigationIcon;
            float y = (TabRowH - icon) / 2f;

            float x = Panel.x + CellX;
            foreach (var definition in TabRegistry.Definitions)
            {
                var tab = definition.Tab;
                Tab(
                    new Rect(x, y, icon, icon),
                    IconFor(definition),
                    CurrentTab == tab,
                    definition.Tooltip,
                    () => Show(tab)
                );
                x += icon + TabGap;
            }

            var filter = FilterRect;
            Tab(
                new Rect(filter.x - icon - TabGap, y, icon, icon),
                Icons.OpenFiles,
                Settings.SidebarOpenFilesVisible,
                $"Open files ({ShortcutLabels.Binding(ModDefOf.SlopToggleOpenFiles)})",
                ToggleOpenFiles
            );
            FilterButton();

            if (HasActions)
                Actions(
                    new Rect(
                        Panel.x + Width - CellX - TabIcon,
                        actions.y + (TabRowH - TabIcon) / 2f,
                        TabIcon,
                        TabIcon
                    )
                );

            if (
                (ColonistBarStrip.MouseOver(strip) || ColonistBarStrip.MouseOver(actions))
                && Event.current.rawType == EventType.MouseDown
                && ColonistBarStrip.Interactive
                && Event.current.mousePosition.x < Panel.x + Width - GripW
            )
                Event.current.Use();
        }

        static Texture2D IconFor(SidebarTabDefinition definition)
        {
            switch (definition.IconKey)
            {
                case "files":
                    return Icons.Files;
                case "search":
                    return Icons.Search;
                case "git":
                    return Icons.Git;
                case "tasks":
                    return Icons.Tasks;
                case "library":
                    return Icons.Library;
                default:
                    return Icons.Agents;
            }
        }

        // Each definition owns the second-row controls for its view.
        static void Actions(Rect r)
        {
            CurrentDefinition.DrawActions(new SidebarTabActionContext(r.x, r.y, r.width, r.height));
        }

        static bool AllFolded()
        {
            return CurrentDefinition.AllFolded();
        }

        static void ToggleAllFolds()
        {
            SetAllFolds(!AllFolded());
        }

        // Keep all navigation and both global controls distinct even at minimum width.
        static float NavigationIcon =>
            Mathf.Min(
                TabIcon,
                (Width - 2f * CellX - (SidebarTabCatalog.Order.Count + 1) * TabGap)
                    / (SidebarTabCatalog.Order.Count + 2)
            );

        // Keep project menus anchored below the navigation row across repeated selections.
        static Rect FilterRect =>
            new Rect(
                Panel.x + Width - CellX - NavigationIcon,
                (TabRowH - NavigationIcon) / 2f,
                NavigationIcon,
                NavigationIcon
            );

        static Rect _projectFilterAnchor;

        static void FilterButton()
        {
            _projectFilterAnchor = UiSelector.ScreenBox(TabHitRect(FilterRect));
            Tab(
                FilterRect,
                Icons.Filter,
                Filtering,
                Filtering
                    ? $"Showing {FilterLabel}. Click to tick another, or all of them."
                    : "Every project. Click to show only some of them.",
                OpenFilterMenu,
                filter: true
            );
        }

        // Ticks, not a pick: a tick closes the menu the way every option in one does, and opens it
        // again where it was. Therefore, several can be set without hunting the button back down
        // between them.

        static void Tab(
            Rect r,
            Texture2D icon,
            bool on,
            string tip,
            System.Action go,
            bool onPress = true,
            bool filter = false
        )
        {
            var hit = TabHitRect(r);
            if (filter)
                UiMenu.ObserveSelectorInput();
            TooltipHandler.TipRegion(hit, tip);
            if (
                UiLayout.IconButton(
                    hit,
                    r.ContractedBy(UiTheme.IconInset),
                    icon,
                    on ? UiTheme.Lead : UiTheme.Off,
                    on: ColonistBarStrip.Interactive,
                    onPress: onPress
                )
            )
            {
                if (!filter || !UiMenu.TryBlockSelectorClickAt(UiControls.MenuAt(hit)))
                    go();
            }

            // The selected tab is marked by the same blue signal used for the current row,
            // at the foot of whichever of the strip's rows the button sits in.
            if (on)
                Slab.Fill(new Rect(r.x, hit.yMax - 2f, r.width, 2f), UiTheme.Accent);
        }

        static Rect TabHitRect(Rect icon)
        {
            // Fill the row and split the gap with neighboring buttons. Keep the resize
            // grip outside the target, and leave the glyph and selection marker unchanged.
            float left = Mathf.Max(Panel.x, icon.x - TabGap / 2f);
            float right = Mathf.Min(Panel.x + Width - GripW, icon.xMax + TabGap / 2f);
            return new Rect(
                left,
                icon.y < TabRowH ? 0f : ActionRow.y,
                Mathf.Max(0f, right - left),
                TabRowH
            );
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
                UiText.FadedRowLabel(countRect, count, TextAnchor.MiddleRight);

                var p = SessionHub.Instance.Project(head.Label);

                Text.Font = GameFont.Small;
                GUI.color = UiTheme.Faint;
                var label = new Rect(lx, r.y, Mathf.Max(0f, countRect.x - Pad - lx), HeadH);
                UiText.FadedRowLabel(label, head.Label);

                Slab.Hairline(new Rect(CellX, r.yMax - 1f, r.width - CellX * 2f, 1f), UiTheme.Edge);

                TooltipHandler.TipRegion(
                    r,
                    p == null
                        ? "Agents whose project has gone, and anyone here who is not an agent.\n\n"
                            + "Click to fold."
                        : $"{p.Dir}\n({ProjectSummary.Of(p)})\n\n"
                            + "Click to fold, right-click for the project."
                );
            }
        }
    }
}
