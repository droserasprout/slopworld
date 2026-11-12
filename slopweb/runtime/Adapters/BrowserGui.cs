// Browser GUI boundary: installed Unity controls retain layout and event ownership.
// Commands carry drawing only; unsupported GPU paths fail explicitly.
using System.Diagnostics;
using System.Text.Json;
using UnityEngine;
using UnityStubs.Gui;
using Waylith.Rendering;
using RenderTexture = UnityEngine.RenderTexture;

namespace BrowserAdapters;

public sealed partial class BrowserGui : IGuiBackend
{
    readonly List<Command> commands = new();
    static readonly BrowserPacketCache packetCache = new();
    static bool packetCacheEnabled;
    internal bool SuppressDrawing;

    public static bool ConfigurePacketCache(bool enabled)
    {
        bool previous = packetCacheEnabled;
        packetCacheEnabled = enabled;
        packetCache.Clear();
        return previous;
    }

    readonly BrowserCommandState commandState = new();
    int outsideTargetQuads;
    bool drawingTarget;
    public static string? OutsideTargetTrace { get; private set; }
    readonly Dictionary<int, WeakReference<RenderTexture>> targets = new();
    readonly List<(Rect rect, Vector2 origin)> groups = new();
    Matrix4x4 matrix = Matrix4x4.identity;
    Vector2 offset;
    readonly Stack<Vector2> offsets = new();
    readonly Stack<Rect> hitClips = new();
    readonly Stack<Rect> drawingClips = new();
    int windowDepth;
    public double LastResourceSweepMs { get; private set; }
    public double LastSerializationMs { get; private set; }
    public bool LastPacketReused { get; private set; }

    public static BrowserGui Install()
    {
        var gui = new BrowserGui();
        GuiBackend.Current = gui;
        return gui;
    }

    public static bool IsInstalled() => GuiBackend.Current is BrowserGui;

    public string Drain()
    {
        try
        {
            return DrainCore();
        }
        catch
        {
            recordingRow = null;
            pendingRows.Clear();
            throw;
        }
    }

    string DrainCore()
    {
        long started = BrowserGuiInput.Profile ? Stopwatch.GetTimestamp() : 0;
        CollectImages();
        foreach (var pair in targets.ToArray())
            if (!pair.Value.TryGetTarget(out var target) || target == null || !target.IsCreated())
            {
                Emit("releaseTarget", pair.Key);
                targets.Remove(pair.Key);
            }
        long swept = BrowserGuiInput.Profile ? Stopwatch.GetTimestamp() : 0;
        var result = packetCacheEnabled ? packetCache.Find(commands) : null;
        LastPacketReused = result != null;
        if (result == null)
        {
            result = BrowserCommandWriter.Serialize(commands);
            if (packetCacheEnabled)
                packetCache.Store(commands, result);
        }
        if (BrowserGuiInput.Profile)
        {
            LastResourceSweepMs = (swept - started) * 1000d / Stopwatch.Frequency;
            LastSerializationMs = (Stopwatch.GetTimestamp() - swept) * 1000d / Stopwatch.Frequency;
        }
        BenchmarkSerialization(commands);
        CommitRows();
        commands.Clear();
        commandState.Clear();
        outsideTargetQuads = 0;
        pendingImages.Clear();
        pendingAtlasImages.Clear();
        return result;
    }

    public void Discard()
    {
        recordingRow = null;
        pendingRows.Clear();
        // Discarded layout/input draws never delivered their image definitions.
        foreach (int id in pendingImages)
            images.Remove(id);
        pendingImages.Clear();
        pendingAtlasImages.Clear();
        commands.Clear();
        commandState.Clear();
        outsideTargetQuads = 0;
    }

    static float[] Box(Rect r) => new[] { r.x, r.y, r.width, r.height };

    // Native ImGui submits byte vertex colors, including alpha. Preserve that
    // quantization before Canvas blends; raw floats shift even flat panel colors.
    static float ColorChannel(float value) => (byte)(Math.Clamp(value, 0f, 1f) * 255f + .5f) / 255f;

