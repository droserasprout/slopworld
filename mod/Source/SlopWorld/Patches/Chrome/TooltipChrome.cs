using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // TipRegion registers tooltip text and delay. ActiveTip draws the tooltip.
    // Replace the background and text style during drawing to include deferred tooltips and map tooltips.
    // Retain base game sizing, placement, delay, and stacking.
    [HarmonyPatch(typeof(ActiveTip), "DrawInner")]
    public static class Patch_TooltipChrome
    {
        static bool Prefix(Rect bgRect, string label)
        {
            Slab.Box(bgRect, UiTheme.PopoverBg, UiTheme.Edge);

            var wasFont = Text.Font;
            var wasColor = GUI.color;
            Text.Font = GameFont.Small;
            GUI.color = UiTheme.Lead;
            Widgets.Label(bgRect.ContractedBy(4f), label);
            Text.Font = wasFont;
            GUI.color = wasColor;
            return false;
        }
    }
}
