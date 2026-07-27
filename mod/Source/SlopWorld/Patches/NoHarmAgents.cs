using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // An agent's colonist is a status light for a process on the host, not a body in
    // a fight. Nothing on the map may hurt one, and the pets - the only other living
    // things here - may not even try.
    //
    // Both halves are needed. Immunity alone leaves a boar chewing on a colonist
    // forever, which reads as an attack whether or not it lands; refusing the attack
    // alone leaves the colonist mortal to everything else the map can still throw.

    /// <summary>
    /// Agents take no damage, from anything. Every hit a pawn receives passes through
    /// Pawn.PreApplyDamage on its way from Thing.TakeDamage, which stops as soon as
    /// the hit reports itself absorbed - so claiming absorption here drops bites,
    /// blasts, fire and falling rock alike before any of it reaches the health tracker.
    ///
    /// This matters more than it looks. A colonist downed by injury is downed for
    /// reasons AgentColony knows nothing about: the reconcile clears the offline
    /// hediff and calls the agent awake, and the pawn stays flat on the floor while
    /// its session reports Working. The board then lies about the one thing it exists
    /// to show.
    /// </summary>
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

    /// <summary>
    /// Agents are not food. Predators pick prey through FoodUtility.IsAcceptablePreyFor,
    /// which weighs a downed pawn as an easy meal - and every stopped agent is a downed
    /// pawn lying in the open.
    /// </summary>
    [HarmonyPatch(typeof(FoodUtility), nameof(FoodUtility.IsAcceptablePreyFor))]
    public static class Patch_NoPreyOnAgents
    {
        static void Postfix(Pawn prey, ref bool __result)
        {
            if (__result && AgentColony.IsAgent(prey)) __result = false;
        }
    }

    /// <summary>
    /// No animal ever picks an agent as something to attack. Hunting, revenge and
    /// manhunter rage all shop for a target through AttackTargetFinder.BestAttackTarget,
    /// so folding agents into the caller's own validator takes them off the list at
    /// source rather than nulling a choice already made.
    /// </summary>
    [HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestAttackTarget))]
    public static class Patch_NoTargetingAgents
    {
        static void Prefix(IAttackTargetSearcher searcher, ref Predicate<Thing> validator)
        {
            var hunter = searcher?.Thing as Pawn;
            if (hunter?.RaceProps == null || !hunter.RaceProps.Animal) return;
            if (AgentColony.Current == null) return;

            var inner = validator;
            validator = t => !(t is Pawn p && AgentColony.IsAgent(p))
                             && (inner == null || inner(t));
        }
    }

    /// <summary>
    /// The backstop, and the one that turns a bite into a lick. Every melee swing an
    /// animal throws - the hunt's, the manhunter's, the one that got started before
    /// the target ever went near a target finder - goes through
    /// Pawn_MeleeVerbs.TryMeleeAttack, so refusing it here is the last word.
    ///
    /// A colony pet gets its swing traded for a nuzzle rather than simply ignored:
    /// the animal has already walked all the way over, and standing there doing
    /// nothing looks like the mod is broken.
    /// </summary>
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

    /// <summary>
    /// Nothing in the colony catches. Both of the plague's ignition effects skip the
    /// player faction the way every other effect does, but fire is the one thing it
    /// starts that then goes on by itself - so the colony has to be spared from where
    /// the fire arrives, not only from where it was lit.
    ///
    /// Attachment and cell damage are separate roads to the same place: a pawn burns
    /// because a Fire is stuck to it, and it also burns because it is standing in a
    /// cell that has one. Closing only the first would let a pet cook by walking into
    /// a fire it never caught, and closing only the second would leave an agent -
    /// already invulnerable, so already unharmed - wearing a flame that never goes
    /// out, because a fire on an unkillable thing has nothing to finish.
    ///
    /// The rule is the whole player faction rather than agents alone, matching
    /// Plague.Infectable: the pets are meant to outlive the map, and a wildfire is
    /// exactly the thing that would quietly take that back.
    /// </summary>
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

        /// Private, so bound by name: every hit a Fire deals - to what it is stuck to
        /// and to what shares its cell - goes through here.
        [HarmonyPatch(typeof(Fire), "DoFireDamage")]
        public static class Patch_NoFireDamage
        {
            static bool Prefix(Thing targ) => !Spared(targ);
        }
    }
}
