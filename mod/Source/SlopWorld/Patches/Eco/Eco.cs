using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace SlopWorld
{
    // Eco pauses and hides the map while retaining the terminal interface. See mod-eco.md.
    public static class Eco
    {
        public static bool Resting => Settings.EcoMode && !Cutscene.Playing;

        // Hide map decorations during cutscenes and Eco rest.
        public static bool Bare => Cutscene.Playing || Resting;

        // Draw loading screen frames in world space as the Eco background.
        [HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
        public static class Patch_Board
        {
            static void Postfix(Map __instance)
            {
                if (!Resting) return;
                // MapUpdate runs for each loaded map. Draw a background only for the displayed map.
                if (__instance != Find.CurrentMap) return;
                // GUI input can close the terminal after world drawing has been queued.
                // Keep the background ready underneath it, including its animation history.
                if (!WorldRendererUtility.DrawingMap) return;

                EcoBackdrop.Draw(__instance);
            }
        }

        // Suppress pawn labels separately because the normal drawing restrictions do not cover this entry point.
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawGUIOverlay))]
        public static class Patch_PawnLabels
        {
            static bool Prefix() => !Resting;
        }

        // Hide map edge geometry while Eco or an opaque terminal covers the map.
        [HarmonyPatch(typeof(MapEdgeClipDrawer), nameof(MapEdgeClipDrawer.DrawClippers))]
        public static class Patch_Clippers
        {
            static bool Prefix() => !Resting && !TerminalWindow.Covering;
        }

        // Weather draws through CameraDriver.OnPreCull, outside PaneOverDraw checks.
        // Skip hidden weather graphics while retaining weather updates and audio.
        [HarmonyPatch(typeof(WeatherManager), nameof(WeatherManager.DrawAllWeather))]
        public static class Patch_Weather
        {
            static bool Prefix() => !Resting && !TerminalWindow.Covering;
        }

        // Suppress world overlays while retaining UI gizmos.
        [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceUpdate))]
        public static class Patch_WorldSpace
        {
            static bool Prefix() => !Resting;
        }

        // Prevent map clicks while Eco hides the map.
        [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.HandleMapClicks))]
        public static class Patch_MapClicks
        {
            static bool Prefix() => !Resting;
        }
    }
}
