using System.Diagnostics;
using System.Text.Json;
using BrowserAdapters;

internal static class CommandWriterBenchmark
{
    internal static void Run(bool enabled)
    {
        if (!enabled)
            return;
        // Workload shape from decoded-cache-workload.json output means: ~850
        // commands and ~2719 image quads. This is synthetic transport work, not
        // a replay of private terminal contents or a whole-frame measurement.
        var commands = new List<BrowserGui.Command>();
        for (int i = 0; i < 650; i++)
            commands.Add(
                new(
                    "fill",
                    new BrowserGui.FillCommand(
                        [i % 80 * 9.1f, i / 80 * 17.3f, 8.1f, 16.7f],
                        [1, .1f, .5f, 1],
                        0
                    )
                )
            );
        for (int batch = 0; batch < 200; batch++)
        {
            var values = new List<float>();
            for (int i = 0; i < 14; i++)
                values.AddRange(
                    new float[]
                    {
                        batch * 7.3f + i * 8.1f,
                        batch * 1.1f,
                        8.1f,
                        16.7f,
                        .1f,
                        .2f,
                        .01f,
                        .02f,
                    }
                );
            commands.Add(
                new("imageBatch", new BrowserGui.ImageBatchCommand(1, [1, 1, 1, 1], values))
            );
        }
        string Reference() =>
            JsonSerializer.Serialize(commands, BrowserGuiJson.Default.ListCommand);
        string Direct() => BrowserCommandWriter.Serialize(commands);
        if (Reference() != Direct())
            throw new Exception("Direct writer changed workload packet");
        for (int i = 0; i < 100; i++)
        {
            Reference();
            Direct();
        }
        var samples = new List<object>();
        bool previousCache = BrowserQuadWriter.CacheEnabled;
        try
        {
            foreach (bool quadCache in new[] { true, false })
            {
                BrowserQuadWriter.ConfigureCache(quadCache);
                Reference();
                Direct();
                for (int round = 0; round < 8; round++)
                    foreach (
                        bool direct in round % 2 == 0
                            ? new[] { false, true }
                            : new[] { true, false }
                    )
                    {
                        long allocated = GC.GetAllocatedBytesForCurrentThread();
                        long started = Stopwatch.GetTimestamp();
                        int chars = 0;
                        for (int i = 0; i < 100; i++)
                            chars = (direct ? Direct() : Reference()).Length;
                        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                        samples.Add(
                            new
                            {
                                quadCache,
                                direct,
                                msPerPacket = elapsed / 100,
                                allocatedBytesPerPacket = bytes / 100,
                                chars,
                            }
                        );
                    }
            }
        }
        finally
        {
            BrowserQuadWriter.ConfigureCache(previousCache);
        }
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    commandCount = commands.Count,
                    equivalent = true,
                    samples,
                }
            )
        );
        ValueCacheBenchmark(commands);
    }

    static void ValueCacheBenchmark(List<BrowserGui.Command> commands)
    {
        commands.Add(new("matrix", new float[] { 1, 0, 0, 1, 0, 0 }));
        commands.Add(new("clipScreen", new float[] { 0, 0, 1536, 864 }));
        commands.Add(new("target", new BrowserGui.TargetCommand(0, 0, 0)));
        int changingIndex = commands.Count;
        commands.Add(new("releaseImage", 0));
        var fills = commands.Select(c => c.value).OfType<BrowserGui.FillCommand>().ToArray();
        var samples = new List<object>();
        bool previous = BrowserCommandWriter.ValueCacheEnabled;
        try
        {
            // A changing integer makes each packet different even when all cached
            // geometry repeats. No whole-packet cache participates in this benchmark.
            foreach (string workload in new[] { "repeated", "quarter-moving", "all-moving" })
            {
                for (int round = 0; round < 6; round++)
                    foreach (
                        bool enabled in round % 2 == 0
                            ? new[] { false, true }
                            : new[] { true, false }
                    )
                    {
                        BrowserCommandWriter.ConfigureValueCache(enabled);
                        void Prepare(int frame)
                        {
                            commands[changingIndex] = new("releaseImage", frame);
                            int count =
                                workload == "repeated" ? 0
                                : workload == "quarter-moving" ? fills.Length / 4
                                : fills.Length;
                            for (int j = 0; j < count; j++)
                                fills[j].rect[0] = j * 9.1f + frame * .3f;
                        }
                        for (int frame = 0; frame < 30; frame++)
                        {
                            Prepare(frame);
                            string expected = JsonSerializer.Serialize(
                                commands,
                                BrowserGuiJson.Default.ListCommand
                            );
                            if (BrowserCommandWriter.Serialize(commands) != expected)
                                throw new Exception("Value cache changed benchmark packet");
                        }
                        long allocated = GC.GetAllocatedBytesForCurrentThread();
                        long started = Stopwatch.GetTimestamp();
                        int chars = 0;
                        for (int frame = 30; frame < 130; frame++)
                        {
                            Prepare(frame);
                            chars = BrowserCommandWriter.Serialize(commands).Length;
                        }
                        samples.Add(
                            new
                            {
                                workload,
                                enabled,
                                msPerPacket = Stopwatch.GetElapsedTime(started).TotalMilliseconds
                                    / 100,
                                allocatedBytesPerPacket = (
                                    GC.GetAllocatedBytesForCurrentThread() - allocated
                                ) / 100,
                                chars,
                            }
                        );
                    }
            }
        }
        finally
        {
            BrowserCommandWriter.ConfigureValueCache(previous);
        }
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    benchmark = "small-command-values",
                    commandCount = commands.Count,
                    equivalent = true,
                    samples,
                }
            )
        );
    }
}
