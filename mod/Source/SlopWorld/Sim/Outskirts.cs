using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Keeps something alive out past the plague.
    ///
    /// The bands only mean anything against a rim that is still living: a map whose
    /// untouched third is as empty as its dead middle is one flat texture again,
    /// which is the failure <see cref="Plague"/> spends most of its length avoiding.
    /// The intro populates the hillside once and the circle eats its way through
    /// that, so without this the map is busy for ten minutes and then still forever.
    ///
    /// So new animals and people keep arriving, and they arrive the way anything
    /// arrives on a map it did not start on: walking in from the edge.
    /// <c>TryFindRandomPawnEntryCell</c> is vanilla's own answer for that - it is
    /// what the wild animal spawner uses - and the validator only says what the
    /// edge already implies, that the cell is not one the plague has reached.
    ///
    /// What it does not do is keep them out there. A thing that wanders into the
    /// circle is marked and comes apart, which is the plague working rather than
    /// this failing, and the count below is a population out on the rim rather than
    /// a population on the map - so the arrivals are a steady state against that
    /// drain instead of a queue feeding the middle.
    ///
    /// Only once the plague is armed. Before that the intro owns what is standing on
    /// this map, and a cutscene with strangers wandering into it is not the scene it
    /// was written as.
    ///
    /// MapComponents are instantiated for every subclass, so this needs no def.
    /// </summary>
    public class Outskirts : MapComponent
    {
        // One arrival every couple of seconds while short. Slow on purpose: a stream
        // of things walking in off the edge reads as life coming back, where forty
        // of them at once reads as a raid.
        const int Interval = 120;

        // How much life the rim is meant to hold. Well under what the intro puts
        // down, because that lot is scenery to be killed and this lot is the world
        // going on around it.
        const int Animals = 30;
        const int Humans = 12;

        // Runtime: the biome's animals, worked out once. Not saved - it is a
        // question about the map, and a load can ask it again.
        List<PawnKindDef> _kinds;

        public Outskirts(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0) return;

            var plague = map.GetComponent<Plague>();
            if (plague == null || !plague.Active) return;

            Census(plague, out int animals, out int humans);

            // One at a time, animals first: they are most of what the rim is, and a
            // tick that sends one of each is a tick that spends twice as long
            // generating pawns.
            if (animals < Animals) Arrive(plague, Kind());
            else if (humans < Humans) Arrive(plague, PawnKindDefOf.Colonist);
        }

        /// <summary>What is alive out past the circle. Deliberately not a count of
        /// the map: everything the plague is currently killing is inside it, and
        /// counting that would hold the arrivals off until the middle was finished.</summary>
        void Census(Plague plague, out int animals, out int humans)
        {
            animals = 0;
            humans = 0;

            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.Dead || pawn.RaceProps == null) continue;
                if (pawn.Faction != null && pawn.Faction.IsPlayer) continue;
                if (plague.Reaches(pawn.Position)) continue;

                if (pawn.RaceProps.Animal) animals++;
                else if (pawn.RaceProps.Humanlike) humans++;
            }
        }

        void Arrive(Plague plague, PawnKindDef kind)
        {
            if (kind == null) return;

            if (!RCellFinder.TryFindRandomPawnEntryCell(out var cell, map,
                    CellFinder.EdgeRoadChance_Animal, false, c => !plague.Reaches(c)))
                return;

            try
            {
                var req = new PawnGenerationRequest(kind, null,
                    PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true);
                GenSpawn.Spawn(PawnGenerator.GeneratePawn(req), cell, map);
            }
            catch (System.Exception e)
            {
                Log.Warning($"[SlopWorld] outskirts: {e.Message}");
            }
        }

        PawnKindDef Kind()
        {
            if (_kinds == null) _kinds = Kinds(map);
            return _kinds.Count > 0 ? _kinds.RandomElement() : null;
        }

        /// <summary>Animals that actually belong to this biome, falling back to any
        /// animal so an odd biome with no fauna still gets a population. Shared with
        /// the intro, which populates the same hillside from the same list.</summary>
        public static List<PawnKindDef> Kinds(Map map)
        {
            var local = new List<PawnKindDef>();
            var any = new List<PawnKindDef>();
            foreach (var k in DefDatabase<PawnKindDef>.AllDefsListForReading)
            {
                if (k.RaceProps == null || !k.RaceProps.Animal) continue;
                any.Add(k);
                if (map.Biome.CommonalityOfAnimal(k) > 0f) local.Add(k);
            }
            return local.Count > 0 ? local : any;
        }
    }
}
