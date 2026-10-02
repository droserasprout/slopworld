using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Process-local shape and selection state for one content tree. Keys are deliberately
    // semantic rather than object references so a refresh may replace nodes without losing
    // the selected file or a collapsed project heading.
    public sealed class ContentTreeState
    {
        readonly HashSet<string> _collapsed = new HashSet<string>();
        string _selected;
        int _revision;

        public int Revision => _revision;
        public string SelectedKey => _selected;

        public bool IsCollapsed(string key) => key != null && _collapsed.Contains(key);

        // Returns true when the group opens.
        public bool ToggleCollapsed(string key)
        {
            if (key == null) return false;
            if (_collapsed.Remove(key))
            {
                Bump();
                return true;
            }

            _collapsed.Add(key);
            Bump();
            return false;
        }

        public void SetCollapsed(string key, bool collapsed)
        {
            if (key == null) return;
            bool changed = collapsed ? _collapsed.Add(key) : _collapsed.Remove(key);
            if (changed) Bump();
        }

        public void SetAllFolded(IEnumerable<string> keys, bool folded)
        {
            bool changed = false;
            foreach (var key in keys ?? Empty)
                if (key != null)
                    changed |= folded ? _collapsed.Add(key) : _collapsed.Remove(key);
            if (changed) Bump();
        }

        public bool AllFolded(IEnumerable<string> keys)
        {
            bool any = false;
            foreach (var key in keys ?? Empty)
            {
                any = true;
                if (!IsCollapsed(key)) return false;
            }
            return any;
        }

        public void SyncGroups(IEnumerable<string> keys)
        {
            var present = new HashSet<string>(keys ?? Empty);
            var stale = new List<string>();
            foreach (var key in _collapsed)
                if (!present.Contains(key)) stale.Add(key);
            if (stale.Count == 0) return;
            foreach (var key in stale) _collapsed.Remove(key);
            Bump();
        }

        public void Bump()
        {
            unchecked { _revision++; }
        }

        public void Select(string key)
        {
            _selected = key;
        }

        public bool IsSelected(string key) => _selected != null && _selected == key;

        public void ClearSelection() => _selected = null;

        static readonly string[] Empty = new string[0];
    }
}
