using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Map vanilla chrome into the workspace content region. Patch stable rectangle seams so
    // the same bounds are used by drawing and input on either navigation side.

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

    // `DoTabs` is drawn outside the pane's group, so shift it in a temporary GUI group and
    // close that group from a finalizer even when the tab row throws.
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

    // Tab windows are registered separately from `DoTabs`' group, so shift their rect directly.
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

    // Shift left-anchored inspect windows on open or layout changes; right-anchored tabs stay
    // vanilla, and the window rect is not rewritten every frame.
    [HarmonyPatch(typeof(MainTabWindow), "SetInitialSizeAndPosition")]
    public static class Patch_MainTabWindowShift
    {
        static readonly Dictionary<MainTabWindow, float> NaturalWidths =
            new Dictionary<MainTabWindow, float>();

        static void Postfix(MainTabWindow __instance)
        {
            ShiftPane(__instance);
        }

        // Called when the layout changes, so the pane moves immediately.
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
