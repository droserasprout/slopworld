using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// What the cat does to a dying world, which is the only argument this map has
    /// against the core. A few cells around it the plague loses: the ground gets
    /// cleaned, what is standing in it is unmarked, and nothing there is taken while
    /// the cat is near - or for a while after it has wandered off.
    ///
    /// It is small on purpose. The circle is a quarter of the map and this is seven
    /// cells of it, so the aura is not a cure and could never be mistaken for one; it
    /// is a patch of green that moves about, which is a thing to watch rather than a
    /// thing to manage. Nobody can steer it either - the cat is not selectable and
    /// there is no sim to give it orders through - so where the good ground goes is
    /// the cat's business.
    ///
    /// Three effects, and each is the exact undo of something in <see cref="Plague"/>:
    ///
    /// - <b>Cells.</b> Filth goes (a couple per sweep, so a bloodied cell takes a
    ///   moment rather than blinking clean), fires go out, and
    ///   <c>Patch_NoRegrowth</c> stands aside so the sterile core grows again where
    ///   the cat is standing. That last one is what makes the aura read: plants come
    ///   back on their own, so a cat that stays put leaves a green disc behind it.
    /// - <b>Fauna.</b> The mark comes off, which is the cleanse, and while a pawn is
    ///   spared the spread declines to put it back on, which is the immunity. The
    ///   effect roll checks as well, since a marked animal that walks in is not
    ///   unmarked until the next sweep and half a second is long enough to detonate
    ///   in.
    /// - <b>Flora.</b> A plant the aura covers is skipped by the sweep, and a tree the
    ///   core already stripped gets its leaves back.
    ///
    /// <b>Temporary</b> is the grace: a thing the aura touched stays spared for
    /// <see cref="GraceTicks"/> after the cat has moved on. Without it the aura would
    /// be a hard-edged circle sliding across the map - the plague's own first cut and
    /// the same mistake - and the plants it walked over would be stripped again
    /// before the cat was out of frame. With it, the green fades out behind the cat
    /// rather than being switched off.
    ///
    /// The grace table is runtime and is not saved. It rebuilds itself within half a
    /// second of a load, because the cat is still standing where it was; persisting it
    /// would mean writing out a dictionary keyed on thing IDs to buy back thirty
    /// ticks.
    ///
    /// MapComponents are instantiated for every subclass, so this needs no def.
    /// </summary>
    public class Aura : MapComponent
    {
        // How far the cat pushes back, and how often it is asked to. Half a second
        // is fast enough that a walking cat leaves no gaps and slow enough that the
        // cell sweep is nothing next to what the plague itself is doing.
        const float Radius = 6.9f;
        const int SweepInterval = 30;

        // How long a thing stays spared after the cat has gone: an hour of colony
        // time, which at the plague's own pace is a couple of dozen sweeps.
        const int GraceTicks = 2500;

        // Filth cleared per sweep, over every cat there is. A cat that walks into
        // the aftermath of a detonation should be wiping it up for a few seconds,
        // not deleting it on arrival.
        const int FilthPerSweep = 2;

        // Sweeps between ambient puffs. The aura has to be visible when it is doing
        // nothing in particular - most of the time it is - but one breath a second
        // is a haze and four is a fog.
        const int PuffEvery = 4;

        // Verse.Plant.madeLeaflessTick, which is protected: LeaflessNow is
        // TicksGame - madeLeaflessTick < 60000, so pushing it into the far past is
        // how a stripped tree comes back. Bound by name and allowed to fail - a
        // field that moved costs the trees and nothing else.
        static readonly AccessTools.FieldRef<Plant, int> LeaflessTick = BindLeafless();

        // Where the cats were standing at the last sweep. Cached rather than asked
        // for per call because Covers is what Patch_NoRegrowth reads, and that runs
        // per cell over the whole map during generation.
        readonly List<IntVec3> _spots = new List<IntVec3>();

        // thingIDNumber -> the tick the aura last had it. Pruned on the sweep, so it
        // holds what the cat has recently walked past and nothing older.
        readonly Dictionary<int, int> _grace = new Dictionary<int, int>();
        readonly List<int> _stale = new List<int>();

        int _sweeps;

        public Aura(Map map) : base(map) { }

        public static Aura Of(Map map) => map?.GetComponent<Aura>();

        /// <summary>Whether the cat is standing over this cell right now. The plain
        /// geometry, with no grace in it: this is what decides whether ground grows
        /// and whether a fire may creep, and both are properties of the place rather
        /// than of anything that was standing in it.</summary>
        public bool Covers(IntVec3 cell)
        {
            for (int i = 0; i < _spots.Count; i++)
                if (_spots[i].DistanceTo(cell) <= Radius) return true;
            return false;
        }

        /// <summary>Whether the plague has to leave this thing alone: under the aura
        /// now, or under it recently enough. The live check is what covers the half
        /// second between something walking in and the next sweep noticing.</summary>
        public bool Spares(Thing t)
        {
            if (t == null || !t.Spawned) return false;
            if (Covers(t.Position)) return true;
            return _grace.TryGetValue(t.thingIDNumber, out int seen)
                && Find.TickManager.TicksGame - seen < GraceTicks;
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % SweepInterval != 0) return;
            Sweep();
        }

        void Sweep()
        {
            int now = Find.TickManager.TicksGame;

            var pets = Pets.On(map);
            _spots.Clear();
            foreach (var pet in pets)
                if (pet.Spawned) _spots.Add(pet.Position);

            Prune(now);
            if (_spots.Count == 0) return;

            bool puff = ++_sweeps % PuffEvery == 0;
            int cells = GenRadial.NumCellsInRadius(Radius);
            int filth = FilthPerSweep;

            foreach (var spot in _spots)
            {
                for (int i = 0; i < cells; i++)
                {
                    var c = spot + GenRadial.RadialPattern[i];
                    if (!c.InBounds(map)) continue;

                    var things = c.GetThingList(map);
                    // Backwards, because destroying takes the thing out of this very
                    // list: anything shifted down is something already visited.
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
                            // Attached fires are spawned in the cell like any other,
                            // so this is also how an animal stops burning.
                            t.Destroy(DestroyMode.Vanish);
                        }
                        else if (t is Plant plant) Bless(plant, now);
                        else if (t is Pawn pawn) Bless(pawn, now);
                    }
                }
            }

            if (!puff) return;
            foreach (var pet in pets) Puff(pet, 3, 1.3f, 1.4f);
        }

        // The cleanse, on something alive. Recording the grace is most of it; the
        // mark coming off is the part with a puff on it, because that is the moment
        // the aura won something rather than merely holding.
        void Bless(Pawn pawn, int now)
        {
            if (pawn.Dead) return;
            _grace[pawn.thingIDNumber] = now;

            var mark = pawn.health?.hediffSet?.GetFirstHediffOfDef(SlopDefOf.SlopPlague);
            if (mark == null) return;

            pawn.health.RemoveHediff(mark);
            Puff(pawn, 4, 1.2f, 0.4f);
        }

        // The same for a plant, plus the one thing here that can actually be undone.
        // A stripped tree is leafless for 60000 ticks from when it was stripped, so
        // dating that back is the whole repair; growth is not touched, because a
        // stunted plant grows again by itself the moment the sweep stops knocking it
        // down, and a plant the core destroyed is gone and comes back as regrowth.
        void Bless(Plant plant, int now)
        {
            _grace[plant.thingIDNumber] = now;

            if (LeaflessTick == null || !plant.LeaflessNow) return;
            LeaflessTick(plant) = -60000;
            // Growth and leaflessness are both printed into the map mesh, and neither
            // setter dirties it - see Plague.StepPlants, which pays the same price.
            map.mapDrawer?.MapMeshDirty(plant.Position, MapMeshFlagDefOf.Things);
            Puff(plant, 2, 0.9f, 0.3f);
        }

        void Prune(int now)
        {
            if (_grace.Count == 0) return;

            _stale.Clear();
            foreach (var kv in _grace)
                if (now - kv.Value >= GraceTicks) _stale.Add(kv.Key);
            foreach (var id in _stale) _grace.Remove(id);
        }

        // The plague's plumbing in the other colour; see PlagueFx.At for why the def
        // is a parameter and everything else is shared.
        static void Puff(Thing t, int count, float scale, float spread) =>
            PlagueFx.At(SlopDefOf.SlopCleanAir, t, count, scale, 0.18f, spread);

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
