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
    }

    // The daemon's emulator already did the hard part, so we only ever see SGR colour
    // escapes and CHA column markers.
    public static class Sgr
    {
        public static readonly Color DefaultFg = new Color(0.83f, 0.85f, 0.86f);
        public static readonly Color DefaultBg = new Color(0.04f, 0.05f, 0.06f);

        static readonly Color[] Basic16 =
        {
            new Color(0.14f, 0.15f, 0.16f), // black
            new Color(0.80f, 0.29f, 0.31f), // red
            new Color(0.45f, 0.71f, 0.36f), // green
            new Color(0.85f, 0.68f, 0.33f), // yellow
            new Color(0.35f, 0.58f, 0.83f), // blue
            new Color(0.69f, 0.45f, 0.78f), // magenta
            new Color(0.35f, 0.72f, 0.72f), // cyan
            new Color(0.78f, 0.80f, 0.81f), // white
            new Color(0.35f, 0.37f, 0.39f), // bright black
            new Color(0.92f, 0.44f, 0.45f),
            new Color(0.58f, 0.83f, 0.47f),
            new Color(0.94f, 0.80f, 0.45f),
            new Color(0.48f, 0.70f, 0.93f),
            new Color(0.80f, 0.58f, 0.89f),
            new Color(0.47f, 0.84f, 0.84f),
            new Color(0.95f, 0.96f, 0.97f),
        };

        struct Attr
        {
            public Color Fg;
            public Color Bg;
            public bool HasBg;
            public bool Bold;
            public bool Reverse;
        }

        static Attr Fresh() => new Attr { Fg = DefaultFg, Bg = DefaultBg, HasBg = false };

        public static List<SgrRun> ParseLine(string line)
        {
            var runs = new List<SgrRun>();
            if (string.IsNullOrEmpty(line)) return runs;

            var attr = Fresh();
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
                        Flush(runs, sb, attr, runStart);
                        Apply(line.Substring(i + 2, j - i - 2), ref attr);
                    }
                    else if (j < line.Length && line[j] == 'G')
                    {
                        // CHA: jump the pen to an absolute (1-based) column.
                        Flush(runs, sb, attr, runStart);
                        int.TryParse(line.Substring(i + 2, j - i - 2), out int n);
                        penCol = Mathf.Max(0, n - 1);
                        runStart = penCol;
                    }
                    i = j + 1;
                    continue;
                }

                if (sb.Length == 0) runStart = penCol;
                sb.Append(line[i]);
                penCol++;
                i++;
            }

            Flush(runs, sb, attr, runStart);
            return runs;
        }

        static void Flush(List<SgrRun> runs, StringBuilder sb, Attr a, int col)
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

            // A run is flushed on every escape and most escapes change nothing a viewer can
            // see - a TUI re-states attributes constantly. What decides whether two runs
            // are one is the drawn colours, not the codes behind them, and never across a
            // column jump: a CHA is the one thing that says the pen moved.
            int last = runs.Count - 1;
            if (last >= 0)
            {
                var prev = runs[last];
                if (prev.Col + prev.Text.Length == col &&
                    prev.Fg == fg && prev.HasBg == hasBg && (!hasBg || prev.Bg == bg))
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
                    case 22: a.Bold = false; break;
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
    }
}
