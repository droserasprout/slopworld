// Native baked plain-text metrics and rasterization. Rich layout remains owned by
// the installed text engine; unsupported runs use the diagnostic renderer.
using UnityEngine;
using UnityStubs.Gui;

namespace BrowserAdapters;

public sealed partial class BrowserGui
{
    bool BakedCaret(
        int index,
        float size,
        FontStyle style,
        string text,
        float wrapWidth,
        bool rich,
        out BrowserFontBank.Face face,
        out BrowserTextCache<TmpGlyphMetrics>.Line line
    )
    {
        line = null!;
        face = null!;
        if (
            matrix.m00 <= 0
            || matrix.m01 != 0
            || matrix.m10 != 0
            || Math.Abs(matrix.m11 - matrix.m00) > .001f
            || text.Contains('\r')
            || text.Contains('\n')
            || !BakedFace(index, size, style, text, rich, out face, out var run)
            || (
                wrapWidth > 0 && run.Measure(static glyph => glyph.advanceX, matrix.m00) > wrapWidth
            )
        )
            return false;
        line = run.Lines[0];
        return true;
    }

    static bool BakedFace(
        int index,
        float size,
        FontStyle style,
        string text,
        bool rich,
        out BrowserFontBank.Face face,
        out BrowserTextCache<TmpGlyphMetrics>.Run run
    )
    {
        face = null!;
        run = null!;
        if (
            style != FontStyle.Normal
            || (rich && BrowserNativeText.HasMarkup(text))
            || !BrowserFontBank.TryFace(index, size, out face)
        )
            return false;
        run = face.TextRuns.Get(text)!;
        return run != null;
    }

    bool DrawBakedText(
        Rect rect,
        string text,
        int index,
        float size,
        FontStyle style,
        TextAnchor anchor,
        bool wrap,
        Color color,
        Rect clip,
        bool rich
    )
    {
        if (!BakedFace(index, size, style, text, rich, out var face, out var run))
            return false;
        float scale = Math.Abs(matrix.m00);
        if (
            matrix.m01 != 0
            || matrix.m10 != 0
            || matrix.m00 <= 0
            || Math.Abs(matrix.m11 - scale) > .001f
        )
            return false;
        // The bank follows viewport density. Exact prepared densities use original
        // glyph pixels; other densities resample the nearest prepared atlas.
        var lines = run.Lines;
        if (wrap && run.Measure(static glyph => glyph.advanceX, scale) > rect.width)
            return false;
        float height = face.FirstLine + (lines.Length - 1) * face.Pitch;
        int horizontal = (int)anchor % 3,
            vertical = (int)anchor / 3;
        float y = rect.y + (rect.height - height) * vertical / 2;
        bool fineClip = clip.width > 0;
        Rect screenClip = fineClip ? Bounds(clip) : default;
        // The transform is constant throughout a label, including all glyphs in
        // each faded sidebar slice; no per-glyph inversion is needed.
        var inverse = matrix.inverse;
        var origin = Device(Vector2.zero);
        // This atlas cannot mutate during a synchronous label draw. Validate its
        // upload on the first visible glyph, rather than repeating weak-table,
        // version and pixel-buffer lookups for every glyph in the run.
        bool uploaded = false;
        for (int row = 0; row < lines.Length; row++, y += face.Pitch)
        {
            var line = lines[row];
            float clipBottom = float.MaxValue;
            if (fineClip && y + face.FirstLine > clip.yMax + .5f)
            {
                if (row > 0)
                    continue;
                clipBottom = screenClip.yMax;
            }
            float x =
                rect.x
                + (rect.width - line.Measure(static glyph => glyph.advanceX, scale))
                    * horizontal
                    / 2;
            float penX = Snap(origin.x) + Snap(x * scale);
            float penY =
                Snap(origin.y)
                + Snap(y * scale)
                + Snap(face.LineAscent * scale)
                - face.Ascent * scale;
            DrawGlyphRun(
                face,
                line,
                penX,
                penY,
                scale,
                inverse,
                color,
                fineClip,
                screenClip,
                clipBottom,
                ref uploaded
            );
        }
        return true;
    }

    void DrawGlyphRun(
        BrowserFontBank.Face face,
        BrowserTextCache<TmpGlyphMetrics>.Line line,
        float penX,
        float penY,
        float scale,
        Matrix4x4 inverse,
        Color color,
        bool fineClip,
        Rect screenClip,
        float clipBottom,
        ref bool uploaded
    )
    {
        int textureId = face.Texture.GetInstanceID();
        foreach (var glyph in line.Glyphs)
        {
            if (glyph.visible)
            {
                float left = Snap(penX + glyph.x0 * scale),
                    top = Snap(penY + glyph.y0 * scale);
                var quad = new BrowserGlyphQuad(
                    left,
                    top,
                    left + (glyph.x1 - glyph.x0) * scale,
                    top + (glyph.y1 - glyph.y0) * scale,
                    glyph.u0,
                    glyph.v0,
                    glyph.u1,
                    glyph.v1
                );
                if (!fineClip || quad.Clip(screenClip.xMin, screenClip.xMax, clipBottom))
                {
                    var position =
                        (Vector2)inverse.MultiplyPoint3x4(new Vector2(quad.Left, quad.Top))
                        - offset;
                    var destination = new Rect(
                        position.x,
                        position.y,
                        (quad.Right - quad.Left) / scale,
                        (quad.Bottom - quad.Top) / scale
                    );
                    TraceTextureDraw();
                    if (!OutsideDrawingClip(destination))
                    {
                        if (!uploaded)
                        {
                            UploadImage(face.Texture);
                            uploaded = true;
                        }
                        AppendImageBatch(
                            commands,
                            textureId,
                            ColorChannel(color.r),
                            ColorChannel(color.g),
                            ColorChannel(color.b),
                            ColorChannel(color.a),
                            [
                                destination.x,
                                destination.y,
                                destination.width,
                                destination.height,
                                quad.U0,
                                1 - quad.V1,
                                quad.U1 - quad.U0,
                                quad.V1 - quad.V0,
                            ]
                        );
                    }
                }
            }
            penX += Snap(glyph.advanceX * scale);
        }
    }

    static float Snap(float value) => MathF.Floor(value + .5f);
}
