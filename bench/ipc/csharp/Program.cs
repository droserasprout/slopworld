using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Google.Protobuf;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        foreach (string kind in new[] { "plain", "ansi", "unicode", "large" })
        {
            int rows = kind == "large" ? 150 : 40;
            string content = kind == "ansi" ? "\u001b[32mlet result = compute(input);\u001b[0m " : kind == "unicode" ? "日本語 café 🦀 λ " : "the quick brown fox jumped over the lazy dog ";
            var screen = new ScreenView { Name = "bench", Seq = 42, Cols = 120, Rows = (uint)rows, Cx = 3, Cy = 2,
                History = 900, CursorBlink = true, CursorShape = 1, Title = "terminal" };
            for (int i = 0; i < rows; i++) screen.Lines.Add(i + " " + string.Concat(Enumerable.Repeat(content, 3)));
            var ev = new Event { Screen = screen };
            var tree = new JObject { ["t"] = "screen", ["screen"] = new JObject {
                ["name"] = screen.Name, ["seq"] = screen.Seq, ["cols"] = screen.Cols, ["rows"] = screen.Rows,
                ["cx"] = screen.Cx, ["cy"] = screen.Cy, ["off"] = screen.Off, ["history"] = screen.History,
                ["cursor_shape"] = screen.CursorShape, ["cursor_blink"] = screen.CursorBlink,
                ["app_mouse"] = screen.AppMouse, ["app_drag"] = screen.AppDrag, ["alt_screen"] = screen.AltScreen,
                ["title"] = screen.Title, ["request_id"] = screen.RequestId, ["lines"] = new JArray(screen.Lines) } };
            string json = tree.ToString(Formatting.None);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json), binary = ev.ToByteArray();
            var parsed = Event.Parser.ParseFrom(binary);
            if (!parsed.Equals(ev) || JVal.Parse(json)["screen"]["lines"].Count != rows) throw new Exception("fixture mismatch");
            if (args.Length > 0) { Directory.CreateDirectory(args[0]); File.WriteAllText(Path.Combine(args[0], kind + ".json"), json); File.WriteAllBytes(Path.Combine(args[0], kind + ".pb"), binary); }
            Measure(kind, "json-receive", jsonBytes.Length, 500, () => {
                string text = Encoding.UTF8.GetString(jsonBytes); HubWire.TryLiveScreenName(text, out var name);
                var value = JVal.Parse(text)["screen"]; var lines = value["lines"];
                for (int i = 0; i < lines.Count; i++) sink += lines.StringAt(i).Length;
            });
            Measure(kind, "protobuf-receive", binary.Length, 500, () => {
                var value = new ReceivedEvent(binary); if (value.Error != null) throw value.Error;
                foreach (var line in value.Value.Screen.Lines) sink += line.Length;
            });
            var oldQueue = new LegacyIncomingMessageQueue(); var oldBatch = new LegacyHubEventBatch();
            var queue = new IncomingMessageQueue(); var batch = new HubEventBatch();
            Measure(kind, "json-burst8", jsonBytes.Length * 8, 150, () => {
                for (int i = 0; i < 8; i++) oldQueue.Enqueue(Encoding.UTF8.GetString(jsonBytes));
                oldBatch.Read(oldQueue, e => { throw e; }); sink += oldBatch.Count; oldBatch.Clear();
            });
            Measure(kind, "protobuf-burst8", binary.Length * 8, 150, () => {
                for (int i = 0; i < 8; i++) queue.Enqueue(binary);
                batch.Read(queue, e => { throw e; }); sink += batch.Count; batch.Clear();
            });
        }
        GC.KeepAlive(sink);
    }
}
