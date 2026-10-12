using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Owns palette filtering, markup and stable result ordering over a caller's snapshot.
    // Indices identify original rows; command execution and game state stay with the window.
    static class PaletteSearch
    {
        const int IdCost = 80;

        internal readonly struct Entry
        {
            public readonly int Index;
            public readonly string Label, Id, Group;
            public readonly bool Enabled;

            public Entry(int index, string label, bool enabled, string id = null, string group = null)
            {
                Index = index;
                Label = label;
                Enabled = enabled;
                Id = id;
                Group = group;
            }
        }

        internal readonly struct Hit
        {
            public readonly int Index;
            public readonly string Label;
            public readonly int Score;
            public Hit(int index, string label, int score = 0)
            {
                Index = index;
                Label = label;
                Score = score;
            }
        }

        internal sealed class Result
        {
            public readonly List<Hit> Hits = new List<Hit>();
            public int RecentCount;
        }

        public static Result Commands(IReadOnlyList<Entry> entries, string filter, IReadOnlyList<string> recent)
        {
            var result = new Result();
            if (string.IsNullOrEmpty(filter))
            {
                var listed = new HashSet<int>();
                foreach (var id in recent)
                {
                    foreach (var entry in entries)
                    {
                        if (entry.Id != id || !entry.Enabled) continue;
                        if (listed.Add(entry.Index))
                        {
                            result.Hits.Add(new Hit(entry.Index, entry.Label));
                            result.RecentCount++;
                        }
                        break;
                    }
                }
                // Keep first-seen category order and catalog order within each category.
                foreach (var group in entries.Where(e => e.Enabled).GroupBy(e => e.Group))
                    foreach (var entry in group)
                        if (listed.Add(entry.Index)) result.Hits.Add(new Hit(entry.Index, entry.Label));
                return result;
            }

            var scored = new List<(Hit Hit, string Id)>();
            foreach (var entry in entries)
            {
                if (!entry.Enabled) continue;
                if (Fuzzy.Match(entry.Label, filter, out int score, out var hits))
                    scored.Add((new Hit(entry.Index, Fuzzy.Highlight(entry.Label, hits), score), entry.Id));
                else if (Fuzzy.Match(entry.Id, filter, out score))
                    // Hidden IDs get a penalty and never highlight unrelated label characters.
                    scored.Add((new Hit(entry.Index, entry.Label, score - IdCost), entry.Id));
            }
            // Recency only breaks score ties; LINQ's stable ordering retains catalog ties.
            result.Hits.AddRange(scored.OrderByDescending(hit => hit.Hit.Score)
                .ThenByDescending(hit => RecentRank(hit.Id, recent)).Select(hit => hit.Hit));
            return result;
        }

        static int RecentRank(string id, IReadOnlyList<string> recent)
        {
            for (int i = 0; i < recent.Count; i++)
                if (recent[i] == id) return recent.Count - i;
            return 0;
        }

        public static List<Hit> Options(IReadOnlyList<Entry> entries, string filter)
        {
            var result = new List<Hit>();
            if (string.IsNullOrEmpty(filter))
            {
                // Disabled placeholder rows remain visible until the user searches.
                foreach (var entry in entries) result.Add(new Hit(entry.Index, entry.Label));
                return result;
            }
            foreach (var entry in entries)
                if (entry.Enabled && Fuzzy.Match(entry.Label, filter, out int score, out var hits))
                    result.Add(new Hit(entry.Index, Fuzzy.Highlight(entry.Label, hits), score));
            return result.OrderByDescending(hit => hit.Score).ToList();
        }
    }
}
