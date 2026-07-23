using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// An agent's colonist goes Downed while its process is stopped. Untouched, that
    /// pops a "colonist needs rescuing" alert and sends other agents hauling it to a
    /// bed. Both the alert (Alert_ColonistNeedsRescuing.NeedsRescue) and the rescue
    /// work (WorkGiver_RescueDowned via HealthAIUtility.CanRescueNow) gate on
    /// WantsToBeRescued, so denying it for agent pawns silences both at once.
    /// </summary>
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
