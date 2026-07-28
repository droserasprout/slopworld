using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A "+" slot after the last colonist opens the new-session dialog. Postfixed onto
    // the bar's OnGUI, so the slot is laid out with the bar's own cached draw locs
    // and scale and stays the size of a real colonist for any number of agents.
    [HarmonyLib.HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    public static class Patch_ColonistBarAddButton
    {
        // Mirrors ColonistBar.Visible, which is private: the bar hides itself under
        // 800x500 and while the tile picker is up.
        static bool BarShown =>
            UI.screenWidth >= 800 && UI.screenHeight >= 500 && !Find.TilePicker.Active;

        static void Postfix()
        {
            if (!BarShown || Cutscene.Playing) return;
            // Left off the strip above a terminal pane: the dialog it opens is a normal
            // window and the terminal draws on the Super layer, so the "+" there would open
            // something the terminal covers.
            if (ColonistBarOverlay.Active) return;

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
                // No colonists yet: drop it where the first would sit, centred at the top.
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
