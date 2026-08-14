using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Keep the bottom gizmo grid at least one shared gap beyond the sidebar.
    // `Active` distinguishes `DrawGizmoGridFor` from the architect tab's designator grid.

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
            startX = InspectPaneAgent.AgentSelectionActive
                ? inset + SlopWidgets.GapS
                : Mathf.Max(startX, inset + SlopWidgets.GapS);
        }
    }
}
