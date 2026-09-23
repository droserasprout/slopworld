using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SlopWorld
{
    // Store the earliest arrival tick for each cell. BandAt uses arrival age to determine the local dose.
    // Apply effects directly because Patch_Health disables health ticks.
    // Gentle mode keeps field timing and growth. It adds flowers instead of damage.
    public partial class Plague : MapComponent
    {
        // Game ticks between checks for pawns within the field.
        const int CatchInterval = 60;

        // Initial plague radius around the core, independent of construction.
        const float CoreRadius = 12f;

        // Arrival delay in game ticks per cell of distance from a source.
        const int CreepPerCell = 240;

        // Game ticks from arrival until a cell always receives the full dose.
        const int RipenTicks = 1800;

        // Game ticks between effect selection attempts. A random roll can select no effect.
        const int EffectInterval = 300;

        const float BlastRadius = 5.5f;
        const int BlastDamage = 200;
        const float BlastSafeRadius = 9f; // no closer to an agent than this, > BlastRadius

        const int BloodPerBleed = 6;
        const float BloodRadius = 1.8f;

        // Limit plant processing per tick because a map can contain thousands of plants.
        const int PlantsPerTick = 5;

        // Game ticks between small core vent effects.
        const int VentInterval = 20;

        // Fraction of cells eligible for flowers. Use a stable cell hash so eligibility survives a reload.
        const float SowChance = 0.05f;

        // Use a separate hash salt so flower eligibility does not match plague band selection.
        const int SowSalt = 0x51ed;

        // Process a limited number of cells per sowing pass to distribute work across ticks.
        const int SowInterval = 15;
        const int SowPerSweep = 400;

        // Vary initial flower growth to give the plants different sizes.
        const float SowGrowthMin = 0.30f;
        const float SowGrowthMax = 1.00f;

        // Select the band from the current cell, independent of the pawn plague condition.
        public enum Band { None, Weak, Full }

        IntVec3 _origin = IntVec3.Invalid;
        bool _active;

        // Vary the cell pattern between colonies.
        int _seed;

        // Store each cell earliest arrival tick. Bloom can only reduce an existing arrival time.
        // Never marks cells without an arrival. Allocate the array after the map is available.
        const int Never = int.MaxValue;
        int[] _cells;

        // Count cells with recorded arrival times, including future arrivals. Use this count for Girth.
        int _reached;

        // True after loading and before FinalizeInit converts relative seconds to game ticks. See Pack.
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

        // The opening scene calls this after placing the core. Loading restores the saved state.
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

            // Suspend visual vent flecks when the application has no focus.
            // Continue effects and plant changes because they modify world state.
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

        // Convert saved time offsets after the tick manager loads.
        // Map loading occurs before tick manager loading, so Unpack cannot use the final clock value.
        // If an active save has no recorded cells, initialize the core area.
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

        // Check map bounds before converting the cell to an array index.
        int At(IntVec3 cell) =>
            cell.InBounds(map) ? Cells[map.cellIndices.CellToIndex(cell)] : Never;

        // Return zero before arrival, then increase the dose to one over RipenTicks.
        // Accept a cached game tick to avoid repeated clock lookups in batch operations.
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

        // Record a new arrival only when it precedes the existing arrival.
        // Worksite calls this for each footprint cell so large buildings create sources across their full area.
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

        // Clear arrival records in the specified area.
        // The field remains absent until a later Bloom records new arrivals, even after temporary aura protection ends.
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

        // Use a stable cell hash without changing the global random number generator.
        float Grit(IntVec3 cell) => Grit(cell, 0);

        // Use a salt to separate flower eligibility from plague band selection.
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

        // Compare the stable cell hash with the dose and its square root to select full, weak, or no effects.
        // Accept a cached game tick to avoid repeated clock lookups.
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

        // Check whether the arrival time has passed, independent of band selection.
        // Fire spread uses this boundary.
        public bool Reaches(IntVec3 cell)
        {
            if (!_active) return false;
            int at = At(cell);
            return at != Never && at <= Find.TickManager.TicksGame;
        }

        // Check companion immunity and aura protection.
        // Individual protection can continue after the thing leaves the pulse area.
        bool Spared(Thing t) => Companion.IsImmune(t as Pawn)
            || (_runtime.Aura ?? (_runtime.Aura = Aura.Of(map)))?.Spares(t) == true;

        public bool Active => _active;

        public IntVec3 Heart => _origin;

        // Return the radius of a circle with the same area as all recorded cells.
        // Use area rather than the furthest cell to prevent isolated sources from rapidly expanding the worksite radius.
        public float Girth => Mathf.Sqrt(_reached / Mathf.PI);

        // Log field coverage in five percent increments.
        // Keep this separate from pawn effects so Gentle mode can report progress.
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

            // Scribe_Values loads _active before this check.
            if (!_active) return;

            MapExposeUtility.ExposeUshort(map, Pack, Unpack, "plagueArrive");
        }

        // Encode signed offsets in real seconds as unsigned 16-bit values. Reserve zero for cells without arrivals.
        // Clamp offsets to the supported range. Older cells already receive the full dose.
        const int Bias = 32768;
        const int Span = 32767;

        ushort Pack(IntVec3 c)
        {
            int at = Cells[map.cellIndices.CellToIndex(c)];
            if (at == Never) return 0;

            int secs = (int)((at - Find.TickManager.TicksGame) / RealClock.TicksPerRealSecond);
            return (ushort)(Mathf.Clamp(secs, -Span, Span) + Bias);
        }

        // Decode relative seconds during map loading. FinalizeInit converts them to game ticks.
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
