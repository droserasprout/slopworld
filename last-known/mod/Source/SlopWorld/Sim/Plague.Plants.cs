using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The vegetation half of the plague: the sweep that withers or strips what the circle has
    // reached, and the sweep that sows flowerbeds in grandma mode. Both are walked in slices
    // because the field is the whole map; the growth model and clock live in Plague.cs.
    public partial class Plague
    {
        // Work from a copy: ThingsInGroup returns the lister's live list, and destroying a
        // plant while walking it shifts the next entry and skips it. Rebuild after each
        // slice so newly grown plants are included without restarting the sweep every tick.
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

                // Every plant comes back round forever: without this a bare tree smokes again
                // each pass and a plant already held back rolls for ignition until it catches.
                bool todo = dose.Strips
                    ? !(tree && p.LeaflessNow)
                    : !tree && p.Growth > dose.StuntFrom;
                if (!todo) continue;

                // After the todo check: same answer, and in the steady state nearly every
                // plant the sweep walks past is one the band has finished with.
                if (Spared(p)) continue;

                // Before the strip: TryStartFireIn weighs what is flammable in the cell, and
                // stripping the plant leaves nothing there to light.
                if (Rand.Value < dose.PlantIgnite &&
                    FireUtility.TryStartFireIn(p.Position, map, dose.FireSize, null))
                    continue;

                if (!dose.Strips)
                {
                    // Trees are left alone here: a bare tree is the core's look, and giving the
                    // falloff one made the two bands indistinguishable.
                    PlagueFx.Wither(p);
                    p.Growth = dose.StuntTo;
                    // Growth is printed into the map mesh and the setter does not dirty it.
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

        // What StepPlants is for the other half: the pass that acts on the ground the circle has
        // reached. Walked over the cell field rather than over the plants, because a flowerbed
        // is a property of a cell and most of the ones wanted have nothing standing on them
        // yet - and in slices for the same reason StepPlants is, the field being the whole map.
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

                // The cheapest of the three questions, and the one that is false for most of
                // the map for most of a colony.
                if (cells[k] == Never) continue;

                var c = idx.IndexToCell(k);
                if (BandAt(c, now) == Band.None) continue;
                if (Grit(c, SowSalt) >= SowChance) continue;
                if (!Plantable(c)) continue;

                var plant = GenSpawn.Spawn(flowers.RandomElement(), c, map) as Plant;
                if (plant == null) continue;

                plant.Growth = Rand.Range(SowGrowthMin, SowGrowthMax);
                // Growth is printed into the map mesh and the setter does not dirty it - the
                // same thing the stunt in StepPlants has to do.
                map.mapDrawer?.MapMeshDirty(c, MapMeshFlagDefOf.Things);
                PlagueFx.Sprout(plant);
            }
        }

        // Deliberately short of everything CanEverPlantAt asks. Fertility carries most of it -
        // it is zero on water, on rock and on every plate the agents lay, so the paving stays
        // bare without being named here - and the rest is only that the cell is empty.
        bool Plantable(IntVec3 c) =>
            c.GetTerrain(map)?.fertility > 0f &&
            c.GetPlant(map) == null &&
            c.GetEdifice(map) == null &&
            !c.Filled(map);

        // Read off the database rather than named: a flower is whatever calls itself one, so
        // anything a mod added turns up here too, and a build that shipped none grows nothing
        // rather than throwing. Trees are out - purpose is Beauty on some of them, and a
        // forest arriving one trunk at a time is not what was asked for.
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
