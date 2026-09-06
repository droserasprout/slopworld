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
        static readonly bool Enabled =
            string.Equals(Environment.GetEnvironmentVariable("SLOPWORLD_PERF_DEBUG"), "1",
                          StringComparison.Ordinal);
        static readonly Dictionary<string, Sample> Samples =
            new Dictionary<string, Sample>(StringComparer.Ordinal);
        static float _nextReport;

        struct Sample
        {
            public int Calls;
            public int Work;
            public int PeakBacklog;
            public long Ticks;
        }

        public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0L;

        public static void End(string name, long started, int work, int backlog = 0)
        {
            if (!Enabled || started == 0L) return;
            if (!Samples.TryGetValue(name, out var sample)) sample = new Sample();
            sample.Calls++;
            sample.Work += work;
            sample.PeakBacklog = Math.Max(sample.PeakBacklog, backlog);
            sample.Ticks += Stopwatch.GetTimestamp() - started;
            Samples[name] = sample;
        }

        public static void Report()
        {
            if (!Enabled) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextReport || Samples.Count == 0) return;
            _nextReport = now + 1f;

            var text = new StringBuilder("[SlopWorld] perf");
            foreach (var pair in Samples)
            {
                var sample = pair.Value;
                double ms = sample.Ticks * 1000.0 / Stopwatch.Frequency;
                text.Append(' ').Append(pair.Key).Append(" calls=").Append(sample.Calls)
                    .Append(" work=").Append(sample.Work).Append(" ms=")
                    .Append(ms.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                if (sample.PeakBacklog > 0)
                    text.Append(" backlog=").Append(sample.PeakBacklog);
                text.Append(';');
            }
            Log.Message(text.ToString());
            Samples.Clear();
        }
    }
}
