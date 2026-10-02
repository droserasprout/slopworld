using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Apply plague effects to vegetation, or add flowers when Grandma's visiting.
    // Process limited batches because the field covers the map. Plague.cs owns field growth and timing.
    public partial class Plague
    {
        // Copy the plant list before processing it. Destroying plants changes the live list from ThingsInGroup.
        // Rebuild the copy after a complete sweep to include new plants.
        void StepPlants()
        {
            if (_runtime.PlantIndex >= _runtime.Plants.Count)
            {
                _runtime.Plants.Clear();
                var all = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
                for (int i = 0; i < all.Count; i++)
                    if (all[i] is Plant p) _runtime.Plants.Add(p);
                _runtime.PlantIndex = 0;
                return; // keep the refill off the same tick as the work
            }

            int budget = PlantsPerTick;
            int now = Find.TickManager.TicksGame;
            while (_runtime.PlantIndex < _runtime.Plants.Count && budget-- > 0)
            {
                var p = _runtime.Plants[_runtime.PlantIndex++];
                if (p == null || p.Destroyed || !p.Spawned) continue;
                if (p.def.plant == null) continue;

                var band = BandAt(p.Position, now);
                if (band == Band.None) continue;

                var dose = band == Band.Full ? Full : Weak;
                bool tree = p.def.plant.IsTree;

                // Skip plants that already have the required damage to avoid repeated smoke and ignition attempts.
                bool todo = dose.Strips
                    ? !(tree && p.LeaflessNow)
                    : !tree && p.Growth > dose.StuntFrom;
                if (!todo) continue;

                // Check protection after checking for required changes to reduce work on later sweeps.
                if (Spared(p)) continue;

                // Roll once per eligible sweep, including plants that regrew or survived an earlier ignition.
                // Try ignition before removing vegetation because TryStartFireIn checks flammable cell contents.
                if (Rand.Value < dose.PlantIgnite &&
                    FireUtility.TryStartFireIn(p.Position, map, dose.FireSize, null))
                    continue;

                if (!dose.Strips)
                {
                    // Leave trees unchanged in the weak band so it remains visually distinct from the full band.
                    PlagueFx.Wither(p);
                    p.Growth = dose.StuntTo;
                    // The growth setter does not invalidate the map mesh. Request a mesh update.
                    map.mapDrawer?.MapMeshDirty(p.Position, MapMeshFlagDefOf.Things);
                }
                else if (tree)
                {
                    PlagueFx.Wither(p);
                    p.MakeLeafless(Plant.LeaflessCause.Poison, false);
                }
                else
                {
                    PlagueFx.Wither(p);
                    p.Destroy(DestroyMode.Vanish);
                }
            }
        }

        // Process map cells in limited batches to add flowers within the field.
        // Use cells rather than existing plants because empty cells can receive flowers.
        void Sow()
        {
            var flowers = Flowers;
            if (flowers.Count == 0) return;

            var cells = Cells;
            var idx = map.cellIndices;
            int now = Find.TickManager.TicksGame;

            for (int budget = SowPerSweep; budget > 0; budget--)
            {
                if (_runtime.SowIndex >= cells.Length) _runtime.SowIndex = 0;
                int k = _runtime.SowIndex++;

                // Check whether the field reached this cell before the more expensive checks.
                if (cells[k] == Never) continue;

                var c = idx.IndexToCell(k);
                if (BandAt(c, now) == Band.None) continue;
                if (Grit(c, SowSalt) >= SowChance) continue;
                if (!Plantable(c)) continue;

                var plant = GenSpawn.Spawn(flowers.RandomElement(), c, map) as Plant;
                if (plant == null) continue;

                plant.Growth = Rand.Range(SowGrowthMin, SowGrowthMax);
                // The growth setter does not invalidate the map mesh. Request a mesh update.
                map.mapDrawer?.MapMeshDirty(c, MapMeshFlagDefOf.Things);
                PlagueFx.Sprout(plant);
            }
        }

        // Require fertile ground without plants, buildings, or other contents that fill the cell.
        // This check is less restrictive than CanEverPlantAt.
        bool Plantable(IntVec3 c) =>
            c.GetTerrain(map)?.fertility > 0f &&
            c.GetPlant(map) == null &&
            c.GetEdifice(map) == null &&
            !c.Filled(map);

        // Select non-tree plants with the Beauty purpose from the definition database.
        // This includes definitions from other mods. If no definitions match, sow no plants.
        static List<ThingDef> _flowers;

        static List<ThingDef> Flowers
        {
            get
            {
                if (_flowers == null)
                {
                    _flowers = new List<ThingDef>();
                    foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading)
                        if (d.plant != null && d.plant.purpose == PlantPurpose.Beauty && !d.plant.IsTree)
                            _flowers.Add(d);
                }
                return _flowers;
            }
        }
    }
}
