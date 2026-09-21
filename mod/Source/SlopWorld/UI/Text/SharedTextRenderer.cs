using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Both layout policies paint through here. Only ordinary text reaches Unity's font
    // system; catalog glyphs use the packaged atlas and retain their size if it is missing.
    static class SharedTextRenderer
    {
        const string AtlasPath = "SlopWorld/TerminalEmoji";
        static Texture2D _atlas;
        static bool _looked;

        static Texture2D Atlas
        {
            get
            {
                if (_looked) return _atlas;
                _looked = true;
                _atlas = ContentFinder<Texture2D>.Get(AtlasPath, false);
                if (_atlas == BaseContent.BadTex) _atlas = null;
                if (_atlas == null) Log.Warning("[SlopWorld] text sprite atlas is missing");
                return _atlas;
            }
        }

        // Bounds are the caller's actual clipping rectangle; glyph overhang never changes
        // advance. UI passes its label box, terminal runs remain inside the pane's clip.
        public static void Draw(InlineTextLayout layout, Rect bounds, float lineHeight,
                                GUIStyle style, float offsetX = 0f, float overhang = 1f)
        {
            if (bounds.width <= 0f || bounds.height <= 0f) return;
            GUI.BeginGroup(bounds);
            try
            {
                foreach (var span in layout.Spans)
                {
                    float x = offsetX + span.X;
                    if (x >= bounds.width || x + span.Width + overhang <= 0f) continue;
                    if (span.Sprite >= 0)
                    {
                        var atlas = Atlas;
                        if (atlas != null)
                        {
                            float side = Mathf.Min(span.Width, lineHeight);
                            var rect = new Rect(x + (span.Width - side) / 2f,
                                (lineHeight - side) / 2f, side, side);
                            int row = span.Sprite / TextSpriteData.AtlasColumns;
                            int column = span.Sprite % TextSpriteData.AtlasColumns;
                            var uv = new Rect(column / (float)TextSpriteData.AtlasColumns,
                                (TextSpriteData.AtlasRows - row - 1) / (float)TextSpriteData.AtlasRows,
                                1f / TextSpriteData.AtlasColumns, 1f / TextSpriteData.AtlasRows);
                            Color color = GUI.color;
                            try
                            {
                                // Preserve caller opacity, but do not tint colored artwork.
                                GUI.color = new Color(1f, 1f, 1f, color.a);
                                GUI.DrawTextureWithTexCoords(rect, atlas, uv);
                            }
                            finally { GUI.color = color; }
                            continue;
                        }
                    }

                    // Never ask an OS emoji font to replace missing packaged artwork.
                    string text = span.Sprite >= 0 ? "\ufffd" : span.Text;
                    Prepare(text, style);
                    GUI.Label(new Rect(x, 0f, span.Width + overhang, bounds.height), text, style);
                }
            }
            finally { GUI.EndGroup(); }
        }

        static void Prepare(string text, GUIStyle style)
        {
            if (style.font == null) return;
            for (int i = 0; i + 1 < text.Length; i++)
            {
                if (!char.IsHighSurrogate(text[i]) || !char.IsLowSurrogate(text[i + 1])) continue;
                int size = style.fontSize > 0 ? style.fontSize : style.font.fontSize;
                style.font.RequestCharactersInTexture(text, size, style.fontStyle);
                return;
            }
        }
    }
}
