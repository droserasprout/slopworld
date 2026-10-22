using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Sidebar drawing orchestration across the colonist-bar back and front passes.
    public static partial class Sidebar
    {
        public static bool Drawing { get; private set; }

        // File-reader sessions belong to Open files rather than the Agents tab.
        static bool AgentTabEmpty
        {
            get
            {
                foreach (var session in SessionHub.Instance.Sessions)
                    if (session != null && !IsRouted(session))
                        return false;
                return true;
            }
        }

        static void DrawChromeAndClicks()
        {
            Tabs();
            DrawAdd();

            Grip();
            if (AddClick())
                return;
            CurrentDefinition.Click();
            Absorb();
        }

        static void DrawOpenFiles()
        {
            UpdateOpenFilesVisibility();
            var split = CurrentSplit;
            Layout.ViewRows.Clear();
            HandleFilesDivider(PaneBody, split);
            if (!split.HasUpper)
                return;

            PrepareRouted();
            var upper = ToRect(split.Upper);
            if (Layout.Routed.Count == 0)
            {
                using (WidgetState.Save())
                {
                    Text.Font = GameFont.Small;
                    GUI.color = UiTheme.Faint;
                    UiText.FadedRowLabel(upper.ContractedBy(Pad), "No open files.");
                }
            }
            else
                DrawRouted(upper, Interaction.FilesRoutedScroll);
            DrawFilesDivider(split, PaneBody);
            if (!Interaction.FilesDividerInput && !Interaction.FilesDividerDragging)
                ClickRouted();
        }

        static void DrawAgentTab()
        {
            Tabs();
            DrawAdd();
            Grip();
            AddClick();

            bool insideBody = Body.Contains(Event.current.mousePosition);
            BeginAgentScroll();

            string currentSession = SessionSelectable.Current;
            foreach (var row in Layout.Rows)
            {
                if (SkipAgentPaint(row.Line))
                    continue;
                bool current = row.Session != null && row.Session == currentSession;

                RowChrome.Hover(row.Line, current, true, RowHoverPolicy.OverlayAware);
            }

            foreach (var head in Layout.Heads)
                if (!SkipAgentPaint(head.Rect))
                    DrawHead(head);
            if (AgentTabEmpty)
            {
                float y = Pad;
                foreach (var head in Layout.Heads)
                    y = Mathf.Max(y, head.Rect.yMax);
                using (WidgetState.Save())
                {
                    Text.Font = GameFont.Small;
                    GUI.color = UiTheme.Faint;
                    UiText.FadedRowLabel(
                        new Rect(Pad, y, Width - 2 * Pad, TextH),
                        "No agents/shells."
                    );
                }
            }
            // This must precede vanilla: its portrait handler consumes right-clicks.
            // The scroll group also lets Menus use the content-local row geometry
            // directly, just like portrait and label hit testing.
            if (insideBody)
                Menus();
        }

        public static void DrawBack()
        {
            if (Event.current.type == EventType.Layout)
                return;

            if (ColonistBarStrip.Suppressed)
                return;

            if (!Wanted())
            {
                SearchView.Closed();
                LibraryView.Closed();
                if (Interaction.Resizing)
                    EndResize();
                EndFilesDivider();
                return;
            }
            SidebarRowRenderer.BeginFrame(SessionHub.Instance.SessionsVersion);
            _renderStarted = PerfTrace.Start();
            Drawing = true;

            // The panel, fixed chrome and menus run here, before vanilla consumes input.
            // Only the agent body remains grouped around vanilla's portrait pass.
            var panel = Panel;
            Slab.Fill(panel, UiTheme.Panel);

            DrawOpenFiles();
            CurrentDefinition.Draw();
            if (CurrentTab == SidebarTab.Agents)
                return;

            DrawChromeAndClicks();
        }
    }
}
