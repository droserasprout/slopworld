using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Exclude agents from relation candidates. Parent naming expects NameTriple, but agents use NameSingle.
    // Patch unique effective implementations, including overrides on abstract ancestors.
    public static class Patch_NoRelateAgents
    {
        public static void Apply(Harmony h)
        {
            var post = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_NoRelateAgents), nameof(Postfix)));

            foreach (var method in EffectiveMethods.Find(typeof(PawnRelationWorker),
                GenTypes.AllSubclassesNonAbstract(typeof(PawnRelationWorker)), "GenerationChance"))
                h.Patch(method, postfix: post);
        }

        static void Postfix(Pawn other, ref float __result)
        {
            if (__result != 0f && AgentColony.IsAgent(other)) __result = 0f;
        }
    }
}
