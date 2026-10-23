using Verse;

namespace SlopWorld
{
    // Owns the map's jukebox, including cargo that has not landed yet.
    public partial class Jukebox : MapComponent
    {
        public Jukebox(Map map)
            : base(map) { }

        Thing _box;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref _box, "jukebox");
        }

        // Keep ownership while the separate jukebox pod is in flight or opening.
        public Thing ArrivalCargo()
        {
            if (On(map) || (_box != null && !_box.Destroyed))
                return null;
            return _box = ThingMaker.MakeThing(ModDefOf.SlopJukebox);
        }

        // Repair missing boxes, but leave pending arrival cargo to its own pod.
        public void EnsurePlaced(IntVec3 cell)
        {
            var box = ArrivalCargo();
            if (box == null)
                return;
            if (!GenPlace.TryPlaceThing(box, cell, map, ThingPlaceMode.Near))
            {
                box.Destroy(DestroyMode.Vanish);
                Log.Warning("[SlopWorld] Could not place the map jukebox.");
            }
        }

        public override void FinalizeInit()
        {
            var boxes = map.listerThings.ThingsOfDef(ModDefOf.SlopJukebox);
            if (boxes.Count < 2)
                return;

            int extra = boxes.Count - 1;
            // Remove items from the end because destruction changes the list.
            while (boxes.Count > 1)
                boxes[boxes.Count - 1].Destroy(DestroyMode.Vanish);
            Log.Message($"[SlopWorld] jukebox: removed {extra} duplicate(s)");
        }

        // Pending pod cargo is intentionally absent until it lands on the map.
        public static bool On(Map map) =>
            map != null && map.listerThings.ThingsOfDef(ModDefOf.SlopJukebox).Count > 0;
    }
}
