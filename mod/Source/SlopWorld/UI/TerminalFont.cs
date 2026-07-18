using UnityEngine;

namespace SlopWorld
{
    /// <summary>
    /// RimWorld's own fonts are proportional, which a terminal cannot use. We ask
    /// the OS for a monospace face instead; under Wine these resolve against both
    /// the prefix's fonts and the host's.
    /// </summary>
    public static class TerminalFont
    {
        static readonly string[] Candidates =
        {
            "DejaVu Sans Mono",
            "JetBrains Mono",
            "Liberation Mono",
            "Noto Sans Mono",
            "Consolas",
            "Courier New",
            "Monospace",
        };

        static GUIStyle _style;
        static int _size;

        public static float CellW { get; private set; }
        public static float CellH { get; private set; }

        public static void Invalidate() => _style = null;

        public static GUIStyle Style
        {
            get
            {
                if (_style != null && _size == Settings.FontSize) return _style;

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

                return _style;
            }
        }
    }
}
