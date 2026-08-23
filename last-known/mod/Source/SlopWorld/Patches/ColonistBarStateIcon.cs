using HarmonyLib;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // The daemon's state dot replaces the colonist bar's idle/down icon strip. Keep vanilla
    // from deciding that an agent is idle so it cannot reintroduce that indicator elsewhere.
    [HarmonyPatch(typeof(Pawn_MindState), nameof(Pawn_MindState.IsIdle), MethodType.Getter)]
    public static class Patch_AgentNeverIdle
    {
        static void Postfix(Pawn_MindState __instance, ref bool __result)
        {
            if (__result && AgentColony.IsAgent(__instance.pawn)) __result = false;
        }
    }
}
