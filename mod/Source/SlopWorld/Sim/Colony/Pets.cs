using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // Manage the colony's pet. Plague.Infectable excludes the player faction.
    // Keep the API plural so Place controls the pet count without assumptions in other callers.
    public static class Pets
    {
        const int PlacementTries = 40;

        // Drag selection calls Select for each enclosed object.
        // Limit repeated selection effects for each pet.
        const float PokeCooldown = 0.4f;

        // Limit repeated nuzzle replacements when an animal keeps trying to attack.
        // A mental state can assign another attack job after interruption.
        const float NuzzleCooldown = 20f;

        // What vanilla's own nuzzle job allows.
        const int NuzzleExpiry = 3000;

        static readonly Dictionary<int, float> _lastPoke = new Dictionary<int, float>();
        static readonly Dictionary<int, float> _lastNuzzle = new Dictionary<int, float>();

        // A colony animal: player faction, not humanlike, not an agent.
        public static bool Is(Pawn p) =>
            p != null && !p.Dead && p.RaceProps != null && p.RaceProps.Animal
            && p.Faction != null && p.Faction.IsPlayer;

        public static List<Pawn> On(Map map) =>
            map?.mapPawns?.SpawnedColonyAnimals?.Where(Is).ToList() ?? new List<Pawn>();

        // The intro calls this before pod arrivals.
        // Prefer a random standable cell so the pet does not appear to arrive with the pods.
        public static void Place(Map map)
        {
            if (map == null) return;

            // ModScenario removes the scenario's starting animal.
            // Remove any remaining colony animals, including pets from a repeated Place call.
            // The intro can repeat because _armed is runtime state while its phase persists.
            foreach (var other in On(map))
            {
                Log.Message($"[SlopWorld] removing stray colony animal '{other.LabelShort}'");
                other.Destroy(DestroyMode.Vanish);
            }

            var cat = ModDefOf.Cat;
            if (cat == null)
            {
                Log.Warning("[SlopWorld] No Cat definition exists. The colony starts without an animal.");
                return;
            }

            Place(map, cat);
        }

        static void Place(Map map, PawnKindDef kind)
        {
            try
            {
                if (!CellFinderLoose.TryGetRandomCellWith(
                        c => c.Standable(map), map, PlacementTries, out var cell))
                    cell = map.Center;

                var req = new PawnGenerationRequest(kind, Faction.OfPlayer,
                    PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true);

                var pet = PawnGenerator.GeneratePawn(req);
                pet.Name = PawnBioAndNameGenerator.GeneratePawnName(pet);

                GenSpawn.Spawn(pet, cell, map);

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
            if (pet == null || pet.Dead || !pet.Spawned) return;

            float now = Time.realtimeSinceStartup;
            if (_lastPoke.TryGetValue(pet.thingIDNumber, out float last)
                && now - last < PokeCooldown) return;
            _lastPoke[pet.thingIDNumber] = now;

            pet.caller?.DoCall();
            DeadCursor.Pat();
            Aura.Of(pet.Map)?.Pat(pet);
        }

        // Assign the base game's nuzzle job with the agent as its target.
        // Force interruption of the previous hunt or mental-state job.
        public static void NuzzleInstead(Pawn pet, Pawn agent)
        {
            if (pet == null || pet.Dead || !pet.Spawned || pet.jobs == null) return;
            if (agent == null || !agent.Spawned) return;
            if (pet.CurJobDef == JobDefOf.Nuzzle) return;

            float now = Time.realtimeSinceStartup;
            if (_lastNuzzle.TryGetValue(pet.thingIDNumber, out float last)
                && now - last < NuzzleCooldown) return;
            _lastNuzzle[pet.thingIDNumber] = now;

            var job = JobMaker.MakeJob(JobDefOf.Nuzzle, agent);
            job.expiryInterval = NuzzleExpiry;
            pet.jobs.StartJob(job, JobCondition.InterruptForced);
        }

    }
}