    static float[] Rgba(Color c) =>
        new[] { ColorChannel(c.r), ColorChannel(c.g), ColorChannel(c.b), ColorChannel(c.a) };

    void Emit(string kind, object value)
    {
        if (SuppressDrawing)
            return;
        RecordEmit(kind, value);
        if (!commandState.Accept(kind, value))
            return;
        // Adjacent transforms have no drawing or saved state between them.
        if (kind == "matrix" && commands.Count > 0 && commands[^1].kind == "matrix")
            commands[^1] = new Command(kind, value);
        else
            commands.Add(new Command(kind, value));
    }

    public int GroupCount => groups.Count;
    public bool InlineControlsObscured => false;
    public bool MouseOutsideHitClip =>
        Event.current != null
        && hitClips.Count > 0
        && !hitClips.Peek().Contains(Device(Event.current.mousePosition));

    // Installed layout handles prepared wrapped/rich runs; unsupported fonts retain fallback.
    public Vector2 MeasureText(
        int fontIndex,
        float fontSize,
        FontStyle fontStyle,
        string text,
        float wrapWidth,
        bool richText = true
    )
    {
        if (BakedFace(fontIndex, fontSize, fontStyle, text, richText, out var face, out var run))
        {
            float width = run.Measure(
                static glyph => glyph.advanceX,
                Math.Max(.001f, Math.Abs(matrix.m00))
            );
            if (wrapWidth <= 0 || width <= wrapWidth)
                return new Vector2(width, face.FirstLine + (run.Lines.Length - 1) * face.Pitch);
        }
        if (
            NativeLayout(
                fontIndex,
                fontSize,
                fontStyle,
                text,
                wrapWidth,
                Color.white,
                richText,
                Math.Max(.001f, Math.Abs(matrix.m00)),
                out var native
            )
        )
            return new Vector2(native.Width, native.Height);
        if (fontSize <= 0)
            fontSize = 16;
        var lines = text.Split('\n');
        return new Vector2(
            lines.Max(s => s.Length) * fontSize * .6f,
            lines.Length * fontSize * 1.2f
        );
    }

    // GUIStyle.lineHeight is also the terminal's cell box. The native rounded
    // first line must fit inside it; fractional font spacing can clip descenders
    // at density 1/2 even though the pane admits the requested number of rows.
    public float LineSpacing(int fontIndex, float fontSize) =>
        BrowserFontBank.TryFace(fontIndex, fontSize, out var face)
            ? Math.Max(face.Spacing, face.FirstLine)
            : (fontSize > 0 ? fontSize : 16) * 1.2f;

    public void FontMetrics(
        int fontIndex,
        out float importSize,
        out float ascent,
        out float lineSpacing
    )
    {
        if (BrowserFontBank.TryFace(fontIndex, 0, out var face))
        {
            importSize = face.Size;
            ascent = face.LineAscent;
            lineSpacing = face.Spacing;
            return;
        }
        importSize = 16;
        ascent = 13;
        lineSpacing = 19.2f;
    }

    public int RegisterFont(string name, byte[] fontData, float importSize, int renderingMode) =>
        BrowserFontBank.RegisterAvailable(fontData, importSize);

    public int TextIndexAt(
        int fontIndex,
        float fontSize,
        FontStyle fontStyle,
        string text,
        float wrapWidth,
        Vector2 local,
        bool richText = true
    )
    {
        if (
            BakedCaret(
                fontIndex,
                fontSize,
                fontStyle,
                text,
                wrapWidth,
                richText,
                out _,
                out var line
            )
        )
            return line.IndexAt(local.x, static glyph => glyph.advanceX, matrix.m00);
        if (
            NativeLayout(
                fontIndex,
                fontSize,
                fontStyle,
                text,
                wrapWidth,
                Color.white,
                richText,
                matrix.m00,
                out _
            )
        )
            return nativeText!.IndexAt(local);
        return Math.Clamp(
            (int)Math.Round(local.x / ((fontSize > 0 ? fontSize : 16) * .6f)),
            0,
            text.Length
        );
    }

