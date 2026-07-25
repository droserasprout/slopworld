using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The vanilla colonist bar shows every map's pawns, so it dims entries that
    /// are not on the map being viewed. There is exactly one map and one colony
    /// here, but a downed agent's pawn counts as somewhere else for the bar's
    /// purposes and a stopped process ends up rendered ghosted - the exact
    /// opposite of how present a working agent should look next to it.
    /// </summary>
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), "ApplyEntryInAnotherMapAlphaFactor")]
    public static class Patch_ColonistBarNoOtherMapDim
    {
        static bool Prefix() => false;
    }
}
