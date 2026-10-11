using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // TipRegion registers tooltip text and delay. ActiveTip draws the tooltip.
    // Replace the background and text style during drawing to include deferred tooltips and map tooltips.
    // Share frozen line layout with sizing; retain native placement, delay, and stacking.
    [HarmonyPatch(typeof(ActiveTip), "DrawInner")]
    public static class Patch_TooltipChrome
    {
        static bool Prefix(ActiveTip __instance, Rect bgRect, string label)
        {
            using (WidgetState.Save())
            {
                Slab.Box(bgRect, UiTheme.PopoverBg, UiTheme.Edge);
                Text.Font = GameFont.Small;
                GUI.color = UiTheme.Lead;
                var layout = TooltipLayout.Get(__instance, label);
                GUI.Label(bgRect.ContractedBy(4f), layout.Text, layout.Style);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(ActiveTip), "TipRect", MethodType.Getter)]
    public static class Patch_TooltipSize
    {
        static bool Prefix(ActiveTip __instance, ref Rect __result)
        {
            var size = TooltipLayout.Get(__instance).Size;
            __result = new Rect(-4f, -4f, Mathf.Ceil(size.x + 8f), Mathf.Ceil(size.y + 8f));
            return false;
        }
    }
}
