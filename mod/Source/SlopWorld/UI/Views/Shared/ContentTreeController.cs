using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // The common owner of project headings, their fold state, tree invalidation and row
    // selection. Feature adapters still own their node model and asynchronous work.
    public sealed class ContentTreeController
    {
        readonly Func<IList<ContentTreeGroup>> _buildGroups;
        readonly ContentTreeState _state = new ContentTreeState();

        public ContentTreeController(Func<IList<ContentTreeGroup>> buildGroups)
        {
            _buildGroups = buildGroups ?? throw new ArgumentNullException(nameof(buildGroups));
        }

        public int Revision => _state.Revision;
        public string SelectedKey => _state.SelectedKey;

        public IList<ContentTreeGroup> Groups()
        {
            return _buildGroups() ?? new List<ContentTreeGroup>();
        }

        public void SyncGroups(IEnumerable<string> groupKeys)
        {
            var keys = new List<string>();
            if (groupKeys != null)
                foreach (var key in groupKeys)
                    if (key != null) keys.Add(key);
            _state.SyncGroups(keys);
        }

        public bool IsGroupCollapsed(ContentTreeGroup group) =>
            group != null && _state.IsCollapsed(group.Key);

        public bool IsGroupCollapsed(string key) => _state.IsCollapsed(key);

        public void SetGroupCollapsed(string key, bool collapsed) =>
            _state.SetCollapsed(key, collapsed);

        // Returns true when the group was opened, which lets Files refresh its cached root
        // without forcing Git's already-materialized tree to do extra work.
        public bool ToggleGroup(ContentTreeGroup group)
        {
            return group != null && _state.ToggleCollapsed(group.Key);
        }

        public bool AllFolded
        {
            get
            {
                var groups = Groups();
                var keys = new List<string>();
                foreach (var group in groups) keys.Add(group.Key);
                return _state.AllFolded(keys);
            }
        }

        public void SetAllFolded(bool folded)
        {
            var groups = Groups();
            var keys = new List<string>();
            foreach (var group in groups) keys.Add(group.Key);
            _state.SetAllFolded(keys, folded);
        }

        public void Bump() => _state.Bump();

        public bool IsSelected(string key) => _state.IsSelected(key);
        public void Select(string key) => _state.Select(key);
        public void ClearSelection() => _state.ClearSelection();
    }
}
