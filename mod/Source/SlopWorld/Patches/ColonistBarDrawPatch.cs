using System;
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
        // The portrait camera looks down -Y with world +Z up, so z pans to the head.
        const float HeadFallbackZ = 0.34f;

        // Slightly wider than a tight head crop so hair and clothing have breathing room.
        const float FaceZoom = 1.9f;

        // Aim just above the head anchor to place the pawn slightly lower in the portrait.
        const float FaceVerticalOffset = 0.03f;

        // Hair and clothing are allowed to extend past the icon. Keep the old square centered
        // in a taller portrait so the face does not move while both ends can overflow.
        const float PortraitOverflow = 0.35f;

        // Keep stopped agents recognizable while making their state obvious.
        static readonly Color DownTint = new Color(0.50f, 0.50f, 0.50f, 1f);
        const float SelectionInset = 2f;

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

        // Keep cache parameters unscaled so compact layouts reuse the same square texture.
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

            var session = AgentColony.Current?.SessionOf(colonist);
            bool isDown = false;
            if (session != null)
            {
                var info = SessionHub.Instance.Get(session);
                isDown = info?.State == AgentState.Down;
            }

            if (highlight)
            {
                int thickness = face.width <= 22f ? 2 : 3;
                GUI.color = Color.white;
                Widgets.DrawBox(face, thickness);
            }

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
            var portrait = new Rect(face.x, face.y - face.height * PortraitOverflow,
                face.width, face.height * (1f + 2f * PortraitOverflow));
            GUI.DrawTexture(portrait, renderTexture);
            GUI.color = Color.white;

            // Draw corners after the opaque square portrait so their inner arms remain visible.
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

            // Vanilla's brackets extend from the supplied rect. Keep them inside the
            // portrait row after the face has been made a little more compact.
            var corners = texRect.ContractedBy(SelectionInset);

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
