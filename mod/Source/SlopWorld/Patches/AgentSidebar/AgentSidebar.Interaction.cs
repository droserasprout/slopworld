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

        // Clip CPU-side row work as well as pixels. Input passes retain their control order.
        static bool SkipAgentPaint(Rect rect) => Event.current.type == EventType.Repaint &&
            Interaction.AgentScrollOpen &&
            (rect.yMax <= Interaction.AgentScroll.Position.y ||
             rect.y >= Interaction.AgentScroll.Position.y + Body.height);

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
            PerfTrace.End("sidebar", _renderStarted, Layout.Rows.Count);
            _renderStarted = 0L;
        }

        static void Grip()
        {
            float w = Width;
            var grip = new Rect(Panel.x + w - GripW, 0f, GripW * 2f, UI.screenHeight);
            if (Interaction.Resizing && (!ColonistBarStrip.Interactive ||
                GUIUtility.hotControl != Interaction.ResizeControl))
            {
                EndResize();
            }

            bool over = ColonistBarStrip.SidebarHover(grip);
            bool lit = over || Interaction.Resizing;

            // The panel's right edge and the grip's own tell are the same line, and it is
            // drawn here alone: a second draw of [UiTheme.Edge] over this one composites
            // into a heavier boundary than the palette's, on the sidebar only. At rest it is
            // one screen pixel like every other rule; lit it is a bar and may be a GUI one.
            if (lit) Slab.Fill(new Rect(Panel.x + w - 1f, 0f, 2f, UI.screenHeight), UiTheme.EdgeLit);
            else Slab.VHairline(new Rect(Panel.x + w - 1f, 0f, 1f, UI.screenHeight), UiTheme.Edge);

            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive, grip);

            // Recover a consumed mouse-down, but respect a scrollbar's existing capture.
            // Polling GetMouseButtonDown also re-started this drag on later GUI passes.
            if (!Interaction.Resizing)
            {
                if (!over || e.rawType != EventType.MouseDown || e.button != 0 ||
                    GUIUtility.hotControl != 0) return;
                Interaction.Resizing = true;
                Interaction.ResizeControl = control;
                GUIUtility.hotControl = control;
                Interaction.WidthChanged = false;
                Interaction.Grab = Panel.x + w - e.mousePosition.x;
            }
            else if (Input.GetMouseButton(0))
            {
                SetWidth(e.mousePosition.x + Interaction.Grab - Panel.x);
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
            if (GUIUtility.hotControl == Interaction.ResizeControl)
                GUIUtility.hotControl = 0;
            Interaction.ResizeControl = 0;
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
            TerminalWindow.RefreshPanels();
        }

        // Appearance changes invalidate workspace geometry but must leave content-view
        // instances, field focus, and scroll positions alive.
        public static void LayoutChanged()
        {
            Patch_MainTabWindowShift.Reposition();
            RefreshPanels();
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
            LayoutChanged();
        }
    }
}
