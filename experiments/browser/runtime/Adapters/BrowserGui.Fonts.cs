// Native baked plain-text metrics and rasterization. Rich layout remains owned by
// the installed text engine; unsupported runs use the diagnostic renderer.
using UnityEngine;
using UnityStubs.Gui;

namespace BrowserAdapters;

public sealed partial class BrowserGui
{
    // Real workspace comparisons have not established a frame-time benefit.
    // Keep projected-run reuse available for diagnostics, without default retention.
    static bool glyphCacheEnabled;

    // Diagnostic comparison switch; disabling retains the original emission path.
    public static bool ConfigureGlyphCache(bool enabled)
    {
        bool previous = glyphCacheEnabled;
        glyphCacheEnabled = enabled;
        return previous;
    }

    static bool cellFastPathEnabled = true;

    // Diagnostic reference path for byte-equivalence checks and CPU benchmarks.
    public static bool ConfigureCellFastPath(bool enabled)
    {
        bool previous = cellFastPathEnabled;
        cellFastPathEnabled = enabled;
        return previous;
    }

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
            (rich && BrowserNativeText.HasMarkup(text))
            || !BrowserFontBank.TryFace(index, size, style, out face)
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
        // Native terminal labels are usually one ASCII cell. Resolve its metrics
        // directly: no text-cache LRU mutation, line arrays, width cache or run loop.
        // Placement still starts from this label's independently rounded origin.
        if (
            cellFastPathEnabled
            && text.Length == 1
            && text[0] >= ' '
            && text[0] <= '~'
            && matrix.m01 == 0
            && matrix.m10 == 0
            && matrix.m00 > 0
            && Math.Abs(matrix.m11 - matrix.m00) <= .001f
            && BrowserFontBank.TryFace(index, size, style, out var cellFace)
            && cellFace.Glyphs.TryGetValue(text[0], out var cell)
            && (!wrap || Snap(cell.advanceX * matrix.m00) / matrix.m00 <= rect.width)
        )
        {
            DrawBakedCell(rect, cellFace, cell, anchor, color, clip);
            return true;
        }
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
        // Axis-aligned uniform transforms map device quads back with subtraction
        // and division. Avoid a full matrix inverse for every terminal cell.
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
                origin,
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
        Vector2 origin,
        Color color,
        bool fineClip,
        Rect screenClip,
        float clipBottom,
        ref bool uploaded
    )
    {
        int textureId = face.Texture.GetInstanceID();
        // Short runs retain direct emission instead of paying a full context lookup.
        var drawing =
            glyphCacheEnabled && line.Glyphs.Length >= 8
                ? line.Drawing ??= new BrowserGlyphRun()
                : null;
        if (drawing != null)
        {
            bool hasClip = drawingClips.TryPeek(out var drawingClip);
            // Include the full transform/offset used by OutsideDrawingClip, not
            // just the ideal device quad: its round-trip arithmetic is observable
            // at clip edges. Fine text clipping has a separate bottom-only rule.
            ReadOnlySpan<float> key = stackalloc float[]
            {
                penX,
                penY,
                scale,
                origin.x,
                origin.y,
                fineClip ? 1 : 0,
                screenClip.xMin,
                screenClip.xMax,
                clipBottom,
                hasClip ? 1 : 0,
                drawingClip.x,
                drawingClip.y,
                drawingClip.width,
                drawingClip.height,
                offset.x,
                offset.y,
                matrix.m00,
                matrix.m01,
                matrix.m02,
                matrix.m03,
                matrix.m10,
                matrix.m11,
                matrix.m12,
                matrix.m13,
                matrix.m20,
                matrix.m21,
                matrix.m22,
                matrix.m23,
                matrix.m30,
                matrix.m31,
                matrix.m32,
                matrix.m33,
            };
            if (drawing.Matches(key))
            {
                if (BrowserGuiInput.Profile)
                    for (int i = 0; i < drawing.TraceCount; i++)
                        TraceTextureDraw();
                EmitGlyphRun(drawing, face, textureId, color, ref uploaded);
                return;
            }
            drawing.Begin(key);
        }
        Span<float> values = stackalloc float[8];
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
                    var destination = new Rect(
                        (quad.Left - origin.x) / scale,
                        (quad.Top - origin.y) / scale,
                        (quad.Right - quad.Left) / scale,
                        (quad.Bottom - quad.Top) / scale
                    );
                    TraceTextureDraw();
                    if (drawing != null)
                        drawing.TraceCount++;
                    if (!OutsideDrawingClip(destination))
                    {
                        if (!uploaded)
                        {
                            UploadImage(face.Texture);
                            uploaded = true;
                        }
                        values[0] = destination.x;
                        values[1] = destination.y;
                        values[2] = destination.width;
                        values[3] = destination.height;
                        values[4] = quad.U0;
                        values[5] = 1 - quad.V1;
                        values[6] = quad.U1 - quad.U0;
                        values[7] = quad.V1 - quad.V0;
                        if (drawing != null)
                            drawing.Add(values);
                        else
                            AppendImageBatch(
                                commands,
                                textureId,
                                ColorChannel(color.r),
                                ColorChannel(color.g),
                                ColorChannel(color.b),
                                ColorChannel(color.a),
                                values
                            );
                    }
                }
            }
            penX += Snap(glyph.advanceX * scale);
        }
        if (drawing != null)
        {
            drawing.Complete();
            EmitGlyphRun(drawing, face, textureId, color, ref uploaded);
        }
    }

    void DrawBakedCell(
        Rect rect,
        BrowserFontBank.Face face,
        TmpGlyphMetrics glyph,
        TextAnchor anchor,
        Color color,
        Rect clip
    )
    {
        if (!glyph.visible)
            return;
        float scale = matrix.m00;
        int horizontal = (int)anchor % 3,
            vertical = (int)anchor / 3;
        float y = rect.y + (rect.height - face.FirstLine) * vertical / 2;
        float width = Snap(glyph.advanceX * scale) / scale;
        float x = rect.x + (rect.width - width) * horizontal / 2;
        var origin = Device(Vector2.zero);
        float penX = Snap(origin.x) + Snap(x * scale);
        float penY =
            Snap(origin.y) + Snap(y * scale) + Snap(face.LineAscent * scale) - face.Ascent * scale;
        bool fineClip = clip.width > 0;
        Rect screenClip = fineClip ? Bounds(clip) : default;
        float clipBottom =
            fineClip && y + face.FirstLine > clip.yMax + .5f ? screenClip.yMax : float.MaxValue;
        // Keep the general DrawGlyphRun loop inline too, avoiding an added call
        // per glyph on interpreted WASM. Packet equivalence tests guard the same
        // rounding/clipping contract in this specialized path.
        int textureId = face.Texture.GetInstanceID();
        Span<float> values = stackalloc float[8];
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
            var destination = new Rect(
                (quad.Left - origin.x) / scale,
                (quad.Top - origin.y) / scale,
                (quad.Right - quad.Left) / scale,
                (quad.Bottom - quad.Top) / scale
            );
            TraceTextureDraw();
            if (!OutsideDrawingClip(destination))
            {
                UploadImage(face.Texture);
                values[0] = destination.x;
                values[1] = destination.y;
                values[2] = destination.width;
                values[3] = destination.height;
                values[4] = quad.U0;
                values[5] = 1 - quad.V1;
                values[6] = quad.U1 - quad.U0;
                values[7] = quad.V1 - quad.V0;
                AppendImageBatch(
                    commands,
                    textureId,
                    ColorChannel(color.r),
                    ColorChannel(color.g),
                    ColorChannel(color.b),
                    ColorChannel(color.a),
                    values
                );
            }
        }
    }

    void EmitGlyphRun(
        BrowserGlyphRun drawing,
        BrowserFontBank.Face face,
        int textureId,
        Color color,
        ref bool uploaded
    )
    {
        if (drawing.Quads.IsEmpty)
            return;
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
            drawing.Quads
        );
    }

    static float Snap(float value) => MathF.Floor(value + .5f);
}
