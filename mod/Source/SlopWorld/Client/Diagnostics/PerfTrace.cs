using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace SlopWorld
{
    // Collects process-local timing samples. PerfTraceReport owns game context and publication.
    static class PerfTrace
    {
        internal static readonly bool Enabled = DebugFlag();
        internal static readonly PerfSamples Samples = new PerfSamples();

        static bool DebugFlag()
        {
            string value = Environment.GetEnvironmentVariable("SLOPWORLD_DEBUG");
            return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static long Start() => Enabled ? Stopwatch.GetTimestamp() : 0L;

        public static void End(string name, long started, int work, int backlog = 0)
        {
            if (!Enabled || started == 0L)
                return;
            Samples.Duration(name, Stopwatch.GetTimestamp() - started, work, backlog);
        }

        public static void Count(string name, int work = 1)
        {
            if (Enabled)
                Samples.Count(name, work);
        }
    }

    // Bounded duration samples retain full totals and peaks for each reporting window.
    sealed class PerfSamples
    {
        const int MaxSamples = 512;
        readonly Dictionary<string, Sample> _samples = new Dictionary<string, Sample>(
            StringComparer.Ordinal
        );

        struct Sample
        {
            public int Calls;
            public int Work;
            public int PeakBacklog;
            public long Ticks;
            public long MaxTicks;
            public List<long> Durations;
        }

        public void Duration(string name, long elapsed, int work, int backlog = 0)
        {
            if (!_samples.TryGetValue(name, out var sample))
                sample = new Sample();
            sample.Calls++;
            sample.Work += work;
            sample.PeakBacklog = Math.Max(sample.PeakBacklog, backlog);
            sample.Ticks += elapsed;
            sample.MaxTicks = Math.Max(sample.MaxTicks, elapsed);
            if (sample.Durations == null)
                sample.Durations = new List<long>();
            if (sample.Durations.Count < MaxSamples)
                sample.Durations.Add(elapsed);
            _samples[name] = sample;
        }

        public void Count(string name, int work = 1)
        {
            if (!_samples.TryGetValue(name, out var sample))
                sample = new Sample();
            sample.Calls++;
            sample.Work += work;
            _samples[name] = sample;
        }

        public void AppendTo(StringBuilder text)
        {
            foreach (var pair in _samples)
                AppendSample(text, pair.Key, pair.Value);
        }

        public void Clear() => _samples.Clear();

        static void AppendSample(StringBuilder text, string name, Sample sample)
        {
            double ms = sample.Ticks * 1000.0 / Stopwatch.Frequency;
            text.Append(' ')
                .Append(name)
                .Append(" calls=")
                .Append(sample.Calls)
                .Append(" work=")
                .Append(sample.Work)
                .Append(" ms=")
                .Append(ms.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            if (sample.Durations != null && sample.Durations.Count > 0)
            {
                sample.Durations.Sort();
                text.Append(" p50=")
                    .Append(
                        Millis(sample.Durations, 50)
                            .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                    );
                text.Append(" p95=")
                    .Append(
                        Millis(sample.Durations, 95)
                            .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                    );
                text.Append(" p99=")
                    .Append(
                        Millis(sample.Durations, 99)
                            .ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                    );
                text.Append(" max=")
                    .Append(
                        (sample.MaxTicks * 1000.0 / Stopwatch.Frequency).ToString(
                            "0.###",
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                    );
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
