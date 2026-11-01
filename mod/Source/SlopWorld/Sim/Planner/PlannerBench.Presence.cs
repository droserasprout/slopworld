using Verse;

namespace SlopWorld
{
    // Owns the map's planner bench, including cargo that has not landed yet.
    public partial class PlannerBench : MapComponent
    {
        public PlannerBench(Map map)
            : base(map) { }

        Thing _bench;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref _bench, "plannerBench");
        }

        // Keep ownership while the separate planner bench pod is in flight or opening.
        public Thing ArrivalCargo()
        {
            if (On(map) || (_bench != null && !_bench.Destroyed))
                return null;
            return _bench = ThingMaker.MakeThing(ModDefOf.SlopPlannerBench);
        }

        // Repair missing benches, but leave pending arrival cargo to its own pod.
        public void EnsurePlaced(IntVec3 cell)
        {
            var bench = ArrivalCargo();
            if (bench == null)
                return;
            if (!GenPlace.TryPlaceThing(bench, cell, map, ThingPlaceMode.Near))
            {
                bench.Destroy(DestroyMode.Vanish);
                Log.Warning("[SlopWorld] Could not place the map planner bench.");
            }
        }

        public override void FinalizeInit()
        {
            var benches = map.listerThings.ThingsOfDef(ModDefOf.SlopPlannerBench);
            if (benches.Count < 2)
                return;

            int extra = benches.Count - 1;
            // Remove items from the end because destruction changes the list.
            while (benches.Count > 1)
                benches[benches.Count - 1].Destroy(DestroyMode.Vanish);
            Log.Message($"[SlopWorld] planner bench: removed {extra} duplicate(s)");
        }

        // Pending pod cargo is intentionally absent until it lands on the map.
        public static bool On(Map map) =>
            map != null && map.listerThings.ThingsOfDef(ModDefOf.SlopPlannerBench).Count > 0;
    }
}
