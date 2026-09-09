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
    public partial class Plague : MapComponent
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

        IntVec3 _origin = IntVec3.Invalid;
        bool _active;

        // Without it every colony thins out through the same speckle.
        int _seed;

        // `_cells` stores each cell's first arrival tick; Min-combined values only grow, with
        // `Never` as unset. Allocate lazily because MapComponents load before the map is ready.
        const int Never = int.MaxValue;
        int[] _cells;

        // Cells with an arrival written, maintained where the writing happens. Girth is read
        // off it.
        int _reached;

        // True between the load and FinalizeInit, while _cells holds offsets in seconds
        // rather than ticks. See Pack.
        bool _relative;

        readonly PlagueRuntime _runtime = new PlagueRuntime();

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

            // Only visual vent flecks pause when unfocused; Effects, Sow, and StepPlants mutate
            // world state and must keep running.
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
            if (_runtime.Core == null || _runtime.Core.Destroyed || !_runtime.Core.Spawned)
            {
                var found = map.listerThings.ThingsOfDef(ModDefOf.Ship_ComputerCore);
                _runtime.Core = found != null && found.Count > 0 ? found[0] : null;
                if (_runtime.Core == null) return;
            }

            PlagueFx.Vent(_runtime.Core);
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

        // A rejuvenation clears the arrival record itself, not only the current symptoms. The
        // plague therefore does not return to these cells after the aura's temporary grace ends.
        public void Rejuvenate(IntVec3 centre, float radius)
        {
            if (!_active || radius <= 0f) return;

            var cells = Cells;
            var idx = map.cellIndices;
            int count = GenRadial.NumCellsInRadius(
                Mathf.Min(radius, GenRadial.MaxRadialPatternRadius - 1f));

            for (int i = 0; i < count; i++)
            {
                var c = centre + GenRadial.RadialPattern[i];
                if (!c.InBounds(map)) continue;

                int k = idx.CellToIndex(c);
                if (cells[k] == Never) continue;

                cells[k] = Never;
                if (_reached > 0) _reached--;
            }

            _runtime.Logged = -1;
        }

        // Use deterministic cell-seeded rolls for stable sweep/reload results without mutating
        // the global RNG state.
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
        bool Spared(Thing t) => CapybaraEgg.IsImmune(t as Pawn)
            || (_runtime.Aura ?? (_runtime.Aura = Aura.Of(map)))?.Spares(t) == true;

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
            if (twentieth == _runtime.Logged) return;

            _runtime.Logged = twentieth;
            Log.Message($"[SlopWorld] the plague has {_reached * 100f / map.Area:F0}% of the map, " +
                        $"{Girth:F0} cells of it as a circle, " +
                        $"on {Worksite.LaidOn(map)} cells of finished ground");
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

        // Save relative offsets before the tick manager exists; FinalizeInit converts them to
        // current ticks. Detect absent save nodes because DataSerializeUtility is silent there.
        void Unpack(IntVec3 c, ushort v)
        {
            int k = map.cellIndices.CellToIndex(c);
            if (v == 0) { Cells[k] = Never; return; }

            Cells[k] = v - Bias;
            _relative = true;
            _reached++;
        }

    }
}
