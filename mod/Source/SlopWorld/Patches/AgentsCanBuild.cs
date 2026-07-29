using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // A colonist is a status light, and the light has to work whatever the pawn
    // generator handed it. Roughly one backstory in five disables ManualSkilled, which
    // takes Construction with it, and Worksite would then have an agent that stood
    // about through every burst it ever worked - a lie about the session, told by a
    // detail of a system this mod otherwise has no use for.
    //
    // The list this postfixes is the pawn's own cache, so removing from it is what
    // makes the answer stick until vanilla rebuilds it, at which point this runs again
    // on the way out. Only Construction, and only for agents: a wanderer walking in off
    // the edge keeps whatever it was born with.
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
