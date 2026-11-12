using BrowserAdapters;

static class TextBoundsTests
{
    internal static void Run()
    {
        var metrics = new Dictionary<char, (float Advance, float Left)>
        {
            ['a'] = (7.25f, 1),
            ['b'] = (0, -25.5f),
            ['c'] = (-.01f, 0),
            ['d'] = (float.PositiveInfinity, 0),
            ['e'] = (1, float.NaN),
        };
        var cache = new BrowserTextCache<(float Advance, float Left)>(
            metrics,
            x => x.Advance,
            x => x.Left,
            x => float.IsFinite(x.Advance) && float.IsFinite(x.Left),
            x => Math.Max(Math.Abs(x.Advance), Math.Abs(x.Left))
        );
        var line = cache.Get("ab")!.Lines[0];
        if (
            !line.NonnegativeAdvances
            || !line.SafeDrawingBounds
            || line.MinimumLeftBearing != -25.5f
            || line.MaximumMetricMagnitude != 25.5f
        )
            throw new Exception("Run bounds omitted zero advances or negative overhang");
        foreach (string text in new[] { "ac", "ad", "ae" })
            if (cache.Get(text)!.Lines[0].NonnegativeAdvances)
                throw new Exception("Unsafe metrics admitted tail termination: " + text);
        metrics['b'] = (-100, -100);
        if (
            !line.NonnegativeAdvances
            || line.Glyphs[1].Advance != 0
            || line.MinimumLeftBearing != -25.5f
        )
            throw new Exception("Run bounds did not retain the copied glyph metrics");
        Console.WriteLine(
            "Text bounds retain immutable metrics and reject negative advances and nonfinite metrics."
        );
    }
}
