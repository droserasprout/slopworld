using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // Manage the colony's pet. Plague.Infectable excludes the player faction.
    // IntroDirector owns arrival timing; this owner generates and delivers the pet.
    public static class Pets
    {
        const int PodOpenDelay = 60;

        // Drag selection calls Select for each enclosed object.
        // Limit repeated selection effects for each pet.
        const float PokeCooldownSeconds = 0.4f;

        // Limit repeated nuzzle replacements when an animal keeps trying to attack.
        // A mental state can assign another attack job after interruption.
        const float NuzzleCooldownSeconds = 20f;

        // Vanilla nuzzle expiry: 3000 game ticks.
        const int NuzzleExpiryTicks = 3000;

        // Cooldowns follow the pawn without retaining it or its game after teardown.
        sealed class Cooldowns
        {
            public float Poke = float.NegativeInfinity;
            public float Nuzzle = float.NegativeInfinity;
        }

        static readonly ConditionalWeakTable<Pawn, Cooldowns> _cooldowns =
            new ConditionalWeakTable<Pawn, Cooldowns>();

        // A colony animal: player faction, not humanlike, not an agent.
        public static bool Is(Pawn p) =>
            p != null
            && !p.Dead
            && p.RaceProps != null
            && p.RaceProps.Animal
            && p.Faction != null
            && p.Faction.IsPlayer;

        public static List<Pawn> On(Map map) =>
            map?.mapPawns?.SpawnedColonyAnimals?.Where(Is).ToList() ?? new List<Pawn>();

        // The intro calls this once at its arrival boundary, alongside the player's
        // separate pod. The saved intro phase prevents repeated delivery.
        public static void Arrive(Map map)
        {
            if (map == null)
                return;

            // ModScenario removes the scenario's starting animal. Remove any
            // remaining colony animals before delivering the intended pet.
            foreach (var other in On(map))
            {
                Log.Message($"[SlopWorld] removing stray colony animal '{other.LabelShort}'");
                other.Destroy(DestroyMode.Vanish);
            }

            var cat = ModDefOf.Cat;
            if (cat == null)
            {
                Log.Warning(
                    "[SlopWorld] No Cat definition exists. The colony starts without an animal."
                );
                return;
            }

            Arrive(map, cat);
        }

        static void Arrive(Map map, PawnKindDef kind)
        {
            try
            {
                var cell = SpawnSpot.Find(map, map.Center);

                var req = new PawnGenerationRequest(
                    kind,
                    Faction.OfPlayer,
                    PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true
                );

                var pet = PawnGenerator.GeneratePawn(req);
                pet.Name = PawnBioAndNameGenerator.GeneratePawnName(pet);

                if (Eco.Resting)
                    GenSpawn.Spawn(pet, cell, map);
                else
                    DropPodUtility.DropThingsNear(
                        cell,
                        map,
                        new List<Thing> { pet },
                        openDelay: PodOpenDelay,
                        canInstaDropDuringInit: false,
                        leaveSlag: false,
                        canRoofPunch: true,
                        forbid: false,
                        allowFogged: true,
                        faction: Faction.OfPlayer
                    );

                Log.Message($"[SlopWorld] the cat is '{pet.LabelShort}' ({kind.defName})");
            }
            catch (System.Exception e)
            {
                Log.Warning($"[SlopWorld] starting pet: {e.Message}");
            }
        }

        // DoCall uses the species' normal call when the pet is not aggressive.
        // Also notify the cursor and Aura of the interaction.
        public static void Poke(Pawn pet)
        {
            if (pet == null || pet.Dead || !pet.Spawned)
                return;

            var cooldown = _cooldowns.GetValue(pet, _ => new Cooldowns());
            float now = Time.realtimeSinceStartup;
            if (now - cooldown.Poke < PokeCooldownSeconds)
                return;
            cooldown.Poke = now;

            pet.caller?.DoCall();
            GameCursor.Pat();
            Aura.Of(pet.Map)?.Pat(pet);
        }

        // Assign the base game's nuzzle job with the agent as its target.
        // Force interruption of the previous hunt or mental-state job.
        public static void NuzzleInstead(Pawn pet, Pawn agent)
        {
            if (pet == null || pet.Dead || !pet.Spawned || pet.jobs == null)
                return;
            if (agent == null || !agent.Spawned)
                return;
            if (pet.CurJobDef == JobDefOf.Nuzzle)
                return;

            var cooldown = _cooldowns.GetValue(pet, _ => new Cooldowns());
            float now = Time.realtimeSinceStartup;
            if (now - cooldown.Nuzzle < NuzzleCooldownSeconds)
                return;
            cooldown.Nuzzle = now;

            var job = JobMaker.MakeJob(JobDefOf.Nuzzle, agent);
            job.expiryInterval = NuzzleExpiryTicks;
            pet.jobs.StartJob(job, JobCondition.InterruptForced);
        }
    }
}
