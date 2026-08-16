using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using RimWorld.Planet;
using Verse;

namespace SlopWorld
{
    // Replace vanilla's body-and-mood portrait with a square head portrait using the
    // sidebar's shared geometry. Patch_SidebarPawnLabel suppresses the vanilla label.
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist))]
    public static class Patch_SidebarPortraitDraw
    {
        // The portrait camera looks down -Y with world +Z up, so z pans to the head and a
        // positive x camera offset moves the pawn slightly left in the resulting image.
        const float HeadFallbackZ = 0.34f;

        // Slightly wider than a tight head crop so hair and clothing have breathing room.
        const float FaceZoom = 1.9f;

        // Aim just above the head anchor to place the pawn slightly lower in the portrait.
        const float FaceVerticalOffset = 0.03f;
        const float FaceHorizontalOffset = 0.04f;

        // Keep the drawn portrait square with the face box. The close-up is framed on the
        // head, so there is no body crop that needs extra vertical room.

        // Keep stopped agents recognizable while making their state obvious.
        static readonly Color DownTint = new Color(0.50f, 0.50f, 0.50f, 1f);
        const float SelectionInset = 2f;

        // The faceplate is authored inside the 128px head frame. Match the brackets to its
        // visible bounds after the portrait camera zooms that frame in the sidebar.
        const float PlateFrame = 128f;
        const float PlateCenterX = 64f;
        const float PlateCenterY = 64.5f;
        const float PlateVisibleCenterX = 64f;
        const float PlateVisibleCenterY = 70.5f;
        const float PlateLeft = 40.5f;
        const float PlateTop = 40f;
        const float PlateRight = 87.5f;
        const float PlateBottom = 89f;
        const float SelectionScale = 0.92f;
        public const float SelectionAlpha = 0.45f;

        static FieldInfo _deadColonistTex;

        // Portraits are drawn inside AgentSidebar's scroll view. Queue selected faces so the
        // brackets go down after every opaque portrait, while that same clipped group is open.
        struct DeferredSelection
        {
            public object Target;
            public Rect Face;
            public float BarScale;
        }

        static readonly List<DeferredSelection> DeferredSelections =
            new List<DeferredSelection>();
        static readonly Vector2[] SelectionCorners = new Vector2[4];

        static bool _ready;

        static Patch_SidebarPortraitDraw()
        {
            var t = typeof(ColonistBarColonistDrawer);
            _deadColonistTex = t.GetField("DeadColonistTex",
                BindingFlags.Static | BindingFlags.NonPublic);

            _ready = _deadColonistTex != null;
            if (!_ready)
                Log.Error("[SlopWorld] Patch_SidebarPortraitDraw: one or more private " +
                    "members not found; sidebar portraits will fall back to vanilla.");
        }

        // Keep callers anchored to the drawn portrait rather than duplicating its geometry.
        public static Rect PortraitRect(Rect face) => face;

        // The sidebar lays its rows out from the font and asks this what the portrait may be,
        // so the square pawn portrait fills the row it is in.
        public static float FaceForHeight(float height) => height;

        static Rect FaceplateRect(Rect face)
        {
            float left = PlateCenterX + (PlateLeft - PlateCenterX) * FaceZoom;
            float top = PlateCenterY + (PlateTop - PlateCenterY) * FaceZoom;
            float right = PlateCenterX + (PlateRight - PlateCenterX) * FaceZoom;
            float bottom = PlateCenterY + (PlateBottom - PlateCenterY) * FaceZoom;
            var rect = new Rect(face.x + face.width * left / PlateFrame,
                face.y + face.height * top / PlateFrame,
                face.width * (right - left) / PlateFrame,
                face.height * (bottom - top) / PlateFrame);
            float centerX = PlateCenterX + (PlateVisibleCenterX - PlateCenterX) * FaceZoom;
            float centerY = PlateCenterY + (PlateVisibleCenterY - PlateCenterY) * FaceZoom;
            rect.size *= SelectionScale;
            rect.center = new Vector2(face.x + face.width * centerX / PlateFrame,
                face.y + face.height * centerY / PlateFrame);
            // Camera motion shifts the rendered image by offset * zoom / 2 of its width.
            rect.center -= new Vector2(face.width * FaceHorizontalOffset * FaceZoom / 2f, 0f);
            return rect;
        }

        // Keep cache parameters unscaled so compact layouts reuse the same square texture.
        static Vector2 TextureSize
        {
            get
            {
                float side = ColonistBarColonistDrawer.PawnTextureSize.y;
                return new Vector2(side, side);
            }
        }

        // Use each pawn's scaled head offset; children and unusual body types frame themselves.
        static Vector3 FaceOffset(Pawn pawn)
        {
            float z = HeadFallbackZ;
            try
            {
                var renderer = pawn.Drawer == null ? null : pawn.Drawer.renderer;
                if (renderer != null)
                {
                    float own = renderer.BaseHeadOffsetAt(Rot4.South).z;
                    // Zero is the renderer's failure fallback and would aim at the torso.
                    if (own > 0f) z = own;
                }
            }
            catch (Exception)
            {
                // Pawns mid-generation may not have a draw tracker yet.
            }
            return new Vector3(FaceHorizontalOffset, 0f, z + FaceVerticalOffset);
        }

        static bool Prefix(Rect rect, Pawn colonist, Map pawnMap, bool highlight, bool reordering)
        {
            if (!_ready) return true;
            if (!AgentSidebar.Drawing) return true;
            if (colonist == null) return true;

            Rect face;
            if (!AgentSidebar.FaceBox(colonist, out face)) return true;

            // Preserve vanilla's entry fade and drag fade.
            var bar = Find.ColonistBar;
            float alpha = bar.GetEntryRectAlpha(rect);
            if (reordering) alpha *= 0.5f;

            // A session the hub has not answered for yet is Down to the sidebar's row, so it
            // is Down here too: the alternative is a red badge over a fully lit face.
            var session = AgentColony.Current?.SessionOf(colonist);
            bool isDown = session != null &&
                (SessionHub.Instance.Get(session)?.State ?? AgentState.Down) == AgentState.Down;

            // Render downed pawns upright; vanilla's 85-degree rotation misses this tight crop.
            PawnHealthState? healthOverride = colonist.Dead || colonist.health == null
                ? (PawnHealthState?)null
                : colonist.health.State == PawnHealthState.Down
                    ? PawnHealthState.Mobile
                    : (PawnHealthState?)null;

            var renderTexture = PortraitsCache.Get(colonist, TextureSize, Rot4.South,
                FaceOffset(colonist), FaceZoom, true, true, true, true, null, null,
                false, healthOverride);

            GUI.color = isDown
                ? new Color(DownTint.r, DownTint.g, DownTint.b, alpha)
                : new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(PortraitRect(face), renderTexture);
            GUI.color = Color.white;

            // Both marks go down after the opaque square portrait, the way vanilla ends its
            // own draw: the corners so their inner arms remain visible, the hover box
            // because the portrait covers the rect it is drawn on.
            if (highlight)
            {
                int thickness = face.width <= 22f ? 2 : 3;
                GUI.color = Color.white;
                Widgets.DrawBox(face, thickness);
            }

            if (!AgentSidebar.CompactView)
            {
                if (AgentSidebar.AgentScrollOpen)
                    DeferSelection(colonist, face, bar.Scale);
                else
                    DrawSelection(colonist, face, bar.Scale);
            }

            if (colonist.Dead)
            {
                var tex = (Texture2D)_deadColonistTex.GetValue(null);
                if (tex != null)
                {
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    GUI.DrawTexture(face, tex);
                    GUI.color = Color.white;
                }
            }

            return false;
        }

        static void DrawSelection(Pawn colonist, Rect texRect, float barScale)
        {
            object target;
            if (!TrySelectionTarget(colonist, out target)) return;

            // Vanilla's brackets extend from the supplied rect. Put them over the visible
            // faceplate rather than around the transparent margin of the head texture.
            var corners = FaceplateRect(texRect).ContractedBy(SelectionInset);
            DrawSelectionOverlay(target, corners, barScale);
        }

        static void DeferSelection(Pawn colonist, Rect texRect, float barScale)
        {
            object target;
            if (!TrySelectionTarget(colonist, out target)) return;

            DeferredSelections.Add(new DeferredSelection
            {
                Target = target,
                Face = texRect,
                BarScale = barScale,
            });
        }

        // Match vanilla's map/world and corpse selection gates, and keep the selected object
        // because its selection timestamp drives the original bracket jump animation.
        static bool TrySelectionTarget(Pawn colonist, out object target)
        {
            target = colonist.Dead ? (object)colonist.Corpse : colonist;
            var selector = Find.Selector;
            if (selector == null) return false;

            if (!selector.SelectedObjects.Contains(target)) return false;

            if (!WorldRendererUtility.WorldSelected) return true;

            var caravan = CaravanUtility.GetCaravan(colonist);
            var worldSelector = Find.WorldSelector;
            if (caravan == null || worldSelector == null || !worldSelector.IsSelected(caravan))
                return false;

            target = caravan;
            return true;
        }

        public static void ClearDeferredSelection()
        {
            DeferredSelections.Clear();
        }

        public static void DrawDeferredSelection()
        {
            if (DeferredSelections.Count == 0) return;

            try
            {
                // The queue is consumed while AgentScroll's group is still open. The face is
                // therefore in exactly the same local coordinates as the portrait, with no
                // second body/scroll conversion that could move the four brackets apart.
                foreach (var selection in DeferredSelections)
                {
                    var corners = FaceplateRect(selection.Face).ContractedBy(SelectionInset);
                    DrawSelectionOverlay(selection.Target, corners, selection.BarScale);
                }
            }
            finally
            {
                ClearDeferredSelection();
            }
        }

        static void DrawSelectionOverlay(object target, Rect corners, float barScale)
        {
            if (Event.current.type != EventType.Repaint) return;

            var tex = SelectionDrawerUtility.SelectedTexGUI;
            float scale = 0.4f * barScale;
            var size = new Vector2(tex.width * scale, tex.height * scale);
            SelectionDrawerUtility.CalculateSelectionBracketPositionsUI(
                SelectionCorners, target, corners, SelectionDrawer.SelectTimes,
                size, 20f * barScale);

            var oldColor = GUI.color;
            GUI.color = new Color(oldColor.r, oldColor.g, oldColor.b, SelectionAlpha);
            try
            {
                float angle = 90f;
                for (int i = 0; i < SelectionCorners.Length; i++, angle += 90f)
                    DrawSelectionCorner(SelectionCorners[i], size, tex, angle);
            }
            finally
            {
                GUI.color = oldColor;
            }
        }

        static void DrawSelectionCorner(Vector2 center, Vector2 size, Texture2D tex, float angle)
        {
            var rect = new Rect(center.x - size.x / 2f, center.y - size.y / 2f,
                size.x, size.y);
            var matrix = GUI.matrix;
            try
            {
                // GUIUtility's rotation pivot is screen-global while the rect remains local
                // to BeginScrollView. Converting exactly here preserves both the scroll
                // transform and vanilla's rotated bracket texture.
                GUIUtility.RotateAroundPivot(angle, GUIUtility.GUIToScreenPoint(center));
                GUI.DrawTexture(rect, tex);
            }
            finally
            {
                GUI.matrix = matrix;
            }
        }
    }
}
