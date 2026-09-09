using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // After the plague is armed, replenish the living rim with slow edge arrivals; count outside
    // the circle separately so they do not form a queue at the center.
    public class Outskirts : MapComponent
    {
        // Slow on purpose: a stream of things walking in off the edge reads as life
        // coming back, where forty at once reads as a raid.
        const int Interval = 120;

        // Well under what the intro puts down, because that lot is scenery to be killed
        // and this lot is the world going on around it.
        const int Animals = 30;
        const int Humans = 12;

        // Not saved - it is a question about the map, and a load can ask it again.
        List<PawnKindDef> _kinds;

        public Outskirts(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0) return;

            var plague = map.GetComponent<Plague>();
            if (plague == null || !plague.Active) return;

            Census(plague, out int animals, out int humans);

            // Animals first: they are most of what the rim is, and a tick that sends one of
            // each spends twice as long generating pawns.
            if (animals < Animals) Arrive(plague, Kind());
            else if (humans < Humans) Arrive(plague, PawnKindDefOf.Colonist);
        }

        // Deliberately not a count of the map: everything the plague is currently killing
        // is inside it, and counting that would hold the arrivals off until the middle
        // was finished.
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

        // Falling back to any animal, so an odd biome with no fauna still gets a
        // population. Shared with the intro, which populates the same hillside.
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
