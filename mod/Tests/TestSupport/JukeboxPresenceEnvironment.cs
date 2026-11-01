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
        readonly Dictionary<object, List<Thing>> _things = new Dictionary<object, List<Thing>>();

        public List<Thing> ThingsOfDef(object def)
        {
            if (!_things.TryGetValue(def, out var things))
                _things[def] = things = new List<Thing>();
            return things;
        }
    }

    public sealed class Thing
    {
        public bool Destroyed;
        public Map Map;
        public object Def;

        public void Destroy(DestroyMode mode)
        {
            Destroyed = true;
            Map?.listerThings.ThingsOfDef(Def).Remove(this);
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
        public static Thing MakeThing(object def) => new Thing { Def = def };
    }

    public static class GenPlace
    {
        public static bool Fail;

        public static bool TryPlaceThing(Thing box, IntVec3 cell, Map map, ThingPlaceMode mode)
        {
            if (Fail)
                return false;
            box.Map = map;
            map.listerThings.ThingsOfDef(box.Def).Add(box);
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
        public static readonly object SlopPlannerBench = new object();
        public static readonly object SlopSettingsHeater = new object();
    }
}
