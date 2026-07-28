using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // The colony's cat: the one living thing on this map that nothing kills, and both
    // halves are deliberate - the purge hands it to GenExplosion as an ignoredThing,
    // and Plague.Infectable spares the whole player faction.
    //
    // One, not a litter: a scattering of biome-appropriate animals reads as a
    // starting scenario, which is what this map is not. Clicking it pats it, the cat
    // not being selectable, so the click has nowhere else to go. The API stays plural
    // so the count is a policy in Place rather than an assumption in three files.
    public static class Pets
    {
        // How far from the starters it lands, and how hard we look for a cell.
        const int ScatterRadius = 7;
        const int PlacementTries = 40;

        // Shorter than vanilla's 110, because the intro is waiting on this: the cat has
        // to be out and walking before the core lands on the party.
        const int PodOpenDelay = 60;

        // A drag box over the colony calls Select once per thing inside it, so without
        // this a stray drag sets off every animal on the map at once.
        const float PokeCooldown = 0.4f;

        // A refused attack is refused every tick the animal keeps trying, and a mental
        // state hands it the attack job straight back.
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

        // Called once by the intro, the moment the first pod opens: any earlier and they
        // are all still in the air, any later and there is nobody left to have owned it.
        // In a pod of its own rather than placed - an animal already standing there when
        // the camera arrives belongs to the map instead of to them.
        public static void Place(Map map, IntVec3 near)
        {
            if (map == null) return;

            // SlopScenario takes the scenario's own starting animal off at source; this
            // closes the rest, including this method running twice, because
            // IntroDirector._armed is runtime state under a persisted phase.
            foreach (var other in On(map))
            {
                Log.Message($"[SlopWorld] removing stray colony animal '{other.LabelShort}'");
                other.Destroy(DestroyMode.Vanish);
            }

            var cat = SlopDefOf.Cat;
            if (cat == null)
            {
                Log.Warning("[SlopWorld] no Cat def; colony starts with nothing living on it");
                return;
            }

            Place(map, near, cat);
        }

        static void Place(Map map, IntVec3 near, PawnKindDef kind)
        {
            try
            {
                if (!CellFinder.TryFindRandomCellNear(near, map, ScatterRadius,
                        c => c.Standable(map), out var cell, PlacementTries))
                    cell = near;

                var req = new PawnGenerationRequest(kind, Faction.OfPlayer,
                    PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true);

                var pet = PawnGenerator.GeneratePawn(req);
                pet.Name = PawnBioAndNameGenerator.GeneratePawnName(pet);

                // Not forbidden: a forbidden pet is one the colony is told to leave alone.
                DropPodUtility.DropThingsNear(cell, map, new List<Thing> { pet },
                    openDelay: PodOpenDelay, canInstaDropDuringInit: false,
                    leaveSlag: false, canRoofPunch: true, forbid: false,
                    allowFogged: true, faction: Faction.OfPlayer);

                Log.Message($"[SlopWorld] the cat is '{pet.LabelShort}' ({kind.defName})");
            }
            catch (System.Exception e)
            {
                Log.Warning($"[SlopWorld] starting pet: {e.Message}");
            }
        }

        // DoCall picks the species' soundCall over its soundAngry for anything not
        // currently aggressive, which a tame pet never is. DeadCursor.Pat waggles the
        // pointer, because a sound with nothing moving under it reads as a misclick.
        //
        // It is also the whole of the input to Aura, which is the only way anything is
        // taken back off the core - so the cooldown above is load-bearing twice over.
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

        // Vanilla's nuzzle is nothing but a job pointed at a pawn, so the animal walks
        // the last step it was going to swing from and licks the colonist instead.
        // Forced, because the job it replaces came from a mental state or a hunt and
        // neither stands aside politely.
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
