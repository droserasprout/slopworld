using System.Collections.Generic;
using UnityEngine;
using UrlSpan = SlopWorld.UrlScan.Span;

namespace SlopWorld
{
    // Guessed links decorate decoded rows without mutating cached base runs.
    static class TerminalAutolinks
    {
        // Return screen-wide autolink spans split into the row/column space used by runs. The
        // base rows are never changed, so callers can reuse them in retained snapshots.
        internal static List<UrlSpan>[] AutoLinkSpans(List<SgrRun>[] rows, int cols, ref char[] scratch)
        {
            if (rows == null || rows.Length == 0) return null;

            int width = cols;
            if (width <= 0)
                foreach (var row in rows) width = Mathf.Max(width, RowWidth(row));
            if (width <= 0) return null;

            if (scratch == null || scratch.Length < rows.Length * width)
                scratch = new char[rows.Length * width];
            var chars = scratch;
            for (int row = 0; row < rows.Length; row++)
                TerminalColumns.WriteScanLine(rows[row], chars, row * width, width);

            var local = new List<UrlSpan>[rows.Length];
            ScanLinkRange(chars, width, 0, rows.Length, local);
            return HasSpans(local) ? local : null;
        }

        // A received frame can edit one row while retaining every other parsed row. Rebuild
        // only the group joined to that edit by a full-width row boundary. Old spans also join
        // rows, so a deleted boundary cannot leave half of a former link in a snapshot.
        internal static List<UrlSpan>[] AutoLinkSpansIncremental(List<SgrRun>[] rows, int cols,
            ref char[] scratch, List<UrlSpan>[] previous, bool[] dirty)
        {
            if (previous == null || previous.Length != rows.Length || cols <= 0)
                return AutoLinkSpans(rows, cols, ref scratch);
            if (scratch == null || scratch.Length < rows.Length * cols)
                scratch = new char[rows.Length * cols];

            var local = (List<UrlSpan>[])previous.Clone();
            for (int row = 0; row < rows.Length;)
            {
                if (!dirty[row]) { row++; continue; }
                int first = row, last = row;
                while (first > 0 && Joined(rows, scratch, cols, previous, first - 1)) first--;
                while (last + 1 < rows.Length && Joined(rows, scratch, cols, previous, last)) last++;
                for (int i = first; i <= last; i++) local[i] = null;
                for (int i = first; i <= last; i++)
                    TerminalColumns.WriteScanLine(rows[i], scratch, i * cols, cols);
                ScanLinkRange(scratch, cols, first, last + 1, local);
                row = last + 1;
            }
            return HasSpans(local) ? local : null;
        }

        static bool Joined(List<SgrRun>[] rows, char[] chars, int width,
            List<UrlSpan>[] previous, int row)
        {
            TerminalColumns.WriteScanLine(rows[row], chars, row * width, width);
            TerminalColumns.WriteScanLine(rows[row + 1], chars, (row + 1) * width, width);
            if (chars[(row + 1) * width - 1] != ' ' && chars[(row + 1) * width] != ' ')
                return true;
            var left = previous[row];
            var right = previous[row + 1];
            if (left == null || right == null) return false;
            foreach (var a in left)
                if (a.End == width)
                    foreach (var b in right)
                        if (b.Start == 0 && b.Url == a.Url) return true;
            return false;
        }

        static bool HasSpans(List<UrlSpan>[] rows)
        {
            foreach (var spans in rows) if (spans != null && spans.Count > 0) return true;
            return false;
        }

        static void ScanLinkRange(char[] chars, int width, int firstRow, int endRow,
            List<UrlSpan>[] local)
        {
            var global = UrlScan.FindUrls(new string(chars, firstRow * width,
                (endRow - firstRow) * width));
            if (global == null) return;
            foreach (var span in global)
            {
                int first = firstRow + span.Start / width;
                int last = firstRow + (span.End - 1) / width;
                for (int row = first; row <= last && row < endRow; row++)
                {
                    int start = row == first ? span.Start % width : 0;
                    int end = row == last ? (span.End - 1) % width + 1 : width;
                    if (local[row] == null) local[row] = new List<UrlSpan>();
                    local[row].Add(new UrlSpan(start, end, span.Url));
                }
            }
        }

        // Apply only guessed links. Explicit OSC 8 runs remain authoritative in Split.
        internal static List<SgrRun> Decorate(List<SgrRun> baseRuns, List<UrlSpan> spans)
        {
            if (baseRuns == null || spans == null || spans.Count == 0) return baseRuns;
            return SplitCopy(baseRuns, spans);
        }

