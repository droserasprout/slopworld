using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Agents need Construction even when a generated backstory disables ManualSkilled.
    // Remove only that cached work type; vanilla rebuilds the cache as needed.
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
