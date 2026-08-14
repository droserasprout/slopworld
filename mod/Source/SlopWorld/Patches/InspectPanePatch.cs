using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    public static class InspectPaneAgent
    {
        public static string Selected()
        {
            if (Current.ProgramState != ProgramState.Playing) return null;

            var selected = Find.Selector?.SingleSelectedThing;
            if (selected != null)
            {
                var pawn = selected as Pawn;
                return pawn == null ? null : AgentColony.Current?.SessionOf(pawn);
            }

            return SessionSelectable.HasCurrent ? SessionSelectable.Current : null;
        }

        public static string SelectedPawn()
        {
            if (Current.ProgramState != ProgramState.Playing) return null;
            var pawn = Find.Selector?.SingleSelectedThing as Pawn;
            return pawn == null ? null : AgentColony.Current?.SessionOf(pawn);
        }

        public static bool AgentPawnSelected => SelectedPawn() != null;

        // A sidebar/ghost selection has no pawn in Selector, but still owns the same
        // action row. Keep the inspect window suppressed for that state too; Eco is
        // especially likely to leave the map selector empty while the row is active.
        public static bool AgentSelectionActive =>
            Current.ProgramState == ProgramState.Playing
            && (AgentPawnSelected || SessionSelectable.HasCurrent);

        [HarmonyPatch(typeof(Window), "WindowOnGUI")]
        public static class Patch_HideWindow
        {
            static bool Prefix(Window __instance) =>
                !(__instance is MainTabWindow_Inspect && AgentSelectionActive);
        }

        // WindowStack draws a window's shadow before it calls WindowOnGUI. Skipping the
        // inspect window therefore removes its body but not the translucent shadow that was
        // tinting the gizmo row underneath it.
        [HarmonyPatch(typeof(WindowStack), "WindowStackOnGUI")]
        public static class Patch_HideInspectShadow
        {
            public sealed class ShadowState
            {
                public MainTabWindow_Inspect Pane;
                public bool DrawShadow;
            }

            static void Prefix(WindowStack __instance, out ShadowState __state)
            {
                var pane = __instance.WindowOfType<MainTabWindow_Inspect>();
                __state = new ShadowState
                {
                    Pane = pane,
                    DrawShadow = pane != null && pane.drawShadow,
                };

                if (AgentSelectionActive && pane != null)
                    pane.drawShadow = false;
            }

            static void Finalizer(ShadowState __state)
            {
                if (__state?.Pane != null)
                    __state.Pane.drawShadow = __state.DrawShadow;
            }
        }

        [HarmonyPatch(typeof(MainTabWindow_Inspect), "AnythingSelected", MethodType.Getter)]
        public static class Patch_AnythingSelected
        {
            static void Postfix(ref bool __result)
            {
                if (AgentSelectionActive) __result = false;
            }
        }

        [HarmonyPatch(typeof(MainTabWindow_Inspect), "ShouldShowPaneContents", MethodType.Getter)]
        public static class Patch_ShouldShowPaneContents
        {
            static void Postfix(ref bool __result)
            {
                if (AgentSelectionActive) __result = false;
            }
        }

        [HarmonyPatch(typeof(InspectPaneUtility), "DoTabs")]
        public static class Patch_HideTabs
        {
            static bool Prefix() => !AgentSelectionActive;
        }
    }
}
