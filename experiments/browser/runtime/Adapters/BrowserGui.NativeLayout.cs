// Plain labels retain their cheap baked path. Wrapped and rich spans use the
// installed layout and share exactly the same glyph emission and clipping.
using System.Reflection;
using UnityEngine;

namespace BrowserAdapters;

public sealed partial class BrowserGui
{
    public static string NativeTextProbe()
    {
        var gui = new BrowserGui { matrix = Matrix4x4.Scale(new Vector3(5f / 3, 5f / 3, 1)) };
        const string text = "<color=red>alpha beta</color> gamma delta";
        if (
            !gui.NativeLayout(
                1,
                0,
                FontStyle.Normal,
                text,
                90,
                Color.white,
                true,
                5f / 3,
                out var layout
            )
            || layout.Lines.Length < 2
        )
            throw new InvalidOperationException("Native wrapped text was not prepared");
        var measured = gui.MeasureText(1, 0, FontStyle.Normal, text, 90, true);
        if (measured.x != layout.Width || measured.y != layout.Height)
            throw new InvalidOperationException("Native measurement and drawing layout disagree");
        int secondStart = layout.Pieces[layout.Lines[1].PieceStart].Start;
        var caret = gui.TextCursorPos(1, 0, FontStyle.Normal, text, 90, secondStart, out var pitch);
        if (
            caret.y != layout.Lines[1].Top
            || pitch != layout.Lines[1].Pitch
            || gui.TextIndexAt(1, 0, FontStyle.Normal, text, 90, caret) != secondStart
        )
            throw new InvalidOperationException("Wrapped editing disagrees with native drawing");
        var selection = new List<Rect>();
        gui.TextSelectionRects(
            1,
            0,
            FontStyle.Normal,
            text,
            90,
            layout.Pieces[0].Start,
            text.Length,
            selection
        );
        if (selection.Count != layout.Lines.Length || selection[1].y != caret.y)
            throw new InvalidOperationException("Selection did not follow native wrapped lines");
        gui.DrawText(
            new Rect(5, 5, 90, 150),
            text,
            1,
            0,
            FontStyle.Normal,
            TextAnchor.UpperLeft,
            true,
            Color.white
        );
        var packet = gui.Drain();
        using var commands = System.Text.Json.JsonDocument.Parse(packet);
        if (
            commands
                .RootElement.EnumerateArray()
                .Any(c => c.GetProperty("kind").GetString() == "text")
        )
            throw new InvalidOperationException("Wrapped rich text used diagnostic Canvas text");
        return "{\"lines\":" + layout.Lines.Length + ",\"commands\":" + packet + "}";
    }

    sealed class UnpreparedTextException : Exception { }

    BrowserNativeLayout? nativeText;

    static BrowserNativeLayout.Metrics NativeMetrics(int font, float size, FontStyle style)
    {
        if (style != FontStyle.Normal || !BrowserFontBank.TryFace(font, size, out var face))
            throw new UnpreparedTextException();
        return new(
            face.Size,
            face.LineAscent,
            face.FirstLine,
            face.Pitch,
            face.Spacing,
            c =>
                face.Glyphs.TryGetValue(c, out var glyph)
                    ? glyph.advanceX
                    : throw new UnpreparedTextException()
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
            if (
                piece.Style != FontStyle.Normal
                || !BrowserFontBank.TryFace(font, piece.Size, out var face)
            )
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
        var inverse = matrix.inverse;
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
                    inverse,
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
