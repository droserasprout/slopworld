using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SlopWorld
{
    // The daemon's emulator already did the hard part. Therefore, we only ever see SGR color
    // escapes, CHA column markers, private cell-width markers and OSC 8 links.
    public static class Sgr
    {
        // The active scheme supplies the default pane foreground and background colors.
        public static Color DefaultFg => TerminalTheme.Current.Fg;
        public static Color DefaultBg => TerminalTheme.Current.Bg;

        static Color[] Basic16 => TerminalTheme.Current.Ansi;

        // Set the retained color strength for faint text.
        // Completion hints must appear dimmer than adjacent text but must retain their ANSI color.
        // Agents apply SGR 2 to ANSI colors and to the default foreground.
        const float FaintMix = 0.55f;

        struct Attr
        {
            public Color Fg;
            public Color Bg;
            public bool HasBg;
            public bool Bold;
            // SGR 2. A color rather than a weight. The cell keeps the foreground it was given and
            // the terminal is what decides how far towards the background it is drawn. Which is why
            // it cannot be resolved in the daemon - see Flush.
            public bool Faint;
            public bool Reverse;
        }

        static Attr Fresh() => new Attr { Fg = DefaultFg, Bg = DefaultBg, HasBg = false };

        public static List<SgrRun> ParseLine(string line)
        {
            var runs = ParseLineCore(line);
            TerminalAutolinks.Autolink(runs);
            return runs;
        }

        // The terminal row cache stores this stage separately from screen-wide URL
        // decoration. It includes explicit OSC 8 metadata, but never guesses a link from text.
        internal static List<SgrRun> ParseLineBase(string line) => ParseLineCore(line);

        // Parse the screen as a group so a plain URL that wraps at the terminal edge can keep
        // the same link across rows. OSC 8 links already carry their own URL on each row.
        public static List<SgrRun>[] ParseLines(string[] lines, int cols)
        {
            var parsed = new List<SgrRun>[lines.Length];
            for (int i = 0; i < lines.Length; i++)
                parsed[i] = ParseLineCore(lines[i]);

            if (TerminalAutolinks.MayContainLink(lines)) TerminalAutolinks.Autolink(parsed, cols);
            return parsed;
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
            // Column the pen is at. Where the next appended char lands.
            int penCol = 0;
            // Column the run currently in `sb` began at.
            int runStart = 0;
            bool printableAsciiOnly = true;

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
                        int next = n > 0 ? n - 1 : 0;
                        if (next == penCol + 1 && runs.Count > 0)
                        {
                            var last = runs[runs.Count - 1];
                            if (last.Col + last.Columns == penCol)
                            {
                                int count = TerminalColumns.ScalarCount(last.Text);
                                if (count > 1 && !last.IsCluster)
                                {
                                    int tailAt = TerminalColumns.TextOffset(last.Text, count - 1);
                                    var tail = last;
                                    tail.Text = last.Text.Substring(tailAt);
                                    tail.Col = penCol - 1;
                                    tail.CellWidth = 2;
                                    last.Text = last.Text.Substring(0, tailAt);
                                    last.CellWidth = count - 1;
                                    runs[runs.Count - 1] = last;
                                    runs.Add(tail);
                                }
                                else
                                {
                                    last.CellWidth = last.Columns + 1;
                                    runs[runs.Count - 1] = last;
                                }
                            }
                        }
                        penCol = next;
                        runStart = penCol;
                    }
                    else if (j < line.Length && line[j] == 'z' &&
                             TryReadCluster(line, i + 2, j, out string cluster,
                                            out int width, out int nextOffset))
                    {
                        Flush(runs, sb, attr, runStart, url);
                        sb.Append(cluster);
                        printableAsciiOnly = false;
                        Flush(runs, sb, attr, penCol, url, false);
                        var last = runs[runs.Count - 1];
                        last.CellWidth = width;
                        last.IsCluster = true;
                        runs[runs.Count - 1] = last;
                        penCol += width;
                        runStart = penCol;
                        i = nextOffset;
                        continue;
                    }
                    i = j + 1;
                    continue;
                }

                // The wire uses OSC only for the OSC 8 hyperlink that the application supplied.
                // The daemon forwards the hyperlink without resolving it.
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
                if (line[i] < ' ' || line[i] > '~') printableAsciiOnly = false;
                int units = TerminalColumns.ScalarUnits(line, i);
                sb.Append(line, i, units);
                penCol++;
                i += units;
            }

            Flush(runs, sb, attr, runStart, url);
            // Keycaps require non-ASCII components; plain rows cannot form them.
            if (!printableAsciiOnly || TextSpriteCatalog.Shared.HasPrintableAsciiOnlyKey)
                TerminalSpriteSequences.Join(runs, TextSpriteCatalog.Shared);
            return runs;
        }

        // Decode the private cell marker atomically: malformed or incomplete
        // payloads leave the caller's text and pen position untouched.
        static bool TryReadCluster(string line, int start, int end, out string text,
                                   out int width, out int nextOffset)
        {
            text = null;
            width = 0;
            nextOffset = end + 1;
            int separator = line.IndexOf(';', start, end - start);
            if (separator <= start ||
                !int.TryParse(line.Substring(start, separator - start), out int scalars) ||
                !int.TryParse(line.Substring(separator + 1, end - separator - 1), out int columns) ||
                // Combining marks have no fixed count limit. Each scalar needs
                // at least one remaining UTF-16 unit; parsing checks completeness.
                scalars < 1 || scalars > line.Length - end - 1 || columns < 1 || columns > 32)
                return false;

            int at = end + 1;
            for (int n = 0; n < scalars; n++)
            {
                if (at >= line.Length || line[at] == '\x1b') return false;
                at += TerminalColumns.ScalarUnits(line, at);
            }
            text = line.Substring(end + 1, at - end - 1);
            width = columns;
            nextOffset = at;
            return true;
        }

        static void Flush(List<SgrRun> runs, StringBuilder sb, Attr a, int col, string url,
                          bool merge = true)
        {
            if (sb.Length == 0) return;

            // Reverse video swaps the pair. The cursor and selections rely on it.
            var fg = a.Reverse ? (a.HasBg ? a.Bg : DefaultBg) : a.Fg;
            var bg = a.Reverse ? a.Fg : a.Bg;
            bool hasBg = a.Reverse || a.HasBg;

            if (a.Bold && !a.Reverse)
                fg = new Color(Mathf.Min(1f, fg.r * 1.25f),
                               Mathf.Min(1f, fg.g * 1.25f),
                               Mathf.Min(1f, fg.b * 1.25f));

            // Towards the background rather than towards black. On a light scheme a faint run
            // scaled down is *darker* than the ordinary text it is meant to recede behind. Skipped
            // under reverse for the reason bold is - the pair has been swapped, and what would be
            // dimmed there is the fill.
            if (a.Faint && !a.Reverse)
                fg = Color.Lerp(hasBg ? bg : DefaultBg, fg, FaintMix);

            if (string.IsNullOrEmpty(url)) url = null;

            // A run is flushed on every escape and most escapes change nothing a viewer can see - a
            // TUI re-states attributes constantly. What decides whether two runs are one is the
            // drawn colors, not the codes behind them, and never across a column jump. A CHA is the
            // one thing that says the pen moved.
            int last = runs.Count - 1;
            if (merge && last >= 0)
            {
                var prev = runs[last];
                if (!prev.IsCluster && prev.Col + prev.Columns == col &&
                    prev.Columns == TerminalColumns.ScalarCount(prev.Text) &&
                    prev.Fg == fg && prev.HasBg == hasBg && (!hasBg || prev.Bg == bg) &&
                    prev.Url == url)
                {
                    sb.Insert(0, prev.Text);
                    prev.Text = sb.ToString();
                    prev.CellWidth = TerminalColumns.ScalarCount(prev.Text);
                    runs[last] = prev;
                    sb.Length = 0;
                    return;
                }
            }

            string text = sb.ToString();
            runs.Add(new SgrRun
            {
                Text = text,
                Col = col,
                CellWidth = TerminalColumns.ScalarCount(text),
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
                // 6x6x6 color cube. Levels are not linear.
                int n = i - 16;
                int r = n / 36, g = (n % 36) / 6, b = n % 6;
                return new Color(Level(r), Level(g), Level(b));
            }

            float v = (8 + (i - 232) * 10) / 255f; // 24-step grey ramp
            return new Color(v, v, v);
        }

        static float Level(int c) => c == 0 ? 0f : (55 + c * 40) / 255f;

    }
}
