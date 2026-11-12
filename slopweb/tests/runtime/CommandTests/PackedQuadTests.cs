using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using BrowserAdapters;

static class PackedQuadTests
{
    public static void Run(string? fixture)
    {
        var values = new List<float>
        {
            0,
            -0f,
            .1f,
            -.1f,
            float.Epsilon,
            -float.Epsilon,
            float.MaxValue,
            float.MinValue,
        };
        var random = new Random(45);
        while (values.Count < BrowserQuadWriter.CacheLimit * 2)
        {
            float value = BitConverter.Int32BitsToSingle(
                (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1)
            );
            if (float.IsFinite(value))
                values.Add(value);
        }
        // Include a previously cached coordinate after cache eviction.
        values.AddRange(values.Take(8).ToArray());
        Check(values);
        Check(values.Take(8).ToList());
        Check(values.Take(BrowserQuadWriter.PackedThreshold).ToList());
        Check([]);
        if (fixture != null)
            File.WriteAllText(
                fixture,
                "{\"numeric\":"
                    + Serialize(values, false)
                    + ",\"packed\":"
                    + Serialize(values, true)
                    + "}"
            );
        foreach (
            float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity }
        )
        {
            try
            {
                Serialize(Enumerable.Repeat(invalid, 32).ToList(), true);
            }
            catch (ArgumentException)
            {
                continue;
            }
            throw new Exception("Packed transport admitted a non-finite coordinate");
        }
        Console.WriteLine(
            "Packed quads preserve JSON double bits, signed zero, order and cache eviction."
        );
        Benchmark();
    }

    static string Serialize(List<float> values, bool packed) =>
        JsonSerializer.Serialize(
            new List<BrowserGui.Command>
            {
                new(
                    "imageBatch",
                    packed
                        ? new BrowserGui.ImageBatchCommand(7, [1, 1, 1, .5f], values)
                        : new BrowserGui.NumericImageBatchCommand(7, [1, 1, 1, .5f], values)
                ),
            },
            BrowserGuiJson.Default.ListCommand
        );

    static void Check(List<float> values)
    {
        string packet = Serialize(values, true);
        if (
            packet
            != JsonSerializer.Serialize(
                new List<BrowserGui.Command>
                {
                    new("imageBatch", new BrowserGui.ImageBatchCommand(7, [1, 1, 1, .5f], values)),
                }
            )
        )
            throw new Exception("Packed generated and reference writers disagree");
        using var parsed = JsonDocument.Parse(packet);
        var wire = parsed.RootElement[0].GetProperty("value").GetProperty("quads");
        if (values.Count < BrowserQuadWriter.PackedThreshold)
        {
            if (packet != Serialize(values, false))
                throw new Exception("Small batch changed its wire format");
            return;
        }
        byte[] bytes = wire.GetBytesFromBase64();
        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        int tableEnd = 8 + count * 8;
        if (
            BinaryPrimitives.ReadUInt32LittleEndian(bytes) != BrowserQuadWriter.Magic
            || bytes.Length != tableEnd + values.Count * 2
        )
            throw new Exception("Packed batch lost coordinates");
        using var expected = JsonDocument.Parse(JsonSerializer.Serialize(values));
        for (int i = 0; i < values.Count; i++)
        {
            int index = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(tableEnd + i * 2));
            double actual = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(8 + index * 8));
            if (
                BitConverter.DoubleToInt64Bits(actual)
                != BitConverter.DoubleToInt64Bits(expected.RootElement[i].GetDouble())
            )
                throw new Exception($"Packed coordinate {i} changed JSON double bits");
        }
    }

    static void Benchmark()
    {
        // Synthetic host transport check, not an end-to-end or WASM measurement.
        var values = new List<float>();
        for (int i = 0; i < 3600; i++)
            values.AddRange([
                (i % 120) * 7.31f,
                (i / 120) * 17.1f,
                7.31f,
                17.1f,
                (i % 96) / 128f,
                .125f,
                .03125f,
                .0625f,
            ]);
        // Match AppendImageBatch's 8192-float cap; build once outside timings.
        var packedCommands = values
            .Chunk(8192)
            .Select(q => new BrowserGui.Command(
                "imageBatch",
                new BrowserGui.ImageBatchCommand(7, [1, 1, 1, .5f], q.ToList())
            ))
            .ToList();
        var numericCommands = packedCommands
            .Select(c =>
            {
                var b = (BrowserGui.ImageBatchCommand)c.value;
                return new BrowserGui.Command(
                    c.kind,
                    new BrowserGui.NumericImageBatchCommand(b.id, b.color, b.quads)
                );
            })
            .ToList();
        string Packet(bool packed) =>
            JsonSerializer.Serialize(
                packed ? packedCommands : numericCommands,
                BrowserGuiJson.Default.ListCommand
            );
        Packet(false);
        Packet(true);
        var timings = new Dictionary<bool, List<double>> { [false] = [], [true] = [] };
        for (int round = 0; round < 100; round++)
            foreach (bool packed in round % 2 == 0 ? new[] { false, true } : new[] { true, false })
            {
                long start = Stopwatch.GetTimestamp();
                Packet(packed);
                timings[packed].Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
        foreach (bool packed in new[] { false, true })
            Console.WriteLine(
                $"Host synthetic 3600-quad transport packed={packed}: mean={timings[packed].Average():F3} ms, chars={Packet(packed).Length}"
            );
    }
}
