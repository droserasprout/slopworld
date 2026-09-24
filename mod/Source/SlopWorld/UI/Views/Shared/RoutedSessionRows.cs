using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Daemon revisions and explicit local reader mutations jointly own routed membership.
    internal sealed class RoutedSessionRows
    {
        public static long LocalRevision { get; private set; }
        public static void Invalidate() { LocalRevision++; }
        long _sessions = -1, _local = -1;
        int _projects = -1, _count = -1;

        public float Ensure(List<SessionInfo> rows, IEnumerable<SessionInfo> sessions,
                            long sessionsRevision, int projectsRevision,
                            Predicate<SessionInfo> include,
                            Action<List<SessionInfo>> append, float rowHeight)
        {
            if (_sessions != sessionsRevision || _projects != projectsRevision ||
                _local != LocalRevision || _count != rows.Count)
            {
                Rebuild(rows, sessions, include, append, rowHeight);
                _sessions = sessionsRevision;
                _projects = projectsRevision;
                _local = LocalRevision;
                _count = rows.Count;
            }
            return rows.Count * rowHeight;
        }

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
