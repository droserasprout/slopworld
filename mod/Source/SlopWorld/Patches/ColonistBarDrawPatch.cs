using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using RimWorld.Planet;
using Verse;

namespace SlopWorld
{
    // The sidebar column draws a close-up of the head where vanilla draws a body cropped at
    // the hips. The blue background and the mood indicators go with the body, and a stopped
    // agent is greyed rather than crossed.
    //
    // The vanilla DrawColonist draws in this order:
    //   1. mood atlas (coloured border)
    //   2. BGTex (blue background)
    //   3. mood background (blue bar)
    //   4. mood solid overlay
    //   5. selection border
    //   6. pawn texture via PortraitsCache.Get (full body)
    //   7. mood gradient
    //   8. icons via DrawIcons
    //   9. dead overlay
    //  10. pawn label
    //
    // We replace all of it with: highlight, head-only portrait, selection brackets, icons, dead
    // overlay. The pawn label is declined separately by Patch_SidebarPawnLabel.
    //
    // Everything is drawn in the square AgentSidebar.Place laid out, asked for rather than
    // worked out here: the row, the labels, the click and now the portrait are four readers of
    // one table, and the vanilla geometry this replaces (a 46x75 texture anchored to the
    // bottom of a 48x48 cell, overhanging 27*scale above it) describes a shape that is no
    // longer being drawn.
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist))]
    public static class Patch_SidebarPortraitDraw
    {
        // The portrait camera sits at (0, 10, 0) looking straight down -Y, orthographic, near
        // plane 5 and far plane 12 (PawnCacheCameraManager.CreatePawnCacheCamera). Euler
        // (90, 0, 0) puts its up vector on world +Z, and PawnCacheRenderer.RenderPawn only
        // ever does `transform.position += cameraOffset`. So z pans the shot, x slides it
        // sideways, and *y is the view axis* - offsetting it moves the camera towards its own
        // far plane and changes no framing at all. Vanilla pans in z throughout: the colonist
        // bar's own PawnTextureCameraOffset is (0, 0, 0.3) and the styling station's is
        // (0, 0, 0.15).
        //
        // The z to aim at is the pawn's own and is read per pawn below, not written down.
        // 0.34 is what every adult human body type states, and the fallback for a pawn whose
        // renderer will not answer.
        const float HeadFallbackZ = 0.34f;

        // orthographicSize is 1/cameraZoom, and orthographicSize is half the framed height in
        // *world units* - so this is a window onto the pawn rather than a magnification.
        // Vanilla's 1.28205 frames 1.56 units, which is the head and the torso. A head with
        // hair is about 0.55 units tall: this mod's own faceplate spans z 0.07..0.46 on the
        // 1.5-unit hair mesh it is drawn on, and the head node's centre is z 0.34. 3 frames
        // 0.667 units, z 0.007..0.673 - the whole head with a little air over it.
        const float FaceZoom = 3.0f;

        // Grey tint for downed agents. The portrait is recognisably the same colonist, just
        // drained of colour. 0.5 is light enough to read the face, dark enough to see it is
        // stopped.
        static readonly Color DownTint = new Color(0.50f, 0.50f, 0.50f, 1f);

        // Reflection targets for private members on ColonistBarColonistDrawer.
        static MethodInfo _drawSelectionOverlay;
        static MethodInfo _drawCaravanSelectionOverlay;
        static MethodInfo _drawIcons;
        static FieldInfo _deadColonistTex;

        static bool _ready;

        static Patch_SidebarPortraitDraw()
        {
            var t = typeof(ColonistBarColonistDrawer);
            _drawSelectionOverlay = t.GetMethod("DrawSelectionOverlayOnGUI",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _drawCaravanSelectionOverlay = t.GetMethod("DrawCaravanSelectionOverlayOnGUI",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _drawIcons = t.GetMethod("DrawIcons",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _deadColonistTex = t.GetField("DeadColonistTex",
                BindingFlags.Static | BindingFlags.NonPublic);

            _ready = _drawIcons != null &&
                _drawSelectionOverlay != null && _deadColonistTex != null;
            if (!_ready)
                Log.Error("[SlopWorld] Patch_SidebarPortraitDraw: one or more private " +
                    "members not found; sidebar portraits will fall back to vanilla.");
        }

        // Asked at the unscaled figure and drawn into the scaled box, which is what vanilla
        // does with its own 46x75: the cache is keyed on the params and not on the rect, so a
        // column that has shrunk to fit reuses one texture instead of minting one per scale.
        // Square, because the shot is.
        static Vector2 TextureSize
        {
            get
            {
                float side = ColonistBarColonistDrawer.PawnTextureSize.y;
                return new Vector2(side, side);
            }
        }

        // PawnRenderer.BaseHeadOffsetAt is bodyType.headOffset.y * sqrt(bodySizeFactor) for a
        // south-facing pawn, so the head's own centre is one call away and a child or a body
        // type with a different offset frames itself.
        static Vector3 FaceOffset(Pawn pawn)
        {
            float z = HeadFallbackZ;
            try
            {
                var renderer = pawn.Drawer == null ? null : pawn.Drawer.renderer;
                if (renderer != null)
                {
                    float own = renderer.BaseHeadOffsetAt(Rot4.South).z;
                    // BaseHeadOffsetAt logs and answers zero for a pawn it cannot read, which
                    // would aim at the navel.
                    if (own > 0f) z = own;
                }
            }
            catch (Exception)
            {
                // A pawn mid-generation has no draw tracker yet, and a portrait aimed at the
                // wrong place beats a bar that throws.
            }
            return new Vector3(0f, 0f, z);
        }

        // When the column is drawing, return false to replace the vanilla draw entirely.
        static bool Prefix(Rect rect, Pawn colonist, Map pawnMap, bool highlight, bool reordering,
            ColonistBarColonistDrawer __instance)
        {
            if (!_ready) return true;
            // The column's own flag. ColonistBarStrip.Drawing is the *strip* layout's, set for
            // the length of the terminal's call to the bar - true in this layout only while a
            // pane is open, and true in the other one where nothing here should fire.
            if (!AgentSidebar.Drawing) return true;
            if (colonist == null) return true;

            // No box means the column laid no row out for this pawn, so there is nowhere to put
            // a face and vanilla can have it.
            Rect face;
            if (!AgentSidebar.FaceBox(colonist, out face)) return true;

            // Alpha: vanilla starts DrawColonist by reading the entry's rect alpha and halving
            // it while a drag is over the bar. Read off the cell, which is the rect vanilla
            // hands that method.
            var bar = Find.ColonistBar;
            float alpha = bar.GetEntryRectAlpha(rect);
            if (reordering) alpha *= 0.5f;

            // State: is this an agent, and is its process down?
            var session = AgentColony.Current?.SessionOf(colonist);
            bool isDown = false;
            if (session != null)
            {
                var info = SessionHub.Instance.Get(session);
                isDown = info?.State == AgentState.Down;
            }

            // ------------------------------------------------------------------
            // 1. Highlight border (white box around the portrait when moused
            //    over, drawn before it so the texture overlaps it).
            // ------------------------------------------------------------------
            if (highlight)
            {
                int thickness = face.width <= 22f ? 2 : 3;
                GUI.color = Color.white;
                Widgets.DrawBox(face, thickness);
            }

            // ------------------------------------------------------------------
            // 2. Head-only portrait.
            // ------------------------------------------------------------------
            // Render the head standing up even if the pawn is downed: PortraitParams
            // .RenderPortrait turns a Down pawn 85 degrees and shifts it, which lays the head
            // out of a shot framed this tightly.
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
            GUI.DrawTexture(face, renderTexture);
            GUI.color = Color.white;

            // ------------------------------------------------------------------
            // 3. Selection brackets, *after* the portrait rather than before it
            //    as vanilla does. Vanilla draws a body into a cell it does not
            //    fill, so brackets underneath it still show at the corners; this
            //    column crops a head to the square, and an opaque texture over
            //    the whole box takes the inner arm of every bracket with it.
            // ------------------------------------------------------------------
            DrawSelection(__instance, colonist, face);

            // ------------------------------------------------------------------
            // 4. Icons. Vanilla draws them at 0.8 of the entry alpha, along the
            //    bottom edge of the rect it is handed (rect.x + 1, rect.yMax - 1),
            //    which is why this hands it the face box: the cell is smaller than
            //    what is drawn, and icons anchored to it float in the middle of a
            //    face. Their size is off PawnTextureSize and the bar's scale, so it
            //    is the same either way. Our own agent-state icons ride along in the
            //    Patch_ColonistBarStateIcon postfix.
            // ------------------------------------------------------------------
            if (_drawIcons != null)
            {
                GUI.color = new Color(1f, 1f, 1f, alpha * 0.8f);
                _drawIcons.Invoke(__instance, new object[] { face, colonist });
                GUI.color = Color.white;
            }

            // ------------------------------------------------------------------
            // 5. Dead overlay, over the portrait rather than over the cell.
            // ------------------------------------------------------------------
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

            // Skip the vanilla draw entirely.
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

            if (!WorldRendererUtility.WorldSelected)
            {
                _drawSelectionOverlay?.Invoke(drawer, new object[] { colonist, texRect });
            }
            else
            {
                var caravan = CaravanUtility.GetCaravan(colonist);
                if (caravan == null) return;
                var worldSelector = Find.WorldSelector;
                if (worldSelector != null && worldSelector.IsSelected(caravan))
                    _drawCaravanSelectionOverlay?.Invoke(drawer,
                        new object[] { caravan, texRect });
            }
        }
    }
}
