using System.Collections.Generic;

namespace SlopWorld
{
    // Geometry and reveal lookup share the flattened order, including variable-height bodies.
    internal sealed class ContentTreeIndex
    {
        readonly List<float> _ends = new List<float>();
        readonly Dictionary<string, float> _tops = new Dictionary<string, float>();
        int _revision;
        bool _valid;

        public bool IsCurrent(int revision) => _valid && revision == _revision;

        public void Clear()
        {
            _valid = false;
            _ends.Clear();
            _tops.Clear();
        }

        public void Add(float top, float end, string selectionKey)
        {
            _ends.Add(end);
            if (selectionKey != null) _tops[selectionKey] = top;
        }

        public void Commit(int revision) { _revision = revision; _valid = true; }
        public int First(float top) => VisibleRows.First(_ends, top);
        public bool Reveal(string key, out float top) => _tops.TryGetValue(key, out top);
    }
}
