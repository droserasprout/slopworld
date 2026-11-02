using System;

namespace SlopWorld.Tests
{
    static class TimeZonePreferenceTests
    {
        public static void AutoAndUnavailableZonesUseTheDesktop()
        {
            AssertEx.Equal(
                TimeZoneInfo.Local,
                TimeZonePreference.Resolve("auto"),
                "Auto uses desktop"
            );
            AssertEx.Equal(
                TimeZoneInfo.Local,
                TimeZonePreference.Resolve("missing/zone"),
                "unavailable fallback"
            );
            AssertEx.True(
                TimeZonePreference.Label("missing/zone").Contains("unavailable"),
                "fallback is visible"
            );
            var settings = ModSettings.FromToml("");
            AssertEx.Equal("auto", settings.timeZone, "default");
            settings.timeZone = "missing/zone";
            AssertEx.Equal(
                "missing/zone",
                ModSettings.FromToml(settings.ToToml()).timeZone,
                "retain unavailable choice"
            );
        }

        public static void NamedZonesCrossCalendarBoundariesAndRoundTrip()
        {
            var settings = new ModSettings { timeZone = "Asia/Kathmandu" };
            var restored = ModSettings.FromToml(settings.ToToml());
            var utc = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc);
            AssertEx.Equal(
                new DateTime(2026, 1, 2, 1, 45, 0),
                TimeZonePreference.Convert(utc, restored.timeZone),
                "fractional offset crosses midnight"
            );
            AssertEx.Equal(
                utc,
                TimeZonePreference.Convert(utc, "UTC"),
                "switching zone invalidates cached choice"
            );
            AssertEx.Equal(
                new DateTime(2026, 1, 1),
                TimeZonePreference.Convert(utc, "America/Montevideo").Date,
                "Planner day follows zone"
            );
        }

        public static void ProfileChangesUpdateTimestampPresentationAndPlannerDay()
        {
            var settings = Settings.S;
            string zone = settings.timeZone;
            string date = settings.dateFormat;
            string time = settings.timeFormat;
            try
            {
                settings.dateFormat = DateFormat.YearFirst;
                settings.timeFormat = TimeFormat.TwentyFourHour;
                settings.timeZone = "Asia/Kathmandu";
                var utc = new DateTime(2026, 1, 1, 20, 0, 0, DateTimeKind.Utc);
                AssertEx.Equal("2026-01-02 01:45:00", Settings.TimeStamp(utc), "profile timestamp");
                var entry = TodoText.Parse("Review due:2026-01-02")[0];
                AssertEx.True(
                    TodoText.Matches(
                        entry,
                        TodoFilter.Today,
                        "",
                        "",
                        Settings.InTimeZone(utc).Date
                    ),
                    "due today in Kathmandu"
                );
                settings.timeZone = "UTC";
                AssertEx.Equal("2026-01-01 20:00:00", Settings.TimeStamp(utc), "live zone change");
                AssertEx.Equal(
                    false,
                    TodoText.Matches(
                        entry,
                        TodoFilter.Today,
                        "",
                        "",
                        Settings.InTimeZone(utc).Date
                    ),
                    "due tomorrow in UTC"
                );
            }
            finally
            {
                settings.timeZone = zone;
                settings.dateFormat = date;
                settings.timeFormat = time;
            }
        }

        public static void DaylightSavingUsesTheInstantOffset()
        {
            var before = new DateTime(2026, 3, 8, 6, 59, 0, DateTimeKind.Utc);
            AssertEx.Equal(
                new DateTime(2026, 3, 8, 1, 59, 0),
                TimeZonePreference.Convert(before, "America/New_York"),
                "before spring transition"
            );
            AssertEx.Equal(
                new DateTime(2026, 3, 8, 3, 0, 0),
                TimeZonePreference.Convert(before.AddMinutes(1), "America/New_York"),
                "skip missing hour"
            );
            var fall = new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc);
            AssertEx.Equal(
                TimeZonePreference.Convert(fall, "America/New_York"),
                TimeZonePreference.Convert(fall.AddHours(1), "America/New_York"),
                "both repeated hours display correctly"
            );
        }
    }
}
