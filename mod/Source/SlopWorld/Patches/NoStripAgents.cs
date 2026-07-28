using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // An agent's colonist is the daemon's, not the player's to loot. The "Strip"
    // order comes from FloatMenuOptionProvider_Strip.GetSingleOptionFor.
    [HarmonyPatch(typeof(FloatMenuOptionProvider_Strip), "GetSingleOptionFor",
        new[] { typeof(Thing), typeof(FloatMenuContext) })]
    public static class Patch_NoStripAgents
    {
        static bool Prefix(Thing clickedThing, ref FloatMenuOption __result)
        {
            var colony = AgentColony.Current;
            if (clickedThing is Pawn p && colony != null && colony.IsAgentPawn(p))
            {
                __result = null;
                return false; // skip the original: no strip option for agents
            }
            return true;
        }
    }
}
