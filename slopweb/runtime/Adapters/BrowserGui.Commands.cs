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

    static bool benchmarkRequested;
    public static string SerializationBenchmark { get; private set; } = "null";

    public static void RequestSerializationBenchmark()
    {
        SerializationBenchmark = "null";
        benchmarkRequested = true;
    }

    static void BenchmarkSerialization(List<Command> commands)
    {
        if (!benchmarkRequested)
            return;
        benchmarkRequested = false;
        var samples = new List<object>();
        var numericSamples = new List<object>();
        var directSamples = new List<object>();
        var numericCommands = commands
            .Select(c =>
                c.value is ImageBatchCommand b
                    ? new Command(c.kind, new NumericImageBatchCommand(b.id, b.color, b.quads))
                    : c
            )
            .ToList();
        string Reference() => JsonSerializer.Serialize(commands);
        string Generated() =>
            JsonSerializer.Serialize(commands, BrowserGuiJson.Default.ListCommand);
        string Direct() => BrowserCommandWriter.Serialize(commands);
        string Numeric() =>
            JsonSerializer.Serialize(numericCommands, BrowserGuiJson.Default.ListCommand);
        if (Reference() != Generated() || Reference() != Direct())
            throw new InvalidOperationException("GUI JSON writers disagree");
        Numeric();
        // Identical immutable packet, alternating order, after warming all writers.
        // This isolates transport cost; it is not a whole-workspace benchmark.
        for (int round = 0; round < 6; round++)
        {
            foreach (int mode in round % 2 == 0 ? new[] { 0, 1, 2, 3 } : new[] { 3, 2, 1, 0 })
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                string packet =
                    mode == 3 ? Direct()
                    : mode == 2 ? Numeric()
                    : mode == 1 ? Generated()
                    : Reference();
                double ms = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
                if (mode >= 2)
                    (mode == 3 ? directSamples : numericSamples).Add(
                        new
                        {
                            ms,
                            allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated,
                            chars = packet.Length,
                        }
                    );
                else
                    samples.Add(
                        new
                        {
                            generated = mode == 1,
                            ms,
                            allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated,
                            chars = packet.Length,
                        }
                    );
            }
        }
        SerializationBenchmark = JsonSerializer.Serialize(
            new
            {
                commandCount = commands.Count,
                equivalent = true,
                samples,
                numericSamples,
                directSamples,
            }
        );
    }

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

    // Opt-in diagnostics compare the previous numeric transport with the packed
    // writer on the same immutable packet, using generated serializers for both.
    internal sealed record NumericImageBatchCommand(int id, float[] color, List<float> quads);

    internal sealed record TargetCommand(int id, int width, int height);
}

[JsonSerializable(typeof(List<BrowserGui.Command>))]
[JsonSerializable(typeof(BrowserGui.TextCommand))]
[JsonSerializable(typeof(BrowserGui.FillCommand))]
[JsonSerializable(typeof(BrowserGui.TextureCommand))]
[JsonSerializable(typeof(BrowserGui.ImageCommand))]
[JsonSerializable(typeof(BrowserGui.ImageBatchCommand))]
[JsonSerializable(typeof(BrowserGui.NumericImageBatchCommand))]
[JsonSerializable(typeof(BrowserGui.TargetCommand))]
[JsonSerializable(typeof(float[]))]
[JsonSerializable(typeof(int))]
internal partial class BrowserGuiJson : JsonSerializerContext { }
