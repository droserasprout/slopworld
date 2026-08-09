using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // A "+" slot after the last colonist opens the new-session dialog. Postfixed onto
    // the bar's OnGUI, so the slot draws in whichever view called it - map or terminal -
    // from the cell geometry ColonistBarStrip reserved for it on the way in. One slot,
    // one place, so toggling a pane never moves it.
    [HarmonyLib.HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    public static class Patch_ColonistBarAddButton
    {
        static void Postfix()
        {
            // The map-layer call while a terminal is up drew nothing; the terminal's own
            // call is the one that counts. Runs before the layout finalizer, so AddRect is
            // still the strip's.
            if (ColonistBarStrip.Suppressed || !ColonistBarStrip.ShowAdd) return;

            var rect = ColonistBarStrip.AddRect;
            if (rect.width <= 0f) return;

            // Background highlight on hover, matching the shortcuts tab's plus.
            if (ColonistBarStrip.MouseOver(rect)) Widgets.DrawHighlight(rect);

            var wasAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            GUI.color = ColonistBarStrip.MouseOver(rect) ? SlopWidgets.Lead : SlopWidgets.Dim;
            Widgets.Label(rect, "+");
            GUI.color = Color.white;
            Text.Anchor = wasAnchor;

            TooltipHandler.TipRegion(rect, "Add agent");

            if (!ColonistBarStrip.Interactive) return;

            if (Widgets.ButtonInvisible(rect, false))
                TerminalWindow.OpenOverPane(new EditSessionDialog(null));
        }
    }
}