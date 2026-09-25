using System;
using Google.Protobuf;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace SlopWorld.Tests
{
    // Uses the same linked production helpers as the tests. Reference cases model simple
    // traversal/allocation patterns. Neither path includes Unity drawing or event dispatch.
    static class Benchmarks
    {
        const int Samples = 50;
        static long _sink;

        sealed class ListRow
        {
            public int Index;
        }

        public static int Run()
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Console.WriteLine("SlopWorld game-free C# benchmarks");
            Console.WriteLine($"CoreCLR ({RuntimeInformation.FrameworkDescription}). {RuntimeInformation.OSArchitecture}.");
#if DEBUG
            Console.WriteLine("Debug build: use make bench-mod BUILD=release for comparisons.");
#else
            Console.WriteLine("Release build. Make disabled tiered compilation.");
#endif
            Console.WriteLine("50 warmed batch samples. p50 and p95 are microseconds per operation.");
            Console.WriteLine("B/op counts managed allocations on this thread. References model simple baseline algorithms.");
            Console.WriteLine($"{"case",-48} {"p50 us",10} {"p95 us",10} {"B/op",12}");
            foreach (int count in new[] { 100, 10000, 100000 }) Viewports(count);
            Projects();
            Routing();
            Terminal();
            ScreenIngestion();
            TerminalHotspots();
            UrlScanning();
            IdleWork();
            EcoWork();
            GC.KeepAlive(_sink);
            return 0;
        }

        static void ScreenIngestion()
        {
            var lines = Enumerable.Repeat(new string('x', 160), 200).ToArray();
            var payload = new Wire.ScreenView { Seq = 1, Rows = 200, Cols = 160, Cy = 199 };
            foreach (string line in lines) payload.Lines.Add(line);
            var screen = new ScreenBuf();
            screen.FromWire(payload);
            Measure("screen unchanged 200 repeated rows", () =>
            {
                screen.Seq = 0;
                screen.FromWire(payload);
                return screen.LiveShift;
            });
            var other = payload.Clone();
            other.Lines[198] = "different";
            bool flip = false;
            Measure("screen changed 200 repeated rows", () =>
            {
                screen.Seq = 0;
                screen.FromWire((flip = !flip) ? other : payload);
                return screen.LiveShift;
            });
        }

        static void TerminalHotspots()
        {
            string plain = new string('a', 120);
            var asciiKeyCatalog = new TextSpriteCatalog(new[] { "~sprite" });
            Measure("terminal cell layout plain 120 reference", () =>
                InlineTextLayout.Cells(plain, 120, 8f, asciiKeyCatalog,
                    c => c >= ' ' && c <= '~').Spans.Length);
            Measure("terminal cell layout plain 120 columns", () =>
                InlineTextLayout.Cells(plain, 120, 8f, TextSpriteCatalog.Shared,
                    c => c >= ' ' && c <= '~').Spans.Length);

            foreach (int rows in new[] { 34, 200 })
            {
                foreach (bool link in new[] { false, true })
                {
                    var lines = Enumerable.Range(0, rows)
                        .Select(i => "\x1b[31mrow " + i + "\x1b[0m " + new string('x', 80)).ToArray();
                    if (link) lines[0] = "https://example.com/static-link";
                    Func<string, Wire.ScreenView> payload = tail =>
                    {
                        var value = new Wire.ScreenView { Rows = (uint)rows, Cols = 120 };
                        foreach (string line in lines.Take(rows - 1)) value.Lines.Add(line);
                        value.Lines.Add(tail);
                        return value;
                    };
                    var a = payload("progress A");
                    var b = payload("progress B");
                    var screen = new ScreenBuf();
                    var cache = new TerminalRunCache();
                    bool flip = false;
                    Measure($"sparse ingest+ANSI {rows} rows / {(link ? "URL" : "plain")}", () =>
                    {
                        screen.FromWire((flip = !flip) ? a : b);
                        screen.Runs = cache.Parse(screen, 1, 1, out _, out _);
                        screen.RunsRev = 1;
                        screen.RunsComplete = true;
                        return screen.Runs.Length;
                    });
                }
            }

            var frame = new Wire.Event { Screen = new Wire.ScreenView { Name = "bench", Rows = 200, Cols = 160 } };
            for (int i = 0; i < 200; i++) frame.Screen.Lines.Add(new string('x', 160));
            var incoming = new IncomingMessageQueue();
            var batch = new HubEventBatch();
            Action<Exception> onError = error => throw error;
            var binary = frame.ToByteArray();
            foreach (int count in new[] { 1, 8, 32 })
                Measure($"screen batch {count} frames / one session", () =>
                {
                    for (int i = 0; i < count; i++) incoming.Enqueue(binary);
                    batch.Read(incoming, onError);
                    int dispatched = 0;
                    for (int i = 0; i < batch.Count; i++)
                        if (batch.ShouldDispatch(i)) dispatched++;
                    return dispatched;
                });
        }

        static void UrlScanning()
        {
            Measure("URL ordinary", () =>
                UrlScan.FindUrls("see https://example.com/path here").Count);
            foreach (int count in new[] { 128, 1024, 4096 })
            {
                string text = "https://example.com/path" + new string(')', count);
                Measure($"URL trailing brackets {count}", () => UrlScan.FindUrls(text).Count);
            }
        }

        static void IdleWork()
        {
            var binaryIncoming = new IncomingMessageQueue();
            var batch = new HubEventBatch();
            Action<Exception> onError = _ => { };
            Measure("idle socket batch", () => batch.Read(binaryIncoming, onError));

            var titles = new SidebarTitleCache();
            var info = new SessionInfo { Title = "agent: compiling a project and checking its tests" };
            var font = new object();
            Func<SessionInfo, string> clean = s =>
            {
                var text = new System.Text.StringBuilder(s.Title.Length);
                foreach (char c in s.Title) text.Append(char.IsControl(c) ? ' ' : c);
                return text.ToString().Trim();
            };
            Compare("sidebar unchanged title cleanup", () => clean(info).Length,
                () => titles.Get(info, font, 0, clean).Length);

            var screen = new ScreenBuf { Lines = new[] { "unchanged" }, ContentRevision = 1 };
            Measure("terminal idle/cursor repaint decision", () =>
                (int)TerminalRepaintPolicy.Choose(false, 1, screen));
        }

        static void EcoWork()
        {
            var sessions = new List<SessionInfo>();
            for (int i = 0; i < 32; i++) sessions.Add(new SessionInfo { Name = "agent-" + i });
            var membership = new ColonySessionIndex();
            membership.Refresh(sessions, 1);
            Compare("colony unchanged membership (32)", () =>
                new HashSet<string>(sessions.Where(s => !s.Ephemeral && !s.Worker).Select(s => s.Name)).Count,
                () => { membership.Refresh(sessions, 1); return membership.Contains("agent-0") ? 32 : 0; });

            var now = new DateTime(2026, 9, 8, 23, 59, 59);
            var clock = new ClockTextCache();
            Compare("topbar unchanged clock text", () =>
            {
                string shortText = now.ToString("HH:mm");
                string tip = now.ToString("dddd, d MMMM yyyy") + "\n" + now.ToString("HH:mm:ss");
                return shortText.Length + tip.Length;
            }, () =>
            {
                clock.Prepare(now, TimeFormat.TwentyFourHour, CultureInfo.InvariantCulture);
                return clock.Short.Length + clock.Tooltip.Length;
            });

            var usage = new UsageInfo();
            usage.Sources.Add("anthropic");
            usage.Sources.Add("openai");
            var config = new DaemonConfig();
            config.UsageItems["claude_session"] = new DaemonConfig.UsageItemConfig { Poll = true };
            var rows = new UsageRowsCache();
            // Cold uses the production row builder, to isolate reuse from row-policy changes.
            Measure("topbar quota rows / cold cache", () =>
            {
                var cold = new UsageRowsCache();
                cold.Prepare(usage, config);
                return cold.Rows.Count;
            });
            Measure("topbar quota rows / unchanged", () =>
            {
                rows.Prepare(usage, config);
                return rows.Rows.Count;
            });
        }

        static void Measure(string name, Func<long> operation)
        {
            for (int i = 0; i < 32; i++) _sink ^= operation();
            // Amortize timer/delegate overhead for sub-microsecond helpers without allowing
            // slow or allocating cases to create an unbounded batch.
            int batch = 1;
            while (batch < 65536)
            {
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < batch; i++) _sink ^= operation();
                if ((Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency >= 0.002) break;
                batch *= 2;
            }
            var times = new double[Samples];
            var bytes = new double[Samples];
            for (int sample = 0; sample < Samples; sample++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                long result = 0;
                for (int i = 0; i < batch; i++) result ^= operation();
                long elapsed = Stopwatch.GetTimestamp() - start;
                bytes[sample] = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)batch;
                times[sample] = elapsed * 1000000.0 / Stopwatch.Frequency / batch;
                _sink ^= result;
            }
            Array.Sort(times);
            Array.Sort(bytes);
            Console.WriteLine($"{name,-48} {times[Samples / 2],10:F3} {times[(int)Math.Ceiling(Samples * .95) - 1],10:F3} {bytes[Samples / 2],12:F1}");
        }

        static void Compare(string name, Func<long> reference, Func<long> current)
        {
            AssertEx.Equal(reference(), current(), name + " equivalent results");
            Measure(name + " reference", reference);
            Measure(name + " current", current);
        }

        static void Viewports(int count)
        {
            var ends = new float[count];
            var rows = Enumerable.Range(0, count).Select(i => new ListRow { Index = i }).ToList();
            var index = new ContentTreeIndex();
            float y = 0;
            for (int i = 0; i < count; i++)
            {
                float end = y + (i % 100 == 0 ? 100 : 20);
                index.Add(y, end, null);
                ends[i] = end;
                y = end;
            }
            index.Commit(1);
            float top = y * .75f, bottom = top + 600;
            Compare($"tree {count} rows", () =>
            {
                long sum = 0;
                for (int i = 0; i < count; i++)
                    if (ends[i] > top && (i == 0 ? 0 : ends[i - 1]) < bottom) sum += i;
                return sum;
            }, () =>
            {
                long sum = 0;
                for (int i = index.First(top);
                     i < count && (i == 0 ? 0 : ends[i - 1]) < bottom; i++) sum += i;
                return sum;
            });

            float listTop = count * 20 * .75f;
            Compare($"list {count} rows copy/scan", () =>
            {
                long sum = 0;
                foreach (var row in rows.ToList())
                    if ((row.Index + 1) * 20 > listTop && row.Index * 20 < listTop + 600)
                        sum += row.Index;
                return sum;
            }, () =>
            {
                VisibleRows.Uniform(count, 20, listTop, 600, out int first, out int end);
                long sum = 0;
                for (int i = first; i < end; i++) sum += rows[i].Index;
                return sum;
            });
        }

        static void Projects()
        {
            var names = Enumerable.Range(0, 64).Select(i => "project-" + i).ToArray();
            var sessions = Enumerable.Range(0, 10000)
                .Select(i => new SessionInfo { Project = names[i % names.Length] }).ToList();
            var cache = new ProjectSessionCounts();
            long revision = 1;
            Func<long> cached = () =>
            {
                long sum = 0;
                foreach (string name in names) sum += cache.Get(sessions, revision, name);
                return sum;
            };
            Compare("64 projects / 10000 sessions", () =>
            {
                long sum = 0;
                foreach (string name in names) sum += sessions.Count(s => s.Project == name);
                return sum;
            }, cached);
            Measure("project totals changed revision", () => { revision++; return cached(); });
        }

        static void Routing()
        {
            var sessions = Enumerable.Range(0, 10000).Select(i => new SessionInfo
            {
                Name = "viewer-" + (10000 - i).ToString("D5"),
                Project = i % 4 == 0 ? "visible" : "hidden",
            }).ToList();
            var rows = new List<SessionInfo>();
            Predicate<SessionInfo> include = s => s.Project == "visible";
            var routed = new RoutedSessionRows();
            Compare("routing 10000 sessions / 2500 visible", () =>
            {
                float height = 0;
                for (int i = 0; i < 3; i++)
                    height = RoutedSessionRows.Rebuild(rows, sessions, include, null, 20);
                return (long)height;
            }, () => (long)routed.Ensure(rows, sessions, 1, 1, "less", "micro", include, null, 20));
            Measure("routing changed revision", () =>
            {
                RoutedSessionRows.Invalidate();
                return (long)routed.Ensure(rows, sessions, 1, 1, "less", "micro", include, null, 20);
            });
        }

        static void Terminal()
        {
            var lines = Enumerable.Range(0, 34).Select(i => "\x1b[31mrow " + i + "\x1b[0m text").ToArray();
            var cache = new TerminalRunCache();
            Measure("terminal ANSI parse 34 rows cold cache", () =>
            {
                var cold = new TerminalRunCache();
                return cold.Parse(lines, 120, 1, 1, out _, out _).Length;
            });
            Measure("terminal ANSI parse 34 rows warm cache", () =>
                cache.Parse(lines, 120, 1, 1, out _, out _).Length);
            var screen = new ScreenBuf { Lines = lines, ContentRevision = 1, ChangedRows = new[] { 1 } };
            Measure("terminal sparse repaint decision", () =>
                (int)TerminalRepaintPolicy.Choose(false, 0, screen));

            var live = new ScreenBuf
            {
                Seq = 1, Cols = 120, Rows = 34, History = 1000,
                Lines = Enumerable.Range(0, 34).Select(i => "row " + i).ToArray(),
            };
            var reply = new ScreenBuf
            {
                Seq = 1, Cols = 120, Rows = 34, Off = 17, History = 1000,
                Lines = Enumerable.Range(-17, 34).Select(i => "row " + i).ToArray(),
            };
            var history = new TerminalHistory();
            history.Reset(live);
            history.Add(reply, live, 17);
            AssertEx.True(history.TryView(1, true, out _), "warm first-scroll fixture");
            Measure("history first view cold index (no network)", () =>
            {
                var cold = new TerminalHistory();
                cold.Reset(live);
                cold.Add(reply, live, 17);
                return cold.TryView(1, true, out var view) ? view.Lines.Length : -1;
            });
            int anchor = 1;
            Measure("history first view warm rows", () =>
            {
                // Alternate shallow anchors so this measures view construction, not just
                // repeatedly returning the exact same assembled ScreenBuf.
                anchor = anchor == 1 ? 2 : 1;
                return history.TryView(anchor, true, out var view) ? view.Lines.Length : -1;
            });
            Measure("history prefetch next-window plan", () => history.WarmupOffset(live));
            for (int offset = history.WarmupOffset(live); offset > 0;
                 offset = history.WarmupOffset(live))
                history.Add(new ScreenBuf
                {
                    Seq = 1, Cols = 120, Rows = 34, Off = offset, History = 1000,
                    Lines = Enumerable.Range(-offset, 34).Select(i => "row " + i).ToArray(),
                }, live, offset);
            Measure("history eight-screen coverage check", () => history.WarmupOffset(live));
        }
    }
}
