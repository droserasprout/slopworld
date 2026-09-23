using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Prevent agent construction failures by setting the success chance to one.
    // Apply the StatPart only to agent pawns.
    public class StatPart_SteadyHands : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (val >= 1f || !req.HasThing) return;
            if (AgentColony.IsAgent(req.Thing as Pawn)) val = 1f;
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!req.HasThing || !AgentColony.IsAgent(req.Thing as Pawn)) return null;
            return "Clanker hands: 100%";
        }
    }

    // Add the custom stat part to the base game definition at startup.
    [StaticConstructorOnStartup]
    public static class SteadyHands
    {
        static SteadyHands()
        {
            // Apply this change only in a SlopWorld profile. See ModProfile.
            if (!ModProfile.Ok) return;

            var stat = StatDefOf.ConstructSuccessChance;
            if (stat == null) return;

            if (stat.parts == null) stat.parts = new List<StatPart>();
            stat.parts.Add(new StatPart_SteadyHands { parentStat = stat });
        }
    }
}
