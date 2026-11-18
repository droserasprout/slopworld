using BrowserAdapters;

static class GlyphRunTests
{
    internal static void Run()
    {
        var line = new BrowserTextCache<float>.Line(new float[12], 12);
        var cache = line.Drawing = new BrowserGlyphRun();
        float[] key = Enumerable.Range(0, 32).Select(i => (float)i).ToArray();
        float[] quad = [1, 2, 3, 4, 0, 0, 1, 1];
        cache.Begin(key);
        cache.Add(quad);
        if (cache.Matches(key))
            throw new Exception("Partial geometry was reused");
        cache.Complete();
        if (!cache.Matches(key))
            throw new Exception("Completed geometry was not reused");
        for (int i = 0; i < key.Length; i++)
        {
            var changed = key.ToArray();
            changed[i] += .25f;
            if (cache.Matches(changed))
                throw new Exception("Geometry input was omitted from identity");
        }
        var signed = key.ToArray();
        signed[0] = -0f;
        if (cache.Matches(signed))
            throw new Exception("Geometry key lost signed zero");
        key[1] = 99;
        quad[0] = 99;
        if (cache.Matches(key) || cache.Quads[0] != 1)
            throw new Exception("Geometry retained mutable inputs");
        cache.Begin(key);
        cache.Complete();
        if (!cache.Quads.IsEmpty || !cache.Matches(key))
            throw new Exception("Empty replacement retained stale glyphs");
        cache.Begin(key);
        if (cache.Matches(key))
            throw new Exception("Interrupted replacement reused previous geometry");
        cache.Begin(key);
        cache.Add(quad);
        cache.Complete();
        if (!cache.Matches(key) || cache.Quads.Length != 8)
            throw new Exception("Geometry did not recover after interrupted replacement");

        var single = new List<BrowserGui.Command>();
        var bulk = new List<BrowserGui.Command>();
        float[] values = Enumerable.Range(0, 8 * 2300).Select(i => i / 7f).ToArray();
        // A run starts in an existing batch and crosses two batch boundaries.
        BrowserGui.AppendImageBatch(single, 1, 1, 1, 1, 1, quad);
        BrowserGui.AppendImageBatch(bulk, 1, 1, 1, 1, 1, quad);
        for (int i = 0; i < values.Length; i += 8)
            BrowserGui.AppendImageBatch(single, 1, 1, 1, 1, 1, values.AsSpan(i, 8));
        BrowserGui.AppendImageBatch(bulk, 1, 1, 1, 1, 1, values);
        if (BrowserCommandWriter.Serialize(single) != BrowserCommandWriter.Serialize(bulk))
            throw new Exception("Bulk glyph replay changed batch geometry or boundaries");
        foreach (var command in bulk)
            if (((BrowserGui.ImageBatchCommand)command.value).quads.Capacity > 8192)
                throw new Exception("Bulk glyph replay exceeded batch capacity");
        Console.WriteLine(
            "Projected glyphs check every key bit, detach storage, recover after partial builds, and replay exact bounded batches."
        );
    }
}
