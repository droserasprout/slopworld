using System.Collections.Generic;

namespace SlopWorld
{
    internal sealed class ProjectSessionCounts
    {
        readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
        long _revision;
        bool _valid;
        int _nullCount;

        // Equal versions must have unchanged project membership and agent classification.
        public int Get(IEnumerable<SessionInfo> sessions, long sessionsVersion, string project)
        {
            if (!_valid || sessionsVersion != _revision)
            {
                _counts.Clear();
                _nullCount = 0;
                foreach (var session in sessions)
                {
                    if (session.Host || session.Worker || session.Ephemeral) continue;
                    if (session.Project == null) { _nullCount++; continue; }
                    _counts.TryGetValue(session.Project, out int count);
                    _counts[session.Project] = count + 1;
                }
                _revision = sessionsVersion;
                _valid = true;
            }
            if (project == null) return _nullCount;
            return _counts.TryGetValue(project, out int result) ? result : 0;
        }
    }
}
