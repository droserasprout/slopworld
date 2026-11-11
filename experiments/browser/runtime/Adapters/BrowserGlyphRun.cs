// One projected run per bounded text-cache line. The drawing owner supplies every
// geometry input; tint, image upload lifetime and command ordering stay outside.
using System.Runtime.InteropServices;

namespace BrowserAdapters;

internal sealed class BrowserGlyphRun
{
    float[]? key;
    bool complete;
    readonly List<float> quads = new();
    internal int TraceCount;
    internal ReadOnlySpan<float> Quads => CollectionsMarshal.AsSpan(quads);

    internal bool Matches(ReadOnlySpan<float> value) =>
        complete
        && MemoryMarshal
            .Cast<float, int>(value)
            .SequenceEqual(MemoryMarshal.Cast<float, int>(key.AsSpan()));

    internal void Begin(ReadOnlySpan<float> value)
    {
        complete = false;
        if (key == null || key.Length != value.Length)
            key = new float[value.Length];
        value.CopyTo(key);
        quads.Clear();
        TraceCount = 0;
    }

    internal void Add(ReadOnlySpan<float> quad) => quads.AddRange(quad);

    internal void Complete() => complete = true;
}
