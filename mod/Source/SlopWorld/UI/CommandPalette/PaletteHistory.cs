using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Owns bounded recent-command ordering and its text format. The palette window owns
    // loading/writing profile settings and deciding when an action counts as a use.
    sealed class PaletteHistory
    {
        const int Capacity = 8;
        readonly List<string> _entries = new List<string>();
        public IReadOnlyList<string> Entries => _entries;
        public string Text => string.Join("\n", _entries);

        public void Load(string text, IEnumerable<string> commandIds)
        {
            var known = new HashSet<string>(commandIds, StringComparer.Ordinal);
            _entries.Clear();
            foreach (
                var id in (text ?? "").Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                )
            )
            {
                if (_entries.Count >= Capacity)
                    break;
                if (known.Contains(id) && !_entries.Contains(id))
                    _entries.Add(id);
            }
        }

        public bool Track(string id)
        {
            if (string.IsNullOrEmpty(id) || (_entries.Count > 0 && _entries[0] == id))
                return false;
            _entries.Remove(id);
            _entries.Insert(0, id);
            if (_entries.Count > Capacity)
                _entries.RemoveAt(_entries.Count - 1);
            return true;
        }
    }
}
