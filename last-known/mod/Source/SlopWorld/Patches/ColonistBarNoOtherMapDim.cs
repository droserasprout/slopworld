using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // The bar dims entries that are not on the map being viewed. There is one map
    // here, but a downed agent's pawn counts as somewhere else for the bar's purposes
    // and a stopped process ends up ghosted.
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), "ApplyEntryInAnotherMapAlphaFactor")]
    public static class Patch_ColonistBarNoOtherMapDim
    {
        static bool Prefix() => false;
    }
}
