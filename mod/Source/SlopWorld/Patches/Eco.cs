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

        // Draw the Loading-screen frames in world space where the map used to be.
        [HarmonyPatch(typeof(Map), nameof(Map.MapUpdate))]
        public static class Patch_Board
        {
            static void Postfix(Map __instance)
            {
                if (!Resting) return;
                // MapUpdate runs for every loaded map. Only the displayed map submits a
                // backdrop; frame lookup and drift math both stay behind the visibility gate.
                if (__instance != Find.CurrentMap) return;
                // TerminalWindow also hosts every maximized content view, including views
                // opened without an agent. All of them cover the animated background.
                if (TerminalWindow.Covering) return;
                if (!WorldRendererUtility.DrawingMap) return;

                Backdrop();
            }
        }

        // Fold dimming into this draw; the shared menu frames remain undimmed. MaterialPool
        // keys on the color, so the slider behind this steps rather than moving freely.
        static Color Shade
        {
            get
            {
                float lit = 1f - Mathf.Clamp01(Settings.EcoDim);
                return new Color(lit, lit, lit, 1f);
            }
        }

        // Keep the frame below any unexpected world draw regardless of its altitude.
        const int Underneath = 1000;

        // Oversize past what the crop needs, so both axes have margin to drift inside; the
        // crop alone leaves one of them exactly on the view. Laps are long and incommensurate.
        const float Zoom = 1.05f;
        const float PanX = 47f;
        const float PanZ = 61f;
        // Short of the whole margin, or a lap would show past the edge of the picture.
        const float PanRoom = 0.85f;

        static void Backdrop()
        {
            var tex = Frame() ?? BaseContent.BlackTex;

            // Project the screen corners instead of assuming a top-down orthographic camera.
            var a = UI.UIToMapPosition(0f, 0f);
            var b = UI.UIToMapPosition(UI.screenWidth, UI.screenHeight);
            float viewW = Mathf.Abs(b.x - a.x), viewH = Mathf.Abs(b.z - a.z);
            if (viewW <= 0f || viewH <= 0f) return;

            // Match vanilla's ScaleAndCrop fit; crop rather than letterbox.
            float w = viewW, h = viewH;
            float want = tex.width / (float)tex.height;
            if (want > w / h) w = h * want;
            else h = w / want;

            w *= Zoom;
            h *= Zoom;

            float t = Time.realtimeSinceStartup;
            float dx = (w - viewW) * 0.5f * PanRoom * Mathf.Sin(t * 2f * Mathf.PI / PanX);
            float dz = (h - viewH) * 0.5f * PanRoom * Mathf.Sin(t * 2f * Mathf.PI / PanZ);

            var at = new Vector3((a.x + b.x) * 0.5f + dx, 0f, (a.z + b.z) * 0.5f + dz);
            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(at, Quaternion.identity, new Vector3(w, 1f, h)),
                MaterialPool.MatFrom(tex, ShaderDatabase.Cutout, Shade, Underneath), 0);
        }

        // ThingOverlays survives the disabled draw chain; suppress all map labels along with
        // the pawns and other map things.
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawGUIOverlay))]
        public static class Patch_PawnLabels
        {
            static bool Prefix() => !Resting;
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

        // Edge quads clip Eco's backdrop; an opaque terminal also makes them unnecessary.
        [HarmonyPatch(typeof(MapEdgeClipDrawer), nameof(MapEdgeClipDrawer.DrawClippers))]
        public static class Patch_Clippers
        {
            static bool Prefix() => !Resting && !TerminalWindow.Covering;
        }

        // Weather draws from CameraDriver.OnPreCull, outside PaneOverDraw's gates.
        // Keep weather updates and audio, but skip pixels hidden by the terminal or Eco.
        [HarmonyPatch(typeof(WeatherManager), nameof(WeatherManager.DrawAllWeather))]
        public static class Patch_Weather
        {
            static bool Prefix() => !Resting && !TerminalWindow.Covering;
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
