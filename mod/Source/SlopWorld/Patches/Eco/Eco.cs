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
                // backdrop; frame lookup and drawing both stay behind the visibility gate.
                if (__instance != Find.CurrentMap) return;
                // TerminalWindow also hosts every maximized content view, including views
                // opened without an agent. All of them cover the animated background.
                if (TerminalWindow.Covering) return;
                if (!WorldRendererUtility.DrawingMap) return;

                Backdrop();
            }
        }

        // Keep the frame below any unexpected world draw regardless of its altitude.
        const int Underneath = 1000;

        // MapUpdate still runs while Eco is resting so the resident frame can be drawn. Keep the
        // expensive, unchanged parts of that submission out of the frame: MenuBackground owns
        // the animated texture choice, while this cache owns its material and the projection
        // fit. Camera input is blocked in Eco, but transform and resolution keys still catch
        // resize, map changes, and any external camera jump before the next draw.
        static Texture2D _backdropTexture;
        static Material _backdropMaterial;
        static float _backdropDim = float.NaN;
        static bool _backdropGeometryReady;
        static Map _backdropMap;
        static Camera _backdropCamera;
        static Vector3 _backdropCameraPosition;
        static Quaternion _backdropCameraRotation;
        static float _backdropCameraOrtho;
        static float _backdropCameraFov;
        static float _backdropCameraAspect;
        static Rect _backdropPixelRect;
        static int _backdropScreenWidth = -1;
        static int _backdropScreenHeight = -1;
        static float _backdropW, _backdropH;
        static Vector3 _backdropCenter;

        static void Backdrop()
        {
            var tex = Frame() ?? BaseContent.BlackTex;
            if (tex == null) return;

            float dim = Mathf.Clamp01(Settings.EcoDim);
            if (_backdropTexture != tex || _backdropMaterial == null || _backdropDim != dim)
            {
                _backdropTexture = tex;
                _backdropDim = dim;
                // One owned material; pooling every (frame, dim) pair retains slider history.
                if (_backdropMaterial == null)
                    _backdropMaterial = new Material(ShaderDatabase.Cutout)
                    {
                        name = "SlopWorld Eco backdrop",
                        renderQueue = Underneath
                    };
                _backdropMaterial.mainTexture = tex;
                _backdropMaterial.color = new Color(1f - dim, 1f - dim, 1f - dim, 1f);
                _backdropGeometryReady = false;
            }

            Map map = Find.CurrentMap;
            if (!FitBackdrop(map, tex)) return;

            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(_backdropCenter, Quaternion.identity,
                    new Vector3(_backdropW, 1f, _backdropH)), _backdropMaterial, 0);
        }

        static bool FitBackdrop(Map map, Texture2D tex)
        {
            Camera camera = Camera.main;
            bool changed = !_backdropGeometryReady || _backdropTexture != tex ||
                _backdropMap != map || _backdropScreenWidth != UI.screenWidth ||
                _backdropScreenHeight != UI.screenHeight || _backdropCamera != camera;

            if (!changed && camera != null)
            {
                var transform = camera.transform;
                changed = _backdropCameraPosition != transform.position ||
                    _backdropCameraRotation != transform.rotation ||
                    _backdropCameraOrtho != camera.orthographicSize ||
                    _backdropCameraFov != camera.fieldOfView ||
                    _backdropCameraAspect != camera.aspect ||
                    _backdropPixelRect != camera.pixelRect;
            }

            if (!changed) return true;

            // Project the screen corners instead of assuming a top-down orthographic camera.
            var a = UI.UIToMapPosition(0f, 0f);
            var b = UI.UIToMapPosition(UI.screenWidth, UI.screenHeight);
            float viewW = Mathf.Abs(b.x - a.x), viewH = Mathf.Abs(b.z - a.z);
            if (viewW <= 0f || viewH <= 0f) return false;

            // Match vanilla's ScaleAndCrop fit; crop rather than letterbox.
            float w = viewW, h = viewH;
            float want = tex.width / (float)tex.height;
            if (want > w / h) w = h * want;
            else h = w / want;

            _backdropW = w;
            _backdropH = h;
            _backdropCenter = new Vector3((a.x + b.x) * 0.5f, 0f, (a.z + b.z) * 0.5f);
            _backdropMap = map;
            _backdropCamera = camera;
            if (camera != null)
            {
                var transform = camera.transform;
                _backdropCameraPosition = transform.position;
                _backdropCameraRotation = transform.rotation;
                _backdropCameraOrtho = camera.orthographicSize;
                _backdropCameraFov = camera.fieldOfView;
                _backdropCameraAspect = camera.aspect;
                _backdropPixelRect = camera.pixelRect;
            }
            else
            {
                _backdropCameraPosition = default;
                _backdropCameraRotation = default;
                _backdropCameraOrtho = 0f;
                _backdropCameraFov = 0f;
                _backdropCameraAspect = 0f;
                _backdropPixelRect = default;
            }
            _backdropScreenWidth = UI.screenWidth;
            _backdropScreenHeight = UI.screenHeight;
            _backdropGeometryReady = true;
            return true;
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
