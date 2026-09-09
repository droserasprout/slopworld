using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Per-map caches and sweep cursors. None of this is part of the plague's save contract:
    // rebuilding a plant list or reacquiring the core is cheaper and safer than serializing
    // references into a changing map.
    sealed class PlagueRuntime
    {
        public readonly List<Plant> Plants = new List<Plant>();
        public readonly List<Pawn> Rolling = new List<Pawn>();
        public int PlantIndex;
        public int SowIndex;
        public int Logged = -1;
        public Aura Aura;
        public Thing Core;
    }
}
