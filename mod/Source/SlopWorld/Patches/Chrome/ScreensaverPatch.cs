using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // Gate the entire GUI dispatch, including tooltips, windows, overlays, and camera input.
    // World updates continue; Screensaver owns the temporary mode and shared backdrop draw.
    [HarmonyPatch(typeof(Root), nameof(Root.OnGUI))]
    internal static class Patch_Screensaver
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix() => !Screensaver.HandleGUI();
    }
}
