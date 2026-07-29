using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Vanilla rolls ConstructSuccessChance once per work tick, and a roll that comes up
    // short eats the frame's materials and every minute anybody put into it. On a map
    // with an economy that is a lesson about who you gave the hammer to; here it is the
    // board deleting an hour of somebody's tokens because the pawn generator rolled a
    // backstory. Worksite states its errands in minutes and they have to mean it.
    //
    // A StatPart rather than a patch on the driver: the roll is what wants changing,
    // not the job, and this is the seam the game itself provides. Agents only - the
    // stat still means what it means for anybody else, not that anybody else on this
    // map ever builds anything.
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
            var stat = StatDefOf.ConstructSuccessChance;
            if (stat == null) return;

            if (stat.parts == null) stat.parts = new List<StatPart>();
            stat.parts.Add(new StatPart_SteadyHands { parentStat = stat });
        }
    }
}
