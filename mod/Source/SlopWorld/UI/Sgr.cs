using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

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

    // The daemon's emulator already did the hard part, so we only ever see SGR colour
    // escapes, CHA column markers and the OSC 8 links it passes on.
    public static class Sgr
    {
        // The palette is the scheme's; these two are read all over the window as "what
        // the pane is when nothing has said otherwise".
        public static Color DefaultFg => TerminalTheme.Current.Fg;
        public static Color DefaultBg => TerminalTheme.Current.Bg;

        static Color[] Basic16 => TerminalTheme.Current.Ansi;

        // How much of a faint run's own colour survives. Low enough that a completion hint
        // reads as a hint next to the line it is offered under, high enough that it is still
        // that colour rather than a grey - agents state 2 over an ANSI colour as often as over
        // the default foreground.
        const float FaintMix = 0.55f;

        struct Attr
        {
            public Color Fg;
            public Color Bg;
            public bool HasBg;
            public bool Bold;
            // SGR 2. A colour rather than a weight: the cell keeps the foreground it was
            // given and the terminal is what decides how far towards the background it is
            // drawn. Which is why it cannot be resolved in the daemon - see Flush.
            public bool Faint;
            public bool Reverse;
        }

        static Attr Fresh() => new Attr { Fg = DefaultFg, Bg = DefaultBg, HasBg = false };

        public static List<SgrRun> ParseLine(string line)
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
                        Apply(line.Substring(i + 2, j - i - 2), ref attr);
                    }
                    else if (j < line.Length && line[j] == 'G')
                    {
                        // CHA: jump the pen to an absolute (1-based) column.
                        Flush(runs, sb, attr, runStart, url);
                        int.TryParse(line.Substring(i + 2, j - i - 2), out int n);
                        penCol = Mathf.Max(0, n - 1);
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
                    url = Osc(line.Substring(i + 2, Mathf.Min(j, line.Length) - (i + 2))) ?? url;
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
            Autolink(runs);
            return runs;
        }

        // The empty URI is how OSC 8 closes a link, so "no link from here" and "this OSC
        // was about something else" have to be different answers: the first is an empty
        // string, the second null, which leaves the caller's link standing.
        static string Osc(string body)
        {
            if (body == null || !body.StartsWith("8;")) return null;
            var parts = body.Split(new[] { ';' }, 3);
            return parts.Length < 3 ? "" : parts[2];
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
            // are one is the drawn colours, not the codes behind them, and never across a
            // column jump: a CHA is the one thing that says the pen moved.
            int last = runs.Count - 1;
            if (last >= 0)
            {
                var prev = runs[last];
                if (prev.Col + prev.Text.Length == col &&
                    prev.Fg == fg && prev.HasBg == hasBg && (!hasBg || prev.Bg == bg) &&
                    prev.Url == url)
                {
                    prev.Text += sb.ToString();
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

        static void Apply(string body, ref Attr a)
        {
            if (body.Length == 0) { a = Fresh(); return; }

            var parts = body.Split(';');
            for (int k = 0; k < parts.Length; k++)
            {
                if (!int.TryParse(parts[k], out int n)) continue;

                switch (n)
                {
                    case 0: a = Fresh(); break;
                    case 1: a.Bold = true; break;
                    case 2: a.Faint = true; break;
                    // 22 is "normal intensity", which is both of them at once.
                    case 22: a.Bold = false; a.Faint = false; break;
                    case 7: a.Reverse = true; break;
                    case 27: a.Reverse = false; break;

                    case 39: a.Fg = DefaultFg; break;
                    case 49: a.HasBg = false; a.Bg = DefaultBg; break;

                    // 38/48 take an argument list: 5;<n> for 256-colour, 2;r;g;b for truecolour.
                    case 38:
                    case 48:
                    {
                        bool fg = n == 38;
                        if (k + 1 >= parts.Length) break;
                        int.TryParse(parts[k + 1], out int mode);

                        if (mode == 5 && k + 2 < parts.Length)
                        {
                            int.TryParse(parts[k + 2], out int idx);
                            var c = Xterm256(idx);
                            if (fg) a.Fg = c; else { a.Bg = c; a.HasBg = true; }
                            k += 2;
                        }
                        else if (mode == 2 && k + 4 < parts.Length)
                        {
                            int.TryParse(parts[k + 2], out int r);
                            int.TryParse(parts[k + 3], out int g);
                            int.TryParse(parts[k + 4], out int b);
                            var c = new Color(r / 255f, g / 255f, b / 255f);
                            if (fg) a.Fg = c; else { a.Bg = c; a.HasBg = true; }
                            k += 4;
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

        public static Color Xterm256(int i)
        {
            if (i < 16) return Basic16[Mathf.Clamp(i, 0, 15)];

            if (i < 232)
            {
                // 6x6x6 colour cube; levels are not linear.
                int n = i - 16;
                int r = n / 36, g = (n % 36) / 6, b = n % 6;
                return new Color(Level(r), Level(g), Level(b));
            }

            float v = (8 + (i - 232) * 10) / 255f; // 24-step grey ramp
            return new Color(v, v, v);
        }

        static float Level(int c) => c == 0 ? 0f : (55 + c * 40) / 255f;

        // ------------------------------------------------------------------ links

        // Scan each row as characters, not SGR runs, because a plain URL may cross runs.
        // Detection is row-local; wrapped links remain separate because the daemon marks no wrap.
        static void Autolink(List<SgrRun> runs)
        {
            if (runs.Count == 0) return;

            int width = 0;
            foreach (var r in runs) width = Mathf.Max(width, r.Col + r.Text.Length);
            if (width <= 0) return;

            // Fast path: skip full-row scan if no run contains "://".
            bool hasProtocol = false;
            foreach (var r in runs)
                if (r.Url == null && r.Text.IndexOf("://", StringComparison.Ordinal) >= 0)
                { hasProtocol = true; break; }
            if (!hasProtocol) return;

            var chars = new char[width];
            for (int i = 0; i < width; i++) chars[i] = ' ';
            foreach (var r in runs)
                for (int k = 0; k < r.Text.Length; k++)
                {
                    int c = r.Col + k;
                    if (c >= 0 && c < width) chars[c] = r.Text[k];
                }

            string text = new string(chars);
            List<Vector2Int> spans = null;
            int at = 0;
            while (at < text.Length)
            {
                int sep = text.IndexOf("://", at, StringComparison.Ordinal);
                if (sep < 0) break;

                int start = sep;
                while (start > 0 && IsScheme(text[start - 1])) start--;
                string scheme = text.Substring(start, sep - start).ToLowerInvariant();
                if (scheme != "http" && scheme != "https") { at = sep + 3; continue; }

                int end = sep + 3;
                while (end < text.Length && IsUrl(text[end])) end++;
                end = TrimTail(text, sep + 3, end);

                // "https://" and nothing after it is not a link, it is the word.
                if (end > sep + 3)
                {
                    if (spans == null) spans = new List<Vector2Int>();
                    spans.Add(new Vector2Int(start, end));
                }
                at = Mathf.Max(end, sep + 3);
            }

            if (spans == null) return;
            Split(runs, text, spans);
        }

        static bool IsScheme(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        // The printable ASCII a URL is allowed to be made of, less the brackets and
        // quotes text wraps them in.
        static bool IsUrl(char c)
        {
            if (c <= ' ' || c > '~') return false;
            switch (c)
            {
                case '<':
                case '>':
                case '"':
                case '`':
                case '{':
                case '}':
                case '|':
                case '\\':
                case '^':
                    return false;
                default:
                    return true;
            }
        }

        // A URL at the end of a sentence takes the full stop with it, and one in
        // parentheses takes the closing bracket. Both are the prose's, not the link's -
        // unless the link opened a bracket of its own, which is how a wiki URL reads.
        static int TrimTail(string text, int from, int end)
        {
            while (end > from)
            {
                char c = text[end - 1];
                if (c == '.' || c == ',' || c == ';' || c == ':' || c == '!' ||
                    c == '?' || c == '\'' || c == '*' || c == '_')
                {
                    end--;
                    continue;
                }
                if (c == ')' || c == ']')
                {
                    char open = c == ')' ? '(' : '[';
                    int depth = 0;
                    for (int i = from; i < end; i++)
                    {
                        if (text[i] == open) depth++;
                        else if (text[i] == c) depth--;
                    }
                    if (depth < 0) { end--; continue; }
                }
                break;
            }
            return end;
        }

        // Runs are cut where the spans cross them, so a link that starts mid-word or ends
        // mid-colour keeps every one of the colours it was drawn in. A run the app already
        // linked is left alone: what it says beats what the text looks like.
        static void Split(List<SgrRun> runs, string text, List<Vector2Int> spans)
        {
            var cut = new List<SgrRun>(runs.Count + spans.Count * 2);
            foreach (var r in runs)
            {
                int start = r.Col, end = r.Col + r.Text.Length;
                if (r.Url != null || end <= start) { cut.Add(r); continue; }

                int at = start;
                foreach (var span in spans)
                {
                    if (span.y <= at || span.x >= end) continue;
                    int a = Mathf.Max(span.x, at);
                    int b = Mathf.Min(span.y, end);
                    if (a > at) cut.Add(Slice(r, at, a, null));
                    cut.Add(Slice(r, a, b, text.Substring(span.x, span.y - span.x)));
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
