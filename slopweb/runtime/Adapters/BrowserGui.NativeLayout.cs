// Plain labels retain their cheap baked path. Wrapped and rich spans use the
// installed layout and share exactly the same glyph emission and clipping.
using System.Reflection;
using UnityEngine;

namespace BrowserAdapters;

public sealed partial class BrowserGui
{
    sealed class UnpreparedTextException : Exception { }

    BrowserNativeLayout? nativeText;
    int nativeResolution = -1;

    static BrowserNativeLayout.Metrics NativeMetrics(int font, float size, FontStyle style)
    {
        if (!BrowserFontBank.TryFace(font, size, style, out var face))
            throw new UnpreparedTextException();
        return new(
            face.Size,
            face.LineAscent,
            face.FirstLine,
            face.Pitch,
            face.Spacing,
            c =>
            {
                if (face.Glyphs.TryGetValue(c, out var glyph))
                    return glyph.advanceX;
                BrowserFontBank.ObserveText(font, size, style, c.ToString());
                if (face.Glyphs.TryGetValue(c, out glyph))
                    return glyph.advanceX;
                throw new UnpreparedTextException();
            }
        );
    }

    bool NativeLayout(
        int font,
        float size,
        FontStyle style,
        string text,
        float width,
        Color color,
        bool rich,
        float scale,
        out BrowserNativeLayout.Result result
    )
    {
        result = null!;
        if (text.Any(char.IsSurrogate))
            return false;
        if (nativeResolution != BrowserFontBank.Instance.ResolutionVersion)
        {
            nativeText = null;
            nativeResolution = BrowserFontBank.Instance.ResolutionVersion;
        }
        if (nativeText == null)
        {
            if (!BrowserNativeLayout.Available)
                return false;
            nativeText = new BrowserNativeLayout(NativeMetrics);
        }
        try
        {
            result = nativeText.Layout(font, size, style, text, width, color, rich, scale);
            return true;
        }
        catch (TargetInvocationException e) when (e.GetBaseException() is UnpreparedTextException)
        {
            return false;
        }
    }

    bool DrawNativeText(
        Rect rect,
        string text,
        int font,
        float size,
        FontStyle style,
        TextAnchor anchor,
        bool wrap,
        Color color,
        Rect clip,
        bool rich
    )
    {
        float scale = matrix.m00;
        if (
            scale <= 0
            || matrix.m01 != 0
            || matrix.m10 != 0
            || Math.Abs(matrix.m11 - scale) > .001f
            || !NativeLayout(
                font,
                size,
                style,
                text,
                wrap ? rect.width : -1,
                color,
                rich,
                scale,
                out var layout
            )
        )
            return false;
        // Resolve every span before emitting: unsupported coverage must not leave
        // half a native label beneath the diagnostic fallback.
        var spans = new (
            BrowserFontBank.Face Face,
            BrowserTextCache<UnityStubs.Gui.TmpGlyphMetrics>.Line Line
        )[layout.Pieces.Length];
        for (int i = 0; i < spans.Length; i++)
        {
            var piece = layout.Pieces[i];
            if (piece.IsSpace)
                continue;
            if (!BrowserFontBank.TryFace(font, piece.Size, piece.Style, out var face))
                return false;
            var run = face.TextRuns.Get(text.Substring(piece.Start, piece.Length));
            if (run == null || run.Lines.Length != 1)
                return false;
            spans[i] = (face, run.Lines[0]);
        }
        int horizontal = (int)anchor % 3,
            vertical = (int)anchor / 3;
        float top = rect.y + (rect.height - layout.Height) * vertical / 2;
        bool fineClip = clip.width > 0;
        Rect screenClip = fineClip ? Bounds(clip) : default;
        var origin = Device(Vector2.zero);
        for (int row = 0; row < layout.Lines.Length; row++)
        {
            var line = layout.Lines[row];
            float y = top + line.Top,
                x = rect.x + (rect.width - line.Width) * horizontal / 2;
            float bottom = float.MaxValue;
            if (fineClip && y + line.FirstLine > clip.yMax + .5f)
            {
                if (row > 0)
                    continue;
                bottom = screenClip.yMax;
            }
            for (int i = line.PieceStart; i < line.PieceStart + line.PieceCount; i++)
            {
                var piece = layout.Pieces[i];
                if (piece.IsSpace)
                    continue;
                var span = spans[i];
                float penX = Snap(origin.x) + Snap(x * scale) + Snap(piece.X * scale);
                float penY =
                    Snap(origin.y)
                    + Snap(y * scale)
                    + Snap(line.Ascent * scale)
                    - span.Face.Ascent * scale;
                bool uploaded = false;
                DrawGlyphRun(
                    span.Face,
                    span.Line,
                    penX,
                    penY,
                    scale,
                    origin,
                    piece.Color,
                    fineClip,
                    screenClip,
                    bottom,
                    ref uploaded
                );
            }
        }
        return true;
    }
}
