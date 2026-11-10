using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Supplies a native monospace face and fixed terminal cell metrics.
    public static class TerminalFont
    {
        static readonly TerminalFontFallbacks Fallbacks = new TerminalFontFallbacks(
            FontconfigFonts.Families,
            message => Log.Warning("[SlopWorld] " + message)
        );

        static GUIStyle _style;
        static Font _font;
        static bool _fontless; // no OS font at all, so a rebuild would not help
        static int _size;
        static string _name = "";
        static int _rev;

        static List<string> _mono;
        static int _catalogRevision = -1;

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
        }

        public static GUIStyle Style
        {
            get
            {
                SyncCatalog();
                if (
                    _style != null
                    && _size == Settings.FontSize
                    && _name == Settings.FontName
                    && (_font != null || _fontless)
                )
                    return _style;
                _size = Mathf.Clamp(Settings.FontSize, 8, 28);
                _name = Settings.FontName;

                // Release the old wrapper after choosing its replacement. The native backend
                // retains and deduplicates registered faces for the process lifetime.
                var old = _font;
                var chain = Chain(_name);
                PreferredName = FontSelection.FirstInstalled(chain, InstalledFonts.All);
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

        static string[] Chain(string name) => Fallbacks.Get(name, InstalledFonts.Revision);

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

        public static void Rescan()
        {
            InstalledFonts.Rescan();
        }

        static List<string> ScanMono() => new List<string>(NativeFonts.Names(monospaced: true));
    }
}
