using System.Collections.Generic;

namespace SlopWorld
{
    // Status snapshots can change without changing colony membership. Retain the name set.
    internal sealed class ColonySessionIndex
    {
        readonly HashSet<string> _names = new HashSet<string>();
        long _version = -1;
        bool _initialized;

        public bool Contains(string name) => _names.Contains(name);

        public bool Refresh(List<SessionInfo> sessions, long version)
        {
            if (_initialized && _version == version) return false;
            _version = version;
            int count = 0;
            bool changed = !_initialized;
            foreach (var session in sessions)
            {
                if (session.Ephemeral || session.Worker) continue;
                count++;
                if (!_names.Contains(session.Name)) changed = true;
            }
            _initialized = true;
            if (!changed && count == _names.Count) return false;

            _names.Clear();
            foreach (var session in sessions)
                if (!session.Ephemeral && !session.Worker) _names.Add(session.Name);
            return true;
        }
    }
}
