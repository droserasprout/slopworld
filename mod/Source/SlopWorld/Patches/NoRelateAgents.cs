using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Nobody generated on this map is related to an agent.
    ///
    /// Vanilla builds a new pawn's family out of everyone already alive:
    /// <c>PawnGenerator.GeneratePawnRelations</c> takes
    /// <c>PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead</c>, weights every
    /// (candidate, relation) pair by that relation's <c>GenerationChance</c> and
    /// picks one. The colonists on this board are in that pool like anybody else,
    /// so an animal or a wanderer walking in off the edge could be handed one of
    /// them for a parent - and that is a crash rather than a curiosity, because
    /// <c>PawnRelationWorker_Parent.ResolveMyName</c> reads the parent's surname
    /// with a plain <c>(NameTriple)</c> cast and an agent's name is a
    /// <see cref="NameSingle"/>. It surfaced as <c>Outskirts</c> logging
    /// "Specified cast is not valid" and the arrival never landing.
    ///
    /// The cast could be patched instead, and it would be the smaller change and
    /// the wrong one: the relation would still be made, and a stranger who is an
    /// agent's daughter is a thread of colony sim in a colony sim that has been
    /// torn out. Agents are the daemon's, the same rule
    /// <see cref="Patch_AgentsInvulnerable"/> and the rescue and strip patches
    /// apply. Zeroing the weight is also all that is needed: with every candidate
    /// at zero, <c>RandomElementByWeightWithDefault</c> hands back the default
    /// pair, whose pawn is null, and vanilla's own null check declines to create
    /// anything.
    ///
    /// The other direction needs nothing - <see cref="AgentColony"/> generates its
    /// colonists with <c>canGeneratePawnRelations: false</c>, so an agent has never
    /// asked for a family of its own.
    ///
    /// Applied by hand because the target set is every subclass:
    /// <c>GenerationChance</c> is virtual and Harmony patches one method body, so a
    /// patch on <see cref="PawnRelationWorker"/> alone would catch only the workers
    /// that never overrode it - which is none of the ones that matter.
    /// </summary>
    public static class Patch_NoRelateAgents
    {
        public static void Apply(Harmony h)
        {
            var post = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_NoRelateAgents), nameof(Postfix)));

            foreach (var type in GenTypes.AllSubclassesNonAbstract(typeof(PawnRelationWorker)))
            {
                // DeclaredMethod, so a worker that inherits the base implementation
                // is patched once - on the base, below - rather than once per
                // subclass, which Harmony would refuse the second time.
                var m = AccessTools.DeclaredMethod(type, "GenerationChance");
                if (m != null) h.Patch(m, postfix: post);
            }

            h.Patch(AccessTools.DeclaredMethod(typeof(PawnRelationWorker), "GenerationChance"),
                postfix: post);
        }

        static void Postfix(Pawn other, ref float __result)
        {
            if (__result != 0f && AgentColony.IsAgent(other)) __result = 0f;
        }
    }
}
