using HarmonyLib;
using RimWorld;

namespace SlopWorld
{
    // Hide version information in the menu corner because the About tab provides it.
    [HarmonyPatch(typeof(VersionControl), nameof(VersionControl.DrawInfoInCorner))]
    public static class Patch_VersionCorner
    {
        static bool Prefix() => false;
    }
}
