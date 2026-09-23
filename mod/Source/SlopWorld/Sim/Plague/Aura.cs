using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Pets.Poke reverses local plague effects. WaterBall uses the same restorative pulse.
    // Keep pulse and protection tables only in memory. The map saves terrain changes.
    public class Aura : MapComponent
    {
        // Affect nearby cells around the animal.
        const float Radius = 3.9f;
        const float RadiusSq = Radius * Radius;

        // Each pat has this chance to restore or plant vegetation.
        const float ReviveChance = 0.42f;

        // Game ticks of protection after a pulse.
        const int GraceTicks = 2500;

        const int FilthPerPat = 6;
        const int PruneInterval = 60;

        // Restore partial growth so plants do not immediately appear mature.
        const float StuntedBelow = 0.55f;
        const float ReviveGrowth = 0.80f;

        // Start new plants at partial growth. Temporary protection permits further growth.
        const float SproutGrowth = 0.25f;
        const int SproutTries = 25;

        // Plant.madeLeaflessTick is protected. LeaflessNow checks whether fewer than 60000 ticks have elapsed.
        // Move the value into the past to restore leaves. Permit field binding to fail.
        static readonly AccessTools.FieldRef<Plant, int> LeaflessTick = BindLeafless();

        // Patch_NoRegrowth checks this list for each cell.
        readonly List<Pulse> _pulses = new List<Pulse>();

        // Map each thingIDNumber to the game tick of its most recent pulse.
        readonly Dictionary<int, int> _grace = new Dictionary<int, int>();
        readonly List<int> _stale = new List<int>();

        struct Pulse
        {
            public IntVec3 At;
            public int Tick;
        }

        public Aura(Map map) : base(map) { }

        public static Aura Of(Map map) => map?.GetComponent<Aura>();

        // Check location and pulse age. Plant growth and fire spread depend on the affected cells.
        public bool Covers(IntVec3 cell)
        {
            if (_pulses.Count == 0) return false;

            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < _pulses.Count; i++)
                if (now - _pulses[i].Tick < GraceTicks
                    && _pulses[i].At.DistanceToSquared(cell) <= RadiusSq) return true;
            return false;
        }

        // Also protect individual things after they leave the pulse area.
        public bool Spares(Thing t)
        {
            if (t == null || !t.Spawned) return false;
            if (Covers(t.Position)) return true;
            return _grace.TryGetValue(t.thingIDNumber, out int seen)
                && Find.TickManager.TicksGame - seen < GraceTicks;
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % PruneInterval != 0) return;
            Prune(Find.TickManager.TicksGame);
        }

        // Heal the pet before applying a pulse to nearby cells.
        public void Pat(Pawn pet)
        {
            if (pet == null || pet.Dead || !pet.Spawned || pet.Map != map) return;

            int now = Find.TickManager.TicksGame;
            Comfort(pet);

            _pulses.Add(new Pulse { At = pet.Position, Tick = now });
            Sweep(pet.Position, now);

            if (Rand.Value < ReviveChance) Revive(pet.Position, now);
        }

        // Apply the WaterBall pulse without the random chance used for pet pats.
        // Heal nearby pawns, restore existing plants, and try to add a new plant.
        public void Rejuvenate(IntVec3 centre)
        {
            if (!centre.InBounds(map)) return;

            int now = Find.TickManager.TicksGame;
            _pulses.Add(new Pulse { At = centre, Tick = now });
            Sweep(centre, now);

            int cells = GenRadial.NumCellsInRadius(Radius);
            for (int i = 0; i < cells; i++)
            {
                var c = centre + GenRadial.RadialPattern[i];
                if (!c.InBounds(map)) continue;

                var plant = c.GetPlant(map);
                if (plant == null || plant.Destroyed || plant.def?.plant == null) continue;

                if (plant.LeaflessNow || plant.Growth < ReviveGrowth) Mend(plant, now);
                else _grace[plant.thingIDNumber] = now;
            }

            Sow(centre, now);
        }

        // Remove harmful health conditions because the game disables normal health ticks.
        // Use the base game recovery method to end mental states and restore job tracking.
        static void Comfort(Pawn pet)
        {
            if (pet == null || pet.Dead || pet.health == null) return;

            HealthUtility.HealNonPermanentInjuriesAndRestoreLegs(pet);

            var set = pet.health?.hediffSet;
            if (set != null)
            {
                // Copy the conditions before RemoveHediff changes the list.
                var bad = set.hediffs.Where(h => h?.def != null && h.def.isBad).ToList();
                foreach (var h in bad) pet.health.RemoveHediff(h);
            }

            pet.mindState?.mentalStateHandler?.CurState?.RecoverFromState();
        }

        // Apply nearby effects without a general smoke effect.
        void Sweep(IntVec3 centre, int now)
        {
            int cells = GenRadial.NumCellsInRadius(Radius);
            int filth = FilthPerPat;

            for (int i = 0; i < cells; i++)
            {
                var c = centre + GenRadial.RadialPattern[i];
                if (!c.InBounds(map)) continue;

                var things = c.GetThingList(map);
                // Iterate backward because destruction removes items from this list.
                for (int j = things.Count - 1; j >= 0; j--)
                {
                    var t = things[j];
                    if (t == null || t.Destroyed) continue;

                    if (t is Filth)
                    {
                        if (filth-- > 0) t.Destroy(DestroyMode.Vanish);
                    }
                    else if (t is Fire)
                    {
                        // Attached fires also appear in the cell list. Remove them to extinguish burning animals.
                        t.Destroy(DestroyMode.Vanish);
                    }
                    else if (t is Plant plant) _grace[plant.thingIDNumber] = now;
                    else if (t is Pawn pawn)
                    {
                        Comfort(pawn);
                        Unmark(pawn, now);
                    }
                }
            }
        }

        // Record temporary protection and remove the plague condition without a visual effect.
        void Unmark(Pawn pawn, int now)
        {
            if (pawn.Dead) return;
            _grace[pawn.thingIDNumber] = now;

            var mark = pawn.health?.hediffSet?.GetFirstHediffOfDef(ModDefOf.SlopPlague);
            if (mark != null) pawn.health.RemoveHediff(mark);
        }

        // Restore a damaged plant if one exists. Otherwise, try to add a new plant.
        void Revive(IntVec3 centre, int now)
        {
            var hurt = Damaged(centre);
            if (hurt != null) { Mend(hurt, now); return; }
            Sow(centre, now);
        }

        // Prefer leafless plants. Plants with low growth can be young rather than damaged.
        Plant Damaged(IntVec3 centre)
        {
            var bare = new List<Plant>();
            var stunted = new List<Plant>();
            int cells = GenRadial.NumCellsInRadius(Radius);

            for (int i = 0; i < cells; i++)
            {
                var c = centre + GenRadial.RadialPattern[i];
                if (!c.InBounds(map)) continue;

                var p = c.GetPlant(map);
                if (p == null || p.Destroyed || p.def?.plant == null) continue;

                if (p.LeaflessNow) bare.Add(p);
                else if (p.Growth < StuntedBelow) stunted.Add(p);
            }

            if (bare.Count > 0) return bare.RandomElement();
            return stunted.Count == 0 ? null : stunted.RandomElement();
        }

        void Mend(Plant plant, int now)
        {
            _grace[plant.thingIDNumber] = now;

            if (plant.LeaflessNow && LeaflessTick != null) LeaflessTick(plant) = -60000;
            if (plant.Growth < ReviveGrowth) plant.Growth = ReviveGrowth;

            // Neither setter invalidates the map mesh. See Plague.StepPlants.
            map.mapDrawer?.MapMeshDirty(plant.Position, MapMeshFlagDefOf.Things);
            Puff(plant);
        }

        // Use CanEverPlantAt to check terrain, roofs, and existing cell contents.
        void Sow(IntVec3 centre, int now)
        {
            var biome = map.Biome;
            if (biome == null) return;

            int cells = GenRadial.NumCellsInRadius(Radius);

            for (int t = 0; t < SproutTries; t++)
            {
                var c = centre + GenRadial.RadialPattern[Rand.Range(0, cells)];
                if (!c.InBounds(map) || c.GetPlant(map) != null) continue;

                var def = biome.AllWildPlants
                    .Where(p => p.CanEverPlantAt(c, map))
                    .RandomElementByWeightWithFallback(p => biome.CommonalityOfPlant(p));
                if (def == null) continue;

                var plant = GenSpawn.Spawn(def, c, map) as Plant;
                if (plant == null) return;

                plant.Growth = SproutGrowth;
                // Invalidate the mesh again after setting growth.
                map.mapDrawer?.MapMeshDirty(c, MapMeshFlagDefOf.Things);

                _grace[plant.thingIDNumber] = now;
                Puff(plant);
                return;
            }
        }

        void Prune(int now)
        {
            _pulses.RemoveAll(p => now - p.Tick >= GraceTicks);

            if (_grace.Count == 0) return;

            _stale.Clear();
            foreach (var kv in _grace)
                if (now - kv.Value >= GraceTicks) _stale.Add(kv.Key);
            foreach (var id in _stale) _grace.Remove(id);
        }

        // Keep the effect small to identify the single restored plant.
        static void Puff(Thing t) =>
            PlagueFx.At(ModDefOf.SlopCleanAir, t, 10, 0.85f, 0.20f, 0.28f);

        static AccessTools.FieldRef<Plant, int> BindLeafless()
        {
            try
            {
                return AccessTools.FieldRefAccess<Plant, int>("madeLeaflessTick");
            }
            catch (Exception e)
            {
                Log.Warning($"[SlopWorld] no Plant.madeLeaflessTick, so the cat cannot " +
                            $"bring a bare tree back: {e.Message}");
                return null;
            }
        }
    }
}
