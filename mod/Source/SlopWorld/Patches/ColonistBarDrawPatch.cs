using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using RimWorld.Planet;
using Verse;

namespace SlopWorld
{
    // Replace vanilla's body-and-mood portrait with a 3:4 head portrait using the
    // sidebar's shared geometry. Patch_SidebarPawnLabel suppresses the vanilla label.
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist))]
    public static class Patch_SidebarPortraitDraw
    {
        // The portrait camera looks down -Y with world +Z up, so z pans to the head.
        const float HeadFallbackZ = 0.34f;

        // Slightly wider than a tight head crop so hair and clothing have breathing room.
        const float FaceZoom = 1.9f;

        // Aim just above the head anchor to place the pawn slightly lower in the portrait.
        const float FaceVerticalOffset = 0.03f;

        // Hair and clothing are allowed to extend past the square face box. A sixth of the
        // box at each end makes the full drawn portrait exactly 3:4.
        const float PortraitAspect = 3f / 4f;
        const float PortraitOverflow = (1f / PortraitAspect - 1f) / 2f;

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
        const float SelectionScale = 1.2f;

        static MethodInfo _drawSelectionOverlay;
        static MethodInfo _drawCaravanSelectionOverlay;
        static FieldInfo _deadColonistTex;

        static bool _ready;

        static Patch_SidebarPortraitDraw()
        {
            var t = typeof(ColonistBarColonistDrawer);
            _drawSelectionOverlay = t.GetMethod("DrawSelectionOverlayOnGUI",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _drawCaravanSelectionOverlay = t.GetMethod("DrawCaravanSelectionOverlayOnGUI",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _deadColonistTex = t.GetField("DeadColonistTex",
                BindingFlags.Static | BindingFlags.NonPublic);

            _ready = _drawSelectionOverlay != null && _deadColonistTex != null;
            if (!_ready)
                Log.Error("[SlopWorld] Patch_SidebarPortraitDraw: one or more private " +
                    "members not found; sidebar portraits will fall back to vanilla.");
        }

        // What is actually on the screen, which is taller than the cell it is centred in.
        // Anything hung off the pawn rather than off the row - the sidebar's state badge -
        // wants this: the face box's lower edge is up at the chin.
        public static Rect PortraitRect(Rect face) =>
            new Rect(face.x, face.y - face.height * PortraitOverflow,
                face.width, face.height * (1f + 2f * PortraitOverflow));

        // The face box that draws to a wanted height. The sidebar lays its rows out from
        // the font and asks this what the portrait may be, so the pawn fills the row it is
        // in rather than being a fixed square the text grows past.
        public static float FaceForHeight(float height) =>
            height / (1f + 2f * PortraitOverflow);

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
            return rect;
        }

        // Keep cache parameters unscaled so compact layouts reuse the same portrait texture.
        static Vector2 TextureSize
        {
            get
            {
                float side = ColonistBarColonistDrawer.PawnTextureSize.y;
                return new Vector2(side, side * (1f + 2f * PortraitOverflow));
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
            return new Vector3(0f, 0f, z + FaceVerticalOffset);
        }

        static bool Prefix(Rect rect, Pawn colonist, Map pawnMap, bool highlight, bool reordering,
            ColonistBarColonistDrawer __instance)
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

            // Both marks go down after the opaque portrait, the way vanilla ends its
            // own draw: the corners so their inner arms remain visible, the hover box
            // because the portrait covers the rect it is drawn on.
            if (highlight)
            {
                int thickness = face.width <= 22f ? 2 : 3;
                GUI.color = Color.white;
                Widgets.DrawBox(face, thickness);
            }

            DrawSelection(__instance, colonist, face);

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

        static void DrawSelection(ColonistBarColonistDrawer drawer, Pawn colonist, Rect texRect)
        {
            var selector = Find.Selector;
            if (selector == null) return;

            bool selected;
            if (colonist.Dead)
            {
                selected = selector.SelectedObjects.Contains(colonist.Corpse);
            }
            else
            {
                selected = selector.SelectedObjects.Contains(colonist);
            }

            if (!selected) return;

            // Vanilla's brackets extend from the supplied rect. Put them over the visible
            // faceplate rather than around the transparent margin of the head texture.
            var corners = FaceplateRect(texRect).ContractedBy(SelectionInset);

            if (!WorldRendererUtility.WorldSelected)
            {
                _drawSelectionOverlay?.Invoke(drawer, new object[] { colonist, corners });
            }
            else
            {
                var caravan = CaravanUtility.GetCaravan(colonist);
                if (caravan == null) return;
                var worldSelector = Find.WorldSelector;
                if (worldSelector != null && worldSelector.IsSelected(caravan))
                    _drawCaravanSelectionOverlay?.Invoke(drawer,
                        new object[] { caravan, corners });
            }
        }
    }
}
