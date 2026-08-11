using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Eco pauses and strips the board while leaving the terminal chrome alive. See mod-eco.md.
    public static class Eco
    {
        public static bool Resting => Settings.EcoMode && !Cutscene.Playing;

        // Hide map cosmetics during either stripped-board state.
        public static bool Bare => Cutscene.Playing || Resting;

        // Draw in world space so the restored agents stand over the backdrop.
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

        // Fold dimming into this draw; the shared menu frames remain undimmed.
        const float Dim = 0.45f;
        static readonly Color Shade = new Color(1f - Dim, 1f - Dim, 1f - Dim, 1f);

        // Render before pawn cutouts regardless of their altitude.
        const int Underneath = 1000;

        static void Backdrop()
        {
            var tex = Frame() ?? BaseContent.BlackTex;

            // Project the screen corners instead of assuming a top-down orthographic camera.
            var a = UI.UIToMapPosition(0f, 0f);
            var b = UI.UIToMapPosition(UI.screenWidth, UI.screenHeight);
            float w = Mathf.Abs(b.x - a.x), h = Mathf.Abs(b.z - a.z);
            if (w <= 0f || h <= 0f) return;

            // Match vanilla's ScaleAndCrop fit; crop rather than letterbox.
            float want = tex.width / (float)tex.height;
            if (want > w / h) w = h * want;
            else h = w / want;

            var at = new Vector3((a.x + b.x) * 0.5f, 0f, (a.z + b.z) * 0.5f);
            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(at, Quaternion.identity, new Vector3(w, 1f, h)),
                MaterialPool.MatFrom(tex, ShaderDatabase.Cutout, Shade, Underneath), 0);
        }

        // DynamicDrawManager is disabled, so replay its phases only for visible agents.
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

        // ThingOverlays survives the disabled draw chain; retain only restored agents' labels.
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawGUIOverlay))]
        public static class Patch_PawnLabels
        {
            static bool Prefix(Pawn __instance) => !Resting || AgentColony.IsAgent(__instance);
        }

        static bool _offered;

        // Preserve resident expansion art; offer the planet once only when no frames exist.
        static Texture2D Frame()
        {
            if (MenuBackground.HasFrames) return MenuBackground.Current(null);
            if (_offered) return null;

            _offered = true;
            return MenuBackground.Current(
                ContentFinder<Texture2D>.Get("UI/HeroArt/BGPlanet", false));
        }

        // Vanilla's solid edge quads would clip the backdrop back to the map bounds.
        [HarmonyPatch(typeof(MapEdgeClipDrawer), nameof(MapEdgeClipDrawer.DrawClippers))]
        public static class Patch_Clippers
        {
            static bool Prefix() => !Resting;
        }

        // Weather draws from CameraDriver.OnPreCull, outside PaneOverDraw's gates.
        [HarmonyPatch(typeof(WeatherManager), nameof(WeatherManager.DrawAllWeather))]
        public static class Patch_Weather
        {
            static bool Prefix() => !Resting;
        }

        // Suppress world-space overlays; keep UI-space gizmos available.
        [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceUpdate))]
        public static class Patch_WorldSpace
        {
            static bool Prefix() => !Resting;
        }

        // Prevent clicks from selecting objects on the hidden board.
        [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.HandleMapClicks))]
        public static class Patch_MapClicks
        {
            static bool Prefix() => !Resting;
        }
    }
}
