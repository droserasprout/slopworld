using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UrlSpan = SlopWorld.UrlScan.Span;

namespace SlopWorld
{
    public struct SgrRun
    {
        public string Text;
        // Absolute start column, set from the `\x1b[<n>G` (CHA) markers the daemon emits
        // ahead of runs whose column jumped past a wide char.
        public int Col;
        public Color Fg;
        public Color Bg;
        public bool HasBg;
        public bool Bold;
        // The whole link this run is part of, whether the app said so (OSC 8) or the
        // text simply reads as a URL. Null for ordinary text.
        public string Url;
    }

    // The daemon's emulator already did the hard part, so we only ever see SGR color
    // escapes, CHA column markers and the OSC 8 links it passes on.
    public static class Sgr
    {
        // The palette is the scheme's; these two are read all over the window as "what
        // the pane is when nothing has said otherwise".
        public static Color DefaultFg => TerminalTheme.Current.Fg;
        public static Color DefaultBg => TerminalTheme.Current.Bg;

        static Color[] Basic16 => TerminalTheme.Current.Ansi;

        // How much of a faint run's own color survives. Low enough that a completion hint
        // reads as a hint next to the line it is offered under, high enough that it is still
        // that color rather than a grey - agents state 2 over an ANSI color as often as over
        // the default foreground.
        const float FaintMix = 0.55f;

        struct Attr
        {
            public Color Fg;
            public Color Bg;
            public bool HasBg;
            public bool Bold;
            // SGR 2. A color rather than a weight: the cell keeps the foreground it was
            // given and the terminal is what decides how far towards the background it is
            // drawn. Which is why it cannot be resolved in the daemon - see Flush.
            public bool Faint;
            public bool Reverse;
        }

        static Attr Fresh() => new Attr { Fg = DefaultFg, Bg = DefaultBg, HasBg = false };

        public static List<SgrRun> ParseLine(string line)
        {
            var runs = ParseLineCore(line);
            Autolink(runs);
            return runs;
        }

        // Parse the screen as a group so a plain URL that wraps at the terminal edge can keep
        // the same link across rows. OSC 8 links already carry their own URL on each row.
        public static List<SgrRun>[] ParseLines(string[] lines, int cols)
        {
            var parsed = new List<SgrRun>[lines.Length];
            for (int i = 0; i < lines.Length; i++)
                parsed[i] = ParseLineCore(lines[i]);

            if (MayContainLink(lines)) Autolink(parsed, cols);
            return parsed;
        }

