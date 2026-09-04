using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Sidebar drawing orchestration across the colonist-bar back and front passes.
    public static partial class AgentSidebar
    {
        public static bool Drawing { get; private set; }

        static void DrawChromeAndClicks()
        {
            Tabs();
            DrawAdd();

            Grip();
            if (AddClick()) return;
            switch (CurrentTab)
            {
                case SidebarTab.Files:
                    if (!ClickRouted()) FilesView.Clicks();
                    break;
                case SidebarTab.Search:
                    SearchView.Clicks();
                    break;
                case SidebarTab.Git:
                    if (!ClickRouted()) GitView.Clicks();
                    break;
                case SidebarTab.Library:
                    LibraryView.Clicks();
                    break;
                case SidebarTab.Tasks:
                    TasksView.Clicks();
                    break;
                default:
                    Menus();
                    break;
            }
            Absorb();
        }

        static void DrawAgentTab()
        {
            Tabs();
            DrawAdd();
            Grip();
            AddClick();

            BeginAgentScroll();

            string currentSession = SessionSelectable.Current;
            foreach (var row in Layout.Rows)
            {
                bool current = row.Session != null && row.Session == currentSession;

                RowChrome.Hover(row.Line, current, true, RowHoverPolicy.OverlayAware);
            }

            foreach (var head in Layout.Heads) DrawHead(head);
            // This must precede vanilla: its portrait handler consumes right-clicks.
            // The scroll group also lets Menus use the content-local row geometry
            // directly, just like portrait and label hit testing.
            Menus();
        }

        public static void DrawBack()
        {
            if (Event.current.type == EventType.Layout) return;

            if (ColonistBarStrip.Suppressed) return;

            if (!Wanted())
            {
                SearchView.Closed();
                Interaction.Resizing = false;
                return;
            }
            Drawing = true;
            Patch_SidebarPortraitDraw.ClearDeferredSelection();

            // The panel, fixed chrome and menus run here, before vanilla consumes input.
            // Only the agent body remains grouped around vanilla's portrait pass.
            var panel = Panel;
            Slab.Fill(panel, SlopWidgets.Panel);

            switch (CurrentTab)
            {
                case SidebarTab.Agents:
                    DrawAgentTab();
                    return;
                case SidebarTab.Files:
                    DrawRouted(Body, SidebarTab.Files);
                    FilesView.Draw(TreeBody(Body, SidebarTab.Files));
                    break;
                case SidebarTab.Search:
                    SearchView.Draw(Body);
                    break;
                case SidebarTab.Git:
                    DrawRouted(Body, SidebarTab.Git);
                    GitView.Draw(TreeBody(Body, SidebarTab.Git));
                    break;
                case SidebarTab.Library:
                    LibraryView.Draw(Body);
                    break;
                case SidebarTab.Tasks:
                    TasksView.Draw(Body);
                    break;
            }

            DrawChromeAndClicks();
        }

    }
}
