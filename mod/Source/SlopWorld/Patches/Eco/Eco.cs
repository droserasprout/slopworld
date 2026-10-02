using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
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
                // Maximized terminal content covers the background, including views opened without an agent.
                if (TerminalWindow.Covering) return;
                if (!WorldRendererUtility.DrawingMap) return;

                Backdrop();
            }
        }

        // Draw the background before other world geometry, independent of altitude.
        const int Underneath = 1000;

        // MapUpdate continues during Eco rest to draw the background.
        // MenuBackground selects the animated texture. This cache owns its material and projection dimensions.
        // Recalculate dimensions when the map, camera, or screen changes.
        static Texture2D _backdropTexture;
        static Material _backdropMaterial;
        static float _backdropDim = float.NaN;
        static bool _backdropGeometryReady;
        static Map _backdropMap;
        static BackdropView _backdropView;

        // One capture supplies both cache comparison and the committed projection inputs.
        struct BackdropView
        {
            public Camera Camera;
            public Vector3 Position;
            public Quaternion Rotation;
            public float Ortho, Fov, Aspect;
            public Rect PixelRect;
            public int Width, Height;

            public static BackdropView Capture()
            {
                var camera = UnityEngine.Camera.main;
                var view = new BackdropView { Camera = camera, Width = UI.screenWidth, Height = UI.screenHeight };
                if (camera != null)
                {
                    var transform = camera.transform;
                    view.Position = transform.position;
                    view.Rotation = transform.rotation;
                    view.Ortho = camera.orthographicSize;
                    view.Fov = camera.fieldOfView;
                    view.Aspect = camera.aspect;
                    view.PixelRect = camera.pixelRect;
                }
                return view;
            }

            public bool Matches(BackdropView other) => Camera == other.Camera &&
                Width == other.Width && Height == other.Height && Position == other.Position &&
                Rotation == other.Rotation && Ortho == other.Ortho && Fov == other.Fov &&
                Aspect == other.Aspect && PixelRect == other.PixelRect;
        }
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
                // Reuse one material. Pooling each texture and dimming combination would retain materials from previous slider values.
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
            var view = BackdropView.Capture();
            bool changed = !_backdropGeometryReady || _backdropTexture != tex ||
                _backdropMap != map || !_backdropView.Matches(view);

            if (!changed) return true;

            // Project screen corners to map coordinates without assuming an orthographic camera viewed from above.
            var a = UI.UIToMapPosition(0f, 0f);
            var b = UI.UIToMapPosition(view.Width, view.Height);
            float viewW = Mathf.Abs(b.x - a.x), viewH = Mathf.Abs(b.z - a.z);
            if (viewW <= 0f || viewH <= 0f) return false;

            // Match ScaleAndCrop by filling the view and cropping excess texture area.
            float w = viewW, h = viewH;
            float want = tex.width / (float)tex.height;
            if (want > w / h) w = h * want;
            else h = w / want;

            _backdropW = w;
            _backdropH = h;
            _backdropCenter = new Vector3((a.x + b.x) * 0.5f, 0f, (a.z + b.z) * 0.5f);
            _backdropMap = map;
            _backdropView = view;
            _backdropGeometryReady = true;
            return true;
        }

        // Suppress pawn labels separately because the normal drawing restrictions do not cover this entry point.
        [HarmonyPatch(typeof(Pawn), nameof(Pawn.DrawGUIOverlay))]
        public static class Patch_PawnLabels
        {
            static bool Prefix() => !Resting;
        }

        static bool _offered;

        // Use existing menu frames. Offer the planet texture once if no frames exist.
        static Texture2D Frame()
        {
            if (MenuBackground.HasFrames) return MenuBackground.Current(null);
            if (_offered) return null;

            _offered = true;
            return MenuBackground.Current(
                ContentFinder<Texture2D>.Get("UI/HeroArt/BGPlanet", false));
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
