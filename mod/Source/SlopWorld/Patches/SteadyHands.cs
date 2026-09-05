using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Agents must not lose construction progress to vanilla's per-tick success roll.
    // Override the stat through a `StatPart`, scoped to agent pawns.
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

    // Added to the def rather than written into one, because the stat is vanilla's and
    // the part is ours.
    [StaticConstructorOnStartup]
    public static class SteadyHands
    {
        static SteadyHands()
        {
            // A part welded onto a vanilla stat is exactly the sort of thing that has no
            // business happening in somebody's own game. See ModProfile.
            if (!ModProfile.Ok) return;

            var stat = StatDefOf.ConstructSuccessChance;
            if (stat == null) return;

            if (stat.parts == null) stat.parts = new List<StatPart>();
            stat.parts.Add(new StatPart_SteadyHands { parentStat = stat });
        }
    }
}
