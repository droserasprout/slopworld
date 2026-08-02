using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The gizmo grid is drawn at the bottom of the screen, above the main button row.
    // When the sidebar layout is active, the inspect pane is shifted right by the sidebar's
    // width, but the gizmo grid's startX (14 + PaneWidthFor(pane)) is not shifted. Since the
    // pane window is drawn over the gizmo grid (window stack after map interface), the first
    // gizmos are hidden under the shifted pane.
    //
    // This patch adds the sidebar inset to the gizmo grid's startX, keeping the gizmos to the
    // right of the shifted pane.
    //
    // A static flag is used to distinguish the bottom-of-screen gizmo grid (drawn from
    // DrawGizmoGridFor) from the architect menu's designator gizmo grid (drawn via a separate
    // DrawGizmoGrid call from inside the architect tab window).

    [HarmonyPatch(typeof(GizmoGridDrawer), "DrawGizmoGridFor")]
    public static class Patch_GizmoGridFlag
    {
        // True while a DrawGizmoGridFor call is on the stack, meaning the next DrawGizmoGrid
        // call is the bottom-of-screen gizmo grid rather than an architect designator list.
        public static bool Active;

        static void Prefix()
        {
            if (SlopLayout.Shown) Active = true;
        }

        static void Postfix()
        {
            Active = false;
        }
    }

    [HarmonyPatch(typeof(GizmoGridDrawer), "DrawGizmoGrid")]
    public static class Patch_GizmoGridShift
    {
        static void Prefix(ref float startX)
        {
            if (!Patch_GizmoGridFlag.Active) return;
            float inset = SlopLayout.LeftInset;
            if (inset <= 0f) return;
            startX += inset;
        }
    }
}