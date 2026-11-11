// Transport-only reuse of small value JSON across changing packets. The direct
// writer owns formatting; packet order and resource playback remain outside here.
using System.Buffers;
using System.Text.Json;
using static BrowserAdapters.BrowserGui;

namespace BrowserAdapters;

internal sealed class BrowserCommandValueCache(int entryLimit = 1024, int byteLimit = 262144)
{
    // Shape separates fields with identical bits but different JSON structure.
    // Integers capture float bits rather than float equality (notably signed zero).
    enum Shape
    {
        Fill,
        Clip,
        Matrix,
        Target,
    }

    readonly record struct Key(
        Shape Shape,
        int A,
        int B,
        int C,
        int D = 0,
        int E = 0,
        int F = 0,
        int G = 0,
        int H = 0,
        int I = 0
    );

    readonly Dictionary<Key, byte[]> entries = new();
    readonly Queue<Key> order = new();
    readonly ArrayBufferWriter<byte> scratch = new(512);
    Utf8JsonWriter? scratchWriter;
    internal int Count => entries.Count;
    internal int RetainedBytes { get; private set; }

    static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    static bool Identify(object value, out Key key)
    {
        key = default;
        switch (value)
        {
            case FillCommand { rect.Length: 4, color.Length: 4 } fill:
                key = new(
                    Shape.Fill,
                    Bits(fill.rect[0]),
                    Bits(fill.rect[1]),
                    Bits(fill.rect[2]),
                    Bits(fill.rect[3]),
                    Bits(fill.color[0]),
                    Bits(fill.color[1]),
                    Bits(fill.color[2]),
                    Bits(fill.color[3]),
                    Bits(fill.rounding)
                );
                return true;
            case float[] { Length: 4 } clip:
                key = new(Shape.Clip, Bits(clip[0]), Bits(clip[1]), Bits(clip[2]), Bits(clip[3]));
                return true;
            case float[] { Length: 6 } matrix:
                key = new(
                    Shape.Matrix,
                    Bits(matrix[0]),
                    Bits(matrix[1]),
                    Bits(matrix[2]),
                    Bits(matrix[3]),
                    Bits(matrix[4]),
                    Bits(matrix[5])
                );
                return true;
            case TargetCommand target:
                key = new(Shape.Target, target.id, target.width, target.height);
                return true;
            default:
                return false;
        }
    }

    internal bool TryWrite(Utf8JsonWriter writer, object value)
    {
        if (entryLimit <= 0 || byteLimit <= 0 || !Identify(value, out var key))
            return false;
        if (!entries.TryGetValue(key, out var payload))
        {
            scratch.Clear();
            // Store only a completed value. Exceptions (including non-finite
            // coordinates) leave no entry and the next miss resets the buffer.
            var candidate = scratchWriter ??= new Utf8JsonWriter(scratch);
            candidate.Reset(scratch);
            BrowserCommandWriter.Write(candidate, value);
            candidate.Flush();
            if (scratch.WrittenCount > Math.Min(512, byteLimit))
                return false;
            payload = scratch.WrittenSpan.ToArray();
            // FIFO avoids per-hit linked-list work; both payload bytes and key/
            // collection overhead are bounded. Keys never retain caller arrays.
            while (entries.Count >= entryLimit || RetainedBytes + payload.Length > byteLimit)
            {
                var oldest = order.Dequeue();
                RetainedBytes -= entries[oldest].Length;
                entries.Remove(oldest);
            }
            entries.Add(key, payload);
            order.Enqueue(key);
            RetainedBytes += payload.Length;
        }
        // Payloads originate solely from the same Utf8JsonWriter path above.
        writer.WriteRawValue(payload, skipInputValidation: true);
        return true;
    }
}
