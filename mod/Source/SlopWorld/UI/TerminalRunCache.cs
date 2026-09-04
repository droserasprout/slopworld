using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Parsed terminal rows are immutable after construction. History views repeatedly expose
    // the same strings at different anchors, so retain a bounded set instead of parsing every
    // assembled ScreenBuf from scratch.
    internal sealed class TerminalRunCache
    {
        const int MaxRows = 2_048;
        readonly Dictionary<Key, List<SgrRun>> _rows =
            new Dictionary<Key, List<SgrRun>>();

        struct Key : IEquatable<Key>
        {
            public string Line;
            public int Cols, ThemeRev, FontRev;

            public bool Equals(Key other) => Cols == other.Cols && ThemeRev == other.ThemeRev &&
                FontRev == other.FontRev && string.Equals(Line, other.Line, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is Key && Equals((Key)obj);
            public override int GetHashCode()
            {
                int hash = Line == null ? 0 : Line.GetHashCode();
                hash = unchecked(hash * 397 ^ Cols);
                hash = unchecked(hash * 397 ^ ThemeRev);
                return unchecked(hash * 397 ^ FontRev);
            }
        }

        public List<SgrRun>[] Parse(string[] lines, int cols, int themeRev, int fontRev,
                                    out int hits, out int misses)
        {
            hits = 0;
            misses = 0;
            lines = lines ?? Array.Empty<string>();

            // Autolinking can join rows and split color runs, so link candidates are parsed as
            // one unit. Plain and ANSI-only output takes the reusable row path.
            if (Sgr.MayContainLink(lines))
            {
                misses = lines.Length;
                return Sgr.ParseLines(lines, cols);
            }

            var parsed = new List<SgrRun>[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                var key = new Key
                {
                    Line = lines[i] ?? "",
                    Cols = cols,
                    ThemeRev = themeRev,
                    FontRev = fontRev,
                };
                if (_rows.TryGetValue(key, out parsed[i]))
                {
                    hits++;
                    continue;
                }

                parsed[i] = Sgr.ParseLine(lines[i]);
                misses++;
                if (_rows.Count >= MaxRows) _rows.Clear();
                _rows[key] = parsed[i];
            }
            return parsed;
        }

        public int Count => _rows.Count;
    }
}
