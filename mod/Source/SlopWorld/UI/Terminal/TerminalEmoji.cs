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

        // Draw an emoji within the daemon's occupied cells. Return false when the atlas
        // has no cell so the font fallback can handle codepoints added after the bake.
        public static bool TryDraw(string text, int offset,
            float x, float y, float cw, float ch, int columns, out int length)
        {
            length = 0;
            int slot;
            if (!TryGetSlot(text, offset, out slot, out length)) return false;

            var atlas = Texture;
            if (atlas == null) { length = 0; return false; }

            float side = Mathf.Min(cw * columns, ch);
            float left = x + (cw * columns - side) / 2f;
            float top = y + (ch - side) / 2f;
            DrawSlot(slot, new Rect(left, top, side, side), atlas);
            return true;
        }

        // UI labels use a proportional layout rather than terminal cells. Keep the same
        // baked artwork, but let the caller give the emoji its one-square inline footprint.
        public static bool TryDrawInline(string text, int offset,
            float x, float y, float size, out int length)
        {
            length = 0;
            int slot;
            if (!TryGetSlot(text, offset, out slot, out length)) return false;

            var atlas = Texture;
            if (atlas == null) { length = 0; return false; }
            DrawSlot(slot, new Rect(x, y, size, size), atlas);
            return true;
        }

        public static bool IsSupportedAt(string text, int offset, out int length)
        {
            int slot;
            return TryGetSlot(text, offset, out slot, out length);
        }

        public static bool HasSupportedEmoji(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                int length;
                if (IsSupportedAt(text, i, out length)) return true;
            }
            return false;
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

        static bool TryGetSlot(string text, int offset, out int slot, out int length)
        {
            slot = -1;
            length = 0;
            int codepoint;
            if (!CodePointAt(text, offset, out codepoint, out length)) return false;

            slot = System.Array.BinarySearch(TerminalEmojiData.CodePoints, codepoint);
            if (slot < 0)
            {
                length = 0;
                return false;
            }
            return true;
        }

        static void DrawSlot(int slot, Rect rect, Texture2D atlas)
        {
            int row = slot / TerminalEmojiData.AtlasColumns;
            int column = slot % TerminalEmojiData.AtlasColumns;
            var uv = new Rect(
                column / (float)TerminalEmojiData.AtlasColumns,
                (TerminalEmojiData.AtlasRows - row - 1) / (float)TerminalEmojiData.AtlasRows,
                1f / TerminalEmojiData.AtlasColumns,
                1f / TerminalEmojiData.AtlasRows);
            GUI.DrawTextureWithTexCoords(rect, atlas, uv);
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
