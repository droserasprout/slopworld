// Compare actual adapter packets, including uploaded RGBA pixels, rather than a
// second model of glyph placement. Identical commands/pixels imply identical paint.
using System.Diagnostics;
using BrowserAdapters;
using UnityEngine;

static class CellChecks
{
    internal static void Run(int index, Texture2D texture, bool benchmark)
    {
        BrowserGui.ConfigureGlyphCache(false);
        var reference = new BrowserGui();
        var fast = new BrowserGui();
        string Frame(BrowserGui gui, bool enabled, int scenario)
        {
            BrowserGui.ConfigureCellFastPath(enabled);
            float scale = new[] { .75f, 1, 1.25f, 5f / 3, 2 }[scenario % 5];
            var matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            matrix.m03 = .35f * scenario;
            matrix.m13 = -.2f * scenario;
            if (scenario % 13 == 0)
                matrix.m11 += .0005f;
            if (scenario % 17 == 0)
                matrix.m01 = .125f; // Unsupported transforms retain the reference path.
            BrowserFontBank.ConfigureDensity(new[] { 1f, 5f / 3, 2f }[scenario % 3]);
            gui.SetMatrix(matrix);
            gui.PushScrollView(new Rect(2, 3, 280, 80), new Vector2(-1.3f, 2.75f));
            gui.PushClipRect(new Rect(3, 2, scenario == 4 ? 0 : 250, scenario == 5 ? 0 : 70));
            var color = new Color(.7f, .3f, .9f, scenario % 2 == 0 ? .5f : 1);
            for (int i = 0; i < 1100; i++)
            {
                // Spaces, literal markup delimiters, all ASCII, plus non-ASCII
                // and multiline fallback; enough visible cells to split batches.
                int kind = i % 101;
                string text = kind switch
                {
                    96 => "\u00e9",
                    97 => "ab",
                    98 => "\n",
                    99 => "\r",
                    100 => "",
                    _ => ((char)(' ' + i % 95)).ToString(),
                };
                var rect = new Rect(
                    i % 28 * 8 - 5.25f,
                    i % 4 * 18 - 3.75f,
                    scenario % 7 == 0 ? 3 : 8,
                    scenario % 11 == 0 ? 9 : 18
                );
                var clip =
                    scenario % 2 == 0
                        ? new Rect(rect.x + .25f, rect.y, 7.5f, rect.height)
                        : default;
                gui.DrawText(
                    rect,
                    text,
                    index,
                    scenario % 2 == 0 ? 15 : 0,
                    (FontStyle)(scenario / 5 % 4),
                    (TextAnchor)(scenario % 9),
                    scenario % 3 == 0,
                    color,
                    clip,
                    scenario % 2 == 0
                );
                if (i == 400)
                    gui.FillRect(new Rect(1, 2, 3, 4), Color.red);
            }
            gui.PopClipRect();
            gui.PopGroup();
            return gui.Drain();
        }
        for (int scenario = 0; scenario < 90; scenario++)
        for (int repeat = 0; repeat < 2; repeat++)
            if (Frame(reference, false, scenario) != Frame(fast, true, scenario))
                throw new Exception($"Cell fast path changed packet at {scenario}/{repeat}");
        texture.SetPixels32([Color.blue, Color.green, Color.red, Color.white]);
        texture.Apply(false, false);
        BrowserGui.TextureApplied(texture);
        if (Frame(reference, false, 1) != Frame(fast, true, 1))
            throw new Exception("Cell fast path changed mutated atlas upload");
        var discarded = new BrowserGui();
        BrowserGui.ConfigureCellFastPath(true);
        discarded.DrawText(
            new Rect(0, 0, 8, 18),
            "x",
            index,
            15,
            FontStyle.Normal,
            TextAnchor.UpperLeft,
            false,
            Color.white
        );
        discarded.Discard();
        if (Frame(new BrowserGui(), false, 1) != Frame(discarded, true, 1))
            throw new Exception("Cell fast path lost discarded atlas upload");
        Console.WriteLine(
            "Single-cell reference/fast packets and RGBA uploads match across 198,000 draws, anchors, fractional scales, clips, wrapping, fallback, batching, mutation and discard."
        );
        if (benchmark)
            Benchmark(index);
        BrowserGui.ConfigureCellFastPath(true);
    }

    static void Benchmark(int index)
    {
        var cells = Enumerable.Range(0, 95).Select(i => ((char)(' ' + i)).ToString()).ToArray();
        var gui = new BrowserGui();
        gui.SetMatrix(Matrix4x4.Scale(new Vector3(5f / 3, 5f / 3, 1)));
        void Draw(bool enabled, int movement)
        {
            BrowserGui.ConfigureCellFastPath(enabled);
            gui.PushClipRect(new Rect(0, 0, 1000, 700));
            for (int i = 0; i < 2671; i++)
            {
                var rect = new Rect(2 + i % 100 * 8 + movement, i / 100 * 18, 8, 18);
                gui.DrawText(
                    rect,
                    cells[i % cells.Length],
                    index,
                    15,
                    FontStyle.Normal,
                    TextAnchor.UpperLeft,
                    false,
                    Color.white,
                    rect,
                    false
                );
            }
            gui.PopClipRect();
        }
        Draw(true, 0);
        gui.Drain();
        for (int i = 0; i < 100; i++)
        {
            Draw(i % 2 == 0, i % 2);
            gui.Discard();
        }
        foreach (bool moving in new[] { false, true })
            for (int round = 0; round < 6; round++)
                foreach (
                    bool enabled in round % 2 == 0 ? new[] { false, true } : new[] { true, false }
                )
                {
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    const int frames = 300;
                    for (int i = 0; i < frames; i++)
                    {
                        Draw(enabled, moving ? i % 2 : 0);
                        gui.Discard();
                    }
                    Console.WriteLine(
                        $"cell-benchmark moving={moving} fast={enabled} round={round} ms/frame={Stopwatch.GetElapsedTime(start).TotalMilliseconds / frames:F4} bytes/frame={(GC.GetAllocatedBytesForCurrentThread() - allocated) / frames} cells=2671"
                    );
                }
    }
}
