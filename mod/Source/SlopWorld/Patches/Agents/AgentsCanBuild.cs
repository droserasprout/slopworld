using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Permit agent construction even when a generated backstory disables ManualSkilled.
    // Remove Construction from the disabled work types. The base game rebuilds this cache as necessary.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetDisabledWorkTypes))]
    public static class Patch_AgentsCanBuild
    {
        static void Postfix(Pawn __instance, List<WorkTypeDef> __result)
        {
            if (__result == null || __result.Count == 0) return;
            if (!AgentColony.IsAgent(__instance)) return;
            __result.Remove(WorkTypeDefOf.Construction);
        }
    }
}
