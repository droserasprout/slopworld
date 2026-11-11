// Execute the real glyph emitter against synthetic metrics and CPU-only pixels.
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BrowserAdapters;
using UnityEngine;
using UnityStubs.Gui;

static class GlyphChecks
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run(bool benchmark)
    {
        const int index = 99;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.SetPixels32([Color.white, Color.red, Color.green, Color.blue]);
        texture.Apply(false, false);
        var face = new BrowserFontBank.Face
        {
            Hash = "synthetic-glyph-check",
            Size = 15,
            Density = 1,
            Ascent = 11.25f,
            LineAscent = 12,
            Descent = 3,
            FirstLine = 16,
            Pitch = 17.25f,
            Spacing = 17.25f,
            Texture = texture,
        };
        for (char c = ' '; c <= '~'; c++)
            face.Glyphs[c] = new TmpGlyphMetrics
            {
                found = true,
                visible = c != ' ',
                advanceX = 7.3f + c % 3 * .25f,
                x0 = -.75f,
                y0 = -2.25f,
                x1 = 6.5f,
                y1 = 12.5f,
                u0 = .125f,
                v0 = .125f,
                u1 = .875f,
                v1 = .875f,
            };
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        (
            (Dictionary<int, BrowserFontBank.Face>)
                typeof(BrowserFontBank)
                    .GetField("faces", flags)!
                    .GetValue(BrowserFontBank.Instance)!
        ).Add(index, face);
        (
            (List<BrowserFontBank.Face>)
                typeof(BrowserFontBank)
                    .GetField("variants", flags)!
                    .GetValue(BrowserFontBank.Instance)!
        ).Add(face);
        // Distinct metrics make accidental style/density reuse observable in packets.
        foreach (FontStyle style in Enum.GetValues<FontStyle>())
        foreach (float density in new[] { 1f, 5f / 3, 2f })
        {
            if (style == FontStyle.Normal && density == 1)
                continue;
            var variant = new BrowserFontBank.Face
            {
                Hash = face.Hash,
                Style = style,
                Size = face.Size,
                Density = density,
                Ascent = face.Ascent + (int)style * .1f,
                LineAscent = face.LineAscent,
                Descent = face.Descent,
                FirstLine = face.FirstLine,
                Pitch = face.Pitch,
                Spacing = face.Spacing,
                Texture = texture,
            };
            foreach (var pair in face.Glyphs)
            {
                var glyph = pair.Value;
                glyph.x0 += (int)style * .2f + density * .1f;
                glyph.advanceX += (int)style * .1f;
                variant.Glyphs.Add(pair.Key, glyph);
            }
            (
                (List<BrowserFontBank.Face>)
                    typeof(BrowserFontBank)
                        .GetField("variants", flags)!
                        .GetValue(BrowserFontBank.Instance)!
            ).Add(variant);
        }
        var original = new BrowserGui();
        var cached = new BrowserGui();
        var text = "glyph clipping Agyp 0123";
        string Frame(BrowserGui gui, bool cache, int scenario)
        {
            BrowserGui.ConfigureGlyphCache(cache);
            float scale =
                scenario % 3 == 0 ? 1
                : scenario % 3 == 1 ? 5f / 3
                : 2;
            var matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            matrix.m03 = .35f * scenario;
            matrix.m13 = -.2f * scenario;
            if (scenario == 18)
                matrix.m11 += .0005f;
            gui.SetMatrix(matrix);
            gui.PushScrollView(new Rect(2, 3, 280, 80), new Vector2(-1.3f, 2.75f));
            gui.PushClipRect(new Rect(3, 2, scenario == 4 ? 0 : 250, scenario == 5 ? 0 : 70));
            var rect = new Rect(scenario % 4 - 5.25f, -3.75f, 275, 70);
            var clip = scenario % 2 == 0 ? new Rect(1.25f, 0, 91.5f, 17.25f) : default;
            var color = new Color(.7f, .3f, .9f, scenario % 2 == 0 ? .5f : 1);
            gui.DrawText(
                rect,
                scenario == 7 ? text + "\n" + text : text,
                index,
                15,
                FontStyle.Normal,
                (TextAnchor)(scenario % 9),
                false,
                color,
                clip,
                false
            );
            gui.FillRect(new Rect(1, 2, 3, 4), Color.red);
            gui.DrawText(
                rect,
                text,
                index,
                15,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                false,
                color,
                clip,
                false
            );
            gui.PopClipRect();
            gui.PopGroup();
            // Cross a partially filled batch's 8192-float boundary.
            if (scenario == 19)
                gui.DrawText(
                    new Rect(0, 0, 20000, 70),
                    new string('x', 1400),
                    index,
                    15,
                    FontStyle.Normal,
                    TextAnchor.UpperLeft,
                    false,
                    Color.white,
                    default,
                    false
                );
            return gui.Drain();
        }
        for (int scenario = 0; scenario < 20; scenario++)
        for (int repeat = 0; repeat < 3; repeat++)
        {
            string expected = Frame(original, false, scenario);
            string actual = Frame(cached, true, scenario);
            if (expected != actual)
                throw new Exception($"Glyph geometry cache changed packet at {scenario}/{repeat}");
        }
        // Reused geometry must still validate mutable pixels and discarded uploads.
        texture.SetPixels32([Color.red, Color.white, Color.blue, Color.green]);
        texture.Apply(false, false);
        BrowserGui.TextureApplied(texture);
        if (Frame(original, false, 1) != Frame(cached, true, 1))
            throw new Exception("Cached geometry skipped an image invalidation");
        var fresh = new BrowserGui();
        BrowserGui.ConfigureGlyphCache(true);
        fresh.DrawText(
            new Rect(0, 0, 200, 20),
            text,
            index,
            15,
            FontStyle.Normal,
            TextAnchor.UpperLeft,
            false,
            Color.white,
            default,
            false
        );
        fresh.Discard();
        if (Frame(new BrowserGui(), false, 1) != Frame(fresh, true, 1))
            throw new Exception("Discarded upload was lost when geometry was reused");
        Console.WriteLine(
            "Real glyph packets match with cache off/on across clips, scales, anchors, scroll offsets, batching, image mutation and discarded uploads."
        );
        LabelChecks.Run(index);
        CellChecks.Run(index, texture, benchmark);
        ImageChecks.RunCells(index);
        if (benchmark)
            Benchmark(index);
        BrowserGui.ConfigureGlyphCache(false);
    }

    static void Benchmark(int index)
    {
        var labels = Enumerable
            .Range(0, 80)
            .Select(i => $"label {i:00} Agyp terminal status")
            .ToArray();
        var gui = new BrowserGui();
        void Draw(bool cache, int movement = 0)
        {
            BrowserGui.ConfigureGlyphCache(cache);
            gui.PushClipRect(new Rect(0, 0, 600, 1600));
            for (int i = 0; i < labels.Length; i++)
                gui.DrawText(
                    new Rect(2 + movement, i * 18, 600, 18),
                    labels[i],
                    index,
                    15,
                    FontStyle.Normal,
                    TextAnchor.UpperLeft,
                    false,
                    Color.white,
                    default,
                    false
                );
            gui.PopClipRect();
        }
        Draw(true);
        gui.Drain();
        for (int i = 0; i < 100; i++)
        {
            Draw(i % 2 == 0);
            gui.Discard();
        }
        foreach (bool moving in new[] { false, true })
            for (int round = 0; round < 6; round++)
                foreach (
                    bool cache in round % 2 == 0 ? new[] { false, true } : new[] { true, false }
                )
                {
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    for (int i = 0; i < 1000; i++)
                    {
                        Draw(cache, moving ? i % 2 : 0);
                        gui.Discard();
                    }
                    Console.WriteLine(
                        $"glyph-benchmark moving={moving} cache={cache} round={round} ms/frame={Stopwatch.GetElapsedTime(start).TotalMilliseconds / 1000:F4} bytes/frame={(GC.GetAllocatedBytesForCurrentThread() - allocated) / 1000} labels={labels.Length}"
                    );
                }
    }
}
