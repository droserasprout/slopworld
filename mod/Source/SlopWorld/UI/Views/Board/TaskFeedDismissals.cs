using System.Collections.Generic;
using Verse;

namespace SlopWorld
{
    public sealed class TaskFeedDismissals : GameComponent
    {
        HashSet<string> _keys = new HashSet<string>();

        public TaskFeedDismissals(Game game) { }

        internal int Count => _keys.Count;

        internal bool Contains(string key) => key != null && _keys.Contains(key);

        internal void RemoveFromFeed(string key) => _keys.Add(key);

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref _keys, "dismissedTaskFeedEntries", LookMode.Value);
            _keys ??= new HashSet<string>();
        }
    }
}
