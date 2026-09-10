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
            CurrentDefinition.Click();
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
                if (SkipAgentPaint(row.Line)) continue;
                bool current = row.Session != null && row.Session == currentSession;

                RowChrome.Hover(row.Line, current, true, RowHoverPolicy.OverlayAware);
            }

            foreach (var head in Layout.Heads)
                if (!SkipAgentPaint(head.Rect)) DrawHead(head);
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
                if (Interaction.Resizing) EndResize();
                return;
            }
            Drawing = true;
            Patch_SidebarPortraitDraw.ClearDeferredSelection();

            // The panel, fixed chrome and menus run here, before vanilla consumes input.
            // Only the agent body remains grouped around vanilla's portrait pass.
            var panel = Panel;
            Slab.Fill(panel, UiTheme.Panel);

            CurrentDefinition.Draw();
            if (CurrentTab == SidebarTab.Agents) return;

            DrawChromeAndClicks();
        }

    }
}
