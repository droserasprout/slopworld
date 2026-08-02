using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // What the sidebar layout does to the rest of the interface: the bottom button row and
    // the inspect pane start where the column ends. Both are vanilla's own, laid out from
    // UI.screenWidth and from zero, so both are moved rather than rewritten.
    //
    // Neither knows the layout exists; each asks SlopLayout for the room to leave, which is
    // zero in the strip layout and during a cutscene, and then this file does nothing at all.

    // The row is laid out contiguously from zero to screenWidth, the last visible button
    // widened to whatever is left, so squeezing the whole line into the room right of the
    // column keeps it contiguous and keeps it ending at the right edge. A prefix on the one
    // method every button's rect goes through - DoButton is virtual and nothing overrides it
    // - rather than a transpiler through DoButtons, which is where this game's Mono has
    // already refused one once (see ColonistBarStrip).
    [HarmonyPatch(typeof(MainButtonWorker), nameof(MainButtonWorker.DoButton))]
    public static class Patch_MainButtonShift
    {
        static void Prefix(ref Rect rect)
        {
            float inset = SlopLayout.LeftInset;
            if (inset <= 0f) return;

            float full = UI.screenWidth;
            if (full <= inset + 1f) return;

            float squeeze = (full - inset) / full;
            rect.x = inset + rect.x * squeeze;
            rect.width *= squeeze;
        }
    }

    // The pane's tab row - Log, Gear, Health, and our own Edit - is drawn from
    // InspectPaneUtility.ExtraOnGUI, which the window stack calls *outside* the window's
    // group. So it is screen coordinates laid out from the pane's width with the pane
    // assumed to start at zero, and moving the pane leaves the row where it was. The whole
    // row is shifted at once rather than a rect at a time: `DoTabs` lays its buttons out
    // right to left from one figure, and the only lever on that figure is the space it is
    // drawn in.
    //
    // A finalizer ends the group, or a throw inside the row leaves Unity with one open and
    // takes the rest of the frame's UI with it.
    [HarmonyPatch(typeof(InspectPaneUtility), "DoTabs")]
    public static class Patch_InspectTabRowShift
    {
        static void Prefix(out bool __state)
        {
            float inset = SlopLayout.LeftInset;
            __state = inset > 0f;
            if (__state)
                GUI.BeginGroup(new Rect(inset, 0f, UI.screenWidth - inset, UI.screenHeight));
        }

        static void Finalizer(bool __state)
        {
            if (__state) GUI.EndGroup();
        }
    }

    // An open tab's own window is anchored the same way - `new Rect(0f, ...)` - and it is
    // registered with the stack rather than drawn where it is asked for, so the group above
    // does not reach it and it is moved on its own. Once each: the group is gone by the time
    // the stack draws the window this rect describes.
    [HarmonyPatch(typeof(InspectTabBase), "TabRect", MethodType.Getter)]
    public static class Patch_InspectTabRectShift
    {
        static void Postfix(ref Rect __result)
        {
            __result.x += SlopLayout.LeftInset;
        }
    }

    // The inspect pane is anchored left, which means x = 0. The right-anchored tabs are left
    // where they are: nothing of ours is over there.
    //
    // This runs when a tab opens and when the resolution changes, so a layout toggled with
    // the pane already up moves it on the next open rather than immediately. The alternative
    // is writing windowRect every frame, which would fight anything else that moves a window.
    [HarmonyPatch(typeof(MainTabWindow), "SetInitialSizeAndPosition")]
    public static class Patch_MainTabWindowShift
    {
        static void Postfix(MainTabWindow __instance)
        {
            ShiftPane(__instance);
        }

        // Called directly when the sidebar layout is toggled, so the pane moves immediately.
        public static void Reposition()
        {
            var pane = Find.WindowStack?.WindowOfType<MainTabWindow_Inspect>();
            if (pane != null) ShiftPane(pane);
        }

        static void ShiftPane(MainTabWindow pane)
        {
            if (pane.Anchor != MainTabWindowAnchor.Left) return;

            // Both ways: the pane goes to the column's edge and comes back to the left edge
            // when the layout is turned off again.
            var r = pane.windowRect;
            r.x = SlopLayout.LeftInset > 0f
                ? Mathf.Min(SlopLayout.LeftInset, Mathf.Max(0f, UI.screenWidth - r.width))
                : 0f;
            pane.windowRect = r;
        }
    }
}
