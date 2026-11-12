// Execute the installed TextLayout. Only font metrics cross this boundary;
// tokenization, wrapping, rich spans and line construction remain native code.
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BrowserAdapters;

public sealed class BrowserNativeLayout
{
    public static bool Available =>
        AppDomain
            .CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "RimWorld")
            ?.GetType("RimWorldWaylith.Imgui.TextLayout")
            ?.GetMethod("BrowserMetricsInstalled") != null;

    public sealed record Metrics(
        float Size,
        float Ascent,
        float FirstLine,
        float Pitch,
        float Spacing,
        Func<char, float> Advance
    );

    public sealed record Piece(
        int Start,
        int Length,
        Color Color,
        FontStyle Style,
        float Size,
        float X,
        float Width,
        bool IsSpace
    );

    public sealed record Line(
        int PieceStart,
        int PieceCount,
        float Width,
        float Top,
        float Ascent,
        float FirstLine,
        float Pitch
    );

    public sealed record Result(float Width, float Height, Piece[] Pieces, Line[] Lines);

    sealed record Advances(Metrics Metrics, float Scale);

    static readonly ConditionalWeakTable<object, BrowserNativeLayout> owners = new();
    static readonly ConditionalWeakTable<object, Advances> advances = new();
    static Type NativeType =>
        AppDomain
            .CurrentDomain.GetAssemblies()
            .Single(a => a.GetName().Name == "RimWorld")
            .GetType("RimWorldWaylith.Imgui.TextLayout", true)!;
    readonly object native;
    readonly Type type;
    readonly Func<int, float, FontStyle, Metrics> resolve;
    readonly Dictionary<(int, float, FontStyle, float), object> runs = new();

    public BrowserNativeLayout(Func<int, float, FontStyle, Metrics> resolve)
    {
        type = NativeType;
        if (type.GetMethod("BrowserMetricsInstalled") == null)
            throw new InvalidOperationException(
                "Installed text layout has not been adapted for browser metrics"
            );
        this.resolve = resolve;
        native = Activator.CreateInstance(type, new object?[] { null })!;
        owners.Add(native, this);
    }

    // The native object is reused synchronously by its UI owner, never concurrently.
    public Result Layout(
        int font,
        float size,
        FontStyle style,
        string text,
        float width,
        Color color,
        bool rich,
        float scale
    )
    {
        type.GetProperty("Scale")!.SetValue(native, scale);
        type.GetMethod("Layout")!.Invoke(native, [font, size, style, text, width, color, rich]);
        static T Field<T>(object value, string name) =>
            (T)value.GetType().GetField(name)!.GetValue(value)!;
        var pieces = ((System.Collections.IEnumerable)type.GetProperty("Pieces")!.GetValue(native)!)
            .Cast<object>()
            .Select(p => new Piece(
                Field<int>(p, "Start"),
                Field<int>(p, "Length"),
                Field<Color>(p, "Color"),
                Field<FontStyle>(p, "Style"),
                Field<float>(p, "Size"),
                Field<float>(p, "X"),
                Field<float>(p, "Width"),
                Field<bool>(p, "IsSpace")
            ))
            .ToArray();
        var lines = ((System.Collections.IEnumerable)type.GetProperty("Lines")!.GetValue(native)!)
            .Cast<object>()
            .Select(p => new Line(
                Field<int>(p, "PieceStart"),
                Field<int>(p, "PieceCount"),
                Field<float>(p, "Width"),
                Field<float>(p, "Top"),
                Field<float>(p, "Ascent"),
                Field<float>(p, "FirstLine"),
                Field<float>(p, "Pitch")
            ))
            .ToArray();
        return new Result(
            (float)type.GetProperty("Width")!.GetValue(native)!,
            (float)type.GetProperty("Height")!.GetValue(native)!,
            pieces,
            lines
        );
    }

    // Queries consume the most recent synchronous Layout on this owner.
    public int IndexAt(Vector2 point) => (int)type.GetMethod("IndexAt")!.Invoke(native, [point])!;

    public int LineOf(int index) => (int)type.GetMethod("LineOf")!.Invoke(native, [index])!;

    public Vector2 PositionOf(int index) =>
        (Vector2)type.GetMethod("PositionOf")!.Invoke(native, [index])!;

    // Called only by the locally staged installed assembly's bounded font hooks.
    public static bool OwnsLayout(object layout) => owners.TryGetValue(layout, out _);

    public static bool OwnsRun(object run) => advances.TryGetValue(run, out _);

    static object NativeHostCall(object layout, string method, int font, float size)
    {
        var host = layout
            .GetType()
            .GetField("host", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(layout)!;
        return host.GetType()
            .GetMethod(
                method,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )!
            .Invoke(host, [font, size])!;
    }

    public static object CreateAdvances(
        object layout,
        int font,
        float size,
        FontStyle style,
        float scale
    )
    {
        var owner = owners.GetValue(
            layout,
            _ => throw new InvalidOperationException("Unknown native layout")
        );
        var key = (font, size, style, scale);
        if (owner.runs.TryGetValue(key, out var found))
            return found;
        var metrics = owner.resolve(font, size, style);
        var value = RuntimeHelpers.GetUninitializedObject(
            owner.type.GetNestedType("GlyphAdvances", BindingFlags.NonPublic)!
        );
        advances.Add(value, new Advances(metrics, scale));
        if (owner.runs.Count >= 64)
            owner.runs.Clear();
        owner.runs.Add(key, value);
        return value;
    }

    public static int CodePoint(object run, string text, int index, out float width)
    {
        var metrics = advances.GetValue(
            run,
            _ => throw new InvalidOperationException("Unknown glyph advances")
        );
        if (char.IsSurrogate(text[index]))
            throw new NotSupportedException("Supplementary font coverage is not prepared");
        width =
            MathF.Floor(metrics.Metrics.Advance(text[index]) * metrics.Scale + .5f) / metrics.Scale;
        return 1;
    }

    public static float PixelSize(object layout, int font, float size) =>
        owners.TryGetValue(layout, out var owner)
            ? owner.resolve(font, size, FontStyle.Normal).Size
            : (float)NativeHostCall(layout, "PixelSizeFor", font, size);

    public static object LineMetrics(object layout, int font, float size)
    {
        if (!owners.TryGetValue(layout, out var registered))
            return NativeHostCall(layout, "LineMetricsFor", font, size);
        var owner = registered;
        var metrics = owner.resolve(font, size, FontStyle.Normal);
        var nativeMetrics = owner.type.Assembly.GetType("RimWorldWaylith.Imgui.LineMetrics", true)!;
        return Activator.CreateInstance(
            nativeMetrics,
            metrics.Ascent,
            metrics.FirstLine,
            metrics.Pitch,
            metrics.Spacing
        )!;
    }
}
