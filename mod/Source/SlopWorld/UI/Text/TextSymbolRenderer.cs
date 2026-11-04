using System;
using UnityEngine;

namespace SlopWorld
{
    // Monochrome fallback for missing text-default glyphs. Prefer the terminal
    // font for supported BMP symbols; legacy Unity cannot query whole supplementary scalars.
    // This fallback owns no column widths and is never used for an emoji presentation sequence.
    static class TextSymbolRenderer
    {
        static readonly TextAtlas Atlas = new TextAtlas("SlopWorld/TerminalSymbols");

        internal static int Slot(string text)
        {
            if (string.IsNullOrEmpty(text))
                return -1;
            int length = char.IsSurrogatePair(text, 0) ? 2 : 1;
            if (text.Length != length && (text.Length != length + 1 || text[length] != '\ufe0e'))
                return -1;
            if (length == 1 && char.IsSurrogate(text[0]))
                return -1;
            return Array.BinarySearch(TextSymbolData.Codepoints, char.ConvertToUtf32(text, 0));
        }

        internal static Rect GlyphRect(Rect source, Rect bounds, float size, float baseline)
        {
            float scale = Mathf.Min(
                size / TextSymbolData.RasterSize,
                Mathf.Min(bounds.width / source.width, bounds.height / source.height)
            );
            float width = source.width * scale;
            float height = source.height * scale;
            // Keep font bearings and baseline where they fit; shift overflowing ink
            // into its cells without distorting it or changing terminal advance.
            return new Rect(
                Mathf.Clamp(bounds.x + source.x * scale, bounds.x, bounds.x + bounds.width - width),
                Mathf.Clamp(
                    bounds.y + baseline + source.y * scale,
                    bounds.y,
                    bounds.y + bounds.height - height
                ),
                width,
                height
            );
        }

        static bool HasVisibleNativeGlyph(string text, GUIStyle style)
        {
            var font = style.font;
            if (font == null || char.IsSurrogate(text[0]) || !font.HasCharacter(text[0]))
                return false;
            int size = style.fontSize > 0 ? style.fontSize : font.fontSize;
            font.RequestCharactersInTexture(text, size, style.fontStyle);
            // A coverage claim is not proof that Unity loaded usable fallback ink.
            // Query the exact size/style after preparing the dynamic font atlas.
            return font.GetCharacterInfo(text[0], out var info, size, style.fontStyle)
                && info.maxX > info.minX
                && info.maxY > info.minY;
        }

        public static bool TryDraw(string text, Rect bounds, float lineHeight, GUIStyle style)
        {
            int slot = Slot(text);
            if (slot < 0)
                return false;
            // Font.HasCharacter takes a UTF-16 char, so never query a surrogate
            // half as evidence that the native path supports a supplementary glyph.
            if (HasVisibleNativeGlyph(text, style))
                return false;
            var atlas = Atlas.Texture;
            if (atlas == null)
            {
                GUI.Label(bounds, "\ufffd", style);
                return true;
            }
            bounds.height = Mathf.Min(bounds.height, lineHeight);
            var uv = TextSpriteGeometry.Uv(
                TextSymbolData.InkBounds[slot],
                slot,
                TextSymbolData.AtlasColumns,
                TextSymbolData.AtlasRows,
                TextSymbolData.CellSize
            );
            float size = TextSpriteGeometry.FontSize(style, lineHeight);
            float baseline = TextSpriteGeometry.Baseline(style.font, size);
            int at = slot * 4;
            var source = new Rect(
                TextSymbolData.FontBounds[at],
                TextSymbolData.FontBounds[at + 1],
                TextSymbolData.FontBounds[at + 2],
                TextSymbolData.FontBounds[at + 3]
            );
            var rect = GlyphRect(source, bounds, size, baseline);
            Color color = GUI.color;
            Color ink = style.normal.textColor;
            TextAtlas.Draw(
                atlas,
                rect,
                uv,
                new Color(color.r * ink.r, color.g * ink.g, color.b * ink.b, color.a * ink.a)
            );
            return true;
        }
    }
}
