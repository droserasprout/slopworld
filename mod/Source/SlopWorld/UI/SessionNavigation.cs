using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Chooses a live session after the one whose pane just went away. The sidebar order is
    // useful for the normal case, but it is only a rendered snapshot: it can omit a session
    // whose pawn has not been reconciled yet, or one hidden by the current sidebar tab.
    public static class SessionNavigation
    {
        public static string NextLive(string departed, IEnumerable<string> visibleOrder,
                                      IEnumerable<string> liveNames)
        {
            var live = new HashSet<string>(liveNames ?? Array.Empty<string>());
            var order = new List<string>();
            var seen = new HashSet<string>();

            if (visibleOrder != null)
            {
                foreach (var name in visibleOrder)
                    if (name != null && seen.Add(name)) order.Add(name);
            }

            if (!seen.Contains(departed))
            {
                // The departed row was not in the snapshot, so there is no reliable visual
                // anchor. Use the stable name order and put it back as a temporary anchor.
                order = new List<string>(live);
                order.Sort(StringComparer.Ordinal);
                int insert = order.FindIndex(name =>
                    string.CompareOrdinal(name, departed) > 0);
                if (insert < 0) insert = order.Count;
                order.Insert(insert, departed);
            }
            else
            {
                // Keep the visual order, then add live sessions that were not in that frame.
                // In particular, do not close merely because the departed row was visible.
                var hidden = new List<string>();
                foreach (var name in live)
                    if (!seen.Contains(name)) hidden.Add(name);
                hidden.Sort(StringComparer.Ordinal);
                order.AddRange(hidden);
            }

            int index = order.IndexOf(departed);
            if (index < 0) index = -1;
            for (int step = 1; step <= order.Count; step++)
            {
                int at = index < 0 ? step - 1 : (index + step) % order.Count;
                string target = order[at];
                if (live.Contains(target)) return target;
            }

            return null;
        }
    }
}
