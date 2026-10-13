using System.Diagnostics;
using System.Text;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class PerfSamplesTests
    {
        public static void BoundedPercentilesRetainAllTotalsAndPeaks()
        {
            var samples = new PerfSamples();
            for (int i = 0; i < 512; i++)
                samples.Duration("http", Stopwatch.Frequency, 2, 4);
            samples.Duration("http", 99 * Stopwatch.Frequency, 2, 17);
            samples.Count("http", 3);
            var text = new StringBuilder();
            samples.AppendTo(text);
            Assert.That(
                text.ToString(),
                Is.EqualTo(
                    " http calls=514 work=1029 ms=611000 p50=1000 p95=1000 p99=1000 max=99000 backlog=17;"
                )
            );
        }

        public static void ClearedWindowsDoNotRetainDurationsOrBacklog()
        {
            var samples = new PerfSamples();
            samples.Duration("draw", Stopwatch.Frequency, 1, 7);
            samples.Clear();
            samples.Clear();
            samples.Count("draw", 2);
            var text = new StringBuilder();
            samples.AppendTo(text);
            Assert.That(text.ToString(), Is.EqualTo(" draw calls=1 work=2 ms=0;"));
        }
    }
}
