using System;
using System.Text;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Samples game context and publishes one diagnostic window per second.
    static class PerfTraceReport
    {
        static float _nextReport;
        static float _lastReport;
        static int _lastFrame;
        static int _lastGc;

        public static void Report()
        {
            if (!PerfTrace.Enabled)
                return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextReport)
                return;
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
            PerfTrace.Samples.AppendTo(text);
            MemoryTrace.Append(text, now);
            TerminalLatency.Record(text.ToString());
            PerfTrace.Samples.Clear();
        }

        static void AppendContext(StringBuilder text, float fps, int collections)
        {
            text.Append(" context eco=")
                .Append(Eco.Resting ? 1 : 0)
                .Append(" terminal=")
                .Append(TerminalWindow.Covering ? 1 : 0)
                .Append(" sessions=")
                .Append(SessionHub.Instance.Sessions.Count)
                .Append(" width=")
                .Append(UI.screenWidth)
                .Append(" height=")
                .Append(UI.screenHeight)
                .Append(" fps=")
                .Append(fps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" gc0=")
                .Append(collections)
                .Append(';');
        }
    }
}
