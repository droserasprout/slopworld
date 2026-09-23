using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Skip map geometry, dynamic things, and flecks while the terminal or Eco covers the map.
    // Eco releases selected geometry. Terminal coverage retains it.
    public static class PaneOverDraw
    {
        // Wanted returns true when patch prefixes must permit normal drawing.
        internal static bool Hidden => TerminalWindow.Covering || Eco.Resting;
        static bool Wanted() => !Hidden;

        // These GUI passes run outside MapUpdate in RimWorld 1.6.
        // Skip the full traversal before individual label calls.
        [HarmonyPatch(typeof(ThingOverlays), nameof(ThingOverlays.ThingOverlaysOnGUI))]
        public static class Patch_ThingLabels
        {
            static bool Prefix() => Wanted();
        }

        [HarmonyPatch(typeof(TooltipGiverList), nameof(TooltipGiverList.DispenseAllThingTooltips))]
        public static class Patch_ThingTooltips
        {
            static bool Prefix() => Wanted();
        }

        // Fleck GUI drawing is separate from normal fleck drawing and real-time aging.
        [HarmonyPatch(typeof(FleckManager), nameof(FleckManager.FleckManagerOnGUI))]
        public static class Patch_FleckGui
        {
            static bool Prefix() => Wanted();
        }

        // Control drawing for terrain and objects printed into map meshes, including plants.
        [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.DrawMapMesh))]
        public static class Patch_MapMesh
        {
            static bool Prefix(MapDrawer __instance)
            {
                if (!Wanted()) return false;
                // Restore geometry before drawing because a setting or cutscene can reveal the map before maintenance runs.
                EcoMapMemory.Restore(__instance);
                return true;
            }
        }

        // Control drawing for dynamic objects, including pawn render trees and fires.
        [HarmonyPatch(typeof(DynamicDrawManager), nameof(DynamicDrawManager.DrawDynamicThings))]
        public static class Patch_DynamicThings
        {
            static bool Prefix() => Wanted();
        }

        // Hide existing flecks. PlagueFx also prevents new effects behind a terminal pane.
        [HarmonyPatch(typeof(FleckManager), nameof(FleckManager.FleckManagerDraw))]
        public static class Patch_Flecks
        {
            static bool Prefix() => Wanted();
        }

        [HarmonyPatch(typeof(OverlayDrawer), nameof(OverlayDrawer.DrawAllOverlays))]
        public static class Patch_Overlays
        {
            static bool Prefix() => Wanted();
        }

        [HarmonyPatch(typeof(GameConditionManager), nameof(GameConditionManager.GameConditionManagerDraw))]
        public static class Patch_Conditions
        {
            static bool Prefix() => Wanted();
        }

        [HarmonyPatch(typeof(DesignationManager), nameof(DesignationManager.DrawDesignations))]
        public static class Patch_Designations
        {
            static bool Prefix() => Wanted();
        }

        [HarmonyPatch(typeof(TemporaryThingDrawer), nameof(TemporaryThingDrawer.Draw))]
        public static class Patch_TemporaryThings
        {
            static bool Prefix() => Wanted();
        }

        // Keep LordManagerUpdate active to age unused stencils. Skip only stencil drawing.
        [HarmonyPatch(typeof(StencilDrawerForCells), nameof(StencilDrawerForCells.Draw))]
        public static class Patch_Stencils
        {
            static bool Prefix() => Wanted();
        }
    }
}
