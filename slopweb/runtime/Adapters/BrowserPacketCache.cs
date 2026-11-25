// Retain one immutable wire packet. Native drawing still runs; only serialization
// may be reused after comparing every command and the exact bits of every float.
using System.Runtime.InteropServices;
using static BrowserAdapters.BrowserGui;

namespace BrowserAdapters;

internal sealed class BrowserPacketCache
{
    Command[]? previous;
    string? packet;
    long generation;
    internal long PacketId { get; private set; }

    // -1 means the cache had no same-length snapshot to compare.
    internal int LastMatchedCommands { get; private set; }

    bool enabled;

    internal bool Configure(bool value)
    {
        bool wasEnabled = enabled;
        enabled = value;
        Clear();
        return wasEnabled;
    }

    internal string Serialize(List<Command> commands, out bool reused)
    {
        int matched = 0;
        var cached = enabled ? Find(commands, out matched) : null;
        reused = cached != null;
        LastMatchedCommands =
            reused ? commands.Count
            : enabled && previous?.Length == commands.Count ? matched
            : -1;
        if (cached != null)
            return cached;
        var json = BrowserCommandWriter.Serialize(commands);
        if (enabled)
            Store(commands, json, matched);
        return json;
    }

    internal void Clear()
    {
        previous = null;
        packet = null;
        PacketId = 0;
    }

    internal string? Find(List<Command> commands) => Find(commands, out _);

    string? Find(List<Command> commands, out int matched)
    {
        matched = 0;
        if (previous == null || previous.Length != commands.Count)
            return null;
        for (int i = 0; i < previous.Length; i++)
            if (
                previous[i].kind != commands[i].kind
                || !Equal(previous[i].value, commands[i].value)
            )
            {
                matched = i;
                return null;
            }
        return packet;
    }

    internal void Store(List<Command> commands, string json) => Store(commands, json, 0);

    void Store(List<Command> commands, string json, int matched)
    {
        var snapshot = previous?.Length == commands.Count ? previous : null;
        Clear();
        if (json.Length > 2 * 1024 * 1024 || commands.Count > 8192)
            return;
        // Bound geometry independently of its compressed JSON representation.
        bool screenOnly = true;
        int floats = 0;
        foreach (var command in commands)
        {
            screenOnly &=
                command.kind
                    is "fill"
                        or "text"
                        or "imageTexture"
                        or "imageBatch"
                        or "targetTexture"
                        or "matrix"
                        or "group"
                        or "clipScreen"
                        or "clip"
                        or "pop";
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
        // Find already compared the prefix against our private deep copies.
        // Replace only the suffix, publishing the snapshot after copying succeeds.
        snapshot ??= new Command[commands.Count];
        for (int i = matched; i < commands.Count; i++)
        {
            var command = commands[i];
            snapshot[i] = new Command(command.kind, Copy(command.value));
        }
        previous = snapshot;
        packet = json;
        // Only a caller retaining this exact screen packet can acknowledge it.
        // Resource and target mutations must always travel as ordered commands.
        if (screenOnly && generation < 9_007_199_254_740_991L)
            PacketId = ++generation;
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
                && x.repeat == y.repeat
                && Bits(x.rect, y.rect)
                && Bits(x.uv, y.uv)
                && Bits(x.color, y.color),
            (ImageBatchCommand x, ImageBatchCommand y) => x.id == y.id
                && x.repeat == y.repeat
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
