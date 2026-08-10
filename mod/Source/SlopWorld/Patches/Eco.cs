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
    // the loading screen use, already resident by the time a colony is, and one quad a frame -
    // dimmed, because here it is what the agents stand on rather than the picture itself. The
    // agents are drawn back over it: a colony nobody is looking at still has them in it, and a
    // board with nothing on it says the wrong thing about a session that is running.
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
        // the two things eco leaves standing - the agents and the column beside them - already
        // say between them everything a plate would. The top bar is drawn from a MapComponent
        // too and asks neither: it is the chrome, and in eco it is most of what is left.
        public static bool Bare => Cutscene.Playing || Resting;

        // Drawn into the board's own space rather than over it, which is the whole difference:
        // an agent has to stand *on* this, and a screen-sized blit on the GUI layer would be
        // over the pawns as much as over the map. Off Map.MapUpdate, the one call in the frame
        // eco does not stand down - the four draws inside it that PaneOverDraw gates are
        // exactly what leaves the board empty for this.
        [HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
        public static class Patch_Board
        {
            static void Postfix(Map __instance)
            {
                if (!Resting) return;
                // The pane is screen-sized and opaque, so a board under it is drawn for
                // nobody; the world view paints its own globe and is not ours to cover.
                if (TerminalWindow.Covering) return;
                if (!WorldRendererUtility.DrawingMap) return;

                Backdrop();
                Agents(__instance);
            }
        }

        // How much of the picture is taken away. Here it is a backdrop and not a picture in
        // its own right - the menu and the loading screen draw that one off the same frames,
        // and neither may darken with this. A grey multiply *is* a black layer laid over it
        // and nothing else: Cutout multiplies _Color into the texture, and c*(1-a) is what
        // compositing black at alpha a over c comes to. The layer is folded into the one draw
        // rather than sent as a second quad, which would have to be sorted under the agents.
        const float Dim = 0.45f;
        static readonly Color Shade = new Color(1f - Dim, 1f - Dim, 1f - Dim, 1f);

        // Under everything: the queue puts it before the pawns' own cutout draws whatever the
        // altitudes work out to, and it writes depth at the floor, so nothing standing on the
        // board can be covered by it.
        const int Underneath = 1000;

        static void Backdrop()
        {
            var tex = Frame() ?? BaseContent.BlackTex;

            // The screen's own corners put back on the map's plane. Exact whatever the camera
            // is doing, where reading a centre and a size off its transform would be assuming
            // the view is orthographic and straight down.
            var a = UI.UIToMapPosition(0f, 0f);
            var b = UI.UIToMapPosition(UI.screenWidth, UI.screenHeight);
            float w = Mathf.Abs(b.x - a.x), h = Mathf.Abs(b.z - a.z);
            if (w <= 0f || h <= 0f) return;

            // ScaleAndCrop, which is what vanilla's own BackgroundOnGUI does with the same
            // picture: a frame baked at the menu's aspect must not letterbox here. What hangs
            // off the screen is the crop.
            float want = tex.width / (float)tex.height;
            if (want > w / h) w = h * want;
            else h = w / want;

            var at = new Vector3((a.x + b.x) * 0.5f, 0f, (a.z + b.z) * 0.5f);
            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(at, Quaternion.identity, new Vector3(w, 1f, h)),
                MaterialPool.MatFrom(tex, ShaderDatabase.Cutout, Shade, Underneath), 0);
        }

        // The one thing eco puts back on the board. Vanilla's three phases in vanilla's own
        // order: DynamicDrawManager walks the whole map through them and is stood down, so the
        // agents are walked through them here and nothing else is. Culled against the view for
        // the reason it culls - a colony is a handful of agents, but a pawn render is not free.
        static void Agents(Map map)
        {
            var colony = AgentColony.Current;
            if (colony == null) return;

            var view = Find.CameraDriver.CurrentViewRect;
            foreach (var kv in colony.All)
            {
                var pawn = kv.Value;
                if (pawn == null || !pawn.Spawned || pawn.Map != map) continue;
                if (!view.Contains(pawn.Position)) continue;

                pawn.DynamicDrawPhase(DrawPhase.EnsureInitialized);
                pawn.DynamicDrawPhase(DrawPhase.ParallelPreDraw);
                pawn.DynamicDrawPhase(DrawPhase.Draw);
            }
        }

        // ThingOverlays is not part of the draw chain that stands down - it runs from
        // MapInterfaceOnGUI_BeforeMainTabs, on the GUI layer, and writes out every name on the
        // map whether or not there is anything under it. In eco there is exactly one thing
        // under a name, so the rest of them are words hanging in the picture.
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawGUIOverlay))]
        public static class Patch_PawnLabels
        {
            static bool Prefix(Pawn __instance) => !Resting || AgentColony.IsAgent(__instance);
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
