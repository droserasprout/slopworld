using RimWorld;
using Verse;

namespace SlopWorld
{
    public partial class Aura
    {
        // Completed worksite graves have no individual ownership record. Only replace
        // empty player graves; construction totals and historical plague seeds stay intact.
        void ConvertGrave(IntVec3 centre, int now)
        {
            var flower = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Rose");
            if (flower?.plant == null)
                return;

            int cells = GenRadial.NumCellsInRadius(Radius);
            for (int i = 0; i < cells; i++)
            {
                var cell = centre + GenRadial.RadialPattern[i];
                if (!cell.InBounds(map))
                    continue;
                if (
                    !(cell.GetEdifice(map) is Building_Grave grave)
                    || grave.Faction != Faction.OfPlayer
                    || grave.HasAnyContents
                )
                    continue;

                var position = grave.Position;
                var rotation = grave.Rotation;
                Plant planted = null;
                grave.DeSpawn();
                try
                {
                    // Check after removing the edifice so vanilla planting rules can
                    // validate fertility, roofs and other contents without destroying it.
                    if (!flower.CanEverPlantAt(position, map) || position.GetPlant(map) != null)
                        continue;
                    planted = GenSpawn.Spawn(flower, position, map) as Plant;
                }
                finally
                {
                    if (planted == null)
                        GenSpawn.Spawn(grave, position, map, rotation);
                }

                grave.Destroy(DestroyMode.Vanish);
                planted.Growth = ReviveGrowth;
                _grace[planted.thingIDNumber] = now;
                map.mapDrawer?.MapMeshDirty(position, MapMeshFlagDefOf.Things);
                Puff(planted);
                return;
            }
        }
    }
}
