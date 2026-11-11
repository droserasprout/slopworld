// Bounded plain-text runs for one font face, including device-rounded advances.
// Coordinates, clipping, color and wrapping remain with the drawing owner.
namespace BrowserAdapters;

internal sealed class BrowserTextCache<T>(
    IReadOnlyDictionary<char, T> glyphs,
    Func<T, float> advance
)
    where T : struct
{
    internal sealed record Line(T[] Glyphs, float Width)
    {
        // Eviction of the text run also releases its last projected geometry.
        internal BrowserGlyphRun? Drawing;
        float[]? carets;
        float caretScale;
        float measuredScale;
        float measuredWidth;

        internal float Measure(Func<T, float> advance, float scale)
        {
            if (measuredScale == scale)
                return measuredWidth;
            measuredScale = scale;
            measuredWidth = 0;
            foreach (var glyph in Glyphs)
                measuredWidth += MathF.Floor(advance(glyph) * scale + .5f) / scale;
            return measuredWidth;
        }

        // Allocate only for editable text, never ordinary sidebar labels. These
        // advances are the same native metrics used to build and draw the run.
        internal float[] Carets(Func<T, float> advance, float scale)
        {
            if (carets != null && caretScale == scale)
                return carets;
            caretScale = scale;
            carets = new float[Glyphs.Length + 1];
            for (int i = 0; i < Glyphs.Length; i++)
                carets[i + 1] = carets[i] + MathF.Floor(advance(Glyphs[i]) * scale + .5f) / scale;
            return carets;
        }

        internal int IndexAt(float x, Func<T, float> advance, float scale)
        {
            if (x <= 0)
                return 0;
            var positions = Carets(advance, scale);
            for (int i = 0; i < Glyphs.Length; i++)
                if (x < (positions[i] + positions[i + 1]) * .5f)
                    return i;
            return Glyphs.Length;
        }
    }

    internal sealed record Run(Line[] Lines, float Width)
    {
        internal float Measure(Func<T, float> advance, float scale)
        {
            float width = 0;
            foreach (var line in Lines)
                width = Math.Max(width, line.Measure(advance, scale));
            return width;
        }
    }

    sealed record Entry(string Text, Run? Run);

    const int MaxEntries = 512;
    const int MaxUnits = 16384;
    readonly Dictionary<string, LinkedListNode<Entry>> entries = new(StringComparer.Ordinal);
    readonly LinkedList<Entry> recent = new();
    int units;

    internal Run? Get(string text)
    {
        if (entries.TryGetValue(text, out var found))
        {
            recent.Remove(found);
            recent.AddLast(found);
            return found.Value.Run;
        }
        var run = Build(text);
        int cost = text.Length + 1;
        if (cost > MaxUnits)
            return run;
        while (entries.Count >= MaxEntries || units + cost > MaxUnits)
        {
            var oldest = recent.First!;
            recent.RemoveFirst();
            entries.Remove(oldest.Value.Text);
            units -= oldest.Value.Text.Length + 1;
        }
        entries.Add(text, recent.AddLast(new Entry(text, run)));
        units += cost;
        return run;
    }

    Run? Build(string text)
    {
        var lines = new List<Line>();
        float maximum = 0;
        foreach (string line in text.Split('\n'))
        {
            var values = new List<T>(line.Length);
            float width = 0;
            foreach (char c in line)
            {
                if (c == '\r')
                    continue;
                if (!glyphs.TryGetValue(c, out var glyph))
                    return null;
                values.Add(glyph);
                width += advance(glyph);
            }
            lines.Add(new Line(values.ToArray(), width));
            maximum = Math.Max(maximum, width);
        }
        return new Run(lines.ToArray(), maximum);
    }
}
