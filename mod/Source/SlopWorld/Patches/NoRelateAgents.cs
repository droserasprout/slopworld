using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Vanilla builds a new pawn's family out of everyone already alive:
    // GeneratePawnRelations weights every (candidate, relation) pair by that
    // relation's GenerationChance. The colonists here are in that pool, so a wanderer
    // walking in off the edge could be handed one for a parent - which is a crash
    // rather than a curiosity, because PawnRelationWorker_Parent.ResolveMyName reads
    // the parent's surname with a plain (NameTriple) cast and an agent's name is a
    // NameSingle. It surfaced as Outskirts logging "Specified cast is not valid" and
    // the arrival never landing.
    //
    // Patching the cast would be the smaller change and the wrong one: the relation
    // would still be made, and a stranger who is an agent's daughter is colony sim in
    // a game with the colony sim torn out. Zeroing the weight is all it takes - with
    // every candidate at zero, RandomElementByWeightWithDefault hands back the
    // default pair, whose pawn is null, and vanilla's own null check declines to
    // create anything. The other direction needs nothing: AgentColony already asks
    // for canGeneratePawnRelations: false.
    //
    // Applied by hand because GenerationChance is virtual and Harmony patches one
    // body at a time: a patch on the base alone would catch only the workers that
    // never overrode it, which is none of the ones that matter.
    public static class Patch_NoRelateAgents
    {
        public static void Apply(Harmony h)
        {
            var post = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_NoRelateAgents), nameof(Postfix)));

            foreach (var type in GenTypes.AllSubclassesNonAbstract(typeof(PawnRelationWorker)))
            {
                // DeclaredMethod, so a worker that inherits the base implementation is patched
                // once - on the base, below - rather than once per subclass, which Harmony would
                // refuse the second time.
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
