using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // The only argument this map has against the core, made one pat at a time (Pets.Poke).
    // Each effect is the exact undo of something in Plague: filth and fire go and
    // Patch_NoRegrowth stands aside, the mark comes off whatever was standing there, plants
    // inside are spared the sweep and *one* is put right, and the cat is healed (Comfort).
    // Neither table is saved.
    public class Aura : MapComponent
    {
        // Aimed: the handful of cells under the animal rather than a weather front.
        const float Radius = 3.9f;

        // Every second or third pat, so the ones that land are worth watching for.
        const float ReviveChance = 0.42f;

        // An hour of colony time - a couple of dozen passes of the plague's sweep.
        const int GraceTicks = 2500;

        const int FilthPerPat = 6;
        const int PruneInterval = 60;

        // Not full growth: restored to ripe reads as a cheat, most of the way reads as
        // something that has been growing again.
        const float StuntedBelow = 0.55f;
        const float ReviveGrowth = 0.80f;

        // It grows from there on its own, the sweep having been told to leave it alone.
        const float SproutGrowth = 0.25f;
        const int SproutTries = 25;

        // Verse.Plant.madeLeaflessTick is protected, and LeaflessNow is
        // `TicksGame - madeLeaflessTick < 60000`, so pushing it into the past is the repair.
        // Bound by name and allowed to fail.
        static readonly AccessTools.FieldRef<Plant, int> LeaflessTick = BindLeafless();

        // Read per cell by Patch_NoRegrowth, so it is a short list of cells.
        readonly List<Pulse> _pulses = new List<Pulse>();

        // thingIDNumber -> the tick a pulse last had it.
        readonly Dictionary<int, int> _grace = new Dictionary<int, int>();
        readonly List<int> _stale = new List<int>();

        struct Pulse
        {
            public IntVec3 At;
            public int Tick;
        }

        public Aura(Map map) : base(map) { }

        public static Aura Of(Map map) => map?.GetComponent<Aura>();

        // Plain geometry: whether ground grows and whether a fire may creep both belong to
        // the place rather than to what was standing in it.
        public bool Covers(IntVec3 cell)
        {
            if (_pulses.Count == 0) return false;

            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < _pulses.Count; i++)
                if (now - _pulses[i].Tick < GraceTicks
                    && _pulses[i].At.DistanceTo(cell) <= Radius) return true;
            return false;
        }

        // The second half carries a revived tree past the sweep's next pass, and covers a
        // thing that has wandered out of the circle it was blessed in.
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

        // The cat first - that is what the click was aimed at - then one pulse on the ground.
        public void Pat(Pawn pet)
        {
            if (pet == null || pet.Dead || !pet.Spawned || pet.Map != map) return;

            int now = Find.TickManager.TicksGame;
            Comfort(pet);

            _pulses.Add(new Pulse { At = pet.Position, Tick = now });
            Sweep(pet.Position, now);

            if (Rand.Value < ReviveChance) Revive(pet.Position, now);
        }

        // Every bad hediff, injuries and pain included: with health ticks stripped nothing
        // heals by itself, so a cat cut in the intro would carry it for the life of the
        // colony. A mental state goes through vanilla's own recovery path, which puts the job
        // tracker back the way it found it.
        static void Comfort(Pawn pet)
        {
            var set = pet.health?.hediffSet;
            if (set != null)
            {
                // Copied first: RemoveHediff writes to the very list being walked.
                var bad = set.hediffs.Where(h => h?.def != null && h.def.isBad).ToList();
                foreach (var h in bad) pet.health.RemoveHediff(h);
            }

            pet.mindState?.mentalStateHandler?.CurState?.RecoverFromState();
        }

        // No haze: the only green smoke this map gets is the one plant.
        void Sweep(IntVec3 centre, int now)
        {
            int cells = GenRadial.NumCellsInRadius(Radius);
            int filth = FilthPerPat;

            for (int i = 0; i < cells; i++)
            {
                var c = centre + GenRadial.RadialPattern[i];
                if (!c.InBounds(map)) continue;

                var things = c.GetThingList(map);
                // Backwards, because destroying takes the thing out of this very list.
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
                        // An attached fire is spawned in the cell like any other, so this is
                        // also how a burning animal stops burning.
                        t.Destroy(DestroyMode.Vanish);
                    }
                    else if (t is Plant plant) _grace[plant.thingIDNumber] = now;
                    else if (t is Pawn pawn) Unmark(pawn, now);
                }
            }
        }

        // Recording the grace is most of it; neither half is worth a puff.
        void Unmark(Pawn pawn, int now)
        {
            if (pawn.Dead) return;
            _grace[pawn.thingIDNumber] = now;

            var mark = pawn.health?.hediffSet?.GetFirstHediffOfDef(SlopDefOf.SlopPlague);
            if (mark != null) pawn.health.RemoveHediff(mark);
        }

        // Something damaged first; where there is nothing left to repair - most of the certain
        // core - a sprout comes up instead.
        void Revive(IntVec3 centre, int now)
        {
            var hurt = Damaged(centre);
            if (hurt != null) { Mend(hurt, now); return; }
            Sow(centre, now);
        }

        // A bare tree first: that is unambiguously something the core did, where a plant short
        // of full growth might only be young.
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

            // Neither setter dirties the map mesh; see Plague.StepPlants.
            map.mapDrawer?.MapMeshDirty(plant.Position, MapMeshFlagDefOf.Things);
            Puff(plant);
        }

        // CanEverPlantAt is the game's own answer to "may this stand here" - terrain, roof,
        // what is already in the cell.
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
                // The spawn dirties the cell, but before the growth is written.
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

        // Small: it marks a single plant, and a cloud over its neighbours would say the pat
        // mended the patch.
        static void Puff(Thing t) =>
            PlagueFx.At(SlopDefOf.SlopCleanAir, t, 10, 0.85f, 0.20f, 0.28f);

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
