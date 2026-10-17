using System.Collections.Generic;

namespace SlopWorld
{
    // Checkbox selection with distinct All and empty states. Views supply the current
    // catalog when leaving All so a checked child always toggles off.
    sealed class FilterSelection<T>
    {
        readonly HashSet<T> _selected;
        public bool AllSelected { get; private set; } = true;

        public FilterSelection(IEqualityComparer<T> comparer = null) =>
            _selected = new HashSet<T>(comparer);

        public bool Contains(T value) => AllSelected || _selected.Contains(value);

        public void SelectAll()
        {
            _selected.Clear();
            AllSelected = true;
        }

        public void Toggle(T value, IEnumerable<T> values)
        {
            if (AllSelected)
            {
                _selected.UnionWith(values);
                AllSelected = false;
            }
            if (!_selected.Remove(value))
                _selected.Add(value);
        }
    }
}
