using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// What the cat does to a dying world, which is the only argument this map has
    /// against the core - and it is made by hand, one pat at a time.
    ///
    /// It used to be weather: a green disc that followed the animal about, on
    /// whether it was standing there rather than on anything anybody did. That
    /// read as a second automatic system running against the first, and it was on
    /// screen constantly, so the pink and the green cancelled out into a colour the
    /// eye stopped seeing. Now the cat is the only thing on this map a player can
    /// touch (<see cref="Pets.Poke"/>, and the click that reaches it goes nowhere
    /// else), and the aura is what that touch is for: a pulse where the animal is
    /// standing, once per pat, and nothing at all in between.
    ///
    /// One pulse, four effects, and each is the exact undo of something in
    /// <see cref="Plague"/>:
    ///
    /// - <b>Cells.</b> Filth goes and fires go out. <c>Patch_NoRegrowth</c> stands
    ///   aside for as long as the pulse lasts, so the sterile core is allowed to
    ///   grow again where it landed.
    /// - <b>Fauna.</b> The mark comes off whatever was standing in it, and while a
    ///   thing is spared the spread declines to put it back on.
    /// - <b>Flora.</b> Plants inside are spared the sweep, and <em>one</em> of them
    ///   is put right: a stripped tree gets its leaves back, a stunted plant its
    ///   growth, and where the core left bare ground a sprout comes up instead.
    ///   That is the pat's answer, and the only green smoke on this map.
    /// - <b>The cat.</b> Every bad hediff on it goes, injuries and pain included -
    ///   see <see cref="Comfort"/>. Nothing else here gets that, and nothing else
    ///   here is being patted.
    ///
    /// The one plant is deliberate and so is the coin flip in front of it
    /// (<see cref="ReviveChance"/>): a pat that always worked would be a button
    /// for repairing the map, and a pat that healed a field would make the core
    /// look weak. One plant every second or third pat is a person kneeling in the
    /// ash putting things back one at a time, against a circle a quarter of the map
    /// wide that is taking them ten a tick.
    ///
    /// <b>Temporary</b> is the grace: a pulse spares what it touched for
    /// <see cref="GraceTicks"/> and its ground for as long. Without it a revived
    /// tree would be stripped again on the sweep's next pass through that cell,
    /// which is a pat visibly undone within the minute.
    ///
    /// Neither table is saved. A pat is a gesture and its half-life is an hour of
    /// colony time; persisting a dictionary of thing IDs to buy that back across a
    /// load would be writing down the weather.
    ///
    /// MapComponents are instantiated for every subclass, so this needs no def.
    /// </summary>
    public class Aura : MapComponent
    {
        // How far a pat reaches. Small - smaller than the aura that used to drift
        // about on its own - because this one is aimed: the player put the cursor
        // there, so the circle is the handful of cells under the animal rather than
        // a weather front it was dragging behind it.
        const float Radius = 3.9f;

        // How often a pat actually puts a plant back. Every second or third one, so
        // the ones that do land are worth watching for.
        const float ReviveChance = 0.42f;

        // How long a pulse holds: an hour of colony time, which at the plague's own
        // pace is a couple of dozen passes of the sweep.
        const int GraceTicks = 2500;

        // Filth cleared per pat. More than the old sweep took per pass, there being
        // one of these per click rather than two a second.
        const int FilthPerPat = 6;

        // How often the tables are swept for entries that have run out. Nothing else
        // happens on a tick here any more.
        const int PruneInterval = 60;

        // What counts as a plant the plague has held back, and what putting it right
        // means. Not full growth: a plant restored to ripe reads as a cheat, where
        // one restored to most of the way reads as one that has been growing again.
        const float StuntedBelow = 0.55f;
        const float ReviveGrowth = 0.80f;

        // A sprout, where there was nothing to revive. Small, and it grows from
        // there on its own, the sweep having been told to leave it alone.
        const float SproutGrowth = 0.25f;
        const int SproutTries = 25;

        // Verse.Plant.madeLeaflessTick, which is protected: LeaflessNow is
        // TicksGame - madeLeaflessTick < 60000, so pushing it into the far past is
        // how a stripped tree comes back. Bound by name and allowed to fail - a
        // field that moved costs the trees and nothing else.
        static readonly AccessTools.FieldRef<Plant, int> LeaflessTick = BindLeafless();

        // Where the pats landed, and when. Read per cell by Patch_NoRegrowth, so it
        // is a short list of cells rather than anything that has to be searched.
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

        /// <summary>Whether a live pulse is standing over this cell. The plain
        /// geometry, with nothing about what was in it: this is what decides whether
        /// ground grows and whether a fire may creep, and both are properties of the
        /// place rather than of anything standing there.</summary>
        public bool Covers(IntVec3 cell)
        {
            if (_pulses.Count == 0) return false;

            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < _pulses.Count; i++)
                if (now - _pulses[i].Tick < GraceTicks
                    && _pulses[i].At.DistanceTo(cell) <= Radius) return true;
            return false;
        }

        /// <summary>Whether the plague has to leave this thing alone: inside a pulse
        /// now, or touched by one recently enough. The second half is what carries a
        /// revived tree past the sweep's next pass, and what covers a thing that has
        /// since wandered out of the circle it was blessed in.</summary>
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

        /// <summary>
        /// A pat, which is the whole of this component's input. The cat is put right
        /// first - that is what the click was aimed at - and then the ground under it
        /// gets one pulse: filth and fire out, marks off, everything spared for a
        /// while, and, on a good roll, one plant back.
        /// </summary>
        public void Pat(Pawn pet)
        {
            if (pet == null || pet.Dead || !pet.Spawned || pet.Map != map) return;

            int now = Find.TickManager.TicksGame;
            Comfort(pet);

            _pulses.Add(new Pulse { At = pet.Position, Tick = now });
            Sweep(pet.Position, now);

            if (Rand.Value < ReviveChance) Revive(pet.Position, now);
        }

        /// <summary>
        /// What the pat does to the animal it lands on. Every bad hediff comes off -
        /// injuries, pain, the plague's own mark, whatever it picked up walking
        /// through a blast - because with health ticks stripped
        /// (<see cref="Patch_Health"/>) nothing on this map ever heals by itself, so
        /// a cat that took a cut in the intro would carry it for the life of the
        /// colony. Good hediffs are left where they are; <c>isBad</c> is the game's
        /// own word for the difference.
        ///
        /// A mental state goes with them. Vanilla's own recovery path is the one
        /// used rather than clearing the field, so the state gets to say its piece
        /// and put the pawn's job tracker back the way it found it.
        /// </summary>
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

        // The cells. Filth and fire go, and everything alive or growing in there is
        // spared - flora included, which is what keeps the revived plant standing
        // until the grace runs out. No haze on any of it: the only green smoke this
        // map gets is the one plant, and a puff on every pat everywhere would spend
        // that.
        void Sweep(IntVec3 centre, int now)
        {
            int cells = GenRadial.NumCellsInRadius(Radius);
            int filth = FilthPerPat;

            for (int i = 0; i < cells; i++)
            {
                var c = centre + GenRadial.RadialPattern[i];
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
                        // An attached fire is spawned in the cell like any other, so
                        // this is also how a burning animal stops burning.
                        t.Destroy(DestroyMode.Vanish);
                    }
                    else if (t is Plant plant) _grace[plant.thingIDNumber] = now;
                    else if (t is Pawn pawn) Unmark(pawn, now);
                }
            }
        }

        // The cleanse, on something alive. Recording the grace is most of it; the
        // mark coming off is the rest, and neither is worth a puff - see Revive for
        // where the pat's one piece of theatre goes.
        void Unmark(Pawn pawn, int now)
        {
            if (pawn.Dead) return;
            _grace[pawn.thingIDNumber] = now;

            var mark = pawn.health?.hediffSet?.GetFirstHediffOfDef(SlopDefOf.SlopPlague);
            if (mark != null) pawn.health.RemoveHediff(mark);
        }

        /// <summary>
        /// One plant, put back, with the green on it. Something the core has damaged
        /// is preferred - a stripped tree, a plant the falloff stunted - because
        /// repairing what is visibly wrong reads better than adding to what is
        /// merely absent. Where there is nothing left to repair, which is most of
        /// the certain core, a sprout comes up instead: the sterile ground is the
        /// thing being argued with, so a pat there has to answer with something
        /// growing rather than with nothing happening.
        /// </summary>
        void Revive(IntVec3 centre, int now)
        {
            var hurt = Damaged(centre);
            if (hurt != null) { Mend(hurt, now); return; }
            Sow(centre, now);
        }

        // The plants inside the pulse that the plague has had at. A bare tree comes
        // first whatever else is standing about: that one is unambiguously something
        // the core did, where a plant merely short of full growth might only be
        // young. Within a list the pick is random rather than nearest, because a run
        // of pats should work outwards in no particular order rather than clearing a
        // tidy ring.
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

        // A tree gets its leaves back, a stunted plant its growth. Leaflessness is
        // nothing but a subtraction against madeLeaflessTick, so dating that into the
        // past is the whole repair.
        void Mend(Plant plant, int now)
        {
            _grace[plant.thingIDNumber] = now;

            if (plant.LeaflessNow && LeaflessTick != null) LeaflessTick(plant) = -60000;
            if (plant.Growth < ReviveGrowth) plant.Growth = ReviveGrowth;

            // Growth and leaflessness are both printed into the map mesh and neither
            // setter dirties it - see Plague.StepPlants, which pays the same price.
            map.mapDrawer?.MapMeshDirty(plant.Position, MapMeshFlagDefOf.Things);
            Puff(plant);
        }

        // Bare ground, so something is put in it: whatever this biome grows, weighted
        // the way the biome weights it, in a cell that would take it. CanEverPlantAt
        // is the game's own answer to "may this stand here" - terrain, roof, what is
        // already in the cell - so nothing here has to know about any of that.
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
                // The spawn dirties the cell, but it does so before the growth is
                // written; see Mend for the setter that never dirties anything.
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

        // The plague's plumbing in the other colour; see PlagueFx.At for why the def
        // is a parameter and everything else is shared. Small, and it stays small
        // however much this one puff has to carry: it marks a single plant, and a
        // cloud that covers its neighbours says the pat mended the patch.
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
