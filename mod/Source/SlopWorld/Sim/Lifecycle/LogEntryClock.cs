using System;
using System.Runtime.CompilerServices;
using Verse;

namespace SlopWorld
{
    // Log ages use UTC independently of the local calendar and map longitude.
    // Weak ownership follows each entry; ExposeData persists its timestamp with the log.
    internal static class LogEntryClock
    {
        sealed class Stamp
        {
            public Stamp() { }
            public long UtcTicks;
        }

        static readonly ConditionalWeakTable<LogEntry, Stamp> Stamps =
            new ConditionalWeakTable<LogEntry, Stamp>();

        public static void Created(LogEntry entry)
        {
            if (Scribe.mode == LoadSaveMode.Inactive)
                Stamps.GetOrCreateValue(entry).UtcTicks = DateTime.UtcNow.Ticks;
        }

        public static void Expose(LogEntry entry)
        {
            var stamp = Stamps.GetOrCreateValue(entry);
            Scribe_Values.Look(ref stamp.UtcTicks, "slopWorldLogUtcTicks", 0L);
        }

        public static float SecondsSince(LogEntry entry)
        {
            var stamp = Stamps.GetOrCreateValue(entry);
            if (stamp.UtcTicks == 0) return 0f;
            return (float)Math.Max(0, (DateTime.UtcNow.Ticks - stamp.UtcTicks) /
                (double)TimeSpan.TicksPerSecond);
        }
    }
}
