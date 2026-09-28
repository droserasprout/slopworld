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
        // Scratch text never escapes the scan. Snapshots share only immutable link spans.
        char[] _linkChars;
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
            return Parse(new ScreenBuf
            {
                Lines = lines,
                Cols = cols,
                Rows = lines.Length,
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

            // First retain/parse only the ANSI rows. Link decoration is a separate pass because
            // a URL can connect rows that are otherwise unchanged. Null decorated rows also
            // mark pending source edits. Keep base arrays intact until this parse copies them.
            bool reuseBase = screen?.BaseRuns != null && screen.BaseRuns.Length == lines.Length &&
                screen.BaseRunsRev == themeRev && screen.Runs != null &&
                screen.Runs.Length == lines.Length;
            bool incrementalLinks = reuseBase && screen.AutoLinks != null &&
                screen.RunsRev == themeRev;
            var dirtyLinks = incrementalLinks ? new bool[lines.Length] : null;
            if (dirtyLinks != null)
                for (int i = 0; i < lines.Length; i++) dirtyLinks[i] = screen.Runs[i] == null;
            bool plainBase = reuseBase && screen.AutoLinks == null;
            var baseRows = plainBase ? screen.Runs :
                reuseBase ? screen.BaseRuns : new List<SgrRun>[lines.Length];
            bool sharedBase = reuseBase && !plainBase;
            for (int i = 0; i < lines.Length; i++)
            {
                if (baseRows[i] != null && screen.Runs[i] != null)
                {
                    hits++;
                    continue;
                }

                if (sharedBase)
                {
                    baseRows = (List<SgrRun>[])baseRows.Clone();
                    sharedBase = false;
                }

                var key = new Key
                {
                    Line = lines[i] ?? "",
                    Cols = cols,
                    ThemeRev = themeRev,
                    FontRev = fontRev,
                };
                if (_rows.TryGetValue(key, out var cached))
                {
                    baseRows[i] = cached;
                    hits++;
                    continue;
                }

                baseRows[i] = Sgr.ParseLineBase(lines[i]);
                misses++;
                if (_rows.Count >= MaxRows) _rows.Clear();
                _rows[key] = baseRows[i];
            }

            if (screen == null) return baseRows;

            screen.BaseRuns = baseRows;
            screen.BaseRunsRev = themeRev;
            bool hasAutoLinks = screen.LinksKnown ? screen.HasLinks : TerminalAutolinks.MayContainLink(lines);
            var local = !hasAutoLinks ? null : incrementalLinks ?
                TerminalAutolinks.AutoLinkSpansIncremental(baseRows, cols, ref _linkChars,
                    screen.AutoLinks, dirtyLinks) :
                TerminalAutolinks.AutoLinkSpans(baseRows, cols, ref _linkChars);
            bool reuseRuns = screen.Runs != null && screen.Runs.Length == lines.Length &&
                screen.RunsRev == themeRev;
            // Without guessed links the base rows are already the final immutable result.
            var parsed = local == null ? baseRows :
                reuseRuns ? screen.Runs : new List<SgrRun>[lines.Length];
            bool sharedRuns = reuseRuns;
            for (int i = 0; local != null && i < parsed.Length; i++)
            {
                // Null rows accumulate source edits until parsing. Comparing full-screen URL
                // spans catches changes to the other rows of a wrapped, joined or removed URL.
                if (parsed[i] != null && SameSpans(screen.AutoLinks?[i], local?[i])) continue;
                if (sharedRuns)
                {
                    parsed = (List<SgrRun>[])parsed.Clone();
                    sharedRuns = false;
                }
                parsed[i] = TerminalAutolinks.Decorate(baseRows[i], local?[i]);
            }
            screen.Runs = parsed;
            screen.AutoLinks = local;
            screen.HasLinks = local != null;
            screen.LinksKnown = true;
            return parsed;
        }

        public int Count => _rows.Count;

        static bool SameSpans(List<UrlScan.Span> left, List<UrlScan.Span> right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
                if (left[i].Start != right[i].Start || left[i].End != right[i].End ||
                    left[i].Url != right[i].Url) return false;
            return true;
        }
    }
}
