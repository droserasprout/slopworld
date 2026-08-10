using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // The map is not drawn while a pane covers it, nor at all in eco mode.
    //
    // Map.MapUpdate runs the whole draw chain and looks at nothing above it, vanilla
    // having no window that fills the screen opaque. TerminalWindow is exactly that -
    // screen-sized, margin 0, its own background painted first - so all of this went
    // into a buffer the next call covered up.
    //
    // Eco is the second reason and the standing one: there the board is not covered, it
    // is switched off, and the menu's background is drawn where it was. Same four calls,
    // so one gate answers for both - see Eco.
    //
    // Only the draw half stands down. MapMeshDrawerUpdate_First keeps running:
    // skipping it banks the work into a hitch on the frame the pane closes.
    public static class PaneOverDraw
    {
        // Prefixes return "run the original", so this is the sense the game wants.
        static bool Wanted() => !TerminalWindow.Covering && !Eco.Resting;

        // Terrain, and everything printed into the mesh - here, every plant there is.
        [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.DrawMapMesh))]
        public static class Patch_MapMesh
        {
            static bool Prefix() => Wanted();
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
    }
}
