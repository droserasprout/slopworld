using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Map input is split around MapUIOnGUI: map components run before the gizmo grid, while
    // selection and designator input run after it. Remember the actual action rects so both
    // sides can use the same hit test instead of relying on a later Event.Use().
    public static class SessionGizmoInput
    {
        const float GizmoH = 75f;
        const float AbsorbX = 12f;
        const float AbsorbBottom = 14f;

        static readonly List<Rect> ActionRects = new List<Rect>();
        static int RectFrame = -1;

        public static void RememberActionRect(Rect rect)
        {
            // A terminal owns the screen while it is open; its gizmos use window-local
            // coordinates and must not become map hit regions.
            if (TerminalWindow.Covering) return;

            int frame = Time.frameCount;
            if (RectFrame != frame)
            {
                RectFrame = frame;
                ActionRects.Clear();
            }

            var absorbed = new Rect(rect.x - AbsorbX, rect.y,
                rect.width + AbsorbX * 2f, rect.height + AbsorbBottom);
            if (!ActionRects.Contains(absorbed)) ActionRects.Add(absorbed);
        }

        public static bool MouseOverActionGrid
        {
            get
            {
                if (!MapActionStripVisible()) return false;

                var e = Event.current;
                if (e == null) return false;

                foreach (var rect in ActionRects)
                    if (rect.Contains(e.mousePosition)) return true;

                // The first event in a frame can arrive before the grid has recorded its
                // rects. Protect the row until this frame's grid pass has run; subsequent
                // events use the exact rects above, including wrapped rows.
                if (RectFrame == Time.frameCount) return false;

                var firstRow = new Rect(0f, AgentSidebar.AddBar.y - GizmoH,
                    UI.screenWidth, GizmoH + AbsorbBottom);
                return firstRow.Contains(e.mousePosition);
            }
        }

        static bool MapActionStripVisible()
        {
            if (!SessionSelectable.HasCurrent || !SlopLayout.Shown || SlopLayout.Hidden
                || TerminalWindow.Covering)
                return false;

            var tabs = Find.MainTabsRoot;
            return tabs == null || tabs.OpenTab == null
                || tabs.OpenTab == MainButtonDefOf.Inspect;
        }
    }

    // MapInterface handles designators and targeters after the action gizmo pass.
    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.HandleMapClicks))]
    public static class Patch_MapClicks_OverSessionGizmos
    {
        static bool Prefix() => !SessionGizmoInput.MouseOverActionGrid;
    }

    // Selector handles pawn/map selection in the later low-priority pass. This also blocks
    // the MouseUp half of a drag gesture, which a gizmo's MouseDown absorption cannot cover.
    [HarmonyPatch(typeof(Selector), "HandleMapClicks")]
    public static class Patch_SelectorClicks_OverSessionGizmos
    {
        static bool Prefix() => !SessionGizmoInput.MouseOverActionGrid;
    }
}
