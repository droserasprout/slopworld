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
            lines = lines ?? Array.Empty<string>();
            var changed = new int[lines.Length];
            for (int i = 0; i < changed.Length; i++) changed[i] = i;
            return Parse(new ScreenBuf
            {
                Lines = lines,
                Cols = cols,
                Rows = lines.Length,
                ChangedRows = changed,
                ContentRevision = 1,
            }, themeRev, fontRev, out hits, out misses);
        }

        public List<SgrRun>[] Parse(ScreenBuf screen, int themeRev, int fontRev,
                                    out int hits, out int misses)
        {
            hits = 0;
            misses = 0;
            var lines = screen?.Lines ?? Array.Empty<string>();
            int cols = screen?.Cols ?? 0;

            bool reuseRows = screen?.Runs != null && screen.Runs.Length == lines.Length &&
                screen.RunsRev == themeRev;
            bool hasLink = screen != null && screen.HasLinks;
            if (screen != null && !screen.LinksKnown)
            {
                if (hasLink)
                {
                    // A previous link may have been removed or moved by a changed row. Keep
                    // the conservative global path until this frame establishes the new state.
                    hasLink = Sgr.MayContainLink(lines);
                }
                else
                {
                    var changed = screen.ChangedRows;
                    if (changed == null || changed.Length == 0 || changed.Length >= lines.Length)
                        hasLink = Sgr.MayContainLink(lines);
                    else
                        foreach (int row in changed)
                            if (row >= 0 && row < lines.Length && LinkCandidate(lines[row]))
                            {
                                // A URL may start or end at a physical row boundary. A changed
                                // ':' or '/' therefore makes neighboring unchanged rows part of
                                // the candidate scan, while ordinary ANSI text stays row-local.
                                hasLink = Sgr.MayContainLink(lines);
                                break;
                            }
                }
                screen.HasLinks = hasLink;
                screen.LinksKnown = true;
            }

            // Autolinking can join rows and split color runs, so link candidates are parsed as
            // one unit. Plain and ANSI-only output takes the reusable row path.
            if (hasLink)
            {
                misses = lines.Length;
                return Sgr.ParseLines(lines, cols);
            }

            var parsed = reuseRows ? screen.Runs : new List<SgrRun>[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                if (parsed[i] != null)
                {
                    hits++;
                    continue;
                }

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

        static bool LinkCandidate(string line) => line != null &&
            (line.IndexOf(':') >= 0 || line.IndexOf('/') >= 0);
    }
}
