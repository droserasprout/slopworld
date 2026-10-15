using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Supplies a native monospace face and fixed terminal cell metrics.
    public static class TerminalFont
    {
        // The first installed candidate becomes the face. The renderer supplies glyph fallbacks.
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
        static Font _font;
        static bool _fontless; // no OS font at all, so a rebuild would not help
        static int _size;
        static string _name = "";
        static int _rev;

        static List<string> _mono;

        // Per-char verdicts from CalcSize, which is far too slow to run per frame.
        static readonly Dictionary<char, bool> _fits = new Dictionary<char, bool>();

        public static float CellW { get; private set; }
        public static float CellH { get; private set; }
        public static int Rev => _rev;

        // GUI text is rasterized after the UI matrix has scaled it. Therefore, a fractional logical
        // advance can become a different integer number of screen pixels from the one CalcSize
        // reports. Use the same pixel grid for the terminal cells. Otherwise the cursor drifts
        // against a long run of text at non-1x UI scales.
        public static float CellWAtScreenScale(float screenScale)
        {
            if (CellW <= 0.01f || screenScale <= 0.01f)
                return CellW;
            return Mathf.RoundToInt(CellW * screenScale) / screenScale;
        }

        public static void Invalidate()
        {
            _rev++;
            _style = null;
            _fontless = false;
            _fits.Clear();
        }

        public static GUIStyle Style
        {
            get
            {
                if (
                    _style != null
                    && _size == Settings.FontSize
                    && _name == Settings.FontName
                    && (_font != null || _fontless)
                )
                    return _style;
                _fits.Clear();

                _size = Mathf.Clamp(Settings.FontSize, 8, 28);
                _name = Settings.FontName;

                // Release the old wrapper after choosing its replacement. The native backend
                // retains and deduplicates registered faces for the process lifetime.
                var old = _font;
                _font =
                    NativeFonts.Create(Chain(_name), _size)
                    ?? NativeFonts.Create("Courier New", _size);
                _fontless = _font == null;
                if (old != null && old != _font)
                    UnityEngine.Object.Destroy(old);

                _style = new GUIStyle
                {
                    font = _font,
                    fontSize = _size,
                    richText = false, // terminal output is full of < and >
                    wordWrap = false,
                    clipping = TextClipping.Overflow,
                    alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                };

                // Measure a run of identical glyphs for an exact monospace advance.
                const string probe = "MMMMMMMMMMMMMMMMMMMM";
                CellW = _style.CalcSize(new GUIContent(probe)).x / probe.Length;
                CellH = Mathf.Max(_style.lineHeight, _size + 2f);

                Log.Message(
                    $"[SlopWorld] terminal font: {_font?.name} at {_size}pt, "
                        + $"cell {CellW:0.##}x{CellH:0.##}"
                );
                return _style;
            }
        }

        static string[] Chain(string name)
        {
            if (string.IsNullOrEmpty(name))
                return Candidates;
            var chain = new List<string> { name };
            foreach (var c in Candidates)
                if (c != name)
                    chain.Add(c);
            return chain.ToArray();
        }

        public static List<string> Mono
        {
            get
            {
                if (_mono == null)
                    _mono = ScanMono();
                return _mono;
            }
        }

        public static void Rescan()
        {
            NativeFonts.Rescan();
            _mono = null;
        }

        static List<string> ScanMono() => new List<string>(NativeFonts.Names(monospaced: true));

        // ASCII always does. A symbol from a fallback face carries that face's advance,
        // and a glyph nobody has advances not at all - either would drag the rest of the
        // row off the grid.
        public static bool FitsCell(char c)
        {
            if (c >= ' ' && c <= '~')
                return true;

            var style = Style;
            bool fits;
            if (_fits.TryGetValue(c, out fits))
                return fits;

            float w = style.CalcSize(new GUIContent(c.ToString())).x;
            fits = Mathf.Abs(w - CellW) < 0.5f;
            _fits[c] = fits;
            return fits;
        }
    }
}
