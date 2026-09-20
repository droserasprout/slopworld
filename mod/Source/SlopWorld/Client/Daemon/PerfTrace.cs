using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Opt-in, once-per-second aggregates for profiling the client without turning the normal
    // frame into a logging workload. The environment flag is read once because the process
    // does not need a live switch for diagnostics.
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
            // Window context permits like-for-like captures without observing the screen.
            // GC collections are process-wide; this is not an allocation-byte measurement.
            float seconds = now - _lastReport;
            int frame = Time.frameCount;
            int gc = GC.CollectionCount(0);
            text.Append(" context eco=").Append(Eco.Resting ? 1 : 0)
                .Append(" terminal=").Append(TerminalWindow.Covering ? 1 : 0)
                .Append(" sessions=").Append(SessionHub.Instance.Sessions.Count)
                .Append(" width=").Append(UI.screenWidth)
                .Append(" height=").Append(UI.screenHeight)
                .Append(" fps=").Append((seconds > 0 ? (frame - _lastFrame) / seconds : 0)
                    .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" gc0=").Append(gc - _lastGc).Append(';');
            _lastReport = now;
            _lastFrame = frame;
            _lastGc = gc;
            foreach (var pair in Samples)
            {
                var sample = pair.Value;
                double ms = sample.Ticks * 1000.0 / Stopwatch.Frequency;
                text.Append(' ').Append(pair.Key).Append(" calls=").Append(sample.Calls)
                    .Append(" work=").Append(sample.Work).Append(" ms=")
                    .Append(ms.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                if (sample.Durations != null && sample.Durations.Count > 0)
                {
                    sample.Durations.Sort();
                    text.Append(" p50=").Append(Millis(sample.Durations, 50)
                        .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                    text.Append(" p95=").Append(Millis(sample.Durations, 95)
                        .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                }
                if (sample.PeakBacklog > 0)
                    text.Append(" backlog=").Append(sample.PeakBacklog);
                text.Append(';');
            }
            MemoryTrace.Append(text, now);
            Log.Message(text.ToString());
            Samples.Clear();
        }

        static double Millis(List<long> samples, int percentile)
        {
            int index = (samples.Count - 1) * percentile / 100;
            return samples[index] * 1000.0 / Stopwatch.Frequency;
        }
    }
}
