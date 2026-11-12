// Typed wire values let the WASM runtime use generated JSON writers rather than
// reflection for every anonymous drawing payload. Canvas retains the same contract.
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrowserAdapters;

public sealed partial class BrowserGui
{
    public static bool ConfigureQuadCache(bool enabled) =>
        BrowserQuadWriter.ConfigureCache(enabled);

    public static bool ConfigureCommandValueCache(bool enabled) =>
        BrowserCommandWriter.ConfigureValueCache(enabled);

    internal readonly record struct Command(string kind, object value);

    internal sealed record TextCommand(
        float[] rect,
        string text,
        float fontSize,
        int style,
        int anchor,
        float[] color
    );

    internal sealed record FillCommand(float[] rect, float[] color, float rounding);

    internal sealed record TextureCommand(int id, float[] rect, float[] uv, float[] color);

    internal sealed record ImageCommand(int id, int width, int height, string pixels);

    internal sealed record ImageBatchCommand(
        int id,
        float[] color,
        [property: JsonConverter(typeof(BrowserQuadWriter))] List<float> quads
    );

    internal sealed record TargetCommand(int id, int width, int height);
}

[JsonSerializable(typeof(List<BrowserGui.Command>))]
[JsonSerializable(typeof(BrowserGui.TextCommand))]
[JsonSerializable(typeof(BrowserGui.FillCommand))]
[JsonSerializable(typeof(BrowserGui.TextureCommand))]
[JsonSerializable(typeof(BrowserGui.ImageCommand))]
[JsonSerializable(typeof(BrowserGui.ImageBatchCommand))]
[JsonSerializable(typeof(BrowserGui.TargetCommand))]
[JsonSerializable(typeof(float[]))]
[JsonSerializable(typeof(int))]
internal partial class BrowserGuiJson : JsonSerializerContext { }
