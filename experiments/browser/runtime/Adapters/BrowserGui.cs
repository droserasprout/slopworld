// Browser GUI boundary: installed Unity controls retain layout and event ownership.
// Commands carry drawing only; unsupported GPU paths fail explicitly.
using System.Text.Json;
using UnityEngine;
using UnityStubs.Gui;
using Waylith.Rendering;
using RenderTexture = UnityEngine.RenderTexture;

namespace BrowserAdapters;

public sealed partial class BrowserGui : IGuiBackend
{
    readonly record struct Command(string kind, object value);

    readonly List<Command> commands = new();
    readonly Dictionary<int, WeakReference<RenderTexture>> targets = new();
    readonly List<(Rect rect, Vector2 origin)> groups = new();
    Matrix4x4 matrix = Matrix4x4.identity;
    Vector2 offset;
    readonly Stack<Vector2> offsets = new();
    readonly Stack<Rect> hitClips = new();
    int windowDepth;

    public static BrowserGui Install()
    {
        var gui = new BrowserGui();
        GuiBackend.Current = gui;
        return gui;
    }

    public static bool IsInstalled() => GuiBackend.Current is BrowserGui;

    public string Drain()
    {
        CollectImages();
        foreach (var pair in targets.ToArray())
            if (!pair.Value.TryGetTarget(out var target) || target == null || !target.IsCreated())
            {
                Emit("releaseTarget", pair.Key);
                targets.Remove(pair.Key);
            }
        var result = JsonSerializer.Serialize(commands);
        commands.Clear();
        pendingImages.Clear();
        return result;
    }

    public void Discard()
    {
        // Discarded layout/input draws never delivered their image definitions.
        foreach (int id in pendingImages)
            images.Remove(id);
        pendingImages.Clear();
        commands.Clear();
    }

    static float[] Box(Rect r) => new[] { r.x, r.y, r.width, r.height };

    static float[] Rgba(Color c) => new[] { c.r, c.g, c.b, c.a };

    void Emit(string kind, object value) => commands.Add(new Command(kind, value));

    public int GroupCount => groups.Count;
    public bool InlineControlsObscured => false;
    public bool MouseOutsideHitClip =>
        Event.current != null
        && hitClips.Count > 0
        && !hitClips.Peek().Contains(Device(Event.current.mousePosition));

    // Initial monospace metrics; browser font metrics and rich text are still required.
    public Vector2 MeasureText(
        int fontIndex,
        float fontSize,
        FontStyle fontStyle,
        string text,
        float wrapWidth,
        bool richText = true
    )
    {
        if (fontSize <= 0)
            fontSize = 16;
        var lines = text.Split('\n');
        return new Vector2(
            lines.Max(s => s.Length) * fontSize * .6f,
            lines.Length * fontSize * 1.2f
        );
    }

    public float LineSpacing(int fontIndex, float fontSize) =>
        (fontSize > 0 ? fontSize : 16) * 1.2f;

    public void FontMetrics(
        int fontIndex,
        out float importSize,
        out float ascent,
        out float lineSpacing
    )
    {
        importSize = 16;
        ascent = 13;
        lineSpacing = 19.2f;
    }

    public int RegisterFont(string name, byte[] fontData, float importSize, int renderingMode) => 0;

    public int TextIndexAt(
        int fontIndex,
        float fontSize,
        FontStyle fontStyle,
        string text,
        float wrapWidth,
        Vector2 local,
        bool richText = true
    ) =>
        Math.Clamp(
            (int)Math.Round(local.x / ((fontSize > 0 ? fontSize : 16) * .6f)),
            0,
            text.Length
        );

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
    ) =>
        rects.Add(
            new Rect(
                from * (fontSize > 0 ? fontSize : 16) * .6f,
                0,
                (to - from) * (fontSize > 0 ? fontSize : 16) * .6f,
                LineSpacing(fontIndex, fontSize)
            )
        );

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
    ) =>
        Emit(
            "text",
            new
            {
                rect = Box(rect),
                text,
                fontSize,
                style = (int)fontStyle,
                anchor = (int)anchor,
                color = Rgba(color),
            }
        );

    public void FillRect(Rect rect, Color color) =>
        Emit(
            "fill",
            new
            {
                rect = Box(rect),
                color = Rgba(color),
                rounding = 0f,
            }
        );

    public void FillRectRounded(Rect rect, Color color, float rounding) =>
        Emit(
            "fill",
            new
            {
                rect = Box(rect),
                color = Rgba(color),
                rounding,
            }
        );

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
        if (image is RenderTexture target)
        {
            RememberTarget(target);
            Emit(
                "targetTexture",
                new
                {
                    id = target.GetInstanceID(),
                    rect = Box(rect),
                    uv = Box(texCoords),
                    color = Rgba(color),
                }
            );
            return;
        }
        if (image is not UnityEngine.Texture2D texture)
            throw new NotSupportedException("Browser GUI render texture");
        UploadImage(texture);
        Emit(
            "imageTexture",
            new
            {
                id = texture.GetInstanceID(),
                rect = Box(rect),
                uv = Box(texCoords),
                color = Rgba(color),
            }
        );
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
        var points = new[]
        {
            Device(new Vector2(rect.xMin, rect.yMin)),
            Device(new Vector2(rect.xMax, rect.yMin)),
            Device(new Vector2(rect.xMin, rect.yMax)),
            Device(new Vector2(rect.xMax, rect.yMax)),
        };
        return Rect.MinMaxRect(
            points.Min(p => p.x),
            points.Min(p => p.y),
            points.Max(p => p.x),
            points.Max(p => p.y)
        );
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
    public void PushClipRect(Rect rect) => Emit("clipScreen", Box(Bounds(rect)));

    public void PopClipRect()
    {
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
        if (active != null)
            RememberTarget(active);
        Emit(
            "target",
            new
            {
                id = active == null ? 0 : active.GetInstanceID(),
                width = active?.width ?? 0,
                height = active?.height ?? 0,
            }
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
            GUI.DrawTexture(new Rect(450, 20, 20, 20), texture);
        }
        finally
        {
            texture.Dispose();
        }
        return backend.Drain();
    }
}
