using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // TipRegion is used throughout our chrome, but it only registers the text and delay;
    // ActiveTip owns the bubble that eventually appears.  Styling at the registrations
    // would therefore miss both lazy tips and the map's thing tips.  Keep vanilla's sizing,
    // placement, delay and stacking and replace its tiled atlas at the final draw seam.
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
