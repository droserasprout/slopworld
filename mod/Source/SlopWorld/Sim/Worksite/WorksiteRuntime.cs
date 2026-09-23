using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Store worksite caches and placement cursors for one map.
    // Worksite saves only the number of completed cells. Rebuild this state after a load.
    sealed class WorksiteRuntime
    {
        public ThingDef Blocks;
        public ThingDef Rock;
        public bool Quarried;
        public Plague Plague;
        public int Blocked;
        public int Swept;
        public readonly HashSet<Thing> Mine = new HashSet<Thing>();
        public readonly List<Frame> Frames = new List<Frame>();
        public readonly List<Thing> SweepDoomed = new List<Thing>();
        public bool FrameIndexDirty = true;
        public int FrameSourceCount = -1;
        public bool SweepPending;
        public int SweepCursor;
        public int SweepLimit;
    }
}
