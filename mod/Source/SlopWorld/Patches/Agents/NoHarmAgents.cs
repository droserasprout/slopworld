using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // Agent pawns represent session state. Prevent damage and animal attacks.
    // Damage immunity alone permits repeated attacks. Blocking attacks alone does not prevent other damage.

    // Mark agent damage as absorbed before Pawn.PreApplyDamage runs.
    // This prevents injuries from downing an agent independently of its session state.
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

    // Exclude agents from predator prey selection, including downed agents.
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.IsAcceptablePreyFor))]
    public static class Patch_NoPreyOnAgents
    {
        static void Postfix(Pawn prey, ref bool __result)
        {
            if (__result && AgentColony.IsAgent(prey)) __result = false;
        }
    }

    // Exclude agents from animal target selection through the caller validator.
    [HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestAttackTarget))]
    public static class Patch_NoTargetingAgents
    {
        // Reuse this delegate when the caller supplies no validator to avoid allocating a new closure.
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

    // Block animal melee attacks against agents. Replace pet attacks with nuzzling.
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
            return false; // suppresses the swing and verb selection
        }
    }

    // Prevent fire attachment and fire damage for player faction pawns.
    // Both checks are necessary because unattached fires can also damage pawns.
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

        // Use the private method name to intercept fire damage.
        [HarmonyPatch(typeof(Fire), "DoFireDamage")]
        public static class Patch_NoFireDamage
        {
            static bool Prefix(Thing targ) => !Spared(targ);
        }
    }
}
