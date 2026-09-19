using System;

namespace SlopWorld
{
    // UI text cannot depend on Unity's color-emoji font support. Measure the literal text
    // around the atlas-backed glyphs, reserving one line-height square for each emoji.
    internal static class UiEmoji
    {
        public static bool HasSupported(string text) => TerminalEmoji.HasSupportedEmoji(text);

        public static float Measure(string text, float emojiSize, Func<string, float> plainWidth)
        {
            if (!HasSupported(text)) return plainWidth(text ?? "");

            float width = 0f;
            int from = 0;
            for (int i = 0; i < text.Length; i++)
            {
                int length;
                if (!TerminalEmoji.IsSupportedAt(text, i, out length)) continue;

                if (i > from) width += plainWidth(text.Substring(from, i - from));
                width += emojiSize;
                i += length - 1;
                from = i + 1;
            }

            if (from < text.Length) width += plainWidth(text.Substring(from));
            return width;
        }
    }
}
