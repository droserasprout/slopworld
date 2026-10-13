using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Owns line-fit ranking and recent history; the stream owns geometry and mode filtering.
    internal sealed class LoadingTipSelection
    {
        const int RecentLimit = 12;
        readonly Queue<string> _recent = new Queue<string>();

        internal void Clear() => _recent.Clear();

        internal string Pick(List<string> pool, int columns, int occupied, Random random)
        {
            if (pool.Count == 0)
                return "Loading.";

            // A full row starts the quote on the next line. Otherwise its separator costs a cell.
            int capacity = occupied > 0 && occupied < columns ? columns - occupied - 1 : columns;
            var best = new List<string>();
            do
            {
                int bestGap = int.MaxValue;
                foreach (string tip in pool)
                {
                    if (_recent.Contains(tip))
                        continue;
                    int gap = PrefixGap(tip, capacity);
                    if (gap > bestGap)
                        continue;
                    if (gap < bestGap)
                    {
                        best.Clear();
                        bestGap = gap;
                    }
                    best.Add(tip);
                }
                if (best.Count == 0)
                    _recent.Dequeue();
            } while (best.Count == 0);

            string picked = best[random.Next(best.Count)];
            _recent.Enqueue(picked);
            while (_recent.Count > RecentLimit)
                _recent.Dequeue();
            return picked;
        }

        internal static int PrefixGap(string tip, int capacity)
        {
            int used = 0;
            // Match the stream's spaces and explicit paragraph breaks. Only the prefix that
            // reaches the first wrap matters; the rest of the quote continues normally.
            string paragraph = tip.Split(new[] { '\n' }, 2)[0];
            foreach (
                string word in paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            )
            {
                int next = used + (used > 0 ? 1 : 0) + word.Length;
                if (next > capacity)
                    break;
                used = next;
            }
            return capacity - used;
        }
    }
}
