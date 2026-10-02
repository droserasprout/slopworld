using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // After the plague starts, add pawns at the map edge at regular intervals.
    // Count pawns outside the plague area separately to control arrivals.
    public class Outskirts : MapComponent
    {
        // Space arrivals over time so they resemble returning wildlife and people, rather than a raid.
        const int Interval = 120;

        // Keep the target population below the initial population in the opening scene.
        const int AnimalTarget = 30;
        const int HumanTarget = 12;

        // Calculate this list again after loading a save.
        List<PawnKindDef> _kinds;

        public Outskirts(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0) return;

            var plague = map.GetComponent<Plague>();
            if (plague == null || !plague.Active) return;

            Census(plague, out int animals, out int humans);

            // Generate at most one pawn per interval to limit generation work. Give animals priority.
            if (animals < AnimalTarget) Arrive(plague, Kind());
            else if (humans < HumanTarget) Arrive(plague, PawnKindDefOf.Colonist);
        }

        // Count living animals and humanlike pawns outside the plague area. Exclude the player faction.
        // Pawns inside the plague area must not delay new arrivals.
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

            float roadChance = kind.RaceProps.Humanlike
                ? CellFinder.EdgeRoadChance_Neutral : CellFinder.EdgeRoadChance_Animal;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out var cell, map,
                    roadChance, false, c => !plague.Reaches(c)))
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

        // Use local animal kinds if available. Otherwise, use all animal kinds.
        // The opening scene also uses this list.
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
