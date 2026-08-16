using System.Collections.Generic;
using Verse;

namespace SlopWorld
{
    // Worksite's map-local caches and placement cursors. The only durable worksite value is
    // the number of finished cells; everything here can be reacquired after a load.
    sealed class WorksiteRuntime
    {
        public ThingDef Blocks;
        public ThingDef Rock;
        public bool Quarried;
        public Plague Plague;
        public int Blocked;
        public int Swept;
        public readonly HashSet<Thing> Mine = new HashSet<Thing>();
    }
}
