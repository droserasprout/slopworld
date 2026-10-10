using System;
using NUnit.Framework;
using Verse;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class LogEntryClockTests
    {
        [Test]
        public void SavedUtcAgeSurvivesLoad()
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
            Assert.That(LogEntryClock.SecondsSince(loaded), Is.InRange(3600f, 3605f));
            Scribe.mode = LoadSaveMode.Inactive;
        }

        [Test]
        public void MissingUtcStampDoesNotInferAgeFromGameTicks()
        {
            Scribe.mode = LoadSaveMode.LoadingVars;
            Scribe_Values.SavedTicks = 0;
            var entry = new LogEntry { Timestamp = 120 };
            LogEntryClock.Expose(entry);
            Assert.That(LogEntryClock.SecondsSince(entry), Is.EqualTo(0f));
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
