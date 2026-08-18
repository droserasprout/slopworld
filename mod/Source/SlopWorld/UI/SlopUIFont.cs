using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Replaces Tiny/Small/Medium with dynamic fonts and matching private line metrics. Keep
    // `GUIStyle.fontSize` zero so measurement and drawing use each tier's native baked size.
    public static class SlopUIFont
    {
        // Unity's text generator can report a line one or two pixels shorter than the
        // dynamic font's actual ink. Keep that slack in the style so CalcSize/CalcHeight
        // callers (notably ActiveTip) allocate it too, and do not clip a glyph that lands
        // on the final pixel of an otherwise correctly measured rect.
        const int BottomSafety = 2;

        // Default proportional faces for the "Automatic" fallback chain. Listed in
        // preference order, so the first one installed on the system becomes the face.
        static readonly string[] Candidates =
        {
            "DejaVu Sans",
            "Noto Sans",
            "Liberation Sans",
            "Arial",
            "Segoe UI",
            "Helvetica",
            "sans-serif",
        };

        // RimWorld's built-in Tiny/Small/Medium sizes, kept so we can leave them alone
        // when the user only picks a face and preserve their offsets for custom sizes.
        static readonly int[] DefaultSizes = { 11, 13, 15 };

        // Keep the fonts alive while the styles reference their dynamic atlases.
        static Font[] _fonts;
        static string _fontName;
        static int _fontSize = -1;

        // Replaces the built-in fonts with the user's chosen OS font in every
        // Text font-style array. Called from WriteSettings and from the bootstrap.
        public static void Apply()
        {
            int size = Settings.UIFontSize;
            string name = Settings.UIFontName ?? "";

            if (size < 0) size = 0;
            if (size > 48) size = 48;

            if (_fonts != null && _fontName == name && _fontSize == size && AllAlive(_fonts))
            {
                // Reapply in case another caller changed the style arrays.
                ApplyFont(_fonts);
                return;
            }

            int[] sizes = new int[3];
            for (int i = 0; i < sizes.Length; i++)
            {
                // The setting is the Small tier's size. Keep Tiny and Medium two points
                // below and above it, as in RimWorld's built-in 11/13/15 ramp.
                sizes[i] = size > 0
                    ? Mathf.Max(1, size + DefaultSizes[i] - DefaultSizes[1])
                    : DefaultSizes[i];
            }

            // Build one native-size font per distinct tier size.
            var fonts = new Font[3];
            var made = new List<Font>();
            try
            {
                var bySize = new Dictionary<int, Font>();
                for (int i = 0; i < sizes.Length; i++)
                {
                    if (!bySize.TryGetValue(sizes[i], out var font))
                    {
                        font = Create(name, sizes[i]);
                        if (font == null)
                            throw new InvalidOperationException("could not create font at " +
                                sizes[i] + "pt");
                        font.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        bySize.Add(sizes[i], font);
                        made.Add(font);
                    }
                    fonts[i] = font;
                }
            }
            catch (Exception e)
            {
                DestroyUnique(made, null);
                Log.Warning("[SlopWorld] UI font: " + e.Message + " " +
                            (name.NullOrEmpty() ? "(auto)" : name));
                return;
            }

            var old = _fonts;
            _fonts = fonts;
            _fontName = name;
            _fontSize = size;

            ApplyFont(fonts);

            // Destroy old fonts only after all style arrays point at the replacements.
            DestroyUnique(old, fonts);
        }

        static Font Create(string name, int size) => name.Length == 0
            ? Font.CreateDynamicFontFromOSFont(Candidates, size)
              ?? Font.CreateDynamicFontFromOSFont("Arial", size)
            : Font.CreateDynamicFontFromOSFont(name, size);

        static bool AllAlive(Font[] fonts)
        {
            for (int i = 0; i < fonts.Length; i++)
                if (fonts[i] == null) return false;
            return true;
        }

        static void DestroyUnique(Font[] old, Font[] keep)
        {
            if (old == null) return;
            for (int i = 0; i < old.Length; i++)
            {
                var font = old[i];
                if (font == null || Contains(keep, font) || SeenBefore(old, i, font)) continue;
                UnityEngine.Object.Destroy(font);
            }
        }

        static void DestroyUnique(List<Font> fonts, Font[] keep)
        {
            for (int i = 0; i < fonts.Count; i++)
            {
                var font = fonts[i];
                if (font != null && !Contains(keep, font) && !SeenBefore(fonts, i, font))
                    UnityEngine.Object.Destroy(font);
            }
        }

        static bool Contains(Font[] fonts, Font font)
        {
            if (fonts == null) return false;
            for (int i = 0; i < fonts.Length; i++)
                if (fonts[i] == font) return true;
            return false;
        }

        static bool SeenBefore(Font[] fonts, int at, Font font)
        {
            for (int i = 0; i < at; i++)
                if (fonts[i] == font) return true;
            return false;
        }

        static bool SeenBefore(List<Font> fonts, int at, Font font)
        {
            for (int i = 0; i < at; i++)
                if (fonts[i] == font) return true;
            return false;
        }

        // Applies the given Font object to all public Text style arrays and updates
        // the private line-height caches.
        static void ApplyFont(Font[] fonts)
        {
            // Replace the font face (and optionally size) in every style array.
            ApplyToStyles(Text.fontStyles, fonts);
            ApplyToStyles(Text.textFieldStyles, fonts);
            ApplyToStyles(Text.textAreaStyles, fonts);
            ApplyToStyles(Text.textAreaReadOnlyStyles, fonts);

            // The bundled Small face uses a -1 content offset; it is not part of measurement,
            // so clear it when replacing that face. Entry styles retain their skin offsets.
            for (int i = 0; i < Text.fontStyles.Length && i < 3; i++)
                Text.fontStyles[i].contentOffset = Vector2.zero;

            // Text.LineHeight reads this cache; keep spaceBetweenLines as extra leading, not line height.
            try
            {
                var lhField = typeof(Text).GetField("lineHeights",
                    BindingFlags.NonPublic | BindingFlags.Static);

                if (lhField != null)
                {
                    var arr = (float[])lhField.GetValue(null);
                    if (arr != null && arr.Length >= 3)
                        for (int i = 0; i < 3; i++)
                            arr[i] = LineHeight(Text.fontStyles[i]);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[SlopWorld] UI font: couldn't update line heights: {e.Message}");
            }

        }

        // Probe both ascenders and descenders. Unity's line-height and CalcHeight values can
        // be a pixel shorter than a dynamic face's actual quads at some native sizes.
        static readonly GUIContent Metric = new GUIContent("\u00c5Wgjpqy");

        // Use the tallest layout or glyph bounds, including the style's padding. The latter is
        // what keeps the lower edge of a dynamic glyph inside the row it is laid out in.
        static float LineHeight(GUIStyle style)
        {
            bool wrap = style.wordWrap;
            try
            {
                style.wordWrap = false;
                float layout = Mathf.Max(style.lineHeight, style.CalcHeight(Metric, 10000f));
                return Mathf.Ceil(Mathf.Max(layout, InkHeight(style)));
            }
            finally
            {
                style.wordWrap = wrap;
            }
        }

        static float InkHeight(GUIStyle style)
        {
            var font = style.font;
            if (font == null) return 0f;

            int size = style.fontSize > 0 ? style.fontSize : font.fontSize;
            if (size <= 0) return 0f;

            try
            {
                font.RequestCharactersInTexture(Metric.text, size, style.fontStyle);
                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                for (int i = 0; i < Metric.text.Length; i++)
                {
                    CharacterInfo info;
                    if (!font.GetCharacterInfo(Metric.text[i], out info, size, style.fontStyle))
                        continue;
                    min = Mathf.Min(min, info.minY);
                    max = Mathf.Max(max, info.maxY);
                }
                if (!float.IsInfinity(min) && !float.IsInfinity(max))
                    return max - min + style.padding.vertical;
            }
            catch (Exception e)
            {
                Log.WarningOnce($"[SlopWorld] UI font: couldn't measure glyph bounds: {e.Message}",
                    0x51_09_12);
            }
            return 0f;
        }
        // Size 0 is not "leave the size as it is" but "put the tier back to what RimWorld
        // shipped": the slider can be dragged up and then back down again, and a style left
        // at the size it was last given would keep 20pt text under a page saying 11/13/15.
        static void ApplyToStyles(GUIStyle[] styles, Font[] fonts)
        {
            if (styles == null || fonts == null) return;
            for (int i = 0; i < styles.Length && i < 3; i++)
            {
                styles[i].font = fonts[i];
                // The font is already baked at the requested size.
                styles[i].fontSize = 0;

                // Keep one shared policy for labels, tooltips and vanilla controls. The
                // padding is idempotent because Apply() also runs while the font picker is
                // open; Overflow is what saves a final descender when a caller supplied a
                // rect measured before the dynamic atlas finished warming up.
                styles[i].clipping = TextClipping.Overflow;
                var p = styles[i].padding;
                if (p != null && p.bottom < BottomSafety)
                    styles[i].padding = new RectOffset(p.left, p.right, p.top, BottomSafety);
            }
        }

        // ---------------------------------------------------------------- font listing

        static List<string> _all;

        public static List<string> All
        {
            get
            {
                if (_all == null) _all = Scan();
                return _all;
            }
        }

        public static void Rescan() => _all = null;

        static List<string> Scan()
        {
            var found = new List<string>();
            try
            {
                var names = Font.GetOSInstalledFontNames();
                if (names == null) return found;
                var seen = new HashSet<string>();
                foreach (var name in names)
                    if (!string.IsNullOrEmpty(name) && seen.Add(name))
                        found.Add(name);
                found.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                Log.Warning($"[SlopWorld] UI font scan: {e.Message}");
            }
            return found;
        }
    }
}
