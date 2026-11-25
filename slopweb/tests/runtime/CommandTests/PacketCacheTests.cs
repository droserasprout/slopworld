using System.Text.Json;
using BrowserAdapters;

static class PacketCacheTests
{
    internal static void Run(List<BrowserGui.Command> commands)
    {
        var cache = new BrowserPacketCache();
        string expected = JsonSerializer.Serialize(commands, BrowserGuiJson.Default.ListCommand);
        cache.Store(commands, expected);
        if (cache.Find(commands.ToList()) != expected)
            throw new Exception("Identical packet missed");
        foreach (var command in commands)
        {
            List<BrowserGui.Command> single = [command];
            string json = JsonSerializer.Serialize(single, BrowserGuiJson.Default.ListCommand);
            cache.Store(single, json);
            var arrays = command.value is float[] direct
                ? new[] { direct }
                : command
                    .value.GetType()
                    .GetProperties()
                    .Where(p => p.PropertyType == typeof(float[]))
                    .Select(p => (float[])p.GetValue(command.value)!);
            foreach (var array in arrays)
            {
                float original = array[0];
                array[0] = original + 1;
                if (cache.Find(single) != null)
                    throw new Exception("Stale array field in " + command.kind);
                array[0] = original;
                if (cache.Find(single) != json)
                    throw new Exception("Snapshot alias in " + command.kind);
            }
        }
        cache.Store(commands, expected);
        // Caller-owned arrays and lists may be reused or mutated after draining.
        var rectangle = ((BrowserGui.TextCommand)commands[0].value).rect;
        float old = rectangle[0];
        rectangle[0] = 99;
        if (cache.Find(commands) != null)
            throw new Exception("Mutable geometry reused stale JSON");
        rectangle[0] = old;
        if (cache.Find(commands) != expected)
            throw new Exception("Snapshot changed with caller");
        var batch = commands.Select(c => c.value).OfType<BrowserGui.ImageBatchCommand>().Single();
        batch.quads.Add(1);
        if (cache.Find(commands) != null)
            throw new Exception("Mutable batch reused stale JSON");
        batch.quads.RemoveAt(batch.quads.Count - 1);
        var changedWrap = commands.ToList();
        int batchIndex = commands.FindIndex(c => c.kind == "imageBatch");
        changedWrap[batchIndex] = new("imageBatch", batch with { repeat = !batch.repeat });
        if (cache.Find(changedWrap) != null)
            throw new Exception("Changed wrap mode reused stale JSON");
        var reordered = commands.AsEnumerable().Reverse().ToList();
        if (cache.Find(reordered) != null)
            throw new Exception("Command order ignored");
        List<BrowserGui.Command> zero = [new("matrix", new float[] { 0 })];
        cache.Store(zero, "positive zero");
        long packetId = cache.PacketId;
        if (packetId <= 0 || cache.Find(zero) == null || cache.PacketId != packetId)
            throw new Exception("Unchanged screen packet lost its identity");
        ((float[])zero[0].value)[0] = -0f;
        if (cache.Find(zero) != null)
            throw new Exception("Signed zero ignored");
        foreach (string mutation in new[] { "image", "releaseImage", "target", "releaseTarget" })
        {
            cache.Store([new(mutation, 1)], "resource mutation");
            if (cache.PacketId != 0)
                throw new Exception("Resource mutation can cross interop as a repeat");
        }
        cache.Clear();
        cache.Store(zero, "negative zero");
        if (cache.PacketId <= packetId)
            throw new Exception("Cached identity was recycled on reset");
        cache.Store(commands, new string('x', 2 * 1024 * 1024 + 1));
        if (cache.PacketId != 0 || cache.Find(commands) != null)
            throw new Exception("Oversize packet retained");
        cache.Store([new("matrix", new float[262145])], "small compressed packet");
        if (cache.Find([new("matrix", new float[262145])]) != null)
            throw new Exception("Geometry limit ignored");
        cache.Store([new("unknown", new object())], "unknown");
        cache.Clear();
        if (cache.Find(commands) != null)
            throw new Exception("Clear retained packet");
        Console.WriteLine(
            "Packet cache preserves immutable snapshots, order, float bits and bounds."
        );
    }
}
