using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // The daemon resolves polling and ranking in replacement usage snapshots.
    internal sealed class UsageRowsCache
    {
        public readonly List<string> Rows = new List<string>();
        UsageInfo _usage;
        bool _ready;

        public bool Prepare(UsageInfo usage)
        {
            if (_ready && ReferenceEquals(_usage, usage)) return false;
            _ready = true;
            _usage = usage;
            Rows.Clear();
            if (usage != null)
                foreach (var row in usage.Rows)
                    if (row.Poll) Add(row.Key);
            Rows.Sort(Compare);
            return true;
        }

        void Add(string key)
        {
            if (!Rows.Contains(key)) Rows.Add(key);
        }

        int Compare(string a, string b) => Rank(a).CompareTo(Rank(b));

        int Rank(string key)
        {
            if (_usage != null)
                foreach (var row in _usage.Rows)
                    if (row.Key == key) return row.Rank;
            return int.MaxValue;
        }
    }
}
