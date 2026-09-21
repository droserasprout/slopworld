using System;
using System.Diagnostics;
using System.IO;
using Google.Protobuf;
using SlopWorld;
using SlopWorld.Wire;
class Program
{
    static long sink;
    static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread"));
    static void Measure(string fixture, string lane, int wire, int iterations, Action action)
    {
        for (int i = 0; i < 300; i++) action();
        var us = new double[21]; var allocations = new double[21];
        for (int sample = 0; sample < us.Length; sample++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = Allocated(); var timer = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) action();
            timer.Stop();
            us[sample] = timer.Elapsed.TotalMilliseconds * 1000 / iterations;
            allocations[sample] = (Allocated() - before) / (double)iterations;
        }
        Array.Sort(us); Array.Sort(allocations);
        Console.WriteLine(string.Join(",", fixture, lane, wire, us[10].ToString("F3", System.Globalization.CultureInfo.InvariantCulture), us[19].ToString("F3", System.Globalization.CultureInfo.InvariantCulture), allocations[10].ToString("F0", System.Globalization.CultureInfo.InvariantCulture)));
    }
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--verify")
        {
            foreach (var name in new[] { "plain", "ansi", "unicode", "large" })
                if (!Event.Parser.ParseFrom(File.ReadAllBytes(Path.Combine(args[1], name + ".pb"))).Equals(
                    Event.Parser.ParseFrom(File.ReadAllBytes(Path.Combine(args[1], name + ".rust.pb")))))
                    throw new Exception("Rust/C# roundtrip mismatch: " + name);
            Console.WriteLine("Rust/C# binary fixtures verified.");
            return;
        }

        Console.WriteLine("fixture,lane,wire_bytes,p50_us,p95_us,allocated_bytes");
        if (args.Length != 1) throw new ArgumentException("fixture directory is required");
        foreach (string kind in new[] { "plain", "ansi", "unicode", "large" })
        {
            byte[] binary = File.ReadAllBytes(Path.Combine(args[0], kind + ".pb"));
            var ev = Event.Parser.ParseFrom(binary);
            var parsed = Event.Parser.ParseFrom(binary);
            if (!parsed.Equals(ev) || parsed.PayloadCase != Event.PayloadOneofCase.Screen)
                throw new Exception("fixture mismatch");
            Measure(kind, "protobuf-receive", binary.Length, 500, () => {
                var value = new ReceivedEvent(binary); if (value.Error != null) throw value.Error;
                foreach (var line in value.Value.Screen.Lines) sink += line.Length;
            });
            var queue = new IncomingMessageQueue(); var batch = new HubEventBatch();
            Measure(kind, "protobuf-burst8", binary.Length * 8, 150, () => {
                for (int i = 0; i < 8; i++) queue.Enqueue(binary);
                batch.Read(queue, e => { throw e; }); sink += batch.Count; batch.Clear();
            });
        }
        GC.KeepAlive(sink);
    }
}
