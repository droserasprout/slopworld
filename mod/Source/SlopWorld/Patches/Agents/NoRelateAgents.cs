using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Exclude agents from relation candidates. Parent naming expects NameTriple, but agents use NameSingle.
    // Patch each worker that declares GenerationChance because the method is virtual.
    public static class Patch_NoRelateAgents
    {
        public static void Apply(Harmony h)
        {
            var post = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_NoRelateAgents), nameof(Postfix)));

            foreach (var type in GenTypes.AllSubclassesNonAbstract(typeof(PawnRelationWorker)))
            {
                // Select only declared methods to avoid patching inherited implementations repeatedly.
                // Patch the base implementation separately below.
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
