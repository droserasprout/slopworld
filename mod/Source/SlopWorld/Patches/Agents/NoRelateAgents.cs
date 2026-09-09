using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Exclude agents from vanilla's relation candidates. Parent naming casts names to
    // NameTriple, while agents use NameSingle; relations also do not belong in this sim.
    // GenerationChance is virtual, so every declaring worker must be patched separately.
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
