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
            rows.Sort(CompareReaders);
            return rows.Count * rowHeight;
        }

        // Session handles change when a diff refresh replaces its process. Order by
        // display/source metadata so that replacement keeps the same sidebar position.
        static int CompareReaders(SessionInfo a, SessionInfo b)
        {
            int order = string.CompareOrdinal(SortLabel(a), SortLabel(b));
            if (order != 0) return order;
            order = string.CompareOrdinal(a?.ReaderPath ?? "", b?.ReaderPath ?? "");
            if (order != 0) return order;
            order = string.CompareOrdinal(a?.ReaderKey ?? "", b?.ReaderKey ?? "");
            if (order != 0) return order;
            order = string.CompareOrdinal(a?.ReaderScope ?? "", b?.ReaderScope ?? "");
            if (order != 0) return order;
            order = string.CompareOrdinal(a?.Intent ?? "", b?.Intent ?? "");
            return order != 0 ? order : string.CompareOrdinal(a?.Name ?? "", b?.Name ?? "");
        }

        static string SortLabel(SessionInfo info) =>
            !string.IsNullOrEmpty(info?.Label) ? info.Label :
            !string.IsNullOrEmpty(info?.ReaderPath) ? info.ReaderPath :
            !string.IsNullOrEmpty(info?.ReaderKey) ? info.ReaderKey : info?.Name ?? "";
    }
}
