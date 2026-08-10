using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Eco mode: the board stops, and the machine goes quiet.
    //
    // What burns a core here is the sim first and the map second, and neither is what the
    // screen is for - this game is a window onto agents running somewhere else, and a colony
    // nobody is looking at does not have to move. So the clock is held at paused (TimeKeeper
    // asks here before starting it again), the whole draw chain stands down the way it does
    // under a pane (PaneOverDraw), and the frames are capped (BackgroundFrames). Everything
    // the terminal is made of keeps running: the socket, the column, the top bar, the strip
    // and the pane itself.
    //
    // With the pane closed that would leave an unlit board and nothing to look at, so the
    // menu's own background is drawn in its place - the same baked frames the main menu and
    // the loading screen use, already resident by the time a colony is, and one blit a frame.
    //
    // What it costs: a paused game reconciles nothing, so an agent that arrives during an eco
    // spell has a row in the column and no colonist on the map until the clock starts again,
    // and AutoSaver takes no autosave - which is the same statement twice, a board that has
    // not moved having nothing new to write down.
    //
    // Not a cutscene's business. A scene *has* the board and is the one thing here that has
    // to finish, so eco stands aside while one plays and takes hold on the far side of it.
    public static class Eco
    {
        public static bool Resting => Settings.EcoMode && !Cutscene.Playing;

        // What the map's own cosmetics ask: an agent's state plate, the core's bubble, the
        // jukebox under the pointer. A scene plays over the board and eco takes it away, and
        // either way there is nothing under them to be about. The top bar is drawn from a
        // MapComponent too and asks neither - it is the chrome, and in eco it is most of what
        // is left.
        public static bool Bare => Cutscene.Playing || Resting;

        // Drawn from the first thing on the map's own GUI layer, so the colonist strip, the
        // column, the top bar, the gizmos and every window land on top of it. Nothing lands
        // under it: what would have painted the board has already stood down, so this is the
        // frame.
        [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_BeforeMainTabs))]
        public static class Patch_Backdrop
        {
            static void Prefix()
            {
                if (!Resting || Event.current.type != EventType.Repaint) return;
                // The pane is screen-sized and opaque, so a frame under it is a blit nobody
                // sees; the world view paints its own globe and is not ours to cover. The map
                // is asked about first, this being the same order the method itself uses -
                // the render mode is read off a world that need not be there yet.
                if (Find.CurrentMap == null || TerminalWindow.Covering) return;
                if (!WorldRendererUtility.DrawingMap) return;
                Draw();
            }
        }

        static void Draw()
        {
            var full = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight);
            var frame = Frame();

            GUI.color = Color.white;
            if (frame == null) Widgets.DrawBoxSolid(full, Color.black);
            // ScaleAndCrop, which is what vanilla's own BackgroundOnGUI does with the same
            // picture: a frame baked at the menu's aspect must not letterbox here.
            else GUI.DrawTexture(full, frame, ScaleMode.ScaleAndCrop);
        }

        // Whether this has already offered a picture to bake from, ever.
        static bool _offered;

        // A null source means "whatever you baked from last time", and that is the point of
        // asking: the menu may have baked an expansion's art rather than the planet, and
        // handing MenuBackground a different picture would re-key its cache every time eco
        // came on. A set that does not exist at all - a process that reached a colony without
        // drawing a menu - is the only case worth naming a source for, and it pays for the
        // bake here. Once, though: a bake that failed leaves no frames and would otherwise be
        // attempted again on the next frame, and the frame after that. Black is the answer to
        // that, not a retry sixty times a second.
        static Texture2D Frame()
        {
            if (MenuBackground.HasFrames) return MenuBackground.Current(null);
            if (_offered) return null;

            _offered = true;
            return MenuBackground.Current(
                ContentFinder<Texture2D>.Get("UI/HeroArt/BGPlanet", false));
        }

        // Weather is a draw and nothing else on this path - the overlays are ticked from
        // WeatherTick, which is the clock's, and the clock is stopped. Off CameraDriver's
        // OnPreCull rather than MapUpdate, which is why PaneOverDraw does not already have it.
        [HarmonyPatch(typeof(WeatherManager), nameof(WeatherManager.DrawAllWeather))]
        public static class Patch_Weather
        {
            static bool Prefix() => !Resting;
        }

        // Selection brackets, room overlays and the rest of what is drawn in world space over
        // the board rather than into it. The gizmo row is not here - it is UI space, and in
        // eco it is still how a session is started.
        [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceUpdate))]
        public static class Patch_WorldSpace
        {
            static bool Prefix() => !Resting;
        }

        // A click on a board nobody is drawing selects whatever happens to be under the
        // pointer on it. The strip and the column are how an agent is picked in eco, and both
        // are drawn above this.
        [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.HandleMapClicks))]
        public static class Patch_MapClicks
        {
            static bool Prefix() => !Resting;
        }
    }
}
