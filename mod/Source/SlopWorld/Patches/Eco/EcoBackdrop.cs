using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Owns Eco's world-space projection and reusable rendering resources.
    // MenuBackground owns frame lifetime; MenuBackgroundLayers owns blend and GUI offsets.
    internal static class EcoBackdrop
    {
        // Draw the background before other world geometry, independent of altitude.
        const int Underneath = 1000;

        static GeometryCache _geometry;
        static Material _backdropBase;
        static readonly Material[] BackdropMaterials = new Material[MenuBackgroundLayers.Count];

        // Frame identity does not affect projection. Keep only dimensions, view inputs,
        // and the matrices derived from them; no generated textures are retained here.
        // Map identity is weak so an idle backdrop cannot keep a discarded colony alive.
        struct GeometryCache
        {
            public bool Ready;
            public System.WeakReference<Map> Map;
            public BackdropView View;
            public int TextureWidth, TextureHeight;
            public Matrix4x4 Base;
            public Matrix4x4[] Layers;

            public bool Matches(Map map, Texture2D texture, BackdropView view) => Ready &&
                Map.TryGetTarget(out var cachedMap) && ReferenceEquals(cachedMap, map) &&
                TextureWidth == texture.width && TextureHeight == texture.height &&
                View.Matches(view);
        }

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

        internal static void Draw(Map map)
        {
            var tex = Frame() ?? BaseContent.BlackTex;
            if (tex == null) return;

            if (!FitBackdrop(map, tex)) return;

            // Two half-alpha layers leave a quarter of the camera contents visible. Establish
            // opaque black first so revealing the backdrop cannot expose stale contents, while
            // retaining the original layer weights, dimming, and offset JPEG block grids.
            if (_backdropBase == null)
                _backdropBase = new Material(ShaderDatabase.Transparent)
                {
                    name = "SlopWorld Eco backdrop base",
                    renderQueue = Underneath,
                    mainTexture = BaseContent.BlackTex,
                    color = Color.white
                };
            Graphics.DrawMesh(MeshPool.plane10, _geometry.Base, _backdropBase, 0);

            float brightness = 1f - Mathf.Clamp01(Settings.EcoDim);
            for (int i = 0; i < MenuBackgroundLayers.Count; i++)
            {
                MenuBackgroundLayers.Layer layer = MenuBackgroundLayers.Get(tex, i);
                // Distinct reusable materials keep queued draws independent. Ordered transparent
                // passes preserve the same previous-then-current blend as the GUI renderer.
                Material material = BackdropMaterials[i];
                if (material == null)
                    BackdropMaterials[i] = material = new Material(ShaderDatabase.Transparent)
                    {
                        name = $"SlopWorld Eco backdrop {i}",
                        renderQueue = Underneath + 1 + i
                    };
                material.mainTexture = layer.Texture;
                material.color = new Color(brightness, brightness, brightness, layer.Opacity);
                Graphics.DrawMesh(MeshPool.plane10, _geometry.Layers[i], material, 0);
            }
        }

        static bool FitBackdrop(Map map, Texture2D tex)
        {
            var view = BackdropView.Capture();
            if (_geometry.Matches(map, tex, view)) return true;

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

            var center = new Vector3((a.x + b.x) * 0.5f, 0f, (a.z + b.z) * 0.5f);
            var scale = new Vector3(w, 1f, h);
            // Allocate the fixed matrix storage once. Recompute offsets only when projection
            // changes, using the same GUI-to-map conversion as the original drawing path.
            var layers = _geometry.Layers ?? new Matrix4x4[MenuBackgroundLayers.Count];
            for (int i = 0; i < layers.Length; i++)
            {
                Vector2 offset = MenuBackgroundLayers.Get(tex, i).Offset;
                Vector3 displacement = UI.UIToMapPosition(offset.x, offset.y) - a;
                layers[i] = Matrix4x4.TRS(center + displacement, Quaternion.identity, scale);
            }
            // Reuse the weak-reference wrapper across camera and screen changes.
            var mapIdentity = _geometry.Map ?? new System.WeakReference<Map>(map);
            mapIdentity.SetTarget(map);
            _geometry = new GeometryCache
            {
                Ready = true,
                Map = mapIdentity,
                View = view,
                TextureWidth = tex.width,
                TextureHeight = tex.height,
                Base = Matrix4x4.TRS(center, Quaternion.identity, scale),
                Layers = layers,
            };
            return true;
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

    }
}
