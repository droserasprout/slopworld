using System.Buffers;
using System.Text;
using System.Text.Json;
using BrowserAdapters;

internal static class CommandValueCacheTests
{
    internal static void Run()
    {
        bool previous = BrowserGui.ConfigureCommandValueCache(false);
        if (
            BrowserGui.ConfigureCommandValueCache(true)
            || !BrowserGui.ConfigureCommandValueCache(true)
        )
            throw new Exception("Value cache toggle did not return previous setting");
        try
        {
            var random = new Random(173901);
            float[] adversarial =
            [
                0,
                -0f,
                float.Epsilon,
                -float.Epsilon,
                float.MaxValue,
                float.MinValue,
                .1f,
                1e-30f,
                1e30f,
                16777216,
                float.BitIncrement(1),
            ];
            float Next()
            {
                if (random.Next(3) == 0)
                    return adversarial[random.Next(adversarial.Length)];
                float value;
                do value = BitConverter.Int32BitsToSingle(
                    (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1)
                );
                while (!float.IsFinite(value));
                return value;
            }
            for (int trial = 0; trial < 3000; trial++)
            {
                float[] rect = [Next(), Next(), Next(), Next()];
                float[] color = [Next(), Next(), Next(), Next()];
                float[] matrix = [Next(), Next(), Next(), Next(), Next(), Next()];
                List<BrowserGui.Command> commands =
                [
                    new("fill", new BrowserGui.FillCommand(rect, color, Next())),
                    new("matrix", matrix),
                    new("clipScreen", rect),
                    new(
                        "target",
                        new BrowserGui.TargetCommand(random.Next(), int.MinValue, int.MaxValue)
                    ),
                    new(
                        "text",
                        new BrowserGui.TextCommand(rect, "é\"\\\n" + trial, Next(), 2, 3, color)
                    ),
                ];
                Check(commands);
                // Every array element participates in identity, including aliases
                // between a fill and a clip and in-place changes after a cache hit.
                foreach (var array in new[] { rect, color, matrix })
                    for (int i = 0; i < array.Length; i++)
                    {
                        array[i] = Next();
                        Check(commands);
                    }
                Check(commands.AsEnumerable().Reverse().ToList());
            }
            foreach (float zero in new[] { 0f, -0f, 0f, -0f })
                Check([
                    new(
                        "fill",
                        new BrowserGui.FillCommand(
                            [zero, zero, zero, zero],
                            [zero, zero, zero, zero],
                            zero
                        )
                    ),
                    new("matrix", new[] { zero, zero, zero, zero, zero, zero }),
                ]);

            foreach (
                float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity }
            )
            {
                foreach (
                    object value in new object[]
                    {
                        new float[] { invalid, 0, 0, 0 },
                        new BrowserGui.FillCommand([0, 0, 0, 0], [0, 0, 0, 0], invalid),
                        new BrowserGui.FillCommand([0, 0, 0, 0], [0, invalid, 0, 0], 0),
                    }
                )
                {
                    try
                    {
                        BrowserCommandWriter.Serialize([new("fill", value)]);
                        throw new Exception("Cache accepted non-finite value");
                    }
                    catch (ArgumentException) { }
                    Check([new("clipScreen", new float[] { 0, 0, 0, 0 })]);
                }
            }
            foreach (float rounding in new[] { 0f, -0f, 3.5f, float.MaxValue })
                Check([
                    new("fill", new BrowserGui.FillCommand([0, 0, 0, 0], [0, 0, 0, 0], rounding)),
                ]);
            Check([new("target", new BrowserGui.TargetCommand(0, 0, 0))]);
            foreach (int changed in new[] { 0, 1, 2 })
                Check([
                    new(
                        "target",
                        new BrowserGui.TargetCommand(
                            changed == 0 ? int.MinValue : 0,
                            changed == 1 ? int.MaxValue : 0,
                            changed == 2 ? -1 : 0
                        )
                    ),
                ]);
            Check([
                new("matrix", new float[5]),
                new("matrix", new float[10000]),
                new("fill", new BrowserGui.FillCommand(new float[5], new float[3], -0f)),
                new("image", new BrowserGui.ImageCommand(1, 1, 1, new string('a', 2200000))),
            ]);
            BoundsAndExclusions();
        }
        finally
        {
            if (!BrowserGui.ConfigureCommandValueCache(false))
                throw new Exception("Disable did not return previous value");
            BrowserGui.ConfigureCommandValueCache(previous);
        }
        Console.WriteLine(
            "Value cache preserves exact randomized JSON, mutable arrays, signed zero, failure recovery and bounds."
        );
    }

    static void Check(List<BrowserGui.Command> commands)
    {
        string expected = JsonSerializer.Serialize(commands);
        if (
            expected != JsonSerializer.Serialize(commands, BrowserGuiJson.Default.ListCommand)
            || expected != BrowserCommandWriter.Serialize(commands)
            || expected != BrowserCommandWriter.Serialize(commands)
        )
            throw new Exception("Cold/warm command value cache changed JSON bytes");
    }

    static void BoundsAndExclusions()
    {
        string? Write(BrowserCommandValueCache cache, object value)
        {
            var output = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(output);
            if (!cache.TryWrite(writer, value))
                return null;
            writer.Flush();
            return Encoding.UTF8.GetString(output.WrittenSpan);
        }
        var cache = new BrowserCommandValueCache(3, 100);
        for (int i = 0; i < 100; i++)
        {
            var target = new BrowserGui.TargetCommand(i, 1, 2);
            if (
                Write(cache, target) != JsonSerializer.Serialize(target)
                || cache.Count > 3
                || cache.RetainedBytes > 100
            )
                throw new Exception("Value cache exceeded bounds or failed after eviction");
        }
        var byteBounded = new BrowserCommandValueCache(100, 60);
        for (int i = 0; i < 100; i++)
        {
            var target = new BrowserGui.TargetCommand(i, 100, 200);
            if (
                Write(byteBounded, target) != JsonSerializer.Serialize(target)
                || byteBounded.Count != 1
                || byteBounded.RetainedBytes > 60
            )
                throw new Exception(
                    "Payload-byte bound did not evict independently of entry count"
                );
        }
        foreach (
            object value in new object[]
            {
                new float[10000],
                new float[5],
                new BrowserGui.FillCommand(new float[10000], new float[4], 0),
                new BrowserGui.ImageCommand(1, 1, 1, new string('a', 2200000)),
                new BrowserGui.ImageBatchCommand(1, [1, 1, 1, 1], [0, 0, 1, 1]),
                new BrowserGui.TextureCommand(1, [0, 0, 1, 1], [0, 0, 1, 1], [1, 1, 1, 1]),
            }
        )
        {
            int count = cache.Count,
                bytes = cache.RetainedBytes;
            if (Write(cache, value) != null || count != cache.Count || bytes != cache.RetainedBytes)
                throw new Exception("Cache admitted large or unsupported payload");
        }
        foreach (
            var bounded in new[]
            {
                new BrowserCommandValueCache(0, 100),
                new BrowserCommandValueCache(3, 1),
            }
        )
            if (Write(bounded, new BrowserGui.TargetCommand(1, 2, 3)) != null || bounded.Count != 0)
                throw new Exception("Cache ignored disabled/undersized bounds");
    }
}
