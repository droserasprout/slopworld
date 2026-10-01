using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // When enabled, report aggregate profiling data once per second to limit logging work during each frame.
    // Read the environment flag once. Diagnostics do not need a runtime switch.
    static class PerfTrace
    {
        internal static readonly bool Enabled = DebugFlag();
        const int MaxSamples = 512;
        static readonly Dictionary<string, Sample> Samples =
            new Dictionary<string, Sample>(StringComparer.Ordinal);
        static float _nextReport;
        static float _lastReport;
        static int _lastFrame;
        static int _lastGc;

        struct Sample
        {
            public int Calls;
            public int Work;
            public int PeakBacklog;
            public long Ticks;
            public long MaxTicks;
            public List<long> Durations;
        }

        static bool DebugFlag()
        {
            string value = Environment.GetEnvironmentVariable("SLOPWORLD_DEBUG");
            return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0L;

        public static void End(string name, long started, int work, int backlog = 0)
        {
            if (!Enabled || started == 0L) return;
            if (!Samples.TryGetValue(name, out var sample)) sample = new Sample();
            sample.Calls++;
            sample.Work += work;
            sample.PeakBacklog = Math.Max(sample.PeakBacklog, backlog);
            long elapsed = Stopwatch.GetTimestamp() - started;
            sample.Ticks += elapsed;
            sample.MaxTicks = Math.Max(sample.MaxTicks, elapsed);
            if (sample.Durations == null) sample.Durations = new List<long>();
            if (sample.Durations.Count < MaxSamples) sample.Durations.Add(elapsed);
            Samples[name] = sample;
        }

        public static void Count(string name, int work = 1)
        {
            if (!Enabled) return;
            if (!Samples.TryGetValue(name, out var sample)) sample = new Sample();
            sample.Calls++;
            sample.Work += work;
            Samples[name] = sample;
        }

        public static void Report()
        {
            if (!Enabled) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextReport) return;
            _nextReport = now + 1f;

            var text = new StringBuilder("[SlopWorld] perf");
            // Window context lets users compare captures without viewing the screen.
            // GC collection counts apply to the whole process. They do not measure allocated bytes.
            float seconds = now - _lastReport;
            int frame = Time.frameCount;
            int gc = GC.CollectionCount(0);
            AppendContext(text, seconds > 0 ? (frame - _lastFrame) / seconds : 0, gc - _lastGc);
            _lastReport = now;
            _lastFrame = frame;
            _lastGc = gc;
            foreach (var pair in Samples) AppendSample(text, pair.Key, pair.Value);
            MemoryTrace.Append(text, now);
            TerminalLatency.Record(text.ToString());
            Samples.Clear();
        }

        static void AppendContext(StringBuilder text, float fps, int collections)
        {
            text.Append(" context eco=").Append(Eco.Resting ? 1 : 0)
                .Append(" terminal=").Append(TerminalWindow.Covering ? 1 : 0)
                .Append(" sessions=").Append(SessionHub.Instance.Sessions.Count)
                .Append(" width=").Append(UI.screenWidth)
                .Append(" height=").Append(UI.screenHeight)
                .Append(" fps=").Append(fps
                    .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" gc0=").Append(collections).Append(';');
        }

        static void AppendSample(StringBuilder text, string name, Sample sample)
        {
            double ms = sample.Ticks * 1000.0 / Stopwatch.Frequency;
            text.Append(' ').Append(name).Append(" calls=").Append(sample.Calls)
                .Append(" work=").Append(sample.Work).Append(" ms=")
                .Append(ms.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            if (sample.Durations != null && sample.Durations.Count > 0)
            {
                sample.Durations.Sort();
                text.Append(" p50=").Append(Millis(sample.Durations, 50)
                    .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                text.Append(" p95=").Append(Millis(sample.Durations, 95)
                    .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                text.Append(" p99=").Append(Millis(sample.Durations, 99)
                    .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                text.Append(" max=").Append((sample.MaxTicks * 1000.0 / Stopwatch.Frequency)
                    .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            if (sample.PeakBacklog > 0)
                text.Append(" backlog=").Append(sample.PeakBacklog);
            text.Append(';');
        }

        static double Millis(List<long> samples, int percentile)
        {
            int index = (samples.Count - 1) * percentile / 100;
            return samples[index] * 1000.0 / Stopwatch.Frequency;
        }
    }
}
