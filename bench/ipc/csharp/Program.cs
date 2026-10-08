using System;
using System.Diagnostics;
using System.IO;
using Google.Protobuf;
using SlopWorld;
using SlopWorld.Wire;
class Program
{
    static long sink;
    const int WarmupCount = 300;
    const int SampleCount = 21;
    static Func<long> AllocationCounter()
    {
        var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread");
        if (method == null)
            throw new NotSupportedException("IPC allocation benchmarks require a Mono runtime exposing GC.GetAllocatedBytesForCurrentThread.");
        return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
    }
    static Func<long> allocated;
    static void Measure(string fixture, string lane, int wire, int iterations, Action action)
    {
        for (int i = 0; i < WarmupCount; i++) action();
        var us = new double[SampleCount]; var allocations = new double[SampleCount];
        for (int sample = 0; sample < us.Length; sample++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = allocated(); var timer = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) action();
            timer.Stop();
            us[sample] = timer.Elapsed.TotalMilliseconds * 1000 / iterations;
            allocations[sample] = (allocated() - before) / (double)iterations;
        }
        Array.Sort(us); Array.Sort(allocations);
        Console.WriteLine(string.Join(",", fixture, lane, wire, us[SampleCount / 2].ToString("F3", System.Globalization.CultureInfo.InvariantCulture), us[(int)Math.Ceiling(SampleCount * 0.95) - 1].ToString("F3", System.Globalization.CultureInfo.InvariantCulture), allocations[SampleCount / 2].ToString("F0", System.Globalization.CultureInfo.InvariantCulture)));
    }
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--verify")
        {
            foreach (var name in new[] { "plain", "ansi", "unicode", "large" })
                if (!Event.Parser.ParseFrom(File.ReadAllBytes(Path.Combine(args[1], name + ".pb"))).Equals(
                    Event.Parser.ParseFrom(File.ReadAllBytes(Path.Combine(args[1], name + ".rust.pb")))))
                    throw new InvalidDataException("Rust/C# roundtrip mismatch: " + name);
            Console.WriteLine("Rust/C# binary fixtures verified.");
            return;
        }

        allocated = AllocationCounter();
        Console.WriteLine("fixture,lane,wire_bytes,p50_us,p95_us,allocated_bytes");
        if (args.Length != 1) throw new ArgumentException("fixture directory is required");
        foreach (string kind in new[] { "plain", "ansi", "unicode", "large" })
        {
            byte[] binary = File.ReadAllBytes(Path.Combine(args[0], kind + ".pb"));
            var ev = Event.Parser.ParseFrom(binary);
            var parsed = Event.Parser.ParseFrom(binary);
            if (!parsed.Equals(ev) || parsed.PayloadCase != Event.PayloadOneofCase.Screen)
                throw new InvalidDataException("fixture mismatch");
            Measure(kind, "protobuf-receive", binary.Length, 500, () =>
            {
                var value = new ReceivedEvent(binary); if (value.Error != null) throw value.Error;
                foreach (var line in value.Value.Screen.Lines) sink += line.Length;
            });
            var queue = new IncomingMessageQueue(); var batch = new HubEventBatch();
            Measure(kind, "protobuf-queue1", binary.Length, 500, () =>
            {
                queue.Enqueue(binary);
                batch.Read(queue, e => { throw e; }); sink += batch.Count; batch.Clear();
            });
            Measure(kind, "protobuf-burst8", binary.Length * 8, 150, () =>
            {
                for (int i = 0; i < 8; i++) queue.Enqueue(binary);
                batch.Read(queue, e => { throw e; }); sink += batch.Count; batch.Clear();
            });
        }
        GC.KeepAlive(sink);
    }
}
