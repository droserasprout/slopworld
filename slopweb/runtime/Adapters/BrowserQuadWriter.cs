// Transport only: pack large glyph batches without changing the JS numbers the
// numeric JSON path produced. Canvas still owns clipping and ordered playback.
using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrowserAdapters;

internal sealed class BrowserQuadWriter : JsonConverter<List<float>>
{
    // Tiny batches do not amortize a separate base64 decode and buffer in JS.
    internal const int PackedThreshold = 32;
    internal const int CacheLimit = 16384;
    internal const uint Magic = 0x31514c53; // SLQ1: double table followed by uint16 indices.

    [ThreadStatic]
    static Dictionary<int, double>? numbers;

    [ThreadStatic]
    static BrowserQuadCache? packets;

    internal static bool CacheEnabled = true;

    internal static bool ConfigureCache(bool enabled)
    {
        bool previous = CacheEnabled;
        CacheEnabled = enabled;
        packets = null;
        return previous;
    }

    public override List<float> Read(
        ref Utf8JsonReader reader,
        Type type,
        JsonSerializerOptions options
    ) => throw new NotSupportedException("GUI quad transport is write-only");

    public override void Write(
        Utf8JsonWriter writer,
        List<float> values,
        JsonSerializerOptions options
    )
    {
        if (values.Count < PackedThreshold || values.Count > ushort.MaxValue)
        {
            writer.WriteStartArray();
            foreach (float value in values)
                writer.WriteNumberValue(value);
            writer.WriteEndArray();
            return;
        }

        var sequence = CollectionsMarshal.AsSpan(values);
        var packetCache = CacheEnabled ? packets ??= new BrowserQuadCache() : null;
        ulong hash = packetCache == null ? 0 : BrowserQuadCache.Hash(sequence);
        if (packetCache?.Find(hash, sequence) is { } retained)
        {
            writer.WriteBase64StringValue(retained);
            return;
        }
        var cache = numbers ??= new Dictionary<int, double>();
        int indexOffset = checked(8 + values.Count * sizeof(double));
        byte[] bytes = ArrayPool<byte>.Shared.Rent(
            checked(indexOffset + values.Count * sizeof(ushort))
        );
        try
        {
            var indices = new Dictionary<int, ushort>(Math.Min(values.Count, 512));
            for (int i = 0; i < values.Count; i++)
            {
                float value = values[i];
                int key = BitConverter.SingleToInt32Bits(value);
                if (!indices.TryGetValue(key, out ushort index))
                {
                    index = checked((ushort)indices.Count);
                    indices.Add(key, index);
                    if (!cache.TryGetValue(key, out double number))
                    {
                        // A JSON float's shortest decimal parses as a JS double, NOT
                        // necessarily its exact float32 value (e.g. 0.1f -> 0.1).
                        // Cache that original parse result, including negative zero.
                        number = double.Parse(
                            JsonSerializer.Serialize(value),
                            CultureInfo.InvariantCulture
                        );
                        if (cache.Count >= CacheLimit)
                            cache.Clear();
                        cache.Add(key, number);
                    }
                    BinaryPrimitives.WriteDoubleLittleEndian(
                        bytes.AsSpan(8 + index * sizeof(double)),
                        number
                    );
                }
                BinaryPrimitives.WriteUInt16LittleEndian(
                    bytes.AsSpan(indexOffset + i * sizeof(ushort)),
                    index
                );
            }
            int tableEnd = 8 + indices.Count * sizeof(double);
            bytes.AsSpan(indexOffset, values.Count * sizeof(ushort)).CopyTo(bytes.AsSpan(tableEnd));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), indices.Count);
            int length = tableEnd + values.Count * sizeof(ushort);
            packetCache?.Store(hash, sequence, bytes.AsSpan(0, length));
            writer.WriteBase64StringValue(bytes.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }
}
