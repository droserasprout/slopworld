using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // The plague is the union of a source per finished thing. The core emits a small
    // fixed circle, being the one bit of this nobody had to build; everything else is a
    // monument or a plate the agents raised, each of which walks the plague a few cells
    // further out of itself. So the map does not die in a disc widening on a figure - it
    // dies where the agents have been, and the shape of the dead ground is the shape of
    // an hour's work.
    //
    // What is kept is one arrival tick a cell (Cells) - the tick the plague got there -
    // and everything else is read off it and off the clock. A cell's *age* is its dose:
    // newly reached ground is the fringe and old ground is certain. That is the same
    // falloff the circle carried as a rim, stated in time rather than in radius, and it
    // costs a lookup where asking every source would cost a loop. Which is not a detail:
    // a source per paved cell is thousands of sources and BandAt is asked ten thousand
    // times a second, so the field is the only reason this model is affordable at all.
    //
    // Saying it in time is also what makes one figure right at both ends of the scale. A
    // stamp two cells across is a fringe entire for half a minute and solid after; ground
    // the agents have been paving over for an hour is old in the middle and young only at
    // the outer hull, with no ring measured off anything.
    //
    // The bands must not have edges and must not converge. A hard threshold draws a line
    // you can trace with a finger, and a chance re-rolled each sweep still ends in one
    // flat dead disc, just later - so the falloff is dithered against Grit, a value each
    // cell keeps forever, and the weak band holds plants back instead of taking them.
    //
    // Agents are immune wholly: an agent's colonist dying would leave its session
    // pointing at a corpse. Effects are applied directly because there is no sim -
    // Patch_Health skips health ticks, so nothing ever bleeds out on its own. Fire is
    // the exception: a Fire is a Thing with its own tick, which the strip does not
    // touch.
    public class Plague : MapComponent
    {
        // Who has caught it, asked once a real second. The plague itself no longer moves on
        // a clock of its own - it moves when something is finished - so this is the pass
        // over what is standing on the map and nothing else.
        const int CatchInterval = 60;

        // The core's own reach, and the only plague here nobody had to build. Fixed and
        // small on purpose: a colony where no session was ever busy is a stain in the middle
        // of a living map, which is the honest picture of nothing having happened.
        const float CoreRadius = 12f;

        // How fast the plague walks out of a source, in ticks per cell of remove. Four real
        // seconds a cell: too slow to catch happening, fast enough that a monument finished
        // now stands in dead ground within the minute.
        const int CreepPerCell = 240;

        // And how long a cell takes to go from newly reached to certainly dead. The fringe
        // is this over the creep - thirty real seconds at four seconds a cell is seven and a
        // half cells of falloff, which is about the rim the circle used to carry, except
        // that it now belongs to the cell rather than to a radius.
        const int RipenTicks = 1800;

        // What is left over after the odds below is a quiet tick.
        const int EffectInterval = 300;

        // Rare and enormous rather than common and a firecracker.
        const float BlastRadius = 5.5f;
        const int BlastDamage = 200;
        const float BlastSafeRadius = 9f; // no closer to an agent than this, > BlastRadius

        const int BloodPerBleed = 6;
        const float BloodRadius = 1.8f;

        // Withering is cheap, but a grown map has thousands of plants.
        const int PlantsPerTick = 5;

        // Three breaths a second of the smallest puff there is. Anything heavier is a fog
        // bank parked on the middle of the map, with everything the plague does out at
        // the edge read through it.
        const int VentInterval = 20;

        // Read from the cell, not from the mark, so a marked animal that wanders out of
        // reach goes quiet and starts up again when it wanders back.
        public enum Band { None, Weak, Full }

        // Probabilities are per roll and share one pass, so they add rather than compose.
        struct Dose
        {
            public float PExplode, PIgnite, PBleed, PVomit;
            public float BleedMin, BleedMax;
            public float FireSize;
            public float PlantIgnite; // per plant, once, on the sweep that strips it

            // The difference the eye actually reads: stripping both bands identically left
            // nothing telling them apart but pawn effects nobody watches.
            public bool Strips;

            // What the weak band knocks a plant's growth back to.
            public float StuntTo;

            // The gap is load-bearing. Nothing stops a plant ticking here - the strip takes
            // needs, health, age and the storyteller, not Plant.TickLong - so a test against
            // StuntTo comes true again within one pass of the sweep: every weak plant puffed,
            // re-stunted by a fraction of a percent and re-rolled for ignition every half
            // minute, forever, for no visible change.
            public float StuntFrom;
        }

        // Detonation and ignition are both rare on purpose: one is loud enough to be an
        // event and the other does not stop when the roll is over.
        static readonly Dose Full = new Dose
        {
            PExplode = 0.015f,
            PIgnite = 0.020f,
            PBleed = 0.300f,
            PVomit = 0.250f,
            BleedMin = 8f,
            BleedMax = 18f,
            FireSize = 1.0f,
            // ~15k plants fall inside the circle on a default map and each is rolled once, so
            // this is a handful of ignitions over the whole first sweep.
            PlantIgnite = 0.00025f,
            Strips = true,
        };

        // Nothing detonates out here: a blast is the plague at full strength and there is
        // no half of one. Plants are held back rather than taken, so the band reads as
        // thin instead of as more dead ground.
        static readonly Dose Weak = new Dose
        {
            PExplode = 0f,
            PIgnite = 0.005f,
            PBleed = 0.120f,
            PVomit = 0.150f,
            BleedMin = 3f,
            BleedMax = 7f,
            FireSize = 0.4f,
            PlantIgnite = 0.00008f,
            Strips = false,
            StuntTo = 0.15f,
            StuntFrom = 0.45f,
        };

        // The seed is what makes the dither this colony's rather than every colony's:
        // without it two maps thin out through the exact same speckle.
        IntVec3 _origin = IntVec3.Invalid;
        bool _active;
        int _seed;

        // The plague's whole extent: the tick the plague arrived at each cell, or Never for
        // ground nothing has got to. Min-combined on the way in and never raised, because
        // nothing on this map takes a finished thing back off the board - so the field only
        // ever fills in, which is exactly what lets a question about it be an array index.
        //
        // Filled with Never rather than left at zero, zero being a tick the game has, and
        // allocated on first use rather than in the constructor, a MapComponent being built
        // while the map still is.
        const int Never = int.MaxValue;
        int[] _cells;

        // Cells with an arrival written. The only score this map keeps, maintained where the
        // writing happens rather than measured afterwards - and Girth is read off it.
        int _reached;

        // True between the load and FinalizeInit, while _cells holds offsets in seconds
        // instead of ticks. See Pack.
        bool _relative;

        // The last twentieth of the map written to the log, so the one number worth having
        // turns up there without a line every three seconds.
        int _logged = -1;

        // Refilled when it runs off the end, which is also how regrowth gets caught.
        // Cleared rather than rebuilt: a lap is a minute and a grown map is tens of
        // thousands of plants.
        readonly List<Plant> _plants = new List<Plant>();
        int _plantIdx;

        // Effects has to walk a copy: an effect can despawn the pawn it lands on.
        readonly List<Pawn> _rolling = new List<Pawn>();

        // See Aura: the one thing on this map that takes ground back off the circle.
        Aura _aura;

        // Held rather than asked for every eighth tick, and dropped if it stops being
        // spawned.
        Thing _core;

        public Plague(Map map) : base(map) { }

        int[] Cells
        {
            get
            {
                if (_cells == null)
                {
                    _cells = new int[map.Area];
                    for (int i = 0; i < _cells.Length; i++) _cells[i] = Never;
                }
                return _cells;
            }
        }

        // Called by the intro once the core is standing; a reload picks up from the
        // persisted state.
        public void Arm(IntVec3 origin)
        {
            if (_active) return;
            _origin = origin;
            _seed = Rand.Int;
            _active = true;
            Bloom(origin, CoreRadius);
            Log.Message($"[SlopWorld] plague seeded at {origin}, {CoreRadius:F0} cells of it, " +
                        "and every cell past that is one the agents have to build for");
        }

        public override void MapComponentTick()
        {
            if (!_active) return;

            int t = Find.TickManager.TicksGame;
            if (t % CatchInterval == 0) Catch();
            if (t % EffectInterval == 0) Effects();
            if (t % VentInterval == 0) Vent();
            StepPlants();
        }

        // Where the offsets the load put in become arrival ticks. It has to be here rather
        // than in Unpack: a map is scribed *before* the tick manager is, so the only place
        // the clock can be asked is after the whole game is standing.
        //
        // A save from before the field existed is not migrated. The plague was a radius and a
        // figure then and nothing about that says which cell died when, so the ground it had
        // is gone; what it gets instead is the core doing what the core always does, since an
        // armed plague with nothing reached at all is not a state this has.
        public override void FinalizeInit()
        {
            base.FinalizeInit();
            if (!_active) return;

            if (_relative)
            {
                _relative = false;
                int now = Find.TickManager.TicksGame;
                var cells = Cells;

                for (int i = 0; i < cells.Length; i++)
                    if (cells[i] != Never)
                        cells[i] = now + (int)(cells[i] * RealClock.TicksPerRealSecond);
            }

            if (_reached == 0 && _origin.IsValid) Bloom(_origin, CoreRadius);
        }

        // The plume is what makes the thing in the middle of the map the source of what
        // is happening to it rather than a prop the plague was seeded next to.
        void Vent()
        {
            if (_core == null || _core.Destroyed || !_core.Spawned)
            {
                var found = map.listerThings.ThingsOfDef(SlopDefOf.Ship_ComputerCore);
                _core = found != null && found.Count > 0 ? found[0] : null;
                if (_core == null) return;
            }

            PlagueFx.Vent(_core);
        }

        // Out of bounds is not in the plague, and asking the grid about it would be reading
        // somebody else's row.
        int At(IntVec3 cell) =>
            cell.InBounds(map) ? Cells[map.cellIndices.CellToIndex(cell)] : Never;

        // The dose a cell is carrying: nothing until the plague gets there, then up from
        // nothing to certain over RipenTicks. Age rather than distance, which is what makes
        // one figure right for a stamp the size of a plate and for the whole board.
        float Bite(IntVec3 cell)
        {
            int at = At(cell);
            if (at == Never) return 0f;

            int age = Find.TickManager.TicksGame - at;
            if (age <= 0) return 0f; // stamped, still on its way
            if (age >= RipenTicks) return 1f;
            return age / (float)RipenTicks;
        }

        // A thing the agents have finished is a source, and this is the whole of how the
        // plague grows: the arrival times that source implies, written only where they beat
        // what is already there - so ground reached ten minutes ago keeps its own date and a
        // plate laid beside it never restarts the creep.
        //
        // Called per cell of the finished thing's footprint (Worksite.Patch_ErrandDone), so
        // a machine five by three blooms as five by three rather than as a point, and a
        // paving square is forty-nine small ones overlapping into one shape.
        public void Bloom(IntVec3 at, float radius)
        {
            if (!_active || radius <= 0f) return;

            var cells = Cells;
            var idx = map.cellIndices;
            int now = Find.TickManager.TicksGame;
            int n = GenRadial.NumCellsInRadius(Mathf.Min(radius, GenRadial.MaxRadialPatternRadius - 1f));

            for (int i = 0; i < n; i++)
            {
                var c = at + GenRadial.RadialPattern[i];
                if (!c.InBounds(map)) continue;

                int when = now + (int)(GenRadial.RadialPatternRadii[i] * CreepPerCell);
                int k = idx.CellToIndex(c);
                if (cells[k] <= when) continue;

                if (cells[k] == Never) _reached++;
                cells[k] = when;
            }
        }

        // Everything probabilistic reads this rather than Rand.Value, and it has to: the
        // plant sweep walks the whole map over and over, so a chance re-rolled every pass
        // converges on certainty. Seeded off the cell, ground that shrugged the plague
        // off keeps shrugging it off, through a reload as well.
        //
        // Hashed (lowbias32, top 24 bits) rather than Rand.ValueSeeded, which pushes the
        // global RNG state onto a stack, reseeds it, draws and pops.
        float Grit(IntVec3 cell)
        {
            uint h = (uint)Gen.HashCombineInt(cell.GetHashCode(), _seed);
            h ^= h >> 16;
            h *= 0x7feb352du;
            h ^= h >> 15;
            h *= 0x846ca68bu;
            h ^= h >> 16;
            return (h >> 8) * (1f / 16777216f);
        }

        // Full needs to beat the bite outright and Weak only its square root, so the
        // certain core is solid and past it the ground breaks up into full, weak and
        // untouched in shifting proportions - 100/0 where the core ends, 50/21 halfway
        // out, 25/25 at three quarters, and nowhere an edge you could trace.
        public Band BandAt(IntVec3 cell)
        {
            if (!_active || !_origin.IsValid) return Band.None;

            float bite = Bite(cell);
            if (bite <= 0f) return Band.None;
            if (bite >= 1f) return Band.Full; // the certain core: no dice at all

            float grit = Grit(cell);
            if (grit < bite) return Band.Full;
            if (grit < Mathf.Sqrt(bite)) return Band.Weak;
            return Band.None;
        }

        // Whether the plague has been here at all, dither or no: a fire that could not cross
        // a cell the dither happened to spare would never get anywhere, and a thing walking
        // in off the edge is only out of the way if the plague has not arrived yet.
        public bool Reaches(IntVec3 cell)
        {
            if (!_active) return false;
            int at = At(cell);
            return at != Never && at <= Find.TickManager.TicksGame;
        }

        // Asked of a thing rather than a cell, because the grace outlives the cat walking
        // away and so belongs to what was standing there.
        bool Spared(Thing t) => (_aura ?? (_aura = Aura.Of(map)))?.Spares(t) == true;

        // A map with no plague running on it - the menu's background, an unfinished intro
        // - is not one whose fires we have any business containing.
        public bool Active => _active;

        // Where the middle is, for Worksite: it is the one thing here that works *with* the
        // plague rather than being told whether it is caught, and it leans its errands inward.
        public IntVec3 Heart => _origin;

        // And how big the plague is, said as a radius: the circle that would hold as much
        // ground as it has actually taken. Worksite reads it to know how far out the colony
        // may work.
        //
        // A bulk rather than a furthest reach, and that is the whole of it. Measured as the
        // outermost cell anybody ever reached, one plate laid at the edge of the leash would
        // move the leash, and the next plate would move it again - ten errands to the map
        // edge, and a leash one errand can drag is not a leash. Bulk means the room to work
        // in is bought with ground that actually died, which goes as the square of the
        // radius, which is the pacing this used to want a constant for.
        public float Girth => Mathf.Sqrt(_reached / Mathf.PI);

        // Who has caught it. The plague does not chase anything, so this is the pass that
        // finds out who has walked into it.
        void Catch()
        {
            int twentieth = _reached * 20 / Mathf.Max(map.Area, 1);
            if (twentieth != _logged)
            {
                _logged = twentieth;
                Log.Message($"[SlopWorld] the plague has {_reached * 100f / map.Area:F0}% of the map, " +
                            $"{Girth:F0} cells of it as a circle, " +
                            $"on {Worksite.LaidOn(map)} cells of finished ground");
            }

            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!Infectable(pawn)) continue;
                if (BandAt(pawn.Position) == Band.None) continue;
                if (Marked(pawn)) continue;
                if (Spared(pawn)) continue; // the cat has it, for now
                pawn.health.AddHediff(SlopDefOf.SlopPlague);
                PlagueFx.Mark(pawn);
            }
        }

        // Walked in slices and rebuilt at the end of each pass, so a plant that grew
        // since last time still gets its turn.
        void StepPlants()
        {
            if (_plantIdx >= _plants.Count)
            {
                _plants.Clear();
                var all = map.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
                for (int i = 0; i < all.Count; i++)
                    if (all[i] is Plant p) _plants.Add(p);
                _plantIdx = 0;
                return; // keep the refill off the same tick as the work
            }

            int budget = PlantsPerTick;
            while (_plantIdx < _plants.Count && budget-- > 0)
            {
                var p = _plants[_plantIdx++];
                if (p == null || p.Destroyed || !p.Spawned) continue;
                if (p.def.plant == null) continue;

                var band = BandAt(p.Position);
                if (band == Band.None) continue;

                var dose = band == Band.Full ? Full : Weak;
                bool tree = p.def.plant.IsTree;

                // Every plant comes back round forever: without this a bare tree smokes again on
                // each pass, and a plant the weak band already held back rolls for ignition until
                // it catches. Which is why the weak band asks about StuntFrom rather than the
                // figure it stunts to.
                bool todo = dose.Strips
                    ? !(tree && p.LeaflessNow)
                    : !tree && p.Growth > dose.StuntFrom;
                if (!todo) continue;

                // After the todo check, not before: same answer, and in the steady state
                // nearly every plant the sweep walks past is one the band has finished with.
                if (Spared(p)) continue;

                // The roll goes first because TryStartFireIn weighs what is flammable in the
                // cell, and stripping the plant is what leaves nothing there to light.
                if (Rand.Value < dose.PlantIgnite &&
                    FireUtility.TryStartFireIn(p.Position, map, dose.FireSize, null))
                    continue;

                if (!dose.Strips)
                {
                    // Trees in the weak band are left alone entirely: a bare tree is the core's look,
                    // and giving the falloff one too is what made the two bands indistinguishable.
                    PlagueFx.Wither(p);
                    p.Growth = dose.StuntTo;
                    // Growth is printed into the map mesh and the setter does not dirty it, so a
                    // stunted plant keeps drawing at full size.
                    map.mapDrawer?.MapMeshDirty(p.Position, MapMeshFlagDefOf.Things);
                }
                else if (tree)
                {
                    PlagueFx.Wither(p);
                    p.MakeLeafless(Plant.LeaflessCause.Poison, false);
                }
                else
                {
                    PlagueFx.Wither(p); // before the destroy - a despawned plant has no DrawPos
                    p.Destroy(DestroyMode.Vanish);
                }
            }
        }

        // The dose comes from where it is standing now, so the same mark means less out
        // at the edge and nothing at all past it.
        void Effects()
        {
            _rolling.Clear();
            _rolling.AddRange(map.mapPawns.AllPawnsSpawned);

            foreach (var pawn in _rolling)
            {
                if (!Infectable(pawn) || !Marked(pawn)) continue;

                var band = BandAt(pawn.Position);
                if (band == Band.None) continue;
                // Belt and braces over Spread: a marked animal that walked into the aura is
                // unmarked at the aura's next sweep and not before, and a detonation in that half
                // second is the one thing the cat is for going visibly wrong.
                if (Spared(pawn)) continue;
                var dose = band == Band.Full ? Full : Weak;

                float roll = Rand.Value;
                if ((roll -= dose.PExplode) < 0f) Detonate(pawn, dose);
                else if ((roll -= dose.PIgnite) < 0f) Ignite(pawn, dose);
                else if ((roll -= dose.PBleed) < 0f) Bleed(pawn, dose);
                else if (roll - dose.PVomit < 0f) Vomit(pawn);
            }
        }

        void Detonate(Pawn pawn, Dose dose)
        {
            // The blast does not care who it is, and immunity has to mean immunity.
            if (AgentNear(pawn.Position, BlastSafeRadius)) { Bleed(pawn, dose); return; }

            PlagueFx.Burst(pawn);
            GenExplosion.DoExplosion(pawn.Position, map, BlastRadius, DamageDefOf.Bomb,
                null, damAmount: BlastDamage, ignoredThings: Untouchable());
        }

        // The core first of all: it stands in the middle of the band that detonates
        // hardest, so at this radius it would eventually blow a hole in its own origin.
        //
        // Then everything the agents have built, finished or half-built, on the same
        // grounds NoBurningTheColony spares the whole player faction: a monument is an
        // hour of somebody's tokens, and a blast that takes it is the board deleting work
        // rather than the plague being dangerous.
        List<Thing> Untouchable()
        {
            var spared = map.listerThings.ThingsOfDef(SlopDefOf.Ship_ComputerCore).ToList();
            spared.AddRange(Pets.On(map).Cast<Thing>());
            spared.AddRange(map.listerBuildings.allBuildingsColonist.Cast<Thing>());
            spared.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));
            return spared;
        }

        // FireUtility declines quietly for anything that cannot burn or is already
        // alight. The haze goes up first, so the tell is the plague's rather than an
        // animal that happens to be on fire.
        static void Ignite(Pawn pawn, Dose dose)
        {
            PlagueFx.Act(pawn);
            pawn.TryAttachFire(dose.FireSize, null);
        }

        // No bleed-out without health ticks, so the damage is the death.
        void Bleed(Pawn pawn, Dose dose)
        {
            var pos = pawn.Position;
            PlagueFx.Act(pawn); // before the damage, which may be the one that drops it
            pawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, Rand.Range(dose.BleedMin, dose.BleedMax)));

            int cells = GenRadial.NumCellsInRadius(BloodRadius);
            for (int i = 0; i < BloodPerBleed; i++)
            {
                var c = pos + GenRadial.RadialPattern[Rand.Range(0, cells)];
                if (c.InBounds(map))
                    FilthMaker.TryMakeFilth(c, map, ThingDefOf.Filth_Blood, pawn.LabelShort, 1);
            }
        }

        // Checks whether it can land before it smokes: a puff over a pawn with no job
        // tracker would advertise an effect that never came.
        static void Vomit(Pawn pawn)
        {
            if (pawn.jobs == null) return;
            if (pawn.CurJobDef == JobDefOf.Vomit) return;
            PlagueFx.Act(pawn);
            pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Vomit), JobCondition.InterruptForced);
        }

        static bool Marked(Pawn pawn) =>
            pawn.health?.hediffSet?.GetFirstHediffOfDef(SlopDefOf.SlopPlague) != null;

        static bool Infectable(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned) return false;
            if (pawn.health?.hediffSet == null) return false;
            if (pawn.RaceProps == null) return false;
            if (!pawn.RaceProps.Animal && !pawn.RaceProps.Humanlike) return false;

            // Agents are immune by design, and the pets fall out of the same check and are
            // meant to. Nobody else on this map belongs to the player.
            return pawn.Faction == null || !pawn.Faction.IsPlayer;
        }

        bool AgentNear(IntVec3 cell, float radius)
        {
            var colony = AgentColony.Current;
            if (colony == null) return false;

            foreach (var kv in colony.All)
            {
                var p = kv.Value;
                if (p == null || !p.Spawned || p.Map != map) continue;
                if (p.Position.DistanceTo(cell) <= radius) return true;
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _origin, "plagueOrigin", IntVec3.Invalid);
            Scribe_Values.Look(ref _active, "plagueActive", false);
            Scribe_Values.Look(ref _seed, "plagueSeed", 0);

            // Read above before it is asked here, Scribe_Values loading as it goes.
            if (!_active) return;

            // The field itself, packed the way the game packs its own per-cell grids. There
            // is nothing to derive it from any more: the arrival times *are* the plague, and
            // a save without them would be a save of a map that had not been touched.
            MapExposeUtility.ExposeUshort(map, Pack, Unpack, "plagueArrive");
        }

        // A signed offset from now, in real seconds, with zero kept back for ground the
        // plague has never reached. Half the size of the ticks themselves, and a second is
        // well under anything the dither shows: what has to survive the write is which cells
        // are old and which are the fringe, and that survives being rounded.
        //
        // Clamped rather than widened. A cell reached nine hours ago and one reached ten are
        // both ground that has been certain for longer than anyone was watching.
        const int Bias = 32768;
        const int Span = 32767;

        ushort Pack(IntVec3 c)
        {
            int at = Cells[map.cellIndices.CellToIndex(c)];
            if (at == Never) return 0;

            int secs = (int)((at - Find.TickManager.TicksGame) / RealClock.TicksPerRealSecond);
            return (ushort)(Mathf.Clamp(secs, -Span, Span) + Bias);
        }

        // The offsets go in raw and are turned back into ticks by FinalizeInit, because a map
        // is scribed *before* the tick manager is (Game.ExposeData) - so the clock read here
        // is the last game's, or nobody's, and never this save's. An offset cannot collide
        // with Never, being a short's worth either side of zero.
        //
        // Being called at all is also the only word we get that this save has a field in it:
        // DataSerializeUtility returns without a murmur when the node is absent, which is
        // what a save from before this looks like from in here.
        void Unpack(IntVec3 c, ushort v)
        {
            int k = map.cellIndices.CellToIndex(c);
            if (v == 0) { Cells[k] = Never; return; }

            Cells[k] = v - Bias;
            _relative = true;
            _reached++;
        }

        // Every wild plant on a map arrives through this one method, so refusing the cell
        // is the whole of it. The sweep alone loses the race: the spawner refills behind
        // it, so the core would spend the colony's life growing grass and having it torn
        // out again.
        //
        // Gated on Band.Full rather than Reaches: the weak band has to keep growing the
        // plants it is only holding back, and a cell the dither spared is untouched
        // ground. Grit being stable is what makes a cell sterile forever or fertile
        // forever rather than flickering.
        [HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.CheckSpawnWildPlantAt))]
        public static class Patch_NoRegrowth
        {
            static bool Prefix(IntVec3 c, Map ___map, ref bool __result)
            {
                var plague = ___map?.GetComponent<Plague>();
                if (plague == null || plague.BandAt(c) != Band.Full) return true;

                // Except where the cat is standing - the only thing that ever takes a cell back
                // off the core.
                if (Aura.Of(___map)?.Covers(c) == true) return true;

                __result = false;
                return false;
            }
        }

        // Without this the bands are a lie the moment anything ignites: a rainforest
        // carries a fire to the map edge in minutes. TrySpread picks its own cell
        // internally, so this can only allow or refuse the whole attempt.
        //
        // Containment is for a map with a future: NextPlanet is six seconds of this one
        // burning on the way out.
        [HarmonyPatch(typeof(Fire), "TrySpread")]
        public static class Patch_ContainFire
        {
            static bool Prefix(Fire __instance)
            {
                if (NextPlanet.Leaving) return true;

                var plague = __instance.Map?.GetComponent<Plague>();
                if (plague == null || !plague.Active) return true;
                // A fire under the cat is going out at the next sweep anyway; this stops it
                // taking the aura's plants with it.
                if (Aura.Of(__instance.Map)?.Covers(__instance.Position) == true) return false;
                return plague.Reaches(__instance.Position);
            }
        }
    }
}
