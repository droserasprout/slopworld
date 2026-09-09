using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Rebuilt once per draw pass: native previews and viewer handoffs can change between
    // input events without a new daemon session revision.
    internal static class RoutedSessionRows
    {
        public static float Rebuild(List<SessionInfo> rows, IEnumerable<SessionInfo> sessions,
                                    Predicate<SessionInfo> include,
                                    Action<List<SessionInfo>> append, float rowHeight)
        {
            rows.Clear();
            foreach (var session in sessions)
                if (include(session)) rows.Add(session);
            append?.Invoke(rows);
            rows.Sort((a, b) => string.CompareOrdinal(a?.Name ?? "", b?.Name ?? ""));
            return rows.Count * rowHeight;
        }
    }
}
