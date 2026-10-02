using System;
using NUnit.Framework;
using Verse;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class LogEntryClockTests
    {
        [Test]
        public void SavedUtcAgeSurvivesLoadAndSolarClockChanges()
        {
            Scribe.mode = LoadSaveMode.Inactive;
            var entry = new LogEntry();
            LogEntryClock.Created(entry);
            Scribe.mode = LoadSaveMode.Saving;
            LogEntryClock.Expose(entry);
            // Simulate an hour spent with the game closed.
            Scribe_Values.SavedTicks -= TimeSpan.TicksPerHour;
            Scribe.mode = LoadSaveMode.LoadingVars;
            var loaded = new LogEntry();
            LogEntryClock.Created(loaded);
            LogEntryClock.Expose(loaded);
            RealClock.LegacyAge = 90000;
            Assert.That(LogEntryClock.SecondsSince(loaded), Is.InRange(3600f, 3605f));
            Scribe.mode = LoadSaveMode.Inactive;
        }

        [Test]
        public void LegacyAgeIsEstimatedOnlyOnce()
        {
            Scribe.mode = LoadSaveMode.LoadingVars;
            Scribe_Values.SavedTicks = 0;
            var entry = new LogEntry();
            LogEntryClock.Expose(entry);
            RealClock.LegacyAge = 120;
            Assert.That(LogEntryClock.SecondsSince(entry), Is.InRange(120f, 125f));
            RealClock.LegacyAge = 3720;
            Assert.That(LogEntryClock.SecondsSince(entry), Is.InRange(120f, 125f));
            Scribe.mode = LoadSaveMode.Inactive;
        }
    }
}

namespace Verse
{
    public class LogEntry { public int Timestamp; }
    public enum LoadSaveMode { Inactive, Saving, LoadingVars }
    public static class Scribe { public static LoadSaveMode mode; }
    public static class Scribe_Values
    {
        public static long SavedTicks;
        public static void Look(ref long value, string key, long defaultValue)
        {
            if (Scribe.mode == LoadSaveMode.Saving) SavedTicks = value;
            if (Scribe.mode == LoadSaveMode.LoadingVars) value = SavedTicks;
        }
    }
}

namespace SlopWorld
{
    public partial class RealClock
    {
        public static float LegacyAge;
        public static float SecondsSince(int timestamp) => LegacyAge;
    }
}
