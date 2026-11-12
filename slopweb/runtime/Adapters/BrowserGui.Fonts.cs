// Native baked plain-text metrics and rasterization. Rich layout remains owned by
// the installed text engine; unsupported runs use the diagnostic renderer.
using UnityEngine;
using UnityStubs.Gui;

namespace BrowserAdapters;

public sealed partial class BrowserGui
{
    // Preserve image-upload ownership so replaced atlases emit release commands
    // on the next frame. Replacing the whole GUI would orphan browser textures.
    public static void FontsChanged()
    {
        var gui = GuiBackend.Current as BrowserGui ?? Install();
        gui.nativeText = null;
        gui.ClearRows();
    }

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

    // Diagnostic reference switch for exact packets and real workload comparisons.
    static bool glyphTailClipEnabled = true;

    public static bool ConfigureGlyphTailClip(bool enabled)
    {
        bool previous = glyphTailClipEnabled;
        glyphTailClipEnabled = enabled;
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
        if (rich && BrowserNativeText.HasMarkup(text))
            return false;
        BrowserFontBank.ObserveText(index, size, style, text);
        if (!BrowserFontBank.TryFace(index, size, style, out face))
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
        bool hasDrawingClip = drawingClips.TryPeek(out var tailBounds);
        if (
            glyphTailClipEnabled
            && hasDrawingClip
            && (tailBounds.width <= 0 || tailBounds.height <= 0)
        )
        {
            // Empty scopes reject every destination, including backward runs.
            TraceClippedGlyphTail(
                line.Glyphs,
                penX,
                penY,
                scale,
                fineClip,
                screenClip,
                clipBottom,
                drawing
            );
            drawing?.Complete();
            return;
        }
        Span<float> values = stackalloc float[8];
        bool monotoneTail =
            glyphTailClipEnabled && line.NonnegativeAdvances && float.IsFinite(scale) && scale > 0;
        bool drawingTail =
            monotoneTail
            && line.SafeDrawingBounds
            && matrix.m01 == 0
            && matrix.m10 == 0
            && hasDrawingClip
            && FiniteGlyphTail(line, penX, penY, scale, origin);
        bool stopAtRight = monotoneTail && (fineClip || drawingTail);
        float minimumLeft = line.MinimumLeftBearing * scale;
        for (int glyphIndex = 0; glyphIndex < line.Glyphs.Length; glyphIndex++)
        {
            var glyph = line.Glyphs[glyphIndex];
            if (stopAtRight)
            {
                float lowerLeft = Snap(penX + minimumLeft);
                if (fineClip && lowerLeft >= screenClip.xMax)
                    break;
                // The device quad is divided back into GUI coordinates before
                // OutsideDrawingClip transforms it again. Reproduce that round trip
                // for the lower bound; ideal screen coordinates differ at clip edges.
                if (
                    drawingTail
                    && lowerLeft >= tailBounds.xMax - 1
                    && Device(new Vector2((lowerLeft - origin.x) / scale, 0)).x >= tailBounds.xMax
                )
                {
                    TraceClippedGlyphTail(
                        line.Glyphs.AsSpan(glyphIndex),
                        penX,
                        penY,
                        scale,
                        fineClip,
                        screenClip,
                        clipBottom,
                        drawing
                    );
                    break;
                }
            }
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
                        if (observeLabels)
                            labelPainted = true;
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
                            AppendImages(
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

    bool FiniteGlyphTail(
        BrowserTextCache<TmpGlyphMetrics>.Line line,
        float penX,
        float penY,
        float scale,
        Vector2 origin
    )
    {
        // Bound all remaining device coordinates, their GUI reconstruction and
        // their projection before using monotonicity. NaN/overflow inputs retain
        // the reference loop, including its observable rejection behavior.
        // Twice the maximum step bounds rounded nonnegative pen accumulation;
        // the extra units account for Snap and rectangle edge reconstruction.
        double extent =
            (line.MaximumMetricMagnitude * (double)scale + 1) * (2d * line.Glyphs.Length + 8);
        double device =
            Math.Abs((double)penX)
            + Math.Abs((double)penY)
            + Math.Abs((double)origin.x)
            + Math.Abs((double)origin.y)
            + extent;
        double local = device / scale + Math.Abs((double)offset.x) + Math.Abs((double)offset.y);
        double projected =
            local * (scale + Math.Abs((double)matrix.m11))
            + Math.Abs((double)matrix.m03)
            + Math.Abs((double)matrix.m13);
        return device < float.MaxValue / 16d
            && local < float.MaxValue / 16d
            && projected < float.MaxValue / 16d;
    }

    // Drawing-clip rejection historically counts every fine-clip-admitted glyph,
    // even though no packet is emitted. Keep that diagnostic contract and cached
    // trace replay, but omit destination construction, transforms and upload work.
    void TraceClippedGlyphTail(
        ReadOnlySpan<TmpGlyphMetrics> glyphs,
        float penX,
        float penY,
        float scale,
        bool fineClip,
        Rect screenClip,
        float clipBottom,
        BrowserGlyphRun? drawing
    )
    {
        if (!BrowserGuiInput.Profile && drawing == null)
            return;
        foreach (var glyph in glyphs)
        {
            if (glyph.visible)
            {
                bool admitted = true;
                if (fineClip)
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
                    admitted = quad.Clip(screenClip.xMin, screenClip.xMax, clipBottom);
                }
                if (admitted)
                {
                    TraceTextureDraw();
                    if (drawing != null)
                        drawing.TraceCount++;
                }
            }
            penX += Snap(glyph.advanceX * scale);
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
                if (observeLabels)
                    labelPainted = true;
                UploadImage(face.Texture);
                values[0] = destination.x;
                values[1] = destination.y;
                values[2] = destination.width;
                values[3] = destination.height;
                values[4] = quad.U0;
                values[5] = 1 - quad.V1;
                values[6] = quad.U1 - quad.U0;
                values[7] = quad.V1 - quad.V0;
                AppendImages(
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
        if (observeLabels)
            labelPainted = true;
        if (!uploaded)
        {
            UploadImage(face.Texture);
            uploaded = true;
        }
        AppendImages(
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
