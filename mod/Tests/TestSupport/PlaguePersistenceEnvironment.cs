using System;
using System.Collections.Generic;

// In-memory Scribe and cell indexing exercise the production persistence partial.
// Pawn/plant effects and RimWorld's XML codec are outside this fixture.
namespace Verse
{
    public struct IntVec3
    {
        public int Index;
        public static IntVec3 Invalid => new IntVec3 { Index = -1 };
        public bool IsValid => Index >= 0;
    }

    public sealed class CellIndices
    {
        public int CellToIndex(IntVec3 cell) => cell.Index;
    }

    public partial class Map
    {
        public int Area;
        public CellIndices cellIndices = new CellIndices();
    }

    public class MapComponent
    {
        protected readonly Map map;

        public MapComponent(Map value)
        {
            map = value;
        }

        public virtual void FinalizeInit() { }

        public virtual void ExposeData() { }
    }

    public class TickManager
    {
        public int TicksGame;
    }

    static partial class Find
    {
        public static TickManager TickManager = new TickManager();
    }

    public static partial class Scribe_Values
    {
        public static readonly Dictionary<string, object> Fields = new Dictionary<string, object>();

        public static void Look<T>(ref T value, string key, T defaultValue)
        {
            if (Scribe.mode == LoadSaveMode.Saving)
                Fields[key] = value;
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                value = Fields.TryGetValue(key, out var saved) ? (T)saved : defaultValue;
        }
    }

    public static class MapExposeUtility
    {
        public static ushort[] Saved;

        public static void ExposeUshort(
            Map map,
            Func<IntVec3, ushort> pack,
            Action<IntVec3, ushort> unpack,
            string key
        )
        {
            if (Scribe.mode == LoadSaveMode.Saving)
                Saved = new ushort[map.Area];
            for (int i = 0; i < map.Area; i++)
            {
                var cell = new IntVec3 { Index = i };
                if (Scribe.mode == LoadSaveMode.Saving)
                    Saved[i] = pack(cell);
                if (Scribe.mode == LoadSaveMode.LoadingVars)
                    unpack(cell, Saved[i]);
            }
        }
    }
}

namespace SlopWorld
{
    public partial class Plague : Verse.MapComponent
    {
        public Plague(Verse.Map map)
            : base(map) { }

        public void SetField(params int[] arrivals)
        {
            _active = true;
            _origin = new Verse.IntVec3 { Index = 0 };
            _cells = (int[])arrivals.Clone();
            _reached = Array.FindAll(arrivals, at => at != Never).Length;
        }

        public int[] Arrivals => Cells;
        public int Reached => _reached;
    }
}