        // Avoid the rows-by-columns URL grid for ordinary output. OSC 8 still takes the parser path
        // and already carries link metadata. In contrast, Visible URLs necessarily contain
        // this delimiter even when it is split across SGR runs or physical rows.
        internal static bool MayContainLink(string[] lines)
        {
            // Ordinary output needs no escape-aware scan. IndexOf can reject whole spans
            // cheaply. Any raw colon still takes the conservative path below.
            bool colon = false;
            foreach (string line in lines)
                if (line != null && line.IndexOf(':') >= 0) { colon = true; break; }
            if (!colon) return false;

            int matched = 0;
            const string needle = "://";
            foreach (string line in lines)
            {
                if (line == null) continue;
                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (c == '\x1b')
                    {
                        // Escape payloads cannot contribute visible URL characters.
                        if (i + 1 < line.Length && line[i + 1] == '[')
                        {
                            i += 2;
                            while (i < line.Length && !(line[i] >= '@' && line[i] <= '~')) i++;
                            // CHA can overwrite intervening text and join a visible delimiter.
                            // Let the decoded column grid decide whether it is a URL.
                            if (i < line.Length && line[i] == 'G') return true;
                        }
                        else if (i + 1 < line.Length && line[i + 1] == ']')
                        {
                            i += 2;
                            while (i < line.Length && line[i] != '\x07' &&
                                   !(line[i] == '\x1b' && i + 1 < line.Length && line[i + 1] == '\\')) i++;
                            if (i < line.Length && line[i] == '\x1b') i++;
                        }
                        continue;
                    }
                    matched = c == needle[matched] ? matched + 1 : c == needle[0] ? 1 : 0;
                    if (matched == needle.Length) return true;
                }
            }
            return false;
        }

        // Scan characters rather than SGR runs, because a plain URL may cross colors. The
        // screen-wide overload also joins only physical row edges. A blank tail still breaks.
        internal static void Autolink(List<SgrRun> runs)
        {
            if (runs.Count == 0) return;

            var chars = new char[RowWidth(runs)];
            TerminalColumns.WriteScanLine(runs, chars, 0, chars.Length);
            var spans = UrlScan.FindUrls(new string(chars));
            if (spans != null) Split(runs, spans);
        }

        internal static void Autolink(List<SgrRun>[] rows, int cols)
        {
            char[] chars = null;
            var local = AutoLinkSpans(rows, cols, ref chars);
            if (local == null) return;
            for (int row = 0; row < rows.Length; row++)
                if (local[row] != null) Split(rows[row], local[row]);
        }

        static int RowWidth(List<SgrRun> row)
        {
            int width = 0;
            foreach (var run in row) width = Mathf.Max(width, run.Col + run.Columns);
            return width;
        }

        // Runs are cut where the spans cross them. Therefore, a link that starts mid-word or ends
        // mid-color keeps every one of the colors it was drawn in. A run the app already linked is
        // left alone: what it says beats what the text looks like.
        static void Split(List<SgrRun> runs, List<UrlSpan> spans)
        {
            var cut = SplitCopy(runs, spans);
            runs.Clear();
            runs.AddRange(cut);
        }

        static List<SgrRun> SplitCopy(List<SgrRun> runs, List<UrlSpan> spans)
        {
            var cut = new List<SgrRun>(runs.Count + spans.Count * 2);
            foreach (var r in runs)
            {
                int start = r.Col, end = r.Col + r.Columns;
                if (r.Url != null || r.IsCluster || end <= start) { cut.Add(r); continue; }

                int at = start;
                foreach (var span in spans)
                {
                    if (span.End <= at || span.Start >= end) continue;
                    int a = Mathf.Max(span.Start, at);
                    int b = Mathf.Min(span.End, end);
                    // A guessed link cannot split the final glyph from its continuation.
                    if (r.Columns > TerminalColumns.ScalarCount(r.Text))
                    {
                        if (a == end - 1) a--;
                        if (b == end - 1) b++;
                        a = Mathf.Max(a, at);
                    }
                    if (a > at) cut.Add(Slice(r, at, a, null));
                    cut.Add(Slice(r, a, b, span.Url));
                    at = b;
                }
                if (at < end) cut.Add(Slice(r, at, end, null));
            }

            return cut;
        }

        static SgrRun Slice(SgrRun r, int from, int to, string url) => new SgrRun
        {
            Text = r.Text.Substring(TerminalColumns.TextOffset(r.Text, from - r.Col),
                TerminalColumns.TextOffset(r.Text, to - r.Col) - TerminalColumns.TextOffset(r.Text, from - r.Col)),
            CellWidth = to - from,
            Col = from,
            Fg = r.Fg,
            Bg = r.Bg,
            HasBg = r.HasBg,
            Bold = r.Bold,
            Url = url,
        };
    }
}
