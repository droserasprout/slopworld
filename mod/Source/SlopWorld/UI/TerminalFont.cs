using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// RimWorld's own fonts are proportional, which a terminal cannot use. We ask
    /// the OS for a monospace face instead; under Wine these resolve against both
    /// the prefix's fonts and the host's.
    /// </summary>
    public static class TerminalFont
    {
        /// The first installed name becomes the face; the rest are per-glyph
        /// fallbacks. The symbol faces at the tail are there for that alone - mono
        /// faces stop well short of the arrows, draughts pieces and prompt chevrons
        /// TUIs decorate with, and Claude Code's whole UI is built out of them.
        static readonly string[] Candidates =
        {
            "DejaVu Sans Mono",
            "JetBrains Mono",
            "Noto Sans Mono",
            "Adwaita Mono",
            "Liberation Mono",
            "Consolas",
            "Courier New",
            "Monospace",
            "Noto Sans Symbols 2",
            "Noto Sans Symbols",
            "Symbola",
            "DejaVu Sans",
            "Unifont",
        };

        static GUIStyle _style;
        static int _size;

        // Per-char verdicts from CalcSize, which is far too slow to run per frame.
        static readonly Dictionary<char, bool> _fits = new Dictionary<char, bool>();

        public static float CellW { get; private set; }
        public static float CellH { get; private set; }

        public static void Invalidate()
        {
            _style = null;
            _fits.Clear();
        }

        public static GUIStyle Style
        {
            get
            {
                if (_style != null && _size == Settings.FontSize) return _style;
                _fits.Clear();

                _size = Mathf.Clamp(Settings.FontSize, 8, 28);
                var font = Font.CreateDynamicFontFromOSFont(Candidates, _size)
                           ?? Font.CreateDynamicFontFromOSFont("Courier New", _size);

                _style = new GUIStyle
                {
                    font = font,
                    fontSize = _size,
                    richText = false, // terminal output is full of < and >
                    wordWrap = false,
                    clipping = TextClipping.Overflow,
                    alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                };

                // Measure a run of identical glyphs: on a monospace face the advance
                // is exact, and this dodges dynamic-font atlas warm-up entirely.
                const string probe = "MMMMMMMMMMMMMMMMMMMM";
                CellW = _style.CalcSize(new GUIContent(probe)).x / probe.Length;
                CellH = Mathf.Max(_style.lineHeight, _size + 2f);

                Log.Message($"[SlopWorld] terminal font: {font?.name} at {_size}pt, " +
                            $"cell {CellW:0.##}x{CellH:0.##}");
                return _style;
            }
        }

        /// <summary>Whether a char advances exactly one cell. ASCII always does;
        /// a symbol pulled from a fallback face carries that face's own advance,
        /// and a glyph nobody has advances not at all - either would drag the rest
        /// of the row off the grid, so the caller draws those one at a time.</summary>
        public static bool FitsCell(char c)
        {
            if (c >= ' ' && c <= '~') return true;

            var style = Style;
            bool fits;
            if (_fits.TryGetValue(c, out fits)) return fits;

            float w = style.CalcSize(new GUIContent(c.ToString())).x;
            fits = Mathf.Abs(w - CellW) < 0.5f;
            _fits[c] = fits;
            return fits;
        }
    }
}
