using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Daemon revisions, configured command classification and local reader mutations own membership.
    internal sealed class RoutedSessionRows
    {
        public static long LocalRevision { get; private set; }
        public static void Invalidate() { LocalRevision++; }
        long _sessionsRevision = -1;
        long _localRevision = -1;
        int _projectsRevision = -1;
        int _rowCount = -1;
        string _pagerCommand;
        string _editorCommand;

        public float Ensure(List<SessionInfo> rows, IEnumerable<SessionInfo> sessions,
                            long sessionsRevision, int projectsRevision, string pager, string editor,
                            Predicate<SessionInfo> include,
                            Action<List<SessionInfo>> append, float rowHeight)
        {
            if (_sessionsRevision != sessionsRevision || _projectsRevision != projectsRevision ||
                _localRevision != LocalRevision || _rowCount != rows.Count ||
                _pagerCommand != pager || _editorCommand != editor)
            {
                Rebuild(rows, sessions, include, append, rowHeight);
                _sessionsRevision = sessionsRevision;
                _projectsRevision = projectsRevision;
                _localRevision = LocalRevision;
                _rowCount = rows.Count;
                _pagerCommand = pager;
                _editorCommand = editor;
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
