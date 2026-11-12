// Retain encoded payloads only for bit-identical quad sequences. Color, image,
// clip, target and command ordering remain outside this transport-only cache.
using System.Runtime.InteropServices;

namespace BrowserAdapters;

internal sealed class BrowserQuadCache
{
    sealed record Entry(float[] Values, byte[] Payload, LinkedListNode<ulong> Order);

    readonly Dictionary<ulong, Entry> entries = new();
    readonly LinkedList<ulong> order = new();
    readonly int entryLimit;
    readonly int valueLimit;
    int retainedValues;

    internal BrowserQuadCache(int entryLimit = 512, int valueLimit = 1048576)
    {
        this.entryLimit = entryLimit;
        this.valueLimit = valueLimit;
    }

    internal int Count => entries.Count;
    internal int RetainedValues => retainedValues;

    internal static ulong Hash(ReadOnlySpan<float> values)
    {
        ulong hash = 14695981039346656037UL;
        foreach (int bits in MemoryMarshal.Cast<float, int>(values))
            hash = unchecked((hash ^ (uint)bits) * 1099511628211UL);
        return hash ^ (ulong)values.Length;
    }

    internal byte[]? Find(ulong hash, ReadOnlySpan<float> values)
    {
        if (
            entries.TryGetValue(hash, out var entry)
            && MemoryMarshal
                .AsBytes(values)
                .SequenceEqual(MemoryMarshal.AsBytes(entry.Values.AsSpan()))
        )
        {
            order.Remove(entry.Order);
            order.AddLast(entry.Order);
            return entry.Payload;
        }
        return null;
    }

    internal void Store(ulong hash, ReadOnlySpan<float> values, ReadOnlySpan<byte> payload)
    {
        if (values.Length > valueLimit || entryLimit <= 0)
            return;
        if (entries.Remove(hash, out var previous))
        {
            retainedValues -= previous.Values.Length;
            order.Remove(previous.Order);
        }
        while (entries.Count >= entryLimit || retainedValues + values.Length > valueLimit)
        {
            ulong oldest = order.First!.Value;
            retainedValues -= entries[oldest].Values.Length;
            entries.Remove(oldest);
            order.RemoveFirst();
        }
        entries.Add(hash, new Entry(values.ToArray(), payload.ToArray(), order.AddLast(hash)));
        retainedValues += values.Length;
    }
}
