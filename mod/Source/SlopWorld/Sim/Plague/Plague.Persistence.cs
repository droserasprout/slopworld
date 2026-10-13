using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Field storage and save/load clocks. Loading never seeds or rejuvenates cells.
    public partial class Plague
    {
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

        int[] Cells
        {
            get
            {
                if (_cells == null)
                {
                    _cells = new int[map.Area];
                    for (int i = 0; i < _cells.Length; i++)
                        _cells[i] = Never;
                }
                return _cells;
            }
        }

        // Convert saved time offsets after the tick manager loads.
        // Map loading occurs before tick manager loading, so Unpack cannot use the final clock value.
        public override void FinalizeInit()
        {
            base.FinalizeInit();
            if (!_active)
                return;

            if (_relative)
            {
                _relative = false;
                int now = Find.TickManager.TicksGame;
                var cells = Cells;

                for (int i = 0; i < cells.Length; i++)
                    if (cells[i] != Never)
                        cells[i] = now + (int)(cells[i] * RealClock.TicksPerRealSecond);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref _origin, "plagueOrigin", IntVec3.Invalid);
            Scribe_Values.Look(ref _active, "plagueActive", false);
            Scribe_Values.Look(ref _seed, "plagueSeed", 0);

            // Scribe_Values loads _active before this check.
            if (!_active)
                return;

            MapExposeUtility.ExposeUshort(map, Pack, Unpack, "plagueArrive");
        }

        // Encode signed offsets in real seconds as unsigned 16-bit values. Reserve zero for cells without arrivals.
        // Clamp offsets to the supported range. Older cells already receive the full dose.
        const int Bias = 32768;
        const int Span = 32767;

        ushort Pack(IntVec3 c)
        {
            int at = Cells[map.cellIndices.CellToIndex(c)];
            if (at == Never)
                return 0;

            int secs = (int)((at - Find.TickManager.TicksGame) / RealClock.TicksPerRealSecond);
            return (ushort)(Mathf.Clamp(secs, -Span, Span) + Bias);
        }

        // Decode relative seconds during map loading. FinalizeInit converts them to game ticks.
        void Unpack(IntVec3 c, ushort v)
        {
            int k = map.cellIndices.CellToIndex(c);
            if (v == 0)
            {
                Cells[k] = Never;
                return;
            }

            Cells[k] = v - Bias;
            _relative = true;
            _reached++;
        }
    }
}
