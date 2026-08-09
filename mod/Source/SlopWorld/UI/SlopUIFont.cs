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

        static bool _applied;

        public static bool Applied => _applied;

        // Replaces the built-in fonts with the user's chosen OS font in every
        // Text font-style array. Called from WriteSettings and from the bootstrap.
        public static void Apply()
        {
            int size = Settings.UIFontSize;
            string name = Settings.UIFontName;

            if (size < 0) size = 0;
            if (size > 48) size = 48;

            // Build the font. Empty name = auto-detect from Candidates.
            Font font;
            if (string.IsNullOrEmpty(name))
            {
                font = Font.CreateDynamicFontFromOSFont(Candidates, size > 0 ? size : 13)
                       ?? Font.CreateDynamicFontFromOSFont("Arial", size > 0 ? size : 13);
            }
            else
            {
                font = Font.CreateDynamicFontFromOSFont(name, size > 0 ? size : 13);
            }

            if (font == null)
            {
                Log.Warning("[SlopWorld] UI font: could not create font " +
                            (name.NullOrEmpty() ? "(auto)" : name));
                _applied = false;
                return;
            }
            font.hideFlags = HideFlags.DontUnloadUnusedAsset;

            ApplyFont(font, size);
            _applied = true;
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

            // Update private lineHeights and spaceBetweenLines arrays via reflection.
            // These are used by Text.LineHeight / LineHeightOf / SpaceBetweenLines.
            //
            // We measure each tier at its own size using GUIStyle.lineHeight, which
            // includes the font's inter-line spacing (ascent + descent + leading).
            // This is the same metric TerminalFont uses for its cell height:
            //   CellH = Mathf.Max(_style.lineHeight, _size + 2f)
            // We floor at (size + 6) for extra headroom across diverse font faces.
            try
            {
                var lhField = typeof(Text).GetField("lineHeights",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var slField = typeof(Text).GetField("spaceBetweenLines",
                    BindingFlags.NonPublic | BindingFlags.Static);

                if (lhField != null)
                {
                    var arr = (float[])lhField.GetValue(null);
                    if (arr != null && arr.Length >= 3)
                        for (int i = 0; i < 3; i++)
                            arr[i] = LineHeight(font, size > 0 ? size : DefaultSizes[i]);
                }

                if (slField != null)
                {
                    var arr = (float[])slField.GetValue(null);
                    if (arr != null && arr.Length >= 3)
                        for (int i = 0; i < 3; i++)
                            arr[i] = LineHeight(font, size > 0 ? size : DefaultSizes[i]) + 2f;
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

        static void ApplyToStyles(GUIStyle[] styles, Font font, int size)
        {
            if (styles == null) return;
            for (int i = 0; i < styles.Length && i < 3; i++)
            {
                styles[i].font = font;
                if (size > 0) styles[i].fontSize = size;
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
