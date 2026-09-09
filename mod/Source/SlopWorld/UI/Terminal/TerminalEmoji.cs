using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Legacy IMGUI cannot rasterize Noto Color Emoji's bitmap font tables. The atlas is baked
    // with Pango, then this small lookup keeps supplementary glyphs out of GUI.Label entirely.
    public static class TerminalEmoji
    {
        const string AtlasPath = "SlopWorld/TerminalEmoji";

        static Texture2D _atlas;
        static bool _looked;

        // Draw a supplementary-plane emoji at the same two-cell footprint as DrawRun's
        // surrogate-pair path. Return false when the atlas has no cell so the ordinary font
        // fallback still gets a chance for a codepoint added after the bake.
        public static bool TryDraw(string text, int offset,
            float x, float y, float cw, float ch, out int length)
        {
            length = 0;
            int codepoint;
            if (!CodePointAt(text, offset, out codepoint, out length)) return false;

            int slot = System.Array.BinarySearch(TerminalEmojiData.CodePoints, codepoint);
            if (slot < 0) { length = 0; return false; }

            var atlas = Texture;
            if (atlas == null) { length = 0; return false; }

            float side = Mathf.Min(cw * 2f, ch);
            float left = x + offset * cw + (cw * 2f - side) / 2f;
            float top = y + (ch - side) / 2f;
            int row = slot / TerminalEmojiData.AtlasColumns;
            int column = slot % TerminalEmojiData.AtlasColumns;
            var uv = new Rect(
                column / (float)TerminalEmojiData.AtlasColumns,
                (TerminalEmojiData.AtlasRows - row - 1) / (float)TerminalEmojiData.AtlasRows,
                1f / TerminalEmojiData.AtlasColumns,
                1f / TerminalEmojiData.AtlasRows);

            GUI.DrawTextureWithTexCoords(new Rect(left, top, side, side), atlas, uv);
            return true;
        }

        static Texture2D Texture
        {
            get
            {
                if (_looked) return _atlas;
                _looked = true;
                _atlas = ContentFinder<Texture2D>.Get(AtlasPath, false);
                if (_atlas == null)
                    Log.Warning("[SlopWorld] terminal emoji atlas is missing");
                return _atlas;
            }
        }

        static bool CodePointAt(string text, int offset, out int codepoint, out int length)
        {
            codepoint = 0;
            length = 0;
            if (string.IsNullOrEmpty(text) || offset < 0 || offset >= text.Length) return false;

            char first = text[offset];
            if (!char.IsHighSurrogate(first) || offset + 1 >= text.Length
                || !char.IsLowSurrogate(text[offset + 1]))
                return false;

            codepoint = 0x10000 + ((first - '\uD800') << 10) + (text[offset + 1] - '\uDC00');
            length = 2;
            return true;
        }
    }
}
