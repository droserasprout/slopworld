using HarmonyLib;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    [HarmonyPatch(typeof(JobGiver_ExitMap), "TryGiveJob")]
    public static class Patch_NoLeavingTheMap
    {
        static bool ShouldKeepFactionlessHumanlikeOnMap(Pawn pawn) =>
            pawn != null && pawn.Faction == null
            && pawn.RaceProps != null && pawn.RaceProps.Humanlike;

        static bool Prefix(Pawn pawn, ref Job __result)
        {
            if (!ShouldKeepFactionlessHumanlikeOnMap(pawn)) return true;
            // Decline the exit job so the think tree can fall through to wandering.
            __result = null;
            return false;
        }
    }
}
