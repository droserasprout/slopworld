using System.Collections.Generic;

namespace SlopWorld
{
    // Status snapshots can change without changing colony membership. Retain the name set.
    internal sealed class ColonySessionIndex
    {
        readonly HashSet<string> _names = new HashSet<string>();
        readonly HashSet<string> _eligibleNames = new HashSet<string>();
        long _version = -1;
        bool _initialized;

        internal static bool IsColonySession(SessionInfo session) =>
            !session.Ephemeral && !session.Worker;

        public bool Contains(string name) => _names.Contains(name);

        public bool Refresh(List<SessionInfo> sessions, long version)
        {
            if (_initialized && _version == version) return false;
            _version = version;
            _eligibleNames.Clear();
            foreach (var session in sessions)
                if (IsColonySession(session)) _eligibleNames.Add(session.Name);
            bool changed = !_initialized || !_names.SetEquals(_eligibleNames);
            _initialized = true;
            if (!changed) return false;

            _names.Clear();
            _names.UnionWith(_eligibleNames);
            return true;
        }
    }
}
