using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public class WaterBall : Projectile
    {
        const float RestorationRadius = 3.9f;
        const float WaterBodyChance = 0.20f;

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            var map = Map;
            var pos = Position;
            base.Impact(hitThing, blockedByShield);
            if (map == null || blockedByShield) return;

            Terraform(map, pos, RestorationRadius);
            Aura.Of(map)?.Rejuvenate(pos);
            map.GetComponent<Plague>()?.Rejuvenate(pos, RestorationRadius);
        }

        // Most impacts make rich soil; occasionally the rejuvenation overflows into a shallow
        // freshwater body. Both terrain changes are saved by RimWorld, so this is the lasting
        // part of the spell rather than another timed aura.
        static void Terraform(Map map, IntVec3 centre, float radius)
        {
            var richSoil = TerrainDefOf.SoilRich;
            var water = TerrainDefOf.WaterShallow;
            bool makeWater = water != null && Rand.Chance(WaterBodyChance);
            var replacement = makeWater ? water : richSoil;
            if (replacement == null) return;

            int cells = GenRadial.NumCellsInRadius(
                Mathf.Min(radius, GenRadial.MaxRadialPatternRadius - 1f));
            for (int i = 0; i < cells; i++)
            {
                var c = centre + GenRadial.RadialPattern[i];
                if (!c.InBounds(map) || c.GetEdifice(map) != null) continue;
                if (map.terrainGrid.WaterAt(c)) continue;

                var terrain = map.terrainGrid.TerrainAt(c);
                if (terrain == null || !terrain.natural || !terrain.canEverTerraform
                    || terrain.IsWater || terrain.IsFloor) continue;
                if (!makeWater && terrain.fertility >= richSoil.fertility) continue;

                map.terrainGrid.SetTerrain(c, replacement);
            }
        }
    }
}
