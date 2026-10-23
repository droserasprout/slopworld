using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // RimWorld's own fonts are proportional, which a terminal cannot use. Under Wine
    // an OS font resolves against both the prefix's fonts and the host's.
    public static class TerminalFont
    {
        // Family preference order; TerminalFontFallbacks inserts Nerd Font variants.
        // Symbol faces at the tail cover glyphs missing from monospace fonts.
        static readonly string[] Candidates =
        {
            "Fira Code",
            "JetBrains Mono",
            "Hack",
            "Noto Sans Mono",
            "Cascadia Mono",
            "Cascadia Code",
            "Source Code Pro",
            "Iosevka",
            "Menlo",
            "Ubuntu Mono",
            "DejaVu Sans Mono",
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
        static bool _textureRebuildHooked;

        const int ProbeSize = 30;
        static List<string> _mono;
        static int _catalogRevision = -1;

        // Per-char verdicts from CalcSize, which is far too slow to run per frame.
        static readonly Dictionary<char, bool> _fits = new Dictionary<char, bool>();

        public static float CellW { get; private set; }
        public static float CellH { get; private set; }
        public static int Rev
        {
            get
            {
                SyncCatalog();
                return _rev;
            }
        }
        public static string PreferredName { get; private set; } = "";

        // Invalidate texture caches through Rev even before Style is read.
        // Defer monospace probing until Mono is read.
        static void SyncCatalog()
        {
            if (_catalogRevision == InstalledFonts.Revision)
                return;
            _catalogRevision = InstalledFonts.Revision;
            _mono = null;
            Invalidate();
        }

        // Snap cell advances to rasterized screen pixels to prevent cursor drift at fractional UI scales.
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
                SyncCatalog();
                HookTextureRebuilds();
                // GUIStyle does not keep its font alive through Unity asset unloading.
                // Destroyed fonts compare equal to null and must be recreated.
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

                // Destroy the previous font after replacement; otherwise each size-slider
                // step retains a DontUnloadUnusedAsset atlas.
                var old = _font;
                var chain = Chain(_name);
                PreferredName = FontSelection.FirstInstalled(chain, InstalledFonts.All);
                _font =
                    Font.CreateDynamicFontFromOSFont(chain, _size)
                    ?? Font.CreateDynamicFontFromOSFont("Courier New", _size);
                _fontless = _font == null;
                if (_font != null)
                    _font.hideFlags = HideFlags.DontUnloadUnusedAsset;
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

                // Measure a run of identical glyphs: on a monospace face the advance is exact,
                // and this dodges dynamic-font atlas warm-up entirely.
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

        // Atlas rebuilds must invalidate cached pane textures or newly loaded glyphs stay blank.
        static void HookTextureRebuilds()
        {
            if (_textureRebuildHooked)
                return;
            Font.textureRebuilt += OnTextureRebuilt;
            _textureRebuildHooked = true;
        }

        static void OnTextureRebuilt(Font font)
        {
            _rev++;
            _fits.Clear();
        }

        static string[] Chain(string name) =>
            TerminalFontFallbacks.Build(Candidates, InstalledFonts.All, name);

        public static List<string> Mono
        {
            get
            {
                SyncCatalog();
                if (_mono == null)
                    _mono = ScanMono();
                return _mono;
            }
        }

        static List<string> ScanMono()
        {
            var found = new List<string>();
            var names = InstalledFonts.All;

            var probe = new GUIStyle
            {
                fontSize = ProbeSize,
                richText = false,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };

            foreach (var name in names)
            {
                Font f = null;
                try
                {
                    f = Font.CreateDynamicFontFromOSFont(name, ProbeSize);
                    if (f == null)
                        continue;
                    probe.font = f;
                    float narrow = probe.CalcSize(new GUIContent("iiiiiiiiii")).x;
                    float wide = probe.CalcSize(new GUIContent("MMMMMMMMMM")).x;
                    if (narrow > 0f && Mathf.Abs(narrow - wide) < 1f)
                        found.Add(name);
                }
                catch { }
                finally
                {
                    if (f != null)
                        UnityEngine.Object.DestroyImmediate(f);
                }
            }

            found.Sort(System.StringComparer.OrdinalIgnoreCase);
            Log.Message($"[SlopWorld] mono faces: {found.Count} of {names.Count}");
            return found;
        }

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
