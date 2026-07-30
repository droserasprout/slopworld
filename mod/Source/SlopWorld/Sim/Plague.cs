using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // The union of a source per finished thing: the core emits a fixed circle, everything
    // else is a monument or plate the agents raised (Bloom, off Worksite.Patch_ErrandDone).
    //
    // What is kept is one arrival tick a cell (Cells); everything else is read off it and
    // the clock, a cell's *age* being its dose. That is the falloff stated in time rather
    // than radius, and it is a lookup where asking every source would be a loop - a source
    // per paved cell is thousands of sources and BandAt is asked ten thousand times a
    // second. Time also makes one figure right at both scales: a two-cell stamp is all
    // fringe for half a minute, hour-old paving is old in the middle and young at the hull.
    //
    // Bands are dithered against Grit rather than thresholded: a hard threshold draws a
    // traceable line, and a chance re-rolled each sweep still ends in one flat dead disc.
    //
    // Agents are immune wholly - a dead colonist would leave its session pointing at a
    // corpse. Effects are applied directly because Patch_Health skips health ticks, so
    // nothing bleeds out on its own. Fire is the exception, being a Thing that ticks itself.
    public class Plague : MapComponent
    {
        // The pass over what is standing on the map. The plague itself moves when something
        // is finished, not on a clock.
        const int CatchInterval = 60;

        // The core's reach, and the only plague here nobody had to build.
        const float CoreRadius = 12f;

        // Ticks per cell of remove - how fast the plague walks out of a source.
        const int CreepPerCell = 240;

        // Newly reached to certainly dead. Over the creep this is the fringe: thirty real
        // seconds at four a cell is seven and a half cells of falloff.
        const int RipenTicks = 1800;

        // What is left over after the odds below is a quiet tick.
        const int EffectInterval = 300;

        const float BlastRadius = 5.5f;
        const int BlastDamage = 200;
        const float BlastSafeRadius = 9f; // no closer to an agent than this, > BlastRadius

        const int BloodPerBleed = 6;
        const float BloodRadius = 1.8f;

        // Withering is cheap, but a grown map has thousands of plants.
        const int PlantsPerTick = 5;

        // Three breaths a second of the smallest puff there is; anything heavier is a fog
        // bank parked on the middle of the map.
        const int VentInterval = 20;

        // Read from the cell, not the mark, so a marked animal that wanders out of reach
        // goes quiet and starts up again when it wanders back.
        public enum Band { None, Weak, Full }

        // Probabilities are per roll and share one pass, so they add rather than compose.
        struct Dose
        {
            public float PExplode, PIgnite, PBleed, PVomit;
            public float BleedMin, BleedMax;
            public float FireSize;
            public float PlantIgnite; // per plant, once, on the sweep that strips it

            // The difference the eye reads: stripping both bands alike left nothing to tell
            // them apart but pawn effects nobody watches.
            public bool Strips;

            // What the weak band knocks a plant's growth back to.
            public float StuntTo;

            // The gap is load-bearing. Nothing stops a plant ticking here (the strip takes
            // needs, health, age and the storyteller, not Plant.TickLong), so a test against
            // StuntTo comes true again within a pass: every weak plant puffed, re-stunted by
            // a fraction of a percent and re-rolled for ignition forever.
            public float StuntFrom;
        }

        static readonly Dose Full = new Dose
        {
            PExplode = 0.015f,
            PIgnite = 0.020f,
            PBleed = 0.300f,
            PVomit = 0.250f,
            BleedMin = 8f,
            BleedMax = 18f,
            FireSize = 1.0f,
            // ~15k plants inside the circle on a default map, each rolled once: a handful of
            // ignitions over the whole first sweep.
            PlantIgnite = 0.00025f,
            Strips = true,
        };

        // Nothing detonates out here - there is no half of a blast - and plants are held
        // back rather than taken, so the band reads as thin instead of as more dead ground.
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

        IntVec3 _origin = IntVec3.Invalid;
        bool _active;

        // Without it every colony thins out through the same speckle.
        int _seed;

        // The plague's whole extent: the tick it arrived at each cell, or Never. Min-combined
        // on the way in and never raised - nothing here takes a finished thing back off the
        // board - so the field only fills in, which is what lets a question about it be an
        // array index. Never rather than zero, zero being a tick the game has. Allocated on
        // first use, a MapComponent being built while the map still is.
        const int Never = int.MaxValue;
        int[] _cells;

        // Cells with an arrival written, maintained where the writing happens. Girth is read
        // off it.
        int _reached;

        // True between the load and FinalizeInit, while _cells holds offsets in seconds
        // rather than ticks. See Pack.
        bool _relative;

        // Last twentieth of the map logged, so there is no line every three seconds.
        int _logged = -1;

        // Refilled when it runs off the end, which is also how regrowth gets caught.
        readonly List<Plant> _plants = new List<Plant>();
        int _plantIdx;

        // Effects walks a copy: an effect can despawn the pawn it lands on.
        readonly List<Pawn> _rolling = new List<Pawn>();

        Aura _aura;
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

        // Called by the intro once the core is standing; a reload picks up the saved state.
        public void Arm(IntVec3 origin)
        {
            if (_active) return;
            _origin = origin;
            _seed = Rand.Int;
            _active = true;
            Bloom(origin, CoreRadius);
            Log.Message($"[SlopWorld] plague seeded at {origin}, {CoreRadius:F0} cells");
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

        // Where the load's offsets become arrival ticks. Here rather than in Unpack: a map is
        // scribed *before* the tick manager, so the clock can only be asked once the whole
        // game is standing. A save from before the field is not migrated - a radius says
        // nothing about which cell died when - and gets the core's own circle instead.
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

        // Out of bounds would read somebody else's row.
        int At(IntVec3 cell) =>
            cell.InBounds(map) ? Cells[map.cellIndices.CellToIndex(cell)] : Never;

        // The dose a cell carries: nothing until the plague gets there, then up to certain
        // over RipenTicks.
        float Bite(IntVec3 cell)
        {
            int at = At(cell);
            if (at == Never) return 0f;

            int age = Find.TickManager.TicksGame - at;
            if (age <= 0) return 0f; // stamped, still on its way
            if (age >= RipenTicks) return 1f;
            return age / (float)RipenTicks;
        }

        // The whole of how the plague grows: arrival times written only where they beat what
        // is there, so ground reached ten minutes ago keeps its date and a plate laid beside
        // it never restarts the creep. Called per cell of the finished thing's footprint, so
        // a five-by-three machine blooms that wide rather than as a point.
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

        // Read instead of Rand.Value wherever the answer must not move: the plant sweep walks
        // the map over and over, and a chance re-rolled every pass converges on certainty.
        // Seeded off the cell, so ground that shrugged the plague off keeps shrugging it off
        // across a reload. Hashed (lowbias32, top 24 bits) rather than Rand.ValueSeeded,
        // which pushes the global RNG state onto a stack, reseeds, draws and pops.
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

        // Full beats the bite outright, Weak only its square root: 100/0 where the core ends,
        // 50/21 halfway out, 25/25 at three quarters, and nowhere an edge you could trace.
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
        // a cell the dither spared would never get anywhere.
        public bool Reaches(IntVec3 cell)
        {
            if (!_active) return false;
            int at = At(cell);
            return at != Never && at <= Find.TickManager.TicksGame;
        }

        // Asked of a thing rather than a cell: the grace outlives the cat walking away, so it
        // belongs to what was standing there.
        bool Spared(Thing t) => (_aura ?? (_aura = Aura.Of(map)))?.Spares(t) == true;

        public bool Active => _active;

        public IntVec3 Heart => _origin;

        // The plague as a radius: the circle that would hold as much ground as it has taken.
        // A bulk rather than a furthest reach - measured off the outermost cell, one plate at
        // the edge of Worksite's leash would drag the leash, and the next would drag it again.
        public float Girth => Mathf.Sqrt(_reached / Mathf.PI);

        // The plague chases nothing, so this is the pass that finds who walked into it.
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

        // Walked in slices and rebuilt at the end of each pass, so a plant that grew since
        // last time still gets its turn.
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
                    PlagueFx.Wither(p); // before the destroy - a despawned plant has no DrawPos
                    p.Destroy(DestroyMode.Vanish);
                }
            }
        }

        // The dose comes from where the pawn is standing now, so the same mark means less at
        // the edge and nothing past it.
        void Effects()
        {
            _rolling.Clear();
            _rolling.AddRange(map.mapPawns.AllPawnsSpawned);

            foreach (var pawn in _rolling)
            {
                if (!Infectable(pawn) || !Marked(pawn)) continue;

                var band = BandAt(pawn.Position);
                if (band == Band.None) continue;
                // A marked animal that walked into the aura is unmarked at the aura's next
                // sweep and not before; a detonation in that half second is the cat failing.
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

        // The core stands in the middle of the band that detonates hardest, so at this radius
        // it would eventually blow a hole in its own origin. Then everything the agents built,
        // on the same grounds NoBurningTheColony spares the player faction: a monument is an
        // hour of somebody's tokens.
        List<Thing> Untouchable()
        {
            var spared = map.listerThings.ThingsOfDef(SlopDefOf.Ship_ComputerCore).ToList();
            spared.AddRange(Pets.On(map).Cast<Thing>());
            spared.AddRange(map.listerBuildings.allBuildingsColonist.Cast<Thing>());
            spared.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame));
            return spared;
        }

        // The haze goes up first, so the tell is the plague's rather than an animal that
        // happens to be on fire.
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

        // Checks it can land before it smokes: a puff over a pawn with no job tracker
        // advertises an effect that never comes.
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

            // Agents are immune by design and the pets fall out of the same check, meaning to.
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

            MapExposeUtility.ExposeUshort(map, Pack, Unpack, "plagueArrive");
        }

        // A signed offset from now in real seconds, zero kept back for never-reached ground.
        // Half the size of the ticks, and what has to survive the write is which cells are old
        // and which are the fringe. Clamped rather than widened: a cell reached nine hours ago
        // and one reached ten have both been certain for longer than anyone was watching.
        const int Bias = 32768;
        const int Span = 32767;

        ushort Pack(IntVec3 c)
        {
            int at = Cells[map.cellIndices.CellToIndex(c)];
            if (at == Never) return 0;

            int secs = (int)((at - Find.TickManager.TicksGame) / RealClock.TicksPerRealSecond);
            return (ushort)(Mathf.Clamp(secs, -Span, Span) + Bias);
        }

        // Offsets go in raw and become ticks in FinalizeInit: a map is scribed *before* the
        // tick manager (Game.ExposeData), so the clock read here is the last game's. An offset
        // cannot collide with Never, being a short's worth either side of zero. Being called
        // at all is also the only word we get that this save has a field -
        // DataSerializeUtility returns silently when the node is absent.
        void Unpack(IntVec3 c, ushort v)
        {
            int k = map.cellIndices.CellToIndex(c);
            if (v == 0) { Cells[k] = Never; return; }

            Cells[k] = v - Bias;
            _relative = true;
            _reached++;
        }

        // Every wild plant arrives through this one method. The sweep alone loses the race -
        // the spawner refills behind it - so the core would spend the colony's life growing
        // grass and tearing it out. Gated on Band.Full rather than Reaches: the weak band has
        // to keep growing the plants it only holds back, and Grit being stable is what makes
        // a cell sterile or fertile forever rather than flickering.
        [HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.CheckSpawnWildPlantAt))]
        public static class Patch_NoRegrowth
        {
            static bool Prefix(IntVec3 c, Map ___map, ref bool __result)
            {
                var plague = ___map?.GetComponent<Plague>();
                if (plague == null || plague.BandAt(c) != Band.Full) return true;

                // Except where the cat is standing.
                if (Aura.Of(___map)?.Covers(c) == true) return true;

                __result = false;
                return false;
            }
        }

        // Without this the bands are a lie the moment anything ignites: a rainforest carries
        // fire to the map edge in minutes. TrySpread picks its own cell internally, so this
        // can only allow or refuse the whole attempt. Off while NextPlanet is burning the map.
        [HarmonyPatch(typeof(Fire), "TrySpread")]
        public static class Patch_ContainFire
        {
            static bool Prefix(Fire __instance)
            {
                if (NextPlanet.Leaving) return true;

                var plague = __instance.Map?.GetComponent<Plague>();
                if (plague == null || !plague.Active) return true;
                // A fire under the cat goes out at the next sweep anyway; this stops it taking
                // the aura's plants with it.
                if (Aura.Of(__instance.Map)?.Covers(__instance.Position) == true) return false;
                return plague.Reaches(__instance.Position);
            }
        }
    }
}
