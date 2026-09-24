using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Daemon revisions, configured command classification and local reader mutations own membership.
    internal sealed class RoutedSessionRows
    {
        public static long LocalRevision { get; private set; }
        public static void Invalidate() { LocalRevision++; }
        long _sessions = -1, _local = -1;
        int _projects = -1, _count = -1;
        string _pager, _editor;

        public float Ensure(List<SessionInfo> rows, IEnumerable<SessionInfo> sessions,
                            long sessionsRevision, int projectsRevision, string pager, string editor,
                            Predicate<SessionInfo> include,
                            Action<List<SessionInfo>> append, float rowHeight)
        {
            if (_sessions != sessionsRevision || _projects != projectsRevision ||
                _local != LocalRevision || _count != rows.Count ||
                _pager != pager || _editor != editor)
            {
                Rebuild(rows, sessions, include, append, rowHeight);
                _sessions = sessionsRevision;
                _projects = projectsRevision;
                _local = LocalRevision;
                _count = rows.Count;
                _pager = pager;
                _editor = editor;
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
