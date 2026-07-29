using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // RimWorld's own fonts are proportional, which a terminal cannot use. Under Wine
    // an OS font resolves against both the prefix's fonts and the host's.
    public static class TerminalFont
    {
        // The first installed name becomes the face; the rest are per-glyph fallbacks.
        // The symbol faces at the tail are there for that alone - mono faces stop well
        // short of the arrows and chevrons Claude Code's whole UI is built out of.
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

        const int ProbeSize = 30;
        static List<string> _mono;

        // Per-char verdicts from CalcSize, which is far too slow to run per frame.
        static readonly Dictionary<char, bool> _fits = new Dictionary<char, bool>();

        public static float CellW { get; private set; }
        public static float CellH { get; private set; }

        public static void Invalidate()
        {
            _style = null;
            _fontless = false;
            _fits.Clear();
        }

        public static GUIStyle Style
        {
            get
            {
                // A GUIStyle is not a UnityEngine.Object, so the font it holds is rooted by
                // nothing Unity can see: the unload RimWorld runs on every map switch destroys it
                // and leaves the style in the default proportional face. The null check catches a
                // face lost some other way, a destroyed Font comparing equal to null.
                if (_style != null && _size == Settings.FontSize && _name == Settings.FontName &&
                    (_font != null || _fontless))
                    return _style;
                _fits.Clear();

                _size = Mathf.Clamp(Settings.FontSize, 8, 28);
                _name = Settings.FontName;
                _font = Font.CreateDynamicFontFromOSFont(Chain(_name), _size)
                        ?? Font.CreateDynamicFontFromOSFont("Courier New", _size);
                _fontless = _font == null;
                if (_font != null) _font.hideFlags = HideFlags.DontUnloadUnusedAsset;

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

                // Measure a run of identical glyphs: on a monospace face the advance is exact,
                // and this dodges dynamic-font atlas warm-up entirely.
                const string probe = "MMMMMMMMMMMMMMMMMMMM";
                CellW = _style.CalcSize(new GUIContent(probe)).x / probe.Length;
                CellH = Mathf.Max(_style.lineHeight, _size + 2f);

                Log.Message($"[SlopWorld] terminal font: {_font?.name} at {_size}pt, " +
                            $"cell {CellW:0.##}x{CellH:0.##}");
                return _style;
            }
        }

        static string[] Chain(string name)
        {
            if (string.IsNullOrEmpty(name)) return Candidates;
            var chain = new List<string> { name };
            foreach (var c in Candidates)
                if (c != name) chain.Add(c);
            return chain.ToArray();
        }

        public static List<string> Mono
        {
            get
            {
                if (_mono == null) _mono = ScanMono();
                return _mono;
            }
        }

        public static void Rescan() => _mono = null;

        static List<string> ScanMono()
        {
            var found = new List<string>();
            string[] names;
            try { names = Font.GetOSInstalledFontNames(); }
            catch (System.Exception e) { Log.Warning($"[SlopWorld] font list: {e.Message}"); return found; }
            if (names == null) return found;

            var probe = new GUIStyle
            {
                fontSize = ProbeSize,
                richText = false,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };

            var seen = new HashSet<string>();
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;

                Font f = null;
                try
                {
                    f = Font.CreateDynamicFontFromOSFont(name, ProbeSize);
                    if (f == null) continue;
                    probe.font = f;
                    float narrow = probe.CalcSize(new GUIContent("iiiiiiiiii")).x;
                    float wide = probe.CalcSize(new GUIContent("MMMMMMMMMM")).x;
                    if (narrow > 0f && Mathf.Abs(narrow - wide) < 1f) found.Add(name);
                }
                catch { }
                finally
                {
                    if (f != null) UnityEngine.Object.DestroyImmediate(f);
                }
            }

            found.Sort(System.StringComparer.OrdinalIgnoreCase);
            Log.Message($"[SlopWorld] mono faces: {found.Count} of {names.Length}");
            return found;
        }

        // ASCII always does; a symbol from a fallback face carries that face's advance,
        // and a glyph nobody has advances not at all - either would drag the rest of the
        // row off the grid.
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
