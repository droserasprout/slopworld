using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The colonist bar is the natural place to grow the colony: a "+" slot after
    // the last colonist opens the new-session dialog, same one the Agents tab
    // uses. We postfix the bar's OnGUI so the slot is laid out with the bar's own
    // cached draw locs and scale, which keeps it on the same row and at the same
    // size as a real colonist for any number of agents.
    [HarmonyLib.HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    public static class Patch_ColonistBarAddButton
    {
        // Mirror ColonistBar.Visible (private): the bar hides itself under 800x500
        // and while the tile picker is up, and so should we.
        static bool BarShown =>
            UI.screenWidth >= 800 && UI.screenHeight >= 500 && !Find.TilePicker.Active;

        static void Postfix()
        {
            if (!BarShown || IntroDirector.UiHidden) return;

            var bar = Find.ColonistBar;
            var locs = bar.DrawLocs;
            var size = bar.Size; // BaseSize * Scale

            Vector2 loc;
            if (locs != null && locs.Count > 0)
            {
                // One slot to the right of the rightmost colonist.
                var last = locs[locs.Count - 1];
                loc = new Vector2(
                    last.x + size.x + bar.SpaceBetweenColonistsHorizontal,
                    last.y);
            }
            else
            {
                // No colonists yet (daemon off, or fresh game): drop it where the
                // first colonist would sit, centered at the top of the screen.
                float scale = bar.Scale > 0f ? bar.Scale : 1f;
                loc = new Vector2(UI.screenWidth * 0.5f - size.x * 0.5f, 21f * scale);
            }

            // Clamp inside the screen so a long bar never pushes it off the right.
            loc.x = Mathf.Min(loc.x, UI.screenWidth - size.x - 4f);

            var rect = new Rect(loc.x, loc.y, size.x, size.y);

            // Look like a colonist slot: the bar's own background, then a plus.
            GUI.DrawTexture(rect, ColonistBar.BGTex);
            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);

            var icon = rect.ScaledBy(0.5f);
            icon.center = rect.center;
            GUI.DrawTexture(icon, TexButton.Plus);

            TooltipHandler.TipRegion(rect, "Add agent");

            if (Widgets.ButtonInvisible(rect, false))
                Find.WindowStack.Add(new EditSessionDialog(null));
        }
    }
}
