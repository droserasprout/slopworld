using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // Each cell stores its first arrival tick, making BandAt a lookup and preserving a young
    // fringe as the plague expands. Agents are immune; effects are applied here because
    // Patch_Health skips health ticks. Grandma mode keeps timing and growth but sows flowers.
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

        // One cell in this many is a flowerbed, and stays one - the roll is Grit's, so a cell
        // the ground refused stays refused across a reload the same way sterile ground does.
        const float SowChance = 0.05f;

        // Salt for that roll. Unsalted it would be the very number the bands are dithered
        // against, and every bed would land on ground the dither had already called certain.
        const int SowSalt = 0x51ed;

        // The sweep is over cells rather than plants, and the cells are the whole map: a pass
        // a tick is sixty thousand hashes. Walked in slices like StepPlants instead - these
        // two together are a pass over a default map every forty seconds or so of real time.
        const int SowInterval = 15;
        const int SowPerSweep = 400;

        // Beds arrive part-grown and at differing sizes, a patch that all came up at once
        // reading as something that was planted.
        const float SowGrowthMin = 0.30f;
        const float SowGrowthMax = 1.00f;

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

        // Where the flowerbed sweep left off. Not saved: it is a position in a walk, and any
        // cell it would have missed comes round again in under a minute.
        int _sowIdx;

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
            Log.Message($"[SlopWorld] plague seeded at {origin}, {CoreRadius:F0} cells" +
                        (Settings.GrandmaMode ? ", flowers only" : ""));
        }

        public override void MapComponentTick()
        {
            if (!_active) return;

            int t = Find.TickManager.TicksGame;

            // The only thing the window losing focus is allowed to stop is the vent's
            // flecks, which nobody is there to see. Everything else here mutates the
            // world - Effects detonates and ignites pawns, Sow spawns plants, StepPlants
            // withers them - and a plague that pauses while the player alt-tabs is a
            // different game depending on where they are looking.
            bool background = !Application.isFocused;

            if (Settings.GrandmaMode)
            {
                if (t % CatchInterval == 0) Progress();
                if (t % SowInterval == 0) Sow();
                return;
            }

            if (t % CatchInterval == 0) Catch();
            if (t % EffectInterval == 0) Effects();
            if (!background && t % VentInterval == 0) Vent();
            StepPlants();
        }

        // Work from a copy: ThingsInGroup returns the lister's live list, and destroying a
        // plant while walking it shifts the next entry and skips it. Rebuild after each
        // slice so newly grown plants are included without restarting the sweep every tick.
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
            int now = Find.TickManager.TicksGame;
            while (_plantIdx < _plants.Count && budget-- > 0)
            {
                var p = _plants[_plantIdx++];
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
        // over RipenTicks. Accepts the current TicksGame so callers can cache it once
        // per batch instead of reading Find.TickManager.TicksGame per cell.
        float Bite(IntVec3 cell, int tick)
        {
            int at = At(cell);
            if (at == Never) return 0f;

            int age = tick - at;
            if (age <= 0) return 0f; // stamped, still on its way
            if (age >= RipenTicks) return 1f;
            return age / (float)RipenTicks;
        }

        float Bite(IntVec3 cell) => Bite(cell, Find.TickManager.TicksGame);

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
        float Grit(IntVec3 cell) => Grit(cell, 0);

        // Salted, for the second question asked of the same cell. One hash answering both
        // would tie the flowerbeds to the dither exactly.
        float Grit(IntVec3 cell, int salt)
        {
            uint h = (uint)Gen.HashCombineInt(cell.GetHashCode(), _seed ^ salt);
            h ^= h >> 16;
            h *= 0x7feb352du;
            h ^= h >> 15;
            h *= 0x846ca68bu;
            h ^= h >> 16;
            return (h >> 8) * (1f / 16777216f);
        }

        // Full beats the bite outright, Weak only its square root: 100/0 where the core ends,
        // 50/21 halfway out, 25/25 at three quarters, and nowhere an edge you could trace.
        // Accepts cached tick to avoid repeated Find.TickManager lookups.
        public Band BandAt(IntVec3 cell, int tick)
        {
            if (!_active || !_origin.IsValid) return Band.None;

            float bite = Bite(cell, tick);
            if (bite <= 0f) return Band.None;
            if (bite >= 1f) return Band.Full; // the certain core: no dice at all

            float grit = Grit(cell);
            if (grit < bite) return Band.Full;
            if (grit < Mathf.Sqrt(bite)) return Band.Weak;
            return Band.None;
        }

        public Band BandAt(IntVec3 cell) => BandAt(cell, Find.TickManager.TicksGame);

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
        // Its own method because it is the one thing in Catch that is not an act on a pawn, and
        // grandma mode wants the reading without the rest of the pass.
        void Progress()
        {
            int twentieth = _reached * 20 / Mathf.Max(map.Area, 1);
            if (twentieth == _logged) return;

            _logged = twentieth;
            Log.Message($"[SlopWorld] the plague has {_reached * 100f / map.Area:F0}% of the map, " +
                        $"{Girth:F0} cells of it as a circle, " +
                        $"on {Worksite.LaidOn(map)} cells of finished ground");
        }

        void Catch()
        {
            Progress();
            int now = Find.TickManager.TicksGame;

            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!Infectable(pawn)) continue;
                if (BandAt(pawn.Position, now) == Band.None) continue;
                if (Marked(pawn)) continue;
                if (Spared(pawn)) continue; // the cat has it, for now
                pawn.health.AddHediff(SlopDefOf.SlopPlague);
                PlagueFx.Mark(pawn);
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
                if (_sowIdx >= cells.Length) _sowIdx = 0;
                int k = _sowIdx++;

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

        // The dose comes from where the pawn is standing now, so the same mark means less at
        // the edge and nothing past it.
        void Effects()
        {
            int now = Find.TickManager.TicksGame;
            _rolling.Clear();
            _rolling.AddRange(map.mapPawns.AllPawnsSpawned);

            foreach (var pawn in _rolling)
            {
                if (!Infectable(pawn) || !Marked(pawn)) continue;

                var band = BandAt(pawn.Position, now);
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
                // Nothing to hold back where the circle grows things: sterilising the ground
                // under the flowerbeds would leave grandma mode a barer map than the rot does.
                if (Settings.GrandmaMode) return true;

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
