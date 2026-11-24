// Editable text queries share the same layout choice and device-rounded advances.
using UnityEngine;
using UnityStubs.Gui;

namespace BrowserAdapters;

public sealed partial class BrowserGui
{
    CaretLayout ResolveCaret(
        int font,
        float size,
        FontStyle style,
        string text,
        float width,
        bool rich
    )
    {
        if (BakedCaret(font, size, style, text, width, rich, out var face, out var line))
            return new(text.Length, face.Pitch, Scale: matrix.m00, Baked: line);
        if (
            text.Length > 0
            && NativeLayout(
                font,
                size,
                style,
                text,
                width,
                Color.white,
                rich,
                matrix.m00,
                out var layout
            )
        )
            return new(text.Length, 0, Native: nativeText, Lines: layout.Lines);
        return new(text.Length, LineSpacing(font, size), (size > 0 ? size : 16) * .6f);
    }

    readonly record struct CaretLayout(
        int Length,
        float Pitch,
        float Advance = 0,
        float Scale = 1,
        BrowserTextCache<TmpGlyphMetrics>.Line? Baked = null,
        BrowserNativeLayout? Native = null,
        BrowserNativeLayout.Line[]? Lines = null
    )
    {
        public int IndexAt(Vector2 point) =>
            Baked?.IndexAt(point.x, static glyph => glyph.advanceX, Scale)
            ?? Native?.IndexAt(point)
            ?? Math.Clamp((int)Math.Round(point.x / Advance), 0, Length);

        public Vector2 PositionOf(int index, out float height)
        {
            if (Native != null)
            {
                height = Lines![Native.LineOf(index)].Pitch;
                return Native.PositionOf(index);
            }
            height = Pitch;
            float x =
                Baked == null
                    ? index * Advance
                    : Baked.Carets(static glyph => glyph.advanceX, Scale)[
                        Math.Clamp(index, 0, Length)
                    ];
            return new Vector2(x, 0);
        }

        public void Select(int from, int to, List<Rect> rects)
        {
            if (Native == null)
            {
                if (Baked != null && Math.Clamp(from, 0, Length) == Math.Clamp(to, 0, Length))
                    return;
                var left = PositionOf(from, out var height).x;
                var right = PositionOf(to, out _).x;
                rects.Add(new Rect(left, 0, right - left, height));
                return;
            }
            var start = Native.PositionOf(from);
            var end = Native.PositionOf(to);
            int first = Native.LineOf(from),
                last = Native.LineOf(to);
            for (int row = first; row <= last && row < Lines!.Length; row++)
            {
                var current = Lines[row];
                float left = row == first ? start.x : 0;
                float right = row == last ? end.x : MathF.Max(current.Width, 4);
                rects.Add(new Rect(left, current.Top, MathF.Max(0, right - left), current.Pitch));
            }
        }
    }

    public int TextIndexAt(
        int fontIndex,
        float fontSize,
        FontStyle fontStyle,
        string text,
        float wrapWidth,
        Vector2 local,
        bool richText = true
    ) => ResolveCaret(fontIndex, fontSize, fontStyle, text, wrapWidth, richText).IndexAt(local);

    public Vector2 TextCursorPos(
        int fontIndex,
        float fontSize,
        FontStyle fontStyle,
        string text,
        float wrapWidth,
        int index,
        out float lineHeight,
        bool richText = true
    ) =>
        ResolveCaret(fontIndex, fontSize, fontStyle, text, wrapWidth, richText)
            .PositionOf(index, out lineHeight);

    public void TextSelectionRects(
        int fontIndex,
        float fontSize,
        FontStyle fontStyle,
        string text,
        float wrapWidth,
        int from,
        int to,
        List<Rect> rects,
        bool richText = true
    )
    {
        rects.Clear();
        if (!string.IsNullOrEmpty(text) && to > from)
            ResolveCaret(fontIndex, fontSize, fontStyle, text, wrapWidth, richText)
                .Select(from, to, rects);
    }
}
