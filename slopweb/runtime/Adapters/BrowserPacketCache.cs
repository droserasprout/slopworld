// Retain one immutable wire packet. Native drawing still runs; only serialization
// may be reused after comparing every command and the exact bits of every float.
using System.Runtime.InteropServices;
using static BrowserAdapters.BrowserGui;

namespace BrowserAdapters;

internal sealed class BrowserPacketCache
{
    Command[]? previous;
    string? packet;

    internal void Clear()
    {
        previous = null;
        packet = null;
    }

    internal string? Find(List<Command> commands)
    {
        if (previous == null || previous.Length != commands.Count)
            return null;
        for (int i = 0; i < previous.Length; i++)
            if (
                previous[i].kind != commands[i].kind
                || !Equal(previous[i].value, commands[i].value)
            )
                return null;
        return packet;
    }

    internal void Store(List<Command> commands, string json)
    {
        Clear();
        if (json.Length > 2 * 1024 * 1024 || commands.Count > 4096)
            return;
        // Bound geometry independently of its compressed JSON representation.
        int floats = 0;
        foreach (var command in commands)
        {
            floats += command.value switch
            {
                float[] a => a.Length,
                TextCommand v => v.rect.Length + v.color.Length,
                FillCommand v => v.rect.Length + v.color.Length,
                TextureCommand v => v.rect.Length + v.uv.Length + v.color.Length,
                ImageBatchCommand v => v.color.Length + v.quads.Count,
                ImageCommand or TargetCommand or int => 0,
                _ => 262145,
            };
            if (floats > 262144)
                return;
        }
        previous = commands.Select(c => new Command(c.kind, Copy(c.value))).ToArray();
        packet = json;
    }

    static bool Bits(ReadOnlySpan<float> a, ReadOnlySpan<float> b) =>
        MemoryMarshal.AsBytes(a).SequenceEqual(MemoryMarshal.AsBytes(b));

    static bool Equal(object a, object b) =>
        (a, b) switch
        {
            (float[] x, float[] y) => Bits(x, y),
            (int x, int y) => x == y,
            (ImageCommand x, ImageCommand y) => x == y,
            (TargetCommand x, TargetCommand y) => x == y,
            (TextCommand x, TextCommand y) => x.text == y.text
                && x.style == y.style
                && x.anchor == y.anchor
                && BitConverter.SingleToInt32Bits(x.fontSize)
                    == BitConverter.SingleToInt32Bits(y.fontSize)
                && Bits(x.rect, y.rect)
                && Bits(x.color, y.color),
            (FillCommand x, FillCommand y) => BitConverter.SingleToInt32Bits(x.rounding)
                == BitConverter.SingleToInt32Bits(y.rounding)
                && Bits(x.rect, y.rect)
                && Bits(x.color, y.color),
            (TextureCommand x, TextureCommand y) => x.id == y.id
                && Bits(x.rect, y.rect)
                && Bits(x.uv, y.uv)
                && Bits(x.color, y.color),
            (ImageBatchCommand x, ImageBatchCommand y) => x.id == y.id
                && Bits(x.color, y.color)
                && Bits(CollectionsMarshal.AsSpan(x.quads), CollectionsMarshal.AsSpan(y.quads)),
            _ => false,
        };

    static object Copy(object value) =>
        value switch
        {
            float[] a => a.ToArray(),
            TextCommand v => v with { rect = v.rect.ToArray(), color = v.color.ToArray() },
            FillCommand v => v with { rect = v.rect.ToArray(), color = v.color.ToArray() },
            TextureCommand v => v with
            {
                rect = v.rect.ToArray(),
                uv = v.uv.ToArray(),
                color = v.color.ToArray(),
            },
            ImageBatchCommand v => v with { color = v.color.ToArray(), quads = new(v.quads) },
            ImageCommand or TargetCommand or int => value,
            _ => throw new InvalidOperationException("Unsupported retained command"),
        };
}
