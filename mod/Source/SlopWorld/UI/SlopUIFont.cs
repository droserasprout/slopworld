using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Manages a custom UI font replacing RimWorld's built-in GameFont faces across all
    // three tiers (Tiny/Small/Medium). Modifies Text.fontStyles in-place — since that
    // array is public — and the private lineHeights/spaceBetweenLines arrays via
    // reflection. Every Widgets.Label, Text.CalcSize and Text.LineHeightOf call
    // immediately reads the new font; no per-call-site changes needed anywhere.
    //
    // The font is baked from Font.CreateDynamicFontFromOSFont, exactly the way
    // TerminalFont works. An empty font name falls through to the Candidates list;
    // size 0 means "keep the existing per-tier sizes and only change the face".
    //
    // Line-height values are derived from GUIStyle.lineHeight (which reports the
    // font's inter-line spacing including leading), floored at (size + 6) so the
    // rendered text always has room for descenders regardless of the font face.
    public static class SlopUIFont
    {
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
        // when the user only picks a face.
        static readonly int[] DefaultSizes = { 11, 13, 15 };

        // The font in the styles now, and what was asked for to get it. Held because the
        // size slider calls Apply on every step it passes through: a dynamic font is an
        // asset with a texture atlas behind it, and this one is marked never to unload, so
        // building a fresh one per step and dropping the last would pin the whole drag.
        static Font _font;
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

            // The face is baked at a size, so a size change is a new font as much as a
            // face change is. Size 0 means "leave the per-tier sizes alone", and the face
            // is baked at 13 for it - dynamic fonts rasterise per request anyway.
            int bake = size > 0 ? size : 13;

            if (_font != null && _fontName == name && _fontSize == size)
            {
                // Same font, but the styles may have been left at another tier's size by
                // an earlier call, so the application itself still has to happen.
                ApplyFont(_font, size);
                return;
            }

            // Build the font. Empty name = auto-detect from Candidates.
            Font font = name.Length == 0
                ? Font.CreateDynamicFontFromOSFont(Candidates, bake)
                  ?? Font.CreateDynamicFontFromOSFont("Arial", bake)
                : Font.CreateDynamicFontFromOSFont(name, bake);

            if (font == null)
            {
                Log.Warning("[SlopWorld] UI font: could not create font " +
                            (name.NullOrEmpty() ? "(auto)" : name));
                return;
            }
            font.hideFlags = HideFlags.DontUnloadUnusedAsset;

            var old = _font;
            _font = font;
            _fontName = name;
            _fontSize = size;

            ApplyFont(font, size);

            // Only once nothing points at it any more: the styles were still holding it
            // until the line above.
            if (old != null && old != font) UnityEngine.Object.Destroy(old);
        }

        // Applies the given Font object to all public Text style arrays and updates
        // the private line-height caches.
        static void ApplyFont(Font font, int size)
        {
            // Replace the font face (and optionally size) in every style array.
            ApplyToStyles(Text.fontStyles, font, size);
            ApplyToStyles(Text.textFieldStyles, font, size);
            ApplyToStyles(Text.textAreaStyles, font, size);
            ApplyToStyles(Text.textAreaReadOnlyStyles, font, size);

            // Text.LineHeight reads this private cache. Measure each tier from
            // GUIStyle.lineHeight and apply LineHeight's cross-font floor.
            // `spaceBetweenLines` stays unchanged: it is extra leading, derived from style
            // padding, not the line height. Setting it to a full line height spaces every
            // multi-line gizmo label by another twenty-odd pixels.
            try
            {
                var lhField = typeof(Text).GetField("lineHeights",
                    BindingFlags.NonPublic | BindingFlags.Static);

                if (lhField != null)
                {
                    var arr = (float[])lhField.GetValue(null);
                    if (arr != null && arr.Length >= 3)
                        for (int i = 0; i < 3; i++)
                            arr[i] = LineHeight(font, size > 0 ? size : DefaultSizes[i]);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[SlopWorld] UI font: couldn't update line heights: {e.Message}");
            }
        }

        // Computes a generous line height for the given font at the given size.
        // Uses GUIStyle.lineHeight (the actual inter-line spacing in Unity's layout)
        // floored at size × 1.6 so ascenders and descenders are never cropped even
        // on faces with tall glyphs (e.g. DejaVu Sans, Noto Sans).  This gives
        // values close to RimWorld's originals (18/22/26 for Tiny/Small/Medium)
        // which were designed for the bundled Arial face at 11/13/15 pt.
        static float LineHeight(Font font, int size)
        {
            var style = new GUIStyle { font = font, fontSize = size };
            return Mathf.Max(style.lineHeight, Mathf.Ceil(size * 1.6f));
        }

        // Size 0 is not "leave the size as it is" but "put the tier back to what RimWorld
        // shipped": the slider can be dragged up and then back down again, and a style left
        // at the size it was last given would keep 20pt text under a page saying 11/13/15.
        static void ApplyToStyles(GUIStyle[] styles, Font font, int size)
        {
            if (styles == null) return;
            for (int i = 0; i < styles.Length && i < 3; i++)
            {
                styles[i].font = font;
                styles[i].fontSize = size > 0 ? size : DefaultSizes[i];
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