        // Avoid the rows-by-columns URL grid for ordinary output. OSC 8 still takes the parser
        // path above and already carries link metadata, while visible URLs necessarily contain
        // this delimiter even when it is split across SGR runs or physical rows.
        internal static bool MayContainLink(string[] lines)
        {
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

        static List<SgrRun> ParseLineCore(string line)
        {
            var runs = new List<SgrRun>();
            if (string.IsNullOrEmpty(line)) return runs;

            var attr = Fresh();
            // Outside Attr on purpose: a link is not an SGR attribute, and the reset that
            // opens every run the daemon writes would otherwise drop it.
            string url = null;
            var sb = new StringBuilder();
            int i = 0;
            // Column the pen is at; where the next appended char lands.
            int penCol = 0;
            // Column the run currently in `sb` began at.
            int runStart = 0;

            while (i < line.Length)
            {
                if (line[i] == '\x1b' && i + 1 < line.Length && line[i + 1] == '[')
                {
                    int j = i + 2;
                    while (j < line.Length && line[j] != 'm' &&
                           !(line[j] >= '@' && line[j] <= '~')) j++;

                    if (j < line.Length && line[j] == 'm')
                    {
                        Flush(runs, sb, attr, runStart, url);
                        Apply(line, i + 2, j, ref attr);
                    }
                    else if (j < line.Length && line[j] == 'G')
                    {
                        Flush(runs, sb, attr, runStart, url);
                        int n = ParseParam(line, i + 2, j);
                        penCol = n > 0 ? n - 1 : 0;
                        runStart = penCol;
                    }
                    i = j + 1;
                    continue;
                }

                // OSC, which off this wire is only ever `8;;<uri>` - the hyperlink the app
                // stated, handed on by the daemon rather than resolved.
                if (line[i] == '\x1b' && i + 1 < line.Length && line[i + 1] == ']')
                {
                    int j = i + 2;
                    while (j < line.Length && line[j] != '\x07' &&
                           !(line[j] == '\x1b' && j + 1 < line.Length && line[j + 1] == '\\')) j++;

                    Flush(runs, sb, attr, runStart, url);
                    url = UrlScan.Osc(line.Substring(i + 2, Mathf.Min(j, line.Length) - (i + 2))) ?? url;
                    if (j < line.Length && line[j] == '\x1b') i = j + 2;
                    else i = j + 1;
                    continue;
                }

                if (sb.Length == 0) runStart = penCol;
                sb.Append(line[i]);
                penCol++;
                i++;
            }

            Flush(runs, sb, attr, runStart, url);
            return runs;
        }

        static void Flush(List<SgrRun> runs, StringBuilder sb, Attr a, int col, string url)
        {
            if (sb.Length == 0) return;

            // Reverse video swaps the pair; the cursor and selections rely on it.
            var fg = a.Reverse ? (a.HasBg ? a.Bg : DefaultBg) : a.Fg;
            var bg = a.Reverse ? a.Fg : a.Bg;
            bool hasBg = a.Reverse || a.HasBg;

            if (a.Bold && !a.Reverse)
                fg = new Color(Mathf.Min(1f, fg.r * 1.25f),
                               Mathf.Min(1f, fg.g * 1.25f),
                               Mathf.Min(1f, fg.b * 1.25f));

            // Towards the background rather than towards black: on a light scheme a faint run
            // scaled down is *darker* than the ordinary text it is meant to recede behind.
            // Skipped under reverse for the reason bold is - the pair has been swapped, and
            // what would be dimmed there is the fill.
            if (a.Faint && !a.Reverse)
                fg = Color.Lerp(hasBg ? bg : DefaultBg, fg, FaintMix);

            if (string.IsNullOrEmpty(url)) url = null;

            // A run is flushed on every escape and most escapes change nothing a viewer can
            // see - a TUI re-states attributes constantly. What decides whether two runs
            // are one is the drawn colors, not the codes behind them, and never across a
            // column jump: a CHA is the one thing that says the pen moved.
            int last = runs.Count - 1;
            if (last >= 0)
            {
                var prev = runs[last];
                if (prev.Col + prev.Text.Length == col &&
                    prev.Fg == fg && prev.HasBg == hasBg && (!hasBg || prev.Bg == bg) &&
                    prev.Url == url)
                {
                    sb.Insert(0, prev.Text);
                    prev.Text = sb.ToString();
                    runs[last] = prev;
                    sb.Length = 0;
                    return;
                }
            }

            runs.Add(new SgrRun
            {
                Text = sb.ToString(),
                Col = col,
                Fg = fg,
                Bg = bg,
                HasBg = hasBg,
                Bold = a.Bold,
                Url = url,
            });
            sb.Length = 0;
        }

        static void Apply(string line, int start, int end, ref Attr a)
        {
            if (start >= end) { a = Fresh(); return; }
            int pos = start;
            while (pos < end)
            {
                int n = NextParam(line, ref pos, end);
                if (n < 0) continue;

                switch (n)
                {
                    case 0: a = Fresh(); break;
                    case 1: a.Bold = true; break;
                    case 2: a.Faint = true; break;
                    case 22: a.Bold = false; a.Faint = false; break;
                    case 7: a.Reverse = true; break;
                    case 27: a.Reverse = false; break;

                    case 39: a.Fg = DefaultFg; break;
                    case 49: a.HasBg = false; a.Bg = DefaultBg; break;

                    case 38:
                    case 48:
                    {
                        bool fg = n == 38;
                        int mode = NextParam(line, ref pos, end);

                        if (mode == 5)
                        {
                            int idx = NextParam(line, ref pos, end);
                            if (idx >= 0)
                            {
                                var c = Xterm256(idx);
                                if (fg) a.Fg = c; else { a.Bg = c; a.HasBg = true; }
                            }
                        }
                        else if (mode == 2)
                        {
                            int rv = NextParam(line, ref pos, end);
                            int gv = NextParam(line, ref pos, end);
                            int bv = NextParam(line, ref pos, end);
                            if (rv >= 0 && gv >= 0 && bv >= 0)
                            {
                                var c = new Color(rv / 255f, gv / 255f, bv / 255f);
                                if (fg) a.Fg = c; else { a.Bg = c; a.HasBg = true; }
                            }
                        }
                        break;
                    }

                    default:
                        if (n >= 30 && n <= 37) a.Fg = Basic16[n - 30];
                        else if (n >= 90 && n <= 97) a.Fg = Basic16[n - 90 + 8];
                        else if (n >= 40 && n <= 47) { a.Bg = Basic16[n - 40]; a.HasBg = true; }
                        else if (n >= 100 && n <= 107) { a.Bg = Basic16[n - 100 + 8]; a.HasBg = true; }
                        break;
                }
            }
        }

        static int NextParam(string s, ref int pos, int end)
        {
            if (pos > end) return -1;
            int semi = pos;
            while (semi < end && s[semi] != ';') semi++;
            int n = ParseParam(s, pos, semi);
            pos = semi + 1;
            return n;
        }

        static int ParseParam(string s, int start, int end)
        {
            if (start >= end) return -1;
            int n = 0;
            for (int i = start; i < end; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9') return -1;
                n = n * 10 + (c - '0');
            }
            return n;
        }

        public static Color Xterm256(int i)
        {
            if (i < 16) return Basic16[Mathf.Clamp(i, 0, 15)];

            if (i < 232)
            {
                // 6x6x6 color cube; levels are not linear.
                int n = i - 16;
                int r = n / 36, g = (n % 36) / 6, b = n % 6;
                return new Color(Level(r), Level(g), Level(b));
            }

            float v = (8 + (i - 232) * 10) / 255f; // 24-step grey ramp
            return new Color(v, v, v);
        }

        static float Level(int c) => c == 0 ? 0f : (55 + c * 40) / 255f;

        // ------------------------------------------------------------------ links

        // Scan characters rather than SGR runs, because a plain URL may cross colors. The
        // screen-wide overload also joins only physical row edges; a blank tail still breaks.
        static void Autolink(List<SgrRun> runs)
        {
            if (runs.Count == 0) return;

            int width = 0;
            foreach (var r in runs) width = Mathf.Max(width, r.Col + r.Text.Length);
            if (width <= 0) return;

            var chars = new char[width];
            for (int i = 0; i < width; i++) chars[i] = ' ';
            foreach (var r in runs)
                for (int k = 0; k < r.Text.Length; k++)
                {
                    int c = r.Col + k;
                    if (c >= 0 && c < width) chars[c] = r.Text[k];
                }

            var spans = UrlScan.FindUrls(new string(chars));
            if (spans != null) Split(runs, spans);
        }

        static void Autolink(List<SgrRun>[] rows, int cols)
        {
            if (rows.Length == 0) return;

            int width = cols;
            if (width <= 0)
                foreach (var row in rows) width = Mathf.Max(width, RowWidth(row));
            if (width <= 0) return;

            var chars = new char[rows.Length * width];
            for (int i = 0; i < chars.Length; i++) chars[i] = ' ';
            for (int row = 0; row < rows.Length; row++)
            {
                var text = RowText(rows[row], width);
                text.CopyTo(0, chars, row * width, width);
            }

            var global = UrlScan.FindUrls(new string(chars));
            if (global == null) return;

            var local = new List<UrlSpan>[rows.Length];
            foreach (var span in global)
            {
                int first = span.Start / width;
                int last = (span.End - 1) / width;
                for (int row = first; row <= last && row < rows.Length; row++)
                {
                    int start = row == first ? span.Start % width : 0;
                    int end = row == last ? (span.End - 1) % width + 1 : width;
                    if (local[row] == null) local[row] = new List<UrlSpan>();
                    local[row].Add(new UrlSpan(start, end, span.Url));
                }
            }

            for (int row = 0; row < rows.Length; row++)
                if (local[row] != null) Split(rows[row], local[row]);
        }

        static int RowWidth(List<SgrRun> row)
        {
            int width = 0;
            foreach (var run in row) width = Mathf.Max(width, run.Col + run.Text.Length);
            return width;
        }

        static string RowText(List<SgrRun> row, int width)
        {
            var chars = new char[width];
            for (int i = 0; i < width; i++) chars[i] = ' ';
            foreach (var run in row)
                for (int k = 0; k < run.Text.Length; k++)
                {
                    int col = run.Col + k;
                    if (col >= 0 && col < width) chars[col] = run.Text[k];
                }
            return new string(chars);
        }

        // Runs are cut where the spans cross them, so a link that starts mid-word or ends
        // mid-color keeps every one of the colors it was drawn in. A run the app already
        // linked is left alone: what it says beats what the text looks like.
        static void Split(List<SgrRun> runs, List<UrlSpan> spans)
        {
            var cut = new List<SgrRun>(runs.Count + spans.Count * 2);
            foreach (var r in runs)
            {
                int start = r.Col, end = r.Col + r.Text.Length;
                if (r.Url != null || end <= start) { cut.Add(r); continue; }

                int at = start;
                foreach (var span in spans)
                {
                    if (span.End <= at || span.Start >= end) continue;
                    int a = Mathf.Max(span.Start, at);
                    int b = Mathf.Min(span.End, end);
                    if (a > at) cut.Add(Slice(r, at, a, null));
                    cut.Add(Slice(r, a, b, span.Url));
                    at = b;
                }
                if (at < end) cut.Add(Slice(r, at, end, null));
            }

            runs.Clear();
            runs.AddRange(cut);
        }

        static SgrRun Slice(SgrRun r, int from, int to, string url) => new SgrRun
        {
            Text = r.Text.Substring(from - r.Col, to - from),
            Col = from,
            Fg = r.Fg,
            Bg = r.Bg,
            HasBg = r.HasBg,
            Bold = r.Bold,
            Url = url,
        };
    }
}
