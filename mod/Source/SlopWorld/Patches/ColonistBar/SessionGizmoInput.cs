using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Map components handle input before the action grid. Selection and designator input run after it.
    // Store action rectangles so both stages use the same hit test.
    public static class SessionGizmoInput
    {
        public const float ActionRowHeight = 75f;
        const float AbsorbX = 12f;
        const float AbsorbBottom = 14f;

        static readonly List<Rect> ActionRects = new List<Rect>();
        static int RectFrame = -1;

        public static void RememberActionRect(Rect rect)
        {
            // Terminal gizmos use window coordinates. Do not use their rectangles for map input.
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

                // Protect the first row before this frame records its action rectangles.
                // Later events use the recorded rectangles, including wrapped rows.
                if (RectFrame == Time.frameCount) return false;

                var content = WorkspaceLayout.Current.Content;
                var firstRow = new Rect(content.x, AgentSidebar.AddBar.y - ActionRowHeight,
                    content.width, ActionRowHeight + AbsorbBottom);
                return firstRow.Contains(e.mousePosition);
            }
        }

        static bool MapActionStripVisible()
        {
            if (!SessionSelectable.HasCurrent || !UiLayout.Shown || UiLayout.Hidden
                || TerminalWindow.Covering)
                return false;

            var tabs = Find.MainTabsRoot;
            return tabs == null || tabs.OpenTab == null
                || tabs.OpenTab == MainButtonDefOf.Inspect;
        }
    }

    // MapInterface handles designators and targeters after action gizmo drawing.
    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.HandleMapClicks))]
    public static class Patch_MapClicks_OverSessionGizmos
    {
        static bool Prefix() => !SessionGizmoInput.MouseOverActionGrid;
    }

    // Block map selection in the later input pass.
    // This includes drag release events that a gizmo MouseDown handler cannot consume.
    [HarmonyPatch(typeof(Selector), "HandleMapClicks")]
    public static class Patch_SelectorClicks_OverSessionGizmos
    {
        static bool Prefix() => !SessionGizmoInput.MouseOverActionGrid;
    }
}
