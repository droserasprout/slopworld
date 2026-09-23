using HarmonyLib;
using Verse;

namespace SlopWorld
{
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Notify_ClickedInsideWindow))]
    static class Patch_UiMenuWindowStack
    {
        static void Prefix(Window window)
        {
            // The base game raises a clicked non-modal window before closing menus above it.
            // A raised terminal can hide menus that still block sidebar input.
            // Close menus first for clicks outside them. Retain normal submenu dismissal for clicks inside menus.
            if (!(window is UiMenu)) UiMenu.CloseAll();
        }
    }
}
