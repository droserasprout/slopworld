using HarmonyLib;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // Use the daemon state indicator instead of the base game idle indicator.
    // Prevent the base game from classifying agent pawns as idle.
    [HarmonyPatch(typeof(Pawn_MindState), nameof(Pawn_MindState.IsIdle), MethodType.Getter)]
    public static class Patch_AgentNeverIdle
    {
        static void Postfix(Pawn_MindState __instance, ref bool __result)
        {
            if (__result && AgentColony.IsAgent(__instance.pawn)) __result = false;
        }
    }
}
