// Exercise the actual adapter converter and draw packets against the byte-cast contract.
using System.Diagnostics;
using System.Reflection;
using BrowserAdapters;
using UnityEngine;

static class ColorChecks
{
    static readonly Func<float, float> Channel = typeof(BrowserGui)
        .GetMethod("ColorChannel", BindingFlags.Static | BindingFlags.NonPublic)!
        .CreateDelegate<Func<float, float>>();

    static float Reference(float value) => (byte)(Math.Clamp(value, 0f, 1f) * 255f + .5f) / 255f;

    internal static void Run(int index, Texture2D texture, bool benchmark)
    {
        if (BrowserGui.ConfigureColorFastPath(false))
            throw new Exception("Color fast path must default off");
        if (BrowserGui.ConfigureColorFastPath(true))
            throw new Exception("Color switch did not return previous false");
        if (!BrowserGui.ConfigureColorFastPath(true) || !BrowserGui.ConfigureColorFastPath(false))
            throw new Exception("Color switch did not return previous true");
        var values = new List<float>
        {
            0f,
            -0f,
            float.Epsilon,
            -float.Epsilon,
            float.MinValue,
            float.MaxValue,
            float.NegativeInfinity,
            float.PositiveInfinity,
        };
        // Both signs, signaling/quiet forms and payloads must use the runtime's
        // original byte conversion rather than assuming a portable NaN result.
        foreach (
            uint bits in new uint[] { 0x7f800001, 0x7fc00000, 0x7fffffff, 0xff800001, 0xffc12345 }
        )
            values.Add(BitConverter.UInt32BitsToSingle(bits));
        for (int i = 0; i <= 255; i++)
        {
            foreach (float value in new[] { i / 255f, (i - .5f) / 255f })
                values.AddRange([MathF.BitDecrement(value), value, MathF.BitIncrement(value)]);
        }
        // Deterministic raw patterns exercise exponents/subnormals as well as
        // values concentrated around the useful quantization interval.
        uint random = 0x4c6f7253;
        for (int i = 0; i < 1_000_000; i++)
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            values.Add(BitConverter.UInt32BitsToSingle(random));
            values.Add((random & 0xffffff) / 16777216f);
        }
        foreach (bool enabled in new[] { false, true })
        {
            BrowserGui.ConfigureColorFastPath(enabled);
            foreach (float value in values)
                if (
                    BitConverter.SingleToInt32Bits(Channel(value))
                    != BitConverter.SingleToInt32Bits(Reference(value))
                )
                    throw new Exception(
                        $"Color bits changed: fast={enabled} input={BitConverter.SingleToUInt32Bits(value):x8}"
                    );
        }

        string Frame(BrowserGui gui, bool enabled, int scenario)
        {
            BrowserGui.ConfigureColorFastPath(enabled);
            for (int i = 0; i < 40; i++)
            {
                var color = new Color(
                    values[(scenario * 37 + i) % values.Count],
                    values[(scenario * 19 + i + 3) % values.Count],
                    i / 39f,
                    (i % 7) / 6f
                );
                var rect = new Rect(i * 8, 0, 8, 18);
                gui.FillRect(rect, color);
                gui.DrawTextureWithTexCoords(rect, texture, new Rect(0, 0, 1, 1), color);
                gui.DrawText(
                    rect,
                    "x",
                    index,
                    15,
                    FontStyle.Normal,
                    TextAnchor.UpperLeft,
                    false,
                    color
                );
                gui.DrawText(
                    new Rect(0, 20, 500, 18),
                    "color glyph run",
                    index,
                    15,
                    FontStyle.Normal,
                    TextAnchor.UpperLeft,
                    false,
                    color
                );
                gui.DrawText(
                    rect,
                    "fallback",
                    -1,
                    15,
                    FontStyle.Normal,
                    TextAnchor.UpperLeft,
                    false,
                    color
                );
            }
            return gui.Drain();
        }
        var reference = new BrowserGui();
        var fast = new BrowserGui();
        foreach (bool cache in new[] { false, true })
        foreach (bool cell in new[] { false, true })
        {
            BrowserGui.ConfigureGlyphCache(cache);
            BrowserGui.ConfigureCellFastPath(cell);
            for (int scenario = 0; scenario < 16; scenario++)
                if (Frame(reference, false, scenario) != Frame(fast, true, scenario))
                    throw new Exception(
                        $"Color draw packets changed: cache={cache} cell={cell} scenario={scenario}"
                    );
        }
        Console.WriteLine(
            $"Color byte-reference bits match for {values.Count} inputs per mode; fill, texture, single-cell, glyph-run and fallback packets match with glyph/cell switches off/on."
        );
        BrowserGui.ConfigureGlyphCache(false);
        BrowserGui.ConfigureCellFastPath(true);
        if (benchmark)
            Benchmark(index);
        BrowserGui.ConfigureColorFastPath(false);
    }

    static float sink;

    static void Benchmark(int index)
    {
        var gui = new BrowserGui();
        // Mixed terminal tints, repeated and changing; no NaNs in ordinary draws.
        var colors = Enumerable
            .Range(0, 256)
            .Select(i => new Color(i / 255f, (255 - i) / 255f, .37f, i % 3 == 0 ? .5f : 1f))
            .ToArray();
        void Draw(int frame)
        {
            for (int i = 0; i < 2671; i++)
                gui.DrawText(
                    new Rect(i % 100 * 8, i / 100 * 18, 8, 18),
                    "x",
                    index,
                    15,
                    FontStyle.Normal,
                    TextAnchor.UpperLeft,
                    false,
                    colors[(i + frame) % colors.Length]
                );
            gui.Discard();
        }
        for (int i = 0; i < 100; i++)
        {
            BrowserGui.ConfigureColorFastPath(i % 2 == 0);
            Draw(i);
        }
        Console.WriteLine(
            "Color benchmark: native CPU only; does not establish Jiterpreter or browser frame-time benefit."
        );
        for (int round = 0; round < 6; round++)
            foreach (bool enabled in round % 2 == 0 ? new[] { false, true } : new[] { true, false })
            {
                BrowserGui.ConfigureColorFastPath(enabled);
                long start = Stopwatch.GetTimestamp();
                float sum = 0;
                const int passes = 10000;
                for (int pass = 0; pass < passes; pass++)
                    foreach (var color in colors)
                        sum +=
                            Channel(color.r)
                            + Channel(color.g)
                            + Channel(color.b)
                            + Channel(color.a);
                sink = sum;
                Console.WriteLine(
                    $"color-channel-benchmark fast={enabled} round={round} ns/channel={Stopwatch.GetElapsedTime(start).TotalNanoseconds / (passes * colors.Length * 4):F3} checksum={sink:R}"
                );
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                start = Stopwatch.GetTimestamp();
                const int frames = 300;
                for (int frame = 0; frame < frames; frame++)
                    Draw(frame);
                Console.WriteLine(
                    $"color-glyph-benchmark fast={enabled} round={round} ms/frame={Stopwatch.GetElapsedTime(start).TotalMilliseconds / frames:F4} bytes/frame={(GC.GetAllocatedBytesForCurrentThread() - allocated) / frames} cells=2671"
                );
            }
    }
}
