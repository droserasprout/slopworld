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
    }
}
