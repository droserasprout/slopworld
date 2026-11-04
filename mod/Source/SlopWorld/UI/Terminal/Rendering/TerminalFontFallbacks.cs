using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Cache system family order until the selection or installed-font revision changes.
    // Fontconfig owns preference; do not reorder Nerd Fonts or individual glyphs here.
    sealed class TerminalFontFallbacks
    {
        readonly Func<string, string[]> _resolve;
        readonly Action<string> _warning;
        string _family;
        int _revision;
        string[] _chain;

        public TerminalFontFallbacks(Func<string, string[]> resolve, Action<string> warning)
        {
            _resolve = resolve;
            _warning = warning;
        }

        public string[] Get(string selected, int revision)
        {
            string family = string.IsNullOrEmpty(selected) ? "monospace" : selected;
            if (_chain != null && _family == family && _revision == revision)
                return _chain;
            var chain = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string name in _resolve(family))
                    if (!string.IsNullOrEmpty(name) && seen.Add(name))
                        chain.Add(name);
                if (chain.Count == 0)
                    throw new InvalidOperationException("no usable system font families");
            }
            catch (Exception e)
            {
                chain.Clear();
                chain.Add(family);
                if (!string.Equals(family, "monospace", StringComparison.OrdinalIgnoreCase))
                    chain.Add("monospace");
                _warning("system font fallback: " + e.Message);
            }
            _family = family;
            _revision = revision;
            return _chain = chain.ToArray();
        }
    }
}
