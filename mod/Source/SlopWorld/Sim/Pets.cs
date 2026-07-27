using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    /// <summary>
    /// The colony's cat: the one living thing on this map that nothing kills.
    ///
    /// It came down with the starters and it is still here, which is the whole joke -
    /// the humans go up in a red mist ten seconds after landing and the plague eats
    /// everything the intro put on the hillside, and one cat walks through all of it
    /// unbothered. That is not an oversight in either system. The purge hands it to
    /// GenExplosion as an ignoredThing, and <see cref="Plague.Infectable"/> spares the
    /// whole player faction, agents and cat alike.
    ///
    /// One, not a litter: a scattering of assorted biome-appropriate animals read as
    /// a starting scenario, which is the thing this map is not.
    ///
    /// Clicking it pats it. It is not selectable - see
    /// <see cref="Patch_Selectable_ColonistsOnly"/> - so the click has nowhere else to
    /// go, and a pat is a better answer than nothing happening.
    ///
    /// Still plural in the API: <see cref="On"/> returns a list and the purge and the
    /// plague both iterate it, so the count stays a policy in
    /// <see cref="Place(Map, IntVec3)"/> rather than an assumption spread across three
    /// other files.
    /// </summary>
    public static class Pets
    {
        // How far from the starters it lands, and how hard we look for a cell.
        const int ScatterRadius = 7;
        const int PlacementTries = 40;

        // Ticks the cat's pod stays shut once it is down. Shorter than vanilla's 110,
        // because the intro is waiting on this: the cat has to be out and walking
        // before the core lands on the party.
        const int PodOpenDelay = 60;

        // Real seconds a pet stays quiet after being patted. A drag box over the
        // colony calls Select once per thing inside it, so without this a stray
        // drag sets off every animal on the map at once.
        const float PokeCooldown = 0.4f;

        // Real seconds before the same pet may be sent to nuzzle again. A refused
        // attack is refused every tick the animal keeps trying, and a mental state
        // will hand it the attack job straight back, so without this the pair would
        // flip between the two for as long as the pet stayed angry.
        const float NuzzleCooldown = 20f;

        // Ticks a redirected nuzzle gets to reach its agent before it gives up, which
        // is what vanilla's own nuzzle job allows.
        const int NuzzleExpiry = 3000;

        static readonly Dictionary<int, float> _lastPoke = new Dictionary<int, float>();
        static readonly Dictionary<int, float> _lastNuzzle = new Dictionary<int, float>();

        /// <summary>A colony animal: player faction, not humanlike, not an agent.</summary>
        public static bool Is(Pawn p) =>
            p != null && !p.Dead && p.RaceProps != null && p.RaceProps.Animal
            && p.Faction != null && p.Faction.IsPlayer;

        public static List<Pawn> On(Map map) =>
            map?.mapPawns?.SpawnedColonyAnimals?.Where(Is).ToList() ?? new List<Pawn>();

        /// <summary>
        /// Drops the cat next to <paramref name="near"/>, which is where the starters
        /// are standing. Called once, by the intro, the moment the first pod opens:
        /// any earlier and they are all still in the air, any later and there is
        /// nobody left to have owned it.
        ///
        /// In a pod of its own, rather than placed - the whole party arrives out of
        /// the sky and a cat that was simply on the ground when the camera got there
        /// belongs to the map instead of to them.
        /// </summary>
        public static void Place(Map map, IntVec3 near)
        {
            if (map == null) return;

            // Anything that got here first goes. <see cref="SlopScenario"/> takes the
            // scenario's own starting animal off at source, but that closes one known
            // door and this closes the rest - including this method running twice,
            // because IntroDirector._armed is runtime state while its phase is
            // persisted, so a save loaded during the fuse comes back through here.
            foreach (var other in On(map))
            {
                Log.Message($"[SlopWorld] removing stray colony animal '{other.LabelShort}'");
                other.Destroy(DestroyMode.Vanish);
            }

            var cat = SlopDefOf.Cat;
            if (cat == null)
            {
                // DefOf resolution would have complained already; say what it cost.
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

                // Not forbidden: a forbidden pet is one the colony is told to leave
                // alone, which is the opposite of what this animal is for.
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

        /// <summary>
        /// A pat: the animal's own call sound, the friendly one. DoCall picks the
        /// species' soundCall over its soundAngry for anything not currently
        /// aggressive, which a tame pet never is, so the argument stays default.
        ///
        /// The hand does the patting - <see cref="DeadCursor.Pat"/> waggles the
        /// pointer - because a sound with nothing moving under it reads as a
        /// misclick that happened to make a noise.
        /// </summary>
        public static void Poke(Pawn pet)
        {
            if (pet == null || pet.Dead || !pet.Spawned) return;

            float now = Time.realtimeSinceStartup;
            if (_lastPoke.TryGetValue(pet.thingIDNumber, out float last)
                && now - last < PokeCooldown) return;
            _lastPoke[pet.thingIDNumber] = now;

            pet.caller?.DoCall();
            DeadCursor.Pat();
        }

        /// <summary>
        /// What a pet does instead of biting an agent - see
        /// <see cref="Patch_NoMaulingAgents"/>, which is the only caller. Vanilla's
        /// nuzzle is nothing but a job pointed at a pawn, so the animal walks the last
        /// step it was going to swing from and licks the colonist instead. Forced,
        /// because the job it is replacing is one a mental state or a hunt handed out
        /// and neither will stand aside politely.
        /// </summary>
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
