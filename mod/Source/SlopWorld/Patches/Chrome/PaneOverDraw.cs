using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Skip map mesh, dynamic things, and flecks while a terminal covers the map or Eco replaces
    // it. Eco releases audited geometry; ordinary terminal coverage keeps it warm.
    public static class PaneOverDraw
    {
        // Prefixes return "run the original", so this is the sense the game wants.
        internal static bool Hidden => TerminalWindow.Covering || Eco.Resting;
        static bool Wanted() => !Hidden;

        // RimWorld 1.6 calls these from MapInterfaceOnGUI_BeforeMainTabs, outside
        // MapUpdate. Gate the whole traversal, not each pawn's eventual label call.
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

        // OnGUI is separate from both FleckManagerDraw and real-time aging.
        [HarmonyPatch(typeof(FleckManager), nameof(FleckManager.FleckManagerOnGUI))]
        public static class Patch_FleckGui
        {
            static bool Prefix() => Wanted();
        }

        // Terrain, and everything printed into the mesh - here, every plant there is.
        [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.DrawMapMesh))]
        public static class Patch_MapMesh
        {
            static bool Prefix(MapDrawer __instance)
            {
                if (!Wanted()) return false;
                // Draw can precede maintenance when a setting or cutscene reveals the board.
                EcoMapMemory.Restore(__instance);
                return true;
            }
        }

        // Pawns and fires: the render trees, the faceplate node with them.
        [HarmonyPatch(typeof(DynamicDrawManager), nameof(DynamicDrawManager.DrawDynamicThings))]
        public static class Patch_DynamicThings
        {
            static bool Prefix() => Wanted();
        }

        // The haze. PlagueFx declines to make any behind a pane, so this is what was
        // already in flight when it opened.
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

        // LordManagerUpdate also ages orphaned stencils, so gate only the drawing method.
        [HarmonyPatch(typeof(StencilDrawerForCells), nameof(StencilDrawerForCells.Draw))]
        public static class Patch_Stencils
        {
            static bool Prefix() => Wanted();
        }
    }
}
