using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Cross-pass input, scrolling, resizing, click routing, and cleanup.
    public static partial class AgentSidebar
    {
        public static bool AgentScrollOpen => Interaction.AgentScrollOpen;

        static void BeginAgentScroll()
        {
            var body = Body;
            // The full-width view keeps SmoothScroll active while omitting the gutter that narrows every row.
            var view = new Rect(0f, 0f, body.width, Layout.AgentContentH);
            Interaction.AgentScroll.Begin(body, view, false);
            Interaction.AgentScrollOpen = true;
        }

        static void EndAgentScroll()
        {
            if (!Interaction.AgentScrollOpen) return;
            Interaction.AgentScrollOpen = false;
            Interaction.AgentScroll.End();
        }

        static void Click(Row row, SessionInfo info)
        {
            if (row.Session == null || !ColonistBarStrip.Interactive) return;
            if (!Widgets.ButtonInvisible(row.Text, false)) return;

            SessionSelectable.Current = row.Session;

            if (!ColonistBarStrip.Drawing)
            {
                Find.Selector.ClearSelection();
                if (row.Pawn == null) return;
                CameraJumper.TryJumpAndSelect(row.Pawn);
                return;
            }

            if (row.Session != TerminalWindow.CurrentName) TerminalWindow.Open(row.Session);
        }

        // Called by the Harmony finalizer when vanilla prevents the normal front pass.
        public static void EndDraw()
        {
            EndAgentScroll();
            Patch_SidebarPortraitDraw.ClearDeferredSelection();
            Drawing = false;
        }

        static void Grip()
        {
            float w = Width;
            var grip = new Rect(w - GripW, 0f, GripW * 2f, UI.screenHeight);
            if (!ColonistBarStrip.Interactive && Interaction.Resizing)
            {
                EndResize();
            }

            bool over = ColonistBarStrip.SidebarHover(grip);
            bool lit = over || Interaction.Resizing;

            // The panel's right edge and the grip's own tell are the same line, and it is
            // drawn here alone: a second draw of [UiWidgets.Edge] over this one composites
            // into a heavier boundary than the palette's, on the sidebar only. At rest it is
            // one screen pixel like every other rule; lit it is a bar and may be a GUI one.
            if (lit) Slab.Fill(new Rect(w - 1f, 0f, 2f, UI.screenHeight), UiWidgets.EdgeLit);
            else Slab.VHairline(new Rect(w - 1f, 0f, 1f, UI.screenHeight), UiWidgets.Edge);

            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;

            // Poll Input: an absorbing window can prevent this layer from receiving MouseDown.
            if (!Interaction.Resizing)
            {
                if (!over || !Input.GetMouseButtonDown(0)) return;
                Interaction.Resizing = true;
                Interaction.WidthChanged = false;
                Interaction.Grab = w - e.mousePosition.x;
            }
            else if (Input.GetMouseButton(0))
            {
                SetWidth(e.mousePosition.x + Interaction.Grab);
            }
            else
            {
                EndResize();
            }

            if (e.rawType == EventType.MouseDown || e.rawType == EventType.MouseUp
                || e.rawType == EventType.MouseDrag)
                e.Use();
        }

        static void SetWidth(float w)
        {
            float before = Width;
            Settings.S.sidebarWidth = Mathf.Clamp(w, MinWidth, MaxWidth);
            if (Mathf.Abs(Width - before) > 0.01f)
                Interaction.WidthChanged = true;
            Patch_MainTabWindowShift.Reposition();
        }

        static void EndResize()
        {
            Interaction.Resizing = false;
            if (Interaction.WidthChanged)
            {
                Interaction.WidthChanged = false;
                RefreshPanels();
            }
            Settings.S.Write();
        }

        static void RefreshPanels()
        {
            if (TerminalWindow.TryPanelShape(out int cols, out int rows))
                SessionHub.Instance.RefreshPanels(cols, rows);
            else
                SessionHub.Instance.RefreshPanels();
        }

        static void Absorb()
        {
            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;
            if (e.rawType != EventType.MouseDown) return;
            if (!ColonistBarStrip.MouseOver(Panel)) return;
            e.Use();
        }

        static bool Wanted()
        {
            return ColonistBarStrip.BarShown && !Cutscene.Playing && !Settings.SidebarHidden;
        }

        public static void ToggleSidebar()
        {
            Settings.S.sidebarHidden = !Settings.S.sidebarHidden;
            if (Settings.S.sidebarHidden) SearchView.Closed();
            Settings.S.Write();
            Patch_MainTabWindowShift.Reposition();
            RefreshPanels();
        }
    }
}
