using System.Collections.Generic;

namespace SlopWorld
{
    internal sealed class ProjectSessionCounts
    {
        readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
        long _revision;
        bool _valid;
        int _nullCount;

        public int Get(IEnumerable<SessionInfo> sessions, long revision, string project)
        {
            if (!_valid || revision != _revision)
            {
                _counts.Clear();
                _nullCount = 0;
                foreach (var session in sessions)
                {
                    if (session.Project == null) { _nullCount++; continue; }
                    _counts.TryGetValue(session.Project, out int count);
                    _counts[session.Project] = count + 1;
                }
                _revision = revision;
                _valid = true;
            }
            if (project == null) return _nullCount;
            return _counts.TryGetValue(project, out int result) ? result : 0;
        }
    }
}
