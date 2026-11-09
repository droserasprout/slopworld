using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Pure transport selection policy. Radio owns visibility, persistence and playback.
    internal static class RadioNavigation
    {
        internal static int Index(int count, int current, int direction, Random random)
        {
            if (count == 0)
                return -1;
            if (random != null)
            {
                if (current < 0 || count == 1)
                    return random.Next(count);
                int pick = random.Next(count - 1);
                return pick >= current ? pick + 1 : pick;
            }
            if (current < 0)
                return direction < 0 ? count - 1 : 0;
            return (current + direction + count) % count;
        }

        internal static Radio.Station Station(
            List<Radio.Station> stations,
            Radio.Station current,
            int direction,
            Random random
        )
        {
            int index = Index(stations.Count, stations.IndexOf(current), direction, random);
            return index < 0 ? null : stations[index];
        }

        // Prefer the lower rate on an exact tie, independent of catalog stream order.
        internal static int ClosestRate(int[] rates, int current)
        {
            int best = rates[0];
            foreach (int rate in rates)
                if (
                    Math.Abs((long)rate - current) < Math.Abs((long)best - current)
                    || (
                        Math.Abs((long)rate - current) == Math.Abs((long)best - current)
                        && rate < best
                    )
                )
                    best = rate;
            return best;
        }
    }
}
