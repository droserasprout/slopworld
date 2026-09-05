using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Shift vanilla bottom buttons and inspect panes by `UiLayout.LeftInset`; zero leaves
    // them untouched. Patch the stable button-rect seam rather than transpiling `DoButtons`.

    [HarmonyPatch(typeof(MainButtonWorker), nameof(MainButtonWorker.DoButton))]
    public static class Patch_MainButtonShift
    {
        static void Prefix(ref Rect rect)
        {
            float inset = UiLayout.LeftInset;
            if (inset <= 0f) return;

            float full = UI.screenWidth;
            if (full <= inset + 1f) return;

            float squeeze = (full - inset) / full;
            rect.x = inset + rect.x * squeeze;
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
            float inset = UiLayout.LeftInset;
            __state = inset > 0f;
            if (__state)
                GUI.BeginGroup(new Rect(inset, 0f, UI.screenWidth - inset, UI.screenHeight));
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
            __result.x += UiLayout.LeftInset;
        }
    }

    // Shift left-anchored inspect windows on open or layout changes; right-anchored tabs stay
    // vanilla, and the window rect is not rewritten every frame.
    [HarmonyPatch(typeof(MainTabWindow), "SetInitialSizeAndPosition")]
    public static class Patch_MainTabWindowShift
    {
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
            if (pane.Anchor != MainTabWindowAnchor.Left) return;

            var r = pane.windowRect;
            r.x = UiLayout.LeftInset > 0f
                ? Mathf.Min(UiLayout.LeftInset, Mathf.Max(0f, UI.screenWidth - r.width))
                : 0f;
            pane.windowRect = r;
        }
    }
}
