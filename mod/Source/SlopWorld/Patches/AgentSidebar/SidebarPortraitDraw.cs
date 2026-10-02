using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Replace vanilla's body-and-mood portrait with a square head portrait using the
    // sidebar's shared geometry. Patch_SidebarPawnLabel suppresses the vanilla label.
    // Compact portraits intentionally omit selection brackets and all vanilla status overlays.
    // AgentSidebar owns daemon status; pawn simulation classification remains vanilla.
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist))]
    public static class Patch_SidebarPortraitDraw
    {
        // The portrait camera looks down -Y with world +Z up, so z pans to the head.
        const float HeadFallbackZ = 0.34f;

        // Slightly wider than a tight head crop so hair and clothing have breathing room.
        const float FaceZoom = 1.9f;

        // Aim just above the head anchor to place the pawn slightly lower in the portrait.
        const float FaceVerticalOffset = 0.03f;

        // Keep the drawn portrait square with the face box. The close-up is framed on the
        // head, so there is no body crop that needs extra vertical room.

        // Keep stopped agents recognizable while making their state obvious.
        static readonly Color DownTint = new Color(0.50f, 0.50f, 0.50f, 1f);

        // Keep callers anchored to the drawn portrait rather than duplicating its geometry.
        public static Rect PortraitRect(Rect face) => face;

        // Task messages use the same close-up as the sidebar, but are drawn outside the
        // colonist-bar pass. Returning the cached portrait keeps sender avatars consistent
        // with the faces the player already knows from the sidebar.
        public static Texture PortraitFor(Pawn pawn)
        {
            if (pawn == null) return null;
            try
            {
                return RequestPortrait(pawn);
            }
            catch (Exception)
            {
                // A pawn can be between generation and registration while a task window is
                // open. The caller has a generic sender glyph to use in that case.
                return null;
            }
        }

        // Render downed pawns upright: vanilla's rotation misses the tight head crop.
        static Texture RequestPortrait(Pawn pawn)
        {
            PawnHealthState? healthOverride = pawn.Dead || pawn.health == null
                ? (PawnHealthState?)null
                : pawn.health.State == PawnHealthState.Down
                    ? PawnHealthState.Mobile
                    : (PawnHealthState?)null;
            return PortraitsCache.Get(pawn, TextureSize, Rot4.South,
                FaceOffset(pawn), FaceZoom, true, true, true, true, null, null,
                false, healthOverride);
        }

        // The sidebar lays its rows out from the font and asks this what the portrait may be.
        // Therefore, the square pawn portrait fills the row it is in.
        public static float FaceForHeight(float height) => height;

        // Keep cache parameters unscaled so compact layouts reuse the same square texture.
        static Vector2 TextureSize
        {
            get
            {
                float side = ColonistBarColonistDrawer.PawnTextureSize.y;
                return new Vector2(side, side);
            }
        }

        // Use each pawn's scaled head offset. Children and unusual body types frame themselves.
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

        static bool Prefix(Rect rect, Pawn colonist, Map pawnMap, bool highlight, bool reordering)
        {
            if (!AgentSidebar.Drawing) return true;
            if (colonist == null) return true;

            Rect face;
            if (!AgentSidebar.FaceBox(colonist, out face)) return true;
            if (Event.current == null || Event.current.type != EventType.Repaint) return false;

            // Preserve vanilla's entry fade and drag fade.
            var bar = Find.ColonistBar;
            float alpha = bar.GetEntryRectAlpha(rect);
            if (reordering) alpha *= 0.5f;

            // A session the hub has not answered for yet is Down to the sidebar's row. Therefore,
            // It is Down here too: the alternative is a red badge over a fully lit face.
            var session = AgentColony.Current?.SessionOf(colonist);
            bool isDown = session != null &&
                (SessionHub.Instance.Get(session)?.State ?? AgentState.Down) == AgentState.Down;

            var renderTexture = RequestPortrait(colonist);

            GUI.color = isDown
                ? new Color(DownTint.r, DownTint.g, DownTint.b, alpha)
                : new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(PortraitRect(face), renderTexture);
            GUI.color = Color.white;

            // Draw hover feedback after the opaque portrait so it remains visible.
            if (highlight)
            {
                int thickness = face.width <= 22f ? 2 : 3;
                GUI.color = Color.white;
                Widgets.DrawBox(face, thickness);
            }

            return false;
        }
    }
}
