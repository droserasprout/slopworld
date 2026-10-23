using System.Collections.Generic;

namespace Verse
{
    // Model map registration separately from pod cargo, as RimWorld does.
    public partial class Map
    {
        public readonly ListerThings listerThings = new ListerThings();
    }

    public sealed class ListerThings
    {
        readonly List<Thing> _boxes = new List<Thing>();

        public List<Thing> ThingsOfDef(object def) => _boxes;
    }

    public sealed class Thing
    {
        public bool Destroyed;
        public Map Map;

        public void Destroy(DestroyMode mode)
        {
            Destroyed = true;
            Map?.listerThings.ThingsOfDef(null).Remove(this);
        }
    }

    public enum DestroyMode
    {
        Vanish,
    }

    public enum ThingPlaceMode
    {
        Near,
    }

    public static class ThingMaker
    {
        public static Thing MakeThing(object def) => new Thing();
    }

    public static class GenPlace
    {
        public static bool Fail;

        public static bool TryPlaceThing(Thing box, IntVec3 cell, Map map, ThingPlaceMode mode)
        {
            if (Fail)
                return false;
            box.Map = map;
            map.listerThings.ThingsOfDef(null).Add(box);
            return true;
        }
    }

    public static class Scribe_References
    {
        public static readonly Dictionary<string, object> Fields = new Dictionary<string, object>();

        public static void Look<T>(ref T value, string key)
            where T : class
        {
            if (Scribe.mode == LoadSaveMode.Saving)
                Fields[key] = value;
            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
                value = Fields.TryGetValue(key, out var saved) ? (T)saved : null;
        }
    }
}

namespace SlopWorld
{
    static class ModDefOf
    {
        public static readonly object SlopJukebox = new object();
    }
}
