using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // An agent's colonist is a status light, not a body in a fight. Both halves are needed:
    // immunity alone leaves a boar chewing on a colonist forever, and refusing the attack
    // alone leaves it mortal to everything else.

    // Every hit passes through Pawn.PreApplyDamage on its way from Thing.TakeDamage, which
    // stops as soon as the hit reports itself absorbed. A colonist downed by injury is downed
    // for reasons AgentColony knows nothing about, so it stays flat while its session works.
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_AgentsInvulnerable
    {
        static bool Prefix(Pawn __instance, ref bool absorbed)
        {
            if (!AgentColony.IsAgent(__instance)) return true;
            absorbed = true;
            return false;
        }
    }

    // Predators pick prey through IsAcceptablePreyFor, which weighs a downed pawn as
    // an easy meal - and every stopped agent is a downed pawn lying in the open.
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.IsAcceptablePreyFor))]
    public static class Patch_NoPreyOnAgents
    {
        static void Postfix(Pawn prey, ref bool __result)
        {
            if (__result && AgentColony.IsAgent(prey)) __result = false;
        }
    }

    // Hunting, revenge and manhunter rage all shop through BestAttackTarget, so folding agents
    // into the caller's validator takes them off the list at source.
    [HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestAttackTarget))]
    public static class Patch_NoTargetingAgents
    {
        // Most callers hand over no validator, and that path gets this same delegate rather
        // than a fresh closure per search.
        static readonly Predicate<Thing> NoAgents =
            t => !(t is Pawn p && AgentColony.IsAgent(p));

        static void Prefix(IAttackTargetSearcher searcher, ref Predicate<Thing> validator)
        {
            var hunter = searcher?.Thing as Pawn;
            if (hunter?.RaceProps == null || !hunter.RaceProps.Animal) return;
            if (AgentColony.Current == null) return;

            if (validator == null) { validator = NoAgents; return; }

            var inner = validator;
            validator = t => NoAgents(t) && inner(t);
        }
    }

    // The backstop: every melee swing goes through TryMeleeAttack. A colony pet's swing is
    // traded for a nuzzle rather than ignored, the animal having already walked all the way
    // over.
    [HarmonyPatch(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.TryMeleeAttack))]
    public static class Patch_NoMaulingAgents
    {
        static bool Prefix(Pawn_MeleeVerbs __instance, Thing target, ref bool __result)
        {
            var attacker = __instance.Pawn;
            if (attacker == null || attacker.RaceProps == null || !attacker.RaceProps.Animal)
                return true;
            if (!(target is Pawn victim) || !AgentColony.IsAgent(victim)) return true;

            if (Pets.Is(attacker)) Pets.NuzzleInstead(attacker, victim);
            __result = false;
            return false; // no swing, and no verb ever chosen for one
        }
    }

    // Attachment and cell damage are separate roads: closing only the first lets a pet cook by
    // walking into a fire, and closing only the second leaves an already-invulnerable agent
    // wearing a flame that never goes out. The whole player faction, matching
    // Plague.Infectable.
    public static class NoBurningTheColony
    {
        static bool Spared(Thing t) =>
            t is Pawn p && p.Faction != null && p.Faction.IsPlayer;

        [HarmonyPatch(typeof(FireUtility), nameof(FireUtility.CanEverAttachFire))]
        public static class Patch_NoFireAttaches
        {
            static void Postfix(Thing t, ref bool __result)
            {
                if (__result && Spared(t)) __result = false;
            }
        }

        // Private, so bound by name: every hit a Fire deals goes through here.
        [HarmonyPatch(typeof(Fire), "DoFireDamage")]
        public static class Patch_NoFireDamage
        {
            static bool Prefix(Thing targ) => !Spared(targ);
        }
    }
}
