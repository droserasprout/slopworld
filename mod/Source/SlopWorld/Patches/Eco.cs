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
                Things(__instance);
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

        // Render before pawn cutouts regardless of their altitude.
        const int Underneath = 1000;

        // A barely visible sway for the objects eco puts back on the board. This is an
        // extra draw angle, not a Thing rotation, so save data and gameplay-facing facing stay
        // unchanged. The seed is stable per thing, so a redraw cannot reshuffle the scene.
        const float WobbleDegrees = 60f;
        const float WobbleSeconds = 20f;
        const float WobbleSlow = 0.65f;
        const float WobbleFast = 1.45f;
        const float TwoPi = Mathf.PI * 2f;
        static bool _drawingThings;
        static bool _drawingPawnTree;
        static Pawn _activePawn;
        static float _activePawnWobble;

        static uint Mix(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7feb352d;
                value ^= value >> 15;
                value *= 0x846ca68b;
                return value ^ (value >> 16);
            }
        }

        static float Unit(ref uint seed)
        {
            seed = Mix(seed + 0x9e3779b9);
            return (seed & 0x00ffffff) / 16777215f;
        }

        static float Wobble(Thing thing)
        {
            if (thing == null) return 0f;

            uint seed = (uint)thing.thingIDNumber;
            if (seed == 0) seed = (uint)thing.GetHashCode();

            float phase = Unit(ref seed) * TwoPi;
            float speed = Mathf.Lerp(WobbleSlow, WobbleFast, Unit(ref seed));
            float direction = Unit(ref seed) < 0.5f ? -1f : 1f;
            return WobbleDegrees * direction *
                Mathf.Sin(Time.realtimeSinceStartup * TwoPi * speed / WobbleSeconds + phase);
        }

        static float Wobble(Pawn pawn) => Wobble((Thing)pawn);

        static float PawnWobble(Pawn pawn) => pawn == _activePawn ? _activePawnWobble : Wobble(pawn);

        static Matrix4x4 RotateAround(Matrix4x4 matrix, Vector3 pivot, float angle)
        {
            return Matrix4x4.TRS(pivot, Quaternion.AngleAxis(angle, Vector3.up), Vector3.one)
                * Matrix4x4.Translate(-pivot) * matrix;
        }

        [HarmonyPatch(typeof(Graphic), nameof(Graphic.Draw))]
        public static class Patch_ThingWobble
        {
            static void Prefix(Thing thing, ref float extraRotation)
            {
                if (_drawingThings && thing != null) extraRotation += Wobble(thing);
            }
        }

        // PawnRenderTree has already made one matrix per body/head/apparel node by this point.
        // Rotate each around the pawn's root, rather than rotating each node around its own
        // center. That keeps the head attached to the body while preserving node offsets.
        [HarmonyPatch(typeof(PawnRenderTree), nameof(PawnRenderTree.TryGetMatrix))]
        public static class Patch_PawnTreeMatrixWobble
        {
            static void Postfix(PawnRenderTree __instance, PawnDrawParms parms,
                ref Matrix4x4 matrix, bool __result)
            {
                if (!_drawingThings || !__result || __instance.pawn == null) return;

                Vector3 pivot = parms.matrix.GetColumn(3);
                matrix = RotateAround(matrix, pivot, PawnWobble(__instance.pawn));
            }
        }

        // The matrices above already contain the pawn's complete sway. Leave the low-level
        // hooks alone for the cached single-mesh path, but do not apply them a second time to
        // every node in the render tree.
        [HarmonyPatch(typeof(PawnRenderTree), nameof(PawnRenderTree.Draw))]
        public static class Patch_PawnTreeDraw
        {
            static void Prefix(ref bool __state)
            {
                __state = _drawingThings;
                if (__state) _drawingPawnTree = true;
            }

            static void Finalizer(bool __state)
            {
                if (__state) _drawingPawnTree = false;
            }
        }

        // DynamicDrawPhaseAt wraps both the pre-draw matrix build and the draw. Its context
        // gives the cached path a pawn identity without changing the pawn's saved rotation.
        [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.DynamicDrawPhaseAt))]
        public static class Patch_PawnDrawContext
        {
            static void Prefix(PawnRenderer __instance, ref PawnDrawState __state)
            {
                if (!_drawingThings) return;

                __state = new PawnDrawState
                {
                    PreviousPawn = _activePawn,
                    PreviousTree = _drawingPawnTree,
                    PreviousWobble = _activePawnWobble
                };
                _activePawn = __instance.renderTree?.pawn;
                _activePawnWobble = Wobble(_activePawn);
                _drawingPawnTree = false;
            }

            static void Finalizer(PawnDrawState __state)
            {
                if (__state == null) return;
                _activePawn = __state.PreviousPawn;
                _drawingPawnTree = __state.PreviousTree;
                _activePawnWobble = __state.PreviousWobble;
            }

            sealed class PawnDrawState
            {
                public Pawn PreviousPawn;
                public bool PreviousTree;
                public float PreviousWobble;
            }
        }

        [HarmonyPatch(typeof(GenDraw), nameof(GenDraw.DrawMeshNowOrLater),
            new[] { typeof(Mesh), typeof(Vector3), typeof(Quaternion), typeof(Material),
                typeof(bool) })]
        public static class Patch_PawnMeshWobble
        {
            static void Prefix(ref Quaternion quat)
            {
                if (_drawingThings && !_drawingPawnTree && _activePawn != null)
                    quat = Quaternion.AngleAxis(_activePawnWobble, Vector3.up) * quat;
            }
        }

        [HarmonyPatch(typeof(GenDraw), nameof(GenDraw.DrawMeshNowOrLater),
            new[] { typeof(Mesh), typeof(Matrix4x4), typeof(Material), typeof(bool),
                typeof(MaterialPropertyBlock) })]
        public static class Patch_PawnMatrixWobble
        {
            static void Prefix(ref Matrix4x4 matrix)
            {
                if (!_drawingThings || _drawingPawnTree || _activePawn == null) return;

                var at = matrix.GetColumn(3);
                matrix = RotateAround(matrix, at, _activePawnWobble);
            }
        }

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

        // DynamicDrawManager is disabled, so replay its phases for the visible agents and cat;
        // map-mesh things need their graphics drawn directly. The wobble patch is scoped to this
        // pass, keeping UI ThingIcons and all other graphics at their normal angle.
        static void Things(Map map)
        {
            var view = Find.CameraDriver.CurrentViewRect;
            _drawingThings = true;
            try
            {
                DrawThingDef(map, ModDefOf.SlopJukebox, view);
                DrawThingDef(map, ModDefOf.Ship_ComputerCore, view);
                DrawAgents(map, view);
                foreach (var pet in Pets.On(map)) DrawPawn(pet, view, map);
            }
            finally
            {
                _drawingThings = false;
            }
        }

        static void DrawAgents(Map map, CellRect view)
        {
            var colony = AgentColony.Current;
            if (colony == null) return;

            foreach (var kv in colony.All) DrawPawn(kv.Value, view, map);
        }

        static void DrawPawn(Pawn pawn, CellRect view, Map map)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map != map) return;
            if (!view.Contains(pawn.Position)) return;

            pawn.DynamicDrawPhase(DrawPhase.EnsureInitialized);
            pawn.DynamicDrawPhase(DrawPhase.ParallelPreDraw);
            pawn.DynamicDrawPhase(DrawPhase.Draw);
        }

        static void DrawThingDef(Map map, ThingDef def, CellRect view)
        {
            if (def == null) return;

            foreach (var thing in map.listerThings.ThingsOfDef(def))
            {
                if (thing == null || !thing.Spawned || thing.Map != map) continue;
                if (!view.Contains(thing.Position)) continue;

                var graphic = thing.Graphic;
                if (graphic != null)
                    graphic.Draw(thing.DrawPos, thing.Rotation, thing, 0f);
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