    public Vector2 TextCursorPos(
        int fontIndex,
        float fontSize,
        FontStyle fontStyle,
        string text,
        float wrapWidth,
        int index,
        out float lineHeight,
        bool richText = true
    )
    {
        if (
            BakedCaret(
                fontIndex,
                fontSize,
                fontStyle,
                text,
                wrapWidth,
                richText,
                out var face,
                out var line
            )
        )
        {
            lineHeight = face.Pitch;
            return new Vector2(
                line.Carets(static glyph => glyph.advanceX, matrix.m00)[
                    Math.Clamp(index, 0, text.Length)
                ],
                0
            );
        }
        if (
            !string.IsNullOrEmpty(text)
            && NativeLayout(
                fontIndex,
                fontSize,
                fontStyle,
                text,
                wrapWidth,
                Color.white,
                richText,
                matrix.m00,
                out var layout
            )
        )
        {
            lineHeight = layout.Lines[nativeText!.LineOf(index)].Pitch;
            return nativeText.PositionOf(index);
        }
        lineHeight = LineSpacing(fontIndex, fontSize);
        return new Vector2(index * (fontSize > 0 ? fontSize : 16) * .6f, 0);
    }

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
        if (string.IsNullOrEmpty(text) || to <= from)
            return;
        if (
            BakedCaret(
                fontIndex,
                fontSize,
                fontStyle,
                text,
                wrapWidth,
                richText,
                out var face,
                out var line
            )
        )
        {
            var positions = line.Carets(static glyph => glyph.advanceX, matrix.m00);
            int start = Math.Clamp(from, 0, text.Length);
            int end = Math.Clamp(to, 0, text.Length);
            if (end > start)
                rects.Add(
                    new Rect(positions[start], 0, positions[end] - positions[start], face.Pitch)
                );
            return;
        }
        if (
            NativeLayout(
                fontIndex,
                fontSize,
                fontStyle,
                text,
                wrapWidth,
                Color.white,
                richText,
                matrix.m00,
                out var layout
            )
        )
        {
            var start = nativeText!.PositionOf(from);
            var end = nativeText.PositionOf(to);
            int first = nativeText.LineOf(from),
                last = nativeText.LineOf(to);
            for (int row = first; row <= last && row < layout.Lines.Length; row++)
            {
                var current = layout.Lines[row];
                float left = row == first ? start.x : 0;
                float right = row == last ? end.x : MathF.Max(current.Width, 4);
                rects.Add(new Rect(left, current.Top, MathF.Max(0, right - left), current.Pitch));
            }
            return;
        }
        rects.Add(
            new Rect(
                from * (fontSize > 0 ? fontSize : 16) * .6f,
                0,
                (to - from) * (fontSize > 0 ? fontSize : 16) * .6f,
                LineSpacing(fontIndex, fontSize)
            )
        );
    }

    public void DrawText(
        Rect rect,
        string text,
        int fontIndex,
        float fontSize,
        FontStyle fontStyle,
        TextAnchor anchor,
        bool wordWrap,
        Color color,
        Rect clipRect = default,
        bool richText = true
    )
    {
        if (SuppressDrawing)
            return;
        if (observeLabels)
            labelPainted = false;
        long started = BrowserGuiInput.Profile ? Stopwatch.GetTimestamp() : 0;
        if (recordingRow != null && text.Any(c => c < ' ' || c > '~'))
            RejectRow();
        bool baked = DrawBakedText(
            rect,
            text,
            fontIndex,
            fontSize,
            fontStyle,
            anchor,
            wordWrap,
            color,
            clipRect,
            richText
        );
        if (BrowserGuiInput.Profile)
        {
            ProfileBakedTicks += Stopwatch.GetTimestamp() - started;
            if (baked)
            {
                ProfileBakedRuns++;
                ProfileBakedCharacters += text.Length;
            }
            started = Stopwatch.GetTimestamp();
        }
        if (baked)
        {
            if (observeLabels && labelPainted)
                ObserveLabel(rect, text, color, clipRect);
            return;
        }
        RejectRow();
        bool native = DrawNativeText(
            rect,
            text,
            fontIndex,
            fontSize,
            fontStyle,
            anchor,
            wordWrap,
            color,
            clipRect,
            richText
        );
        if (BrowserGuiInput.Profile)
        {
            ProfileNativeTicks += Stopwatch.GetTimestamp() - started;
            if (native)
                ProfileNativeRuns++;
            else
                ProfileFallbackRuns++;
        }
        if (native)
        {
            if (observeLabels && labelPainted)
                ObserveLabel(rect, text, color, clipRect);
            return;
        }
        Emit(
            "text",
            new TextCommand(Box(rect), text, fontSize, (int)fontStyle, (int)anchor, Rgba(color))
        );
        if (observeLabels)
            ObserveLabel(rect, text, color, clipRect);
    }

    public void FillRect(Rect rect, Color color) => FillRectRounded(rect, color, 0f);

    public void FillRectRounded(Rect rect, Color color, float rounding)
    {
        if (SuppressDrawing)
            return;
        Emit("fill", new FillCommand(Box(rect), Rgba(color), rounding));
    }

    public void DrawTexture(
        Rect rect,
        UnityEngine.Texture image,
        ScaleMode scaleMode,
        Color color
    ) => DrawTextureWithTexCoords(rect, image, new Rect(0, 0, 1, 1), color);

    public void DrawTextureWithTexCoords(
        Rect rect,
        UnityEngine.Texture image,
        Rect texCoords,
        Color color
    )
    {
        if (SuppressDrawing)
            return;
        TraceTextureDraw();
        if (OutsideDrawingClip(rect))
            return;
        if (image is RenderTexture target)
        {
            RememberTarget(target);
            Emit(
                "targetTexture",
                new TextureCommand(target.GetInstanceID(), Box(rect), Box(texCoords), Rgba(color))
            );
            return;
        }
        if (image is not UnityEngine.Texture2D texture)
            throw new NotSupportedException("Browser GUI render texture");
        UploadImage(texture);
        AppendImages(
            texture.GetInstanceID(),
            ColorChannel(color.r),
            ColorChannel(color.g),
            ColorChannel(color.b),
            ColorChannel(color.a),
            [
                rect.x,
                rect.y,
                rect.width,
                rect.height,
                texCoords.x,
                texCoords.y,
                texCoords.width,
                texCoords.height,
            ]
        );
    }

    void TraceTextureDraw()
    {
        // One opt-in stack sample locates unexpected uncached drawing without
        // collecting terminal text or allocating a trace on ordinary frames.
        if (
            BrowserGuiInput.Profile
            && !drawingTarget
            && ++outsideTargetQuads == 500
            && OutsideTargetTrace == null
        )
            OutsideTargetTrace = Environment.StackTrace;
    }

    public void DrawTextureWithMaterial(
        Rect rect,
        UnityEngine.Texture image,
        Rect texCoords,
        Color color,
        UnityEngine.Material material,
        Waylith.Rendering.Material passMaterial
    ) => throw new NotSupportedException("Browser GUI material");

    public void DrawTextureQuad(
        Vector2 p0,
        Vector2 p1,
        Vector2 p2,
        Vector2 p3,
        UnityEngine.Texture image,
        Color color
    ) => throw new NotSupportedException("Browser GUI texture quad");

    public void DrawPrimitives(
        int mode,
        ReadOnlySpan<GuiVertex> vertices,
        UnityEngine.Texture texture,
        bool screenSpace
    ) => throw new NotSupportedException("Browser GUI primitives");

    Vector2 Device(Vector2 point) => matrix.MultiplyPoint3x4(point + offset);

    Vector2 Local(Vector2 point) => (Vector2)matrix.inverse.MultiplyPoint3x4(point) - offset;

    Rect Bounds(Rect rect)
    {
        var a = Device(new Vector2(rect.xMin, rect.yMin));
        var d = Device(new Vector2(rect.xMax, rect.yMax));
        if (matrix.m01 == 0 && matrix.m10 == 0)
            return Rect.MinMaxRect(
                Math.Min(a.x, d.x),
                Math.Min(a.y, d.y),
                Math.Max(a.x, d.x),
                Math.Max(a.y, d.y)
            );
        var b = Device(new Vector2(rect.xMax, rect.yMin));
        var c = Device(new Vector2(rect.xMin, rect.yMax));
        return Rect.MinMaxRect(
            Math.Min(Math.Min(a.x, b.x), Math.Min(c.x, d.x)),
            Math.Min(Math.Min(a.y, b.y), Math.Min(c.y, d.y)),
            Math.Max(Math.Max(a.x, b.x), Math.Max(c.x, d.x)),
            Math.Max(Math.Max(a.y, b.y), Math.Max(c.y, d.y))
        );
    }

    bool OutsideDrawingClip(Rect rect)
    {
        if (!drawingClips.TryPeek(out var clip))
            return false;
        if (clip.width <= 0 || clip.height <= 0)
            return true;
        var bounds = Bounds(rect);
        return bounds.xMax <= clip.xMin
            || bounds.xMin >= clip.xMax
            || bounds.yMax <= clip.yMin
            || bounds.yMin >= clip.yMax;
    }

    void PushDrawingClip(Rect clip)
    {
        if (drawingClips.TryPeek(out var parent))
        {
            float left = Math.Max(clip.xMin, parent.xMin),
                top = Math.Max(clip.yMin, parent.yMin);
            clip = new Rect(
                left,
                top,
                Math.Max(0, Math.Min(clip.xMax, parent.xMax) - left),
                Math.Max(0, Math.Min(clip.yMax, parent.yMax) - top)
            );
        }
        drawingClips.Push(clip);
    }

    void EmitMatrix()
    {
        var origin = Device(Vector2.zero);
        Emit(
            "matrix",
            new[] { matrix.m00, matrix.m10, matrix.m01, matrix.m11, origin.x, origin.y }
        );
    }

    public void SetMatrix(Matrix4x4 value)
    {
        var pointer = Event.current == null ? Vector2.zero : Device(Event.current.mousePosition);
        matrix = value;
        if (Event.current != null)
            Event.current.mousePosition = Local(pointer);
        EmitMatrix();
    }

    public void PushGroup(Rect rect) => PushScrollView(rect, new Vector2(rect.x, rect.y));

    public void PushScrollView(Rect clipRect, Vector2 contentOffset)
    {
        var pointer = Event.current == null ? Vector2.zero : Device(Event.current.mousePosition);
        var clip = Bounds(clipRect);
        PushDrawingClip(clip);
        Emit("clipScreen", Box(clip));
        if (hitClips.TryPeek(out var parent))
            clip = Rect.MinMaxRect(
                Math.Max(clip.xMin, parent.xMin),
                Math.Max(clip.yMin, parent.yMin),
                Math.Min(clip.xMax, parent.xMax),
                Math.Min(clip.yMax, parent.yMax)
            );
        hitClips.Push(clip);
        offsets.Push(offset);
        offset += contentOffset;
        groups.Add((clipRect, offset));
        if (Event.current != null)
            Event.current.mousePosition = Local(pointer);
        EmitMatrix();
    }

    public void PopGroup()
    {
        var pointer = Event.current == null ? Vector2.zero : Device(Event.current.mousePosition);
        groups.RemoveAt(groups.Count - 1);
        offset = offsets.Pop();
        hitClips.Pop();
        drawingClips.Pop();
        if (Event.current != null)
            Event.current.mousePosition = Local(pointer);
        Emit("pop", 0);
        EmitMatrix();
    }

    public void GetGroup(int index, out Rect rect, out Vector2 contentOrigin) =>
        (rect, contentOrigin) = groups[index];

    public void BeginWindowContent(Rect windowRect) => windowDepth++;

    public void EndWindowContent() => windowDepth = Math.Max(0, windowDepth - 1);

    // Draw-only clips do not affect hit testing in the installed backend.
    public void PushClipRect(Rect rect)
    {
        var clip = Bounds(rect);
        PushDrawingClip(clip);
        Emit("clipScreen", Box(clip));
    }

    public void PopClipRect()
    {
        drawingClips.Pop();
        Emit("pop", 0);
        EmitMatrix();
    }

    void RememberTarget(RenderTexture target)
    {
        if (!target.IsCreated() && !target.Create())
            throw new InvalidOperationException("Browser GUI render target could not be created");
        targets[target.GetInstanceID()] = new WeakReference<RenderTexture>(target);
    }

    public void ActiveRenderTextureChanged(UnityEngine.RenderTexture active)
    {
        drawingTarget = active != null;
        if (active != null)
            RememberTarget(active);
        Emit(
            "target",
            new TargetCommand(
                active == null ? 0 : active.GetInstanceID(),
                active?.width ?? 0,
                active?.height ?? 0
            )
        );
    }

    public void SetTextLanguage(string languageFolderName) { }

    public Vector2 ScreenToGuiPoint(Vector2 screenPoint) =>
        matrix.inverse.MultiplyPoint3x4(screenPoint - offset);

    public Vector2 GuiToScreenPoint(Vector2 guiPoint) =>
        (Vector2)matrix.MultiplyPoint3x4(guiPoint) + offset;

    public Vector2 GuiToDevicePoint(Vector2 guiPoint) => Device(guiPoint);

    public static string ControlProbe()
    {
        var backend = Install();
        Event.current = new Event
        {
            type = EventType.MouseDown,
            mousePosition = new Vector2(35, 45),
        };
        GUI.BeginGroup(new Rect(20, 30, 100, 100));
        if (Event.current.mousePosition != new Vector2(15, 15) || backend.MouseOutsideHitClip)
            throw new InvalidOperationException("Group pointer translation failed");
        backend.PushScrollView(new Rect(0, 0, 10, 10), new Vector2(-5, -6));
        if (!backend.MouseOutsideHitClip || Event.current.type != EventType.Ignore)
            throw new InvalidOperationException("Nested hit clipping failed");
        backend.GetGroup(1, out _, out var origin);
        if (origin != new Vector2(15, 24))
            throw new InvalidOperationException("Scroll origin contract failed");
        backend.PopGroup();
        GUI.EndGroup();
        if (Event.current.mousePosition != new Vector2(35, 45) || backend.GroupCount != 0)
            throw new InvalidOperationException("Group restoration failed");
        backend.Drain();
        Event.current = new Event { type = EventType.Repaint };
        GUI.Label(new Rect(20, 20, 300, 40), "Actual Unity GUI in WASM");
        GUI.Box(new Rect(20, 75, 300, 60), "Installed game control");
        GUI.BeginGroup(new Rect(400, 20, 20, 20));
        backend.FillRect(new Rect(-5, -5, 40, 40), Color.red);
        GUI.EndGroup();
        var texture = new UnityEngine.Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            texture.SetPixels32(new Color32[] { Color.red, Color.red, Color.blue, Color.blue });
            backend.PushClipRect(new Rect(0, 0, 10, 10));
            backend.DrawTextureWithTexCoords(
                new Rect(20, 20, 2, 2),
                texture,
                new Rect(0, 0, 1, 1),
                Color.white
            );
            backend.PushClipRect(new Rect(20, 20, 10, 10));
            backend.DrawTextureWithTexCoords(
                new Rect(21, 21, 2, 2),
                texture,
                new Rect(0, 0, 1, 1),
                Color.white
            );
            backend.PopClipRect();
            if (backend.images.ContainsKey(texture.GetInstanceID()))
                throw new InvalidOperationException("Fully clipped images must not upload");
            backend.DrawTextureWithTexCoords(
                new Rect(9, 9, 2, 2),
                texture,
                new Rect(0, 0, 1, 1),
                Color.white
            );
            if (!backend.images.ContainsKey(texture.GetInstanceID()))
                throw new InvalidOperationException("Partially visible images must upload");
            backend.PopClipRect();
            GUI.DrawTexture(new Rect(450, 20, 20, 20), texture);
        }
        finally
        {
            texture.Dispose();
        }
        return backend.Drain();
    }
}
