using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Disable the base game opacity reduction for entries on other maps.
    // This also prevents dimming stopped agents whose pawns the bar classifies as elsewhere.
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), "ApplyEntryInAnotherMapAlphaFactor")]
    public static class Patch_ColonistBarNoOtherMapDim
    {
        static bool Prefix() => false;
    }
}
