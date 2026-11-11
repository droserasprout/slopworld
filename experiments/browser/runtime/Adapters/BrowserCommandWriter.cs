// Serialize drawing values directly, preserving the existing JSON property order,
// escaping and float formatting. Native drawing and Canvas playback own semantics.
using System.Buffers;
using System.Text;
using System.Text.Json;
using static BrowserAdapters.BrowserGui;

namespace BrowserAdapters;

internal static class BrowserCommandWriter
{
    [ThreadStatic]
    static ArrayBufferWriter<byte>? buffer;
    static readonly BrowserQuadWriter quads = new();

    [ThreadStatic]
    static BrowserCommandValueCache? values;
    internal static bool ValueCacheEnabled { get; private set; } = true;

    internal static bool ConfigureValueCache(bool enabled)
    {
        bool previous = ValueCacheEnabled;
        ValueCacheEnabled = enabled;
        values = null;
        return previous;
    }

    internal static string Serialize(List<Command> commands)
    {
        var output = buffer ??= new ArrayBufferWriter<byte>(16384);
        output.Clear();
        try
        {
            using var writer = new Utf8JsonWriter(output);
            var valueCache = ValueCacheEnabled ? values ??= new BrowserCommandValueCache() : null;
            writer.WriteStartArray();
            foreach (var command in commands)
            {
                writer.WriteStartObject();
                writer.WriteString("kind"u8, command.kind);
                writer.WritePropertyName("value"u8);
                if (valueCache == null || !valueCache.TryWrite(writer, command.value))
                    Write(writer, command.value);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.Flush();
            return Encoding.UTF8.GetString(output.WrittenSpan);
        }
        finally
        {
            // Image uploads can dwarf an ordinary frame. Do not retain their peak
            // capacity for the lifetime of the UI thread.
            if (output.Capacity > 2 * 1024 * 1024)
                buffer = null;
        }
    }

    static void Array(Utf8JsonWriter writer, ReadOnlySpan<byte> name, float[] values)
    {
        writer.WriteStartArray(name);
        foreach (float value in values)
            writer.WriteNumberValue(value);
        writer.WriteEndArray();
    }

    internal static void Write(Utf8JsonWriter writer, object value)
    {
        if (value is int number)
        {
            writer.WriteNumberValue(number);
            return;
        }
        if (value is float[] values)
        {
            writer.WriteStartArray();
            foreach (float item in values)
                writer.WriteNumberValue(item);
            writer.WriteEndArray();
            return;
        }
        writer.WriteStartObject();
        switch (value)
        {
            case TextCommand text:
                Array(writer, "rect"u8, text.rect);
                writer.WriteString("text"u8, text.text);
                writer.WriteNumber("fontSize"u8, text.fontSize);
                writer.WriteNumber("style"u8, text.style);
                writer.WriteNumber("anchor"u8, text.anchor);
                Array(writer, "color"u8, text.color);
                break;
            case FillCommand fill:
                Array(writer, "rect"u8, fill.rect);
                Array(writer, "color"u8, fill.color);
                writer.WriteNumber("rounding"u8, fill.rounding);
                break;
            case TextureCommand texture:
                writer.WriteNumber("id"u8, texture.id);
                Array(writer, "rect"u8, texture.rect);
                Array(writer, "uv"u8, texture.uv);
                Array(writer, "color"u8, texture.color);
                break;
            case ImageCommand image:
                writer.WriteNumber("id"u8, image.id);
                writer.WriteNumber("width"u8, image.width);
                writer.WriteNumber("height"u8, image.height);
                writer.WriteString("pixels"u8, image.pixels);
                break;
            case ImageBatchCommand batch:
                writer.WriteNumber("id"u8, batch.id);
                Array(writer, "color"u8, batch.color);
                writer.WritePropertyName("quads"u8);
                quads.Write(writer, batch.quads, BrowserGuiJson.Default.Options);
                break;
            case TargetCommand target:
                writer.WriteNumber("id"u8, target.id);
                writer.WriteNumber("width"u8, target.width);
                writer.WriteNumber("height"u8, target.height);
                break;
            default:
                throw new InvalidOperationException("Unsupported drawing command value");
        }
        writer.WriteEndObject();
    }
}
