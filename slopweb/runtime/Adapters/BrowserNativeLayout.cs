// Execute the installed TextLayout. Only font metrics cross this boundary;
// tokenization, wrapping, rich spans and line construction remain native code.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
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
        Func<int, float> Advance
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

    // Bind only after the private assembly has loaded. Each reader belongs to the
    // concrete element type declared by the native Pieces or Lines collection.
    sealed class Members(Type type)
    {
        internal readonly PropertyInfo Scale = Property(type, "Scale");
        internal readonly PropertyInfo Width = Property(type, "Width");
        internal readonly PropertyInfo Height = Property(type, "Height");
        internal readonly PropertyInfo Pieces = Property(type, "Pieces");
        internal readonly PropertyInfo Lines = Property(type, "Lines");
        internal readonly MethodInfo Layout = Method(type, "Layout");
        internal readonly MethodInfo IndexAt = Method(type, "IndexAt");
        internal readonly MethodInfo LineOf = Method(type, "LineOf");
        internal readonly MethodInfo PositionOf = Method(type, "PositionOf");

        internal readonly Type GlyphAdvances =
            type.GetNestedType("GlyphAdvances", BindingFlags.NonPublic)
            ?? throw new TypeLoadException(type.FullName + ".GlyphAdvances");
        internal readonly ConstructorInfo Metrics =
            type.Assembly.GetType("RimWorldWaylith.Imgui.LineMetrics", true)!
                .GetConstructor([typeof(float), typeof(float), typeof(float), typeof(float)])
            ?? throw new MissingMethodException("LineMetrics", ".ctor");
        internal readonly FieldInfo Host =
            type.GetField("host", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, "host");

        static PropertyInfo Property(Type owner, string name) =>
            owner.GetProperty(name) ?? throw new MissingMemberException(owner.FullName, name);

        static MethodInfo Method(Type owner, string name) =>
            owner.GetMethod(name) ?? throw new MissingMethodException(owner.FullName, name);
    }

    static readonly Lazy<Members> members = new(() => new Members(NativeType));

    static Func<object, T> Reader<T>(Type owner, string name)
    {
        var field = owner.GetField(name);
        if (field?.FieldType != typeof(T))
            throw new MissingFieldException(owner.FullName, name);
        return value => (T)field.GetValue(value)!;
    }

    static Func<object, Piece> BindPiece(Type type)
    {
        var start = Reader<int>(type, "Start");
        var length = Reader<int>(type, "Length");
        var color = Reader<Color>(type, "Color");
        var style = Reader<FontStyle>(type, "Style");
        var size = Reader<float>(type, "Size");
        var x = Reader<float>(type, "X");
        var width = Reader<float>(type, "Width");
        var isSpace = Reader<bool>(type, "IsSpace");
        return p => new Piece(
            start(p),
            length(p),
            color(p),
            style(p),
            size(p),
            x(p),
            width(p),
            isSpace(p)
        );
    }

    Func<object, Piece>? readPiece;

    static Func<object, Line> BindLine(Type type)
    {
        var pieceStart = Reader<int>(type, "PieceStart");
        var pieceCount = Reader<int>(type, "PieceCount");
        var width = Reader<float>(type, "Width");
        var top = Reader<float>(type, "Top");
        var ascent = Reader<float>(type, "Ascent");
        var firstLine = Reader<float>(type, "FirstLine");
        var pitch = Reader<float>(type, "Pitch");
        return p => new Line(
            pieceStart(p),
            pieceCount(p),
            width(p),
            top(p),
            ascent(p),
            firstLine(p),
            pitch(p)
        );
    }

    Func<object, Line>? readLine;

    readonly object native;
    readonly Func<int, float, FontStyle, Metrics> resolve;
    readonly Dictionary<(int, float, FontStyle, float), object> runs = new();

    public BrowserNativeLayout(Func<int, float, FontStyle, Metrics> resolve)
    {
        var type = NativeType;
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
        members.Value.Scale.SetValue(native, scale);
        members.Value.Layout.Invoke(native, [font, size, style, text, width, color, rich]);
        var pieces = ((System.Collections.IEnumerable)members.Value.Pieces.GetValue(native)!)
            .Cast<object>()
            .Select(p => (readPiece ??= BindPiece(p.GetType()))(p))
            .ToArray();
        var lines = ((System.Collections.IEnumerable)members.Value.Lines.GetValue(native)!)
            .Cast<object>()
            .Select(p => (readLine ??= BindLine(p.GetType()))(p))
            .ToArray();
        return new Result(
            (float)members.Value.Width.GetValue(native)!,
            (float)members.Value.Height.GetValue(native)!,
            pieces,
            lines
        );
    }

    // Queries consume the most recent synchronous Layout on this owner.
    public int IndexAt(Vector2 point) => (int)members.Value.IndexAt.Invoke(native, [point])!;

    public int LineOf(int index) => (int)members.Value.LineOf.Invoke(native, [index])!;

    public Vector2 PositionOf(int index) =>
        (Vector2)members.Value.PositionOf.Invoke(native, [index])!;

    // Called only by the locally staged installed assembly's bounded font hooks.
    public static bool OwnsLayout(object layout) => owners.TryGetValue(layout, out _);

    public static bool OwnsRun(object run) => advances.TryGetValue(run, out _);

    // The patched engine can also call these hooks for a layout created by
    // its own host. Bind the original methods on the declared host type.
    static MethodInfo HostMethod(string name) =>
        members.Value.Host.FieldType.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(int), typeof(float)]
        ) ?? throw new MissingMethodException(members.Value.Host.FieldType.FullName, name);

    static readonly Lazy<MethodInfo> hostPixels = new(() => HostMethod("PixelSizeFor"));
    static readonly Lazy<MethodInfo> hostMetrics = new(() => HostMethod("LineMetricsFor"));

    static object NativeHostCall(object layout, MethodInfo method, int font, float size) =>
        method.Invoke(members.Value.Host.GetValue(layout), [font, size])!;

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
        var value = RuntimeHelpers.GetUninitializedObject(members.Value.GlyphAdvances);
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
        if (!Rune.TryGetRuneAt(text, index, out var rune))
            rune = Rune.ReplacementChar;
        width =
            MathF.Floor(metrics.Metrics.Advance(rune.Value) * metrics.Scale + .5f) / metrics.Scale;
        return rune.Utf16SequenceLength;
    }

    public static float PixelSize(object layout, int font, float size) =>
        owners.TryGetValue(layout, out var owner)
            ? owner.resolve(font, size, FontStyle.Normal).Size
            : (float)NativeHostCall(layout, hostPixels.Value, font, size);

    public static object LineMetrics(object layout, int font, float size)
    {
        if (!owners.TryGetValue(layout, out var registered))
            return NativeHostCall(layout, hostMetrics.Value, font, size);
        var owner = registered;
        var metrics = owner.resolve(font, size, FontStyle.Normal);
        return members.Value.Metrics.Invoke([
            metrics.Ascent,
            metrics.FirstLine,
            metrics.Pitch,
            metrics.Spacing,
        ]);
    }
}
