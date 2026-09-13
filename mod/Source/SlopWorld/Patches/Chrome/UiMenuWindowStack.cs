using HarmonyLib;
using Verse;

namespace SlopWorld
{
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Notify_ClickedInsideWindow))]
    static class Patch_UiMenuWindowStack
    {
        static void Prefix(Window window)
        {
            // Vanilla promotes a clicked non-modal window before closing menus above it.
            // The fullscreen terminal then hides UiMenu without removing it, so sidebar
            // hover stays blocked. Close menus before that promotion; clicks within the
            // menu chain retain vanilla's child-dismissal behavior.
            if (!(window is UiMenu)) UiMenu.CloseAll();
        }
    }
}
