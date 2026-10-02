using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    public static class InspectPaneAgent
    {
        public static string SelectedSession()
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

        public static string SelectedPawnSession()
        {
            if (Current.ProgramState != ProgramState.Playing) return null;
            var pawn = Find.Selector?.SingleSelectedThing as Pawn;
            return pawn == null ? null : AgentColony.Current?.SessionOf(pawn);
        }

        public static bool AgentPawnSelected => SelectedPawnSession() != null;

        // A sidebar selection can have an action row without a pawn in Selector.
        // Suppress the inspect window for this state too, including during Eco rest.
        public static bool AgentSelectionActive =>
            Current.ProgramState == ProgramState.Playing
            && (AgentPawnSelected || SessionSelectable.HasCurrent);

        [HarmonyPatch(typeof(Window), "WindowOnGUI")]
        public static class Patch_HideWindow
        {
            static bool Prefix(Window __instance) =>
                !(__instance is MainTabWindow_Inspect && AgentSelectionActive);
        }

        // WindowStack draws the shadow before WindowOnGUI.
        // Suppress the inspect window shadow separately so it does not cover the action row.
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
