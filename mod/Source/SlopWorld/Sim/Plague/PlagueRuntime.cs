using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // Store caches and sweep cursors for one map. Do not save this state.
    // Rebuild plant lists and find the core again after loading.
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
