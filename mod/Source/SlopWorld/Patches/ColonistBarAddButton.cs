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

            // Look like a colonist slot: the bar's own background, then a plus.
            GUI.DrawTexture(rect, ColonistBar.BGTex);
            if (Mouse.IsOver(rect)) Widgets.DrawHighlight(rect);

            var icon = rect.ScaledBy(0.5f);
            icon.center = rect.center;
            GUI.DrawTexture(icon, TexButton.Plus);

            TooltipHandler.TipRegion(rect, "Add agent");

            if (ColonistBarStrip.Blocked) return;

            if (Widgets.ButtonInvisible(rect, false))
                TerminalWindow.OpenOverPane(new EditSessionDialog(null));
        }
    }
}
