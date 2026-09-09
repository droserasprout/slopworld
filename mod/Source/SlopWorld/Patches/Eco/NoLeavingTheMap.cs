using HarmonyLib;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    [HarmonyPatch(typeof(JobGiver_ExitMap), "TryGiveJob")]
    public static class Patch_NoLeavingTheMap
    {
        public static bool Stays(Pawn pawn) =>
            pawn != null && pawn.Faction == null
            && pawn.RaceProps != null && pawn.RaceProps.Humanlike;

        static bool Prefix(Pawn pawn, ref Job __result)
        {
            if (!Stays(pawn)) return true;
            __result = null;
            return false;
        }
    }
}
