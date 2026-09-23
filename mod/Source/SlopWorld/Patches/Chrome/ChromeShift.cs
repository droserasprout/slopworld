using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Fit base game controls within the workspace content area.
    // Patch rectangle calculations so drawing and input use the same bounds on either navigation side.

    [HarmonyPatch(typeof(MainButtonWorker), nameof(MainButtonWorker.DoButton))]
    public static class Patch_MainButtonShift
    {
        static void Prefix(ref Rect rect)
        {
            var content = WorkspaceLayout.Current.Content;
            float full = UI.screenWidth;
            if (content.width >= full - 0.01f || full <= 0.01f) return;

            float squeeze = content.width / full;
            rect.x = content.x + rect.x * squeeze;
            rect.width *= squeeze;
        }
    }

    // Draw DoTabs in a temporary GUI group because it runs outside the pane group.
    // Close the group in a finalizer, including after exceptions.
    [HarmonyPatch(typeof(InspectPaneUtility), "DoTabs")]
    public static class Patch_InspectTabRowShift
    {
        static void Prefix(out bool __state)
        {
            var content = WorkspaceLayout.Current.Content;
            __state = content.x > 0f || content.width < UI.screenWidth - 0.01f;
            if (__state)
                GUI.BeginGroup(new Rect(content.x, 0f, content.width, UI.screenHeight));
        }

        static void Finalizer(bool __state)
        {
            if (__state) GUI.EndGroup();
        }
    }

    // Shift tab window rectangles directly because window registration is separate from the DoTabs GUI group.
    [HarmonyPatch(typeof(InspectTabBase), "TabRect", MethodType.Getter)]
    public static class Patch_InspectTabRectShift
    {
        static void Postfix(ref Rect __result)
        {
            var content = WorkspaceLayout.Current.Content;
            if (content.width >= UI.screenWidth - 0.01f) return;
            __result.x += content.x;
        }
    }

    // Fit left- and right-anchored windows within the content area when they open or the layout changes.
    // Retain their original widths as limits.
    [HarmonyPatch(typeof(MainTabWindow), "SetInitialSizeAndPosition")]
    public static class Patch_MainTabWindowShift
    {
        static readonly Dictionary<MainTabWindow, float> NaturalWidths =
            new Dictionary<MainTabWindow, float>();

        static void Postfix(MainTabWindow __instance)
        {
            ShiftPane(__instance);
        }

        // Move the open inspect pane immediately after a layout change.
        public static void Reposition()
        {
            var pane = Find.WindowStack?.WindowOfType<MainTabWindow_Inspect>();
            if (pane != null) ShiftPane(pane);
        }

        static void ShiftPane(MainTabWindow pane)
        {
            var content = WorkspaceLayout.Current.Content;
            var r = pane.windowRect;
            if (pane.Anchor == MainTabWindowAnchor.Left)
                r.x = content.x;
            else if (pane.Anchor == MainTabWindowAnchor.Right)
                r.x = Mathf.Max(content.x, content.xMax - r.width);
            else return;
            if (!NaturalWidths.TryGetValue(pane, out float natural))
            {
                natural = r.width;
                NaturalWidths[pane] = natural;
            }
            r.width = Mathf.Min(natural, content.width);
            pane.windowRect = r;
        }
    }
}
