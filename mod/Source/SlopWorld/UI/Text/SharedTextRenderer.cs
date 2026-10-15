using UnityEngine;

namespace SlopWorld
{
    // Both layout policies paint through here. Only ordinary text reaches Unity's font
    // system. Catalog glyphs use the packaged atlas and retain their size if it is missing.
    static class SharedTextRenderer
    {
        static readonly TextAtlas Atlas = new TextAtlas("SlopWorld/TerminalEmoji");

        // Bounds are the caller's actual clipping rectangle. Glyph overhang never changes
        // advance. UI passes its label box, terminal runs remain inside the pane's clip.
        public static void DrawTerminalRun(
            InlineTextLayout layout,
            Rect bounds,
            float lineHeight,
            GUIStyle style
        )
        {
            if (bounds.width <= 0f || bounds.height <= 0f)
                return;
            // A single font span can use the label's own clipping rectangle. This avoids
            // an IMGUI group for each scalar separated by terminal column markers. Keep
            // mixed text/sprite runs on the grouped path so every span retains its clip.
            if (
                layout.Spans.Length == 1
                && layout.Spans[0].Sprite < 0
                && layout.Spans[0].X == 0f
                && layout.Spans[0].Width == layout.Width
            )
            {
                bounds.width = layout.Width;
                DrawFontCell(layout.Spans[0].Text, bounds, lineHeight, style);
                return;
            }
            Draw(layout, bounds, lineHeight, style, overhang: 0f, terminal: true);
        }

        public static void Draw(
            InlineTextLayout layout,
            Rect bounds,
            float lineHeight,
            GUIStyle style,
            float offsetX = 0f,
            float overhang = 1f,
            bool terminal = false
        )
        {
            if (bounds.width <= 0f || bounds.height <= 0f)
                return;
            GUI.BeginGroup(bounds);
            try
            {
                foreach (var span in layout.Spans)
                {
                    float x = offsetX + span.X;
                    if (x >= bounds.width || x + span.Width + overhang <= 0f)
                        continue;
                    if (span.Sprite >= 0)
                    {
                        var atlas = Atlas.Texture;
                        if (atlas != null)
                        {
                            float side = Mathf.Min(span.Width, lineHeight);
                            var rect = new Rect(
                                x + (span.Width - side) / 2f,
                                (lineHeight - side) / 2f,
                                side,
                                side
                            );
                            int row = span.Sprite / TextSpriteData.AtlasColumns;
                            int column = span.Sprite % TextSpriteData.AtlasColumns;
                            var uv = new Rect(
                                column / (float)TextSpriteData.AtlasColumns,
                                (TextSpriteData.AtlasRows - row - 1)
                                    / (float)TextSpriteData.AtlasRows,
                                1f / TextSpriteData.AtlasColumns,
                                1f / TextSpriteData.AtlasRows
                            );
                            if (terminal)
                                TextSpriteGeometry.Fit(
                                    TextSpriteData.InkBounds[span.Sprite],
                                    span.Sprite,
                                    TextSpriteData.AtlasColumns,
                                    TextSpriteData.AtlasRows,
                                    TextSpriteData.CellSize,
                                    new Rect(x, 0f, span.Width, lineHeight),
                                    out rect,
                                    out uv
                                );
                            // Preserve caller opacity without tinting colored artwork.
                            TextAtlas.Draw(atlas, rect, uv, new Color(1f, 1f, 1f, GUI.color.a));
                            continue;
                        }
                    }

                    // Never ask an OS emoji font to replace missing packaged artwork.
                    string text = span.Sprite >= 0 ? "\ufffd" : span.Text;
                    if (terminal)
                    {
                        DrawFontCell(
                            text,
                            new Rect(x, 0f, span.Width, bounds.height),
                            lineHeight,
                            style
                        );
                        continue;
                    }
                    Prepare(text, style);
                    DrawLabel(new Rect(x, 0f, span.Width + overhang, bounds.height), text, style);
                }
            }
            finally
            {
                GUI.EndGroup();
            }
        }

        internal static void DrawLabel(Rect bounds, string text, GUIStyle style)
        {
            // The native label path forwards GUI tint, but ignores the style state's text color.
            // Apply the foreground here for both plain spans and text beside atlas sprites.
            Color tint = GUI.color;
            GUI.color = tint * style.normal.textColor;
            try
            {
                GUI.Label(bounds, text, style);
            }
            finally
            {
                GUI.color = tint;
            }
        }

        static void DrawFontCell(string text, Rect bounds, float lineHeight, GUIStyle style)
        {
            var clipping = style.clipping;
            int originalSize = style.fontSize;
            style.clipping = TextClipping.Clip;
            try
            {
                if (TextSymbolRenderer.TryDraw(text, bounds, lineHeight, style))
                    return;
                Prepare(text, style);
                if (text.Length > 0 && text[0] > 127)
                {
                    float width = style.CalcSize(new GUIContent(text)).x;
                    if (width > bounds.width)
                    {
                        int size = (int)TextSpriteGeometry.FontSize(style, lineHeight);
                        float baseline = TextSpriteGeometry.Baseline(style.font, size);
                        int fittedSize = Mathf.Max(1, (int)(size * bounds.width / width));
                        style.fontSize = fittedSize;
                        float offset = Mathf.Clamp(
                            baseline * (1f - fittedSize / (float)size),
                            0f,
                            bounds.height
                        );
                        bounds.y += offset;
                        bounds.height -= offset;
                        if (style.font != null)
                            style.font.RequestCharactersInTexture(
                                text,
                                fittedSize,
                                style.fontStyle
                            );
                    }
                }
                DrawLabel(bounds, text, style);
            }
            finally
            {
                style.fontSize = originalSize;
                style.clipping = clipping;
            }
        }

        static void Prepare(string text, GUIStyle style)
        {
            if (style.font == null)
                return;
            for (int i = 0; i + 1 < text.Length; i++)
            {
                if (!char.IsHighSurrogate(text[i]) || !char.IsLowSurrogate(text[i + 1]))
                    continue;
                int size = style.fontSize > 0 ? style.fontSize : style.font.fontSize;
                style.font.RequestCharactersInTexture(text, size, style.fontStyle);
                return;
            }
        }
    }
}
