using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Keep every entry at full location opacity, including entries in world view.
    // Agent visibility is intentionally independent of the viewed map or caravan;
    // the sidebar portrait owns stopped-session tint and the bar retains drag fades.
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), "ApplyEntryInAnotherMapAlphaFactor")]
    public static class Patch_ColonistBarNoOtherMapDim
    {
        static bool Prefix() => false;
    }
}
