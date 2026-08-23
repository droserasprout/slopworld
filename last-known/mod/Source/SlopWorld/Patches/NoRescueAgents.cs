using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // A stopped agent's colonist is Downed, which pops a "needs rescuing" alert and
    // sends other agents hauling it to a bed. Both the alert and the rescue work gate
    // on WantsToBeRescued, so denying it silences both at once.
    [HarmonyPatch(typeof(HealthAIUtility), nameof(HealthAIUtility.WantsToBeRescued))]
    public static class Patch_NoRescueAgents
    {
        static void Postfix(Pawn pawn, ref bool __result)
        {
            if (!__result) return;
            var colony = AgentColony.Current;
            if (colony != null && colony.IsAgentPawn(pawn)) __result = false;
        }
    }
}
