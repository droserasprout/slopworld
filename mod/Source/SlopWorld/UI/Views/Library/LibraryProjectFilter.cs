using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Library owns this transient selection independently of persisted sidebar browse scopes.
    sealed class LibraryProjectFilter
    {
        readonly HashSet<string> _selected = new HashSet<string>(StringComparer.Ordinal);
        bool _all = true;

        public bool Filtering => !_all;

        public string Label
        {
            get
            {
                if (_all)
                    return "All";
                if (_selected.Count == 0)
                    return "No projects";
                if (_selected.Count == 1)
                    foreach (var key in _selected)
                        return key;
                return _selected.Count + " projects";
            }
        }

        public bool Passes(string key) => _all || _selected.Contains(key);

        public void SelectAll()
        {
            _selected.Clear();
            _all = true;
        }

        public void Toggle(string key, IEnumerable<string> projects)
        {
            // Materialize All before removing a visible project. An empty explicit set
            // must remain empty, and All must continue to include future projects.
            if (_all)
            {
                _selected.UnionWith(projects);
                _all = false;
            }
            if (!_selected.Remove(key))
                _selected.Add(key);
        }
    }
}
