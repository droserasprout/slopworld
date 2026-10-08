using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SlopWorld.Tests
{
    static class SettingsBehaviorTests
    {
        static void WithSettings(Action<ModSettings> action)
        {
            var settings = Settings.S;
            var fields = typeof(ModSettings).GetFields(BindingFlags.Instance | BindingFlags.Public);
            var saved = fields.Select(field => field.GetValue(settings)).ToArray();
            var culture = CultureInfo.CurrentCulture;
            try { action(settings); }
            finally
            {
                for (int i = 0; i < fields.Length; i++) fields[i].SetValue(settings, saved[i]);
                CultureInfo.CurrentCulture = culture;
            }
        }

        static void WithProfile(Action<string> action)
        {
            string oldProfile = Verse.GenFilePaths.SaveDataFolderPath;
            string directory = Path.Combine(Path.GetTempPath(), "slop-settings-" + Guid.NewGuid());
            Verse.GenFilePaths.SaveDataFolderPath = directory;
            try { action(directory); }
            finally
            {
                Verse.GenFilePaths.SaveDataFolderPath = oldProfile;
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                yield return ("nonfinite sidebar fraction: " + value, () => WithProfile(profile =>
                {
                    var settings = new ModSettings { sidebarFilesOpenFraction = value };
                    settings.Write();
                    AssertEx.Equal(.25f, settings.sidebarFilesOpenFraction, "write repairs nonfinite fraction");
                    AssertEx.Equal(.25f, ModSettings.Load().sidebarFilesOpenFraction, "repaired value survives reload");
                }));
        }

        public static void DirtyFlushWaitsForQuietPeriodAndWritesOnlyOnce() => WithProfile(profile =>
        {
            string path = Path.Combine(profile, "Config", "SlopWorld.toml");
            var settings = new ModSettings();
            for (int i = 0; i < 200; i++) settings.FlushIfDue();
            AssertEx.False(File.Exists(path), "clean settings never create a file");
            settings.MarkDirty();
            for (int i = 0; i < 119; i++) settings.FlushIfDue();
            AssertEx.False(File.Exists(path), "dirty settings wait for 120 ticks");
            settings.MarkDirty();
            settings.theme = "new-theme";
            for (int i = 0; i < 119; i++) settings.FlushIfDue();
            AssertEx.False(File.Exists(path), "new interaction restarts quiet period");
            settings.FlushIfDue();
            AssertEx.Equal("new-theme", ModSettings.Load().theme, "flush writes latest preference");
            string saved = File.ReadAllText(path);
            settings.theme = "not-dirty";
            for (int i = 0; i < 240; i++) settings.FlushIfDue();
            AssertEx.Equal(saved, File.ReadAllText(path), "completed flush does not keep rewriting");
            settings.MarkDirty();
            for (int i = 0; i < 120; i++) settings.FlushIfDue();
            AssertEx.Equal("not-dirty", ModSettings.Load().theme, "later dirty cycle persists again");
        });

        public static void FailedDirtyFlushRetainsPendingSettingsForRetry() => WithProfile(profile =>
        {
            Directory.CreateDirectory(profile);
            string config = Path.Combine(profile, "Config");
            File.WriteAllText(config, "blocks directory creation");
            var settings = new ModSettings { theme = "pending-theme" };
            settings.MarkDirty();
            for (int i = 0; i < 119; i++) settings.FlushIfDue();
            AssertEx.Throws<IOException>(() => settings.FlushIfDue(), "failed write remains observable");
            File.Delete(config);
            settings.theme = "latest-pending-theme";
            settings.FlushIfDue();
            AssertEx.Equal("latest-pending-theme", ModSettings.Load().theme, "next frame retries current settings");
            string path = Path.Combine(config, "SlopWorld.toml");
            string saved = File.ReadAllText(path);
            settings.theme = "clean-change";
            settings.FlushIfDue();
            AssertEx.Equal(saved, File.ReadAllText(path), "successful retry clears dirty state");
        });

        public static void MissingProfileUsesRelativeConfigDirectory() => WithProfile(profile =>
        {
            string oldDirectory = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(profile);
            try
            {
                Directory.SetCurrentDirectory(profile);
                Verse.GenFilePaths.SaveDataFolderPath = "";
                new ModSettings { theme = "relative-profile" }.Write();
                AssertEx.True(File.Exists(Path.Combine(profile, "Config", "SlopWorld.toml")), "fallback stays under current profile directory");
                AssertEx.Equal("relative-profile", ModSettings.Load().theme, "load and write use same fallback");
            }
            finally { Directory.SetCurrentDirectory(oldDirectory); }
        });

        public static void RuntimeStringPreferencesAreNullSafeAndReflectLiveEdits() => WithSettings(settings =>
        {
            var cases = new (string Name, Action<string> Set, Func<string> Get, string Fallback)[]
            {
                ("foldedProjects", v => settings.foldedProjects = v, () => Settings.FoldedProjects, ""),
                ("sidebarTab", v => settings.sidebarTab = v, () => Settings.SidebarTab, ""),
                ("sidebarAgentStatus", v => settings.sidebarAgentStatus = v, () => Settings.SidebarAgentStatus, "all"),
                ("sidebarFilter", v => settings.sidebarFilter = v, () => Settings.SidebarFilter, ""),
                ("commandPaletteHistory", v => settings.commandPaletteHistory = v, () => Settings.CommandPaletteHistory, ""),
                ("usageIcons", v => settings.usageIcons = v, () => Settings.UsageIcons, ""),
                ("fontName", v => settings.fontName = v, () => Settings.FontName, ""),
                ("uiFontName", v => settings.uiFontName = v, () => Settings.UIFontName, ""),
                ("uiScheme", v => settings.uiScheme = v, () => Settings.UIScheme, ""),
                ("theme", v => settings.theme = v, () => Settings.Theme, ""),
                ("cursorColor", v => settings.cursorColor = v, () => Settings.CursorColor, ""),
                ("cursor", v => settings.cursor = v, () => Settings.Cursor, "tame"),
                ("radio", v => settings.radio = v, () => Settings.Radio, ""),
                ("radioHiddenSources", v => settings.radioHiddenSources = v, () => Settings.RadioHiddenSources, ""),
            };
            for (int i = 0; i < cases.Length; i++)
            {
                var item = cases[i];
                item.Set(null);
                AssertEx.Equal(item.Fallback, item.Get(), "null preference fallback " + item.Name);
                item.Set("live value\nsecond line");
                AssertEx.Equal("live value\nsecond line", item.Get(), "live preference preserved " + item.Name);
                item.Set("");
                AssertEx.Equal("", item.Get(), "explicit empty preference preserved " + item.Name);
            }
        });

        public static void RuntimeTogglesFollowBothEnabledAndDisabledValues() => WithSettings(settings =>
        {
            var cases = new (string Name, Action<bool> Set, Func<bool> Get)[]
            {
                ("autoConnect", v => settings.autoConnect = v, () => Settings.AutoConnect),
                ("fullscreen", v => settings.fullscreen = v, () => Settings.Fullscreen),
                ("sidebarHidden", v => settings.sidebarHidden = v, () => Settings.SidebarHidden),
                ("sidebarShowHidden", v => settings.sidebarShowHidden = v, () => Settings.SidebarShowHidden),
                ("sidebarShowGitignored", v => settings.sidebarShowGitignored = v, () => Settings.SidebarShowGitignored),
                ("usageSpent", v => settings.usageSpent = v, () => Settings.UsageSpent),
                ("cursorGrayscale", v => settings.cursorGrayscale = v, () => Settings.CursorGrayscale),
                ("radioMute", v => settings.radioMute = v, () => Settings.RadioMute),
                ("statusbarUsage", v => settings.statusbarUsage = v, () => Settings.StatusbarUsage),
                ("statusbarJukebox", v => settings.statusbarJukebox = v, () => Settings.StatusbarJukebox),
                ("statusbarGM", v => settings.statusbarGM = v, () => Settings.StatusbarGM),
                ("statusbarAgentIndicators", v => settings.statusbarAgentIndicators = v, () => Settings.StatusbarAgentIndicators),
                ("radioStopOnExit", v => settings.radioStopOnExit = v, () => Settings.RadioStopOnExit),
                ("grandmaMode", v => settings.grandmaMode = v, () => Settings.GrandmaMode),
                ("ecoMode", v => settings.ecoMode = v, () => Settings.EcoMode),
            };
            foreach (var item in cases)
                foreach (bool value in new[] { false, true })
                {
                    item.Set(value);
                    AssertEx.Equal(value, item.Get(), "runtime toggle follows live preference: " + item.Name);
                }
            settings.sidebarWidth = 333;
            settings.fontSize = 18;
            settings.uiFontSize = 22;
            settings.ecoDim = .7f;
            AssertEx.Equal(333f, Settings.SidebarWidth, "sidebar owns width clamping");
            AssertEx.Equal(18, Settings.FontSize, "terminal font size");
            AssertEx.Equal(22, Settings.UIFontSize, "UI font size independent of terminal");
            AssertEx.Equal(.7f, Settings.EcoDim, "backdrop dimming");
        });

        public static void RuntimeNormalizationPreservesStoredPreferences() => WithSettings(settings =>
        {
            settings.sidebarSide = "unknown";
            settings.uiDensity = "unknown";
            settings.displayMode = "old-mode";
            settings.foregroundFps = 119;
            settings.temperatureUnit = "unknown";
            settings.sidebarFilesOpenFraction = float.NaN;
            AssertEx.Equal(NavigationSide.Left, Settings.SidebarSide, "unknown navigation side falls back");
            AssertEx.Equal(UiDensityPreset.Default, Settings.UiDensity, "unknown density falls back");
            AssertEx.Equal(FramePolicy.Sync, Settings.DisplayMode, "old display policy falls back");
            AssertEx.Equal(120, Settings.ForegroundFps, "nearest FPS preset is used");
            AssertEx.Equal("", Settings.TemperatureUnit, "unknown temperature override follows game");
            AssertEx.Equal(.25f, Settings.SidebarFilesOpenFraction, "nonfinite runtime fraction falls back");
            AssertEx.Equal("old-mode", settings.displayMode, "normalization is a read projection");
            AssertEx.Equal(119, settings.foregroundFps, "read does not rewrite stored FPS");
            settings.sidebarSide = NavigationSide.Right;
            settings.displayMode = FramePolicy.Limit;
            settings.temperatureUnit = TemperatureUnit.MemeMode;
            settings.sidebarFilesOpenFraction = 2;
            AssertEx.Equal(NavigationSide.Right, Settings.SidebarSide, "explicit right side retained");
            AssertEx.Equal(FramePolicy.Limit, Settings.DisplayMode, "explicit FPS cap retained");
            AssertEx.Equal(TemperatureUnit.MemeMode, Settings.TemperatureUnit, "custom unit retained");
            AssertEx.Equal(1f, Settings.SidebarFilesOpenFraction, "runtime fraction clamped");
            AssertEx.Equal("Celsius", TemperatureUnit.Label("unknown", "Celsius"), "unknown unit uses supplied label");
        });

        public static void ClockAndSummaryModesNormalizeVisibilityAndLabels() => WithSettings(settings =>
        {
            foreach (var item in new[] { ("right", "right", "Right"), ("center", "center", "Center"), ("hidden", "hidden", "Hidden"), ("unknown", "right", "Right"), (null, "right", "Right") })
            {
                settings.statusbarClockPosition = item.Item1;
                AssertEx.Equal(item.Item2, Settings.StatusbarClockPosition, "clock mode normalization");
                AssertEx.Equal(item.Item3, StatusbarClockMode.Label(item.Item1), "clock menu label");
                AssertEx.Equal(item.Item2 != "hidden", Settings.StatusbarClock, "only hidden mode hides clock");
            }
            foreach (var item in new[] { ("center", "center", "Center"), ("left", "left", "Left"), ("unknown", "left", "Left"), (null, "left", "Left") })
            {
                settings.statusbarSummaryPosition = item.Item1;
                AssertEx.Equal(item.Item2, Settings.StatusbarSummaryPosition, "summary normalization");
                AssertEx.Equal(item.Item3, StatusbarSummaryMode.Label(item.Item1), "summary menu label");
            }
        });

        public static void TimeFormatsHandleMidnightNoonAndSeconds() => WithSettings(settings =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            foreach (var item in new[]
            {
                (0, "12:05 AM", "12:05:09 AM", "00:05", "00:05:09"),
                (12, "12:05 PM", "12:05:09 PM", "12:05", "12:05:09"),
                (23, "11:05 PM", "11:05:09 PM", "23:05", "23:05:09"),
            })
            {
                var time = new DateTime(2026, 1, 1, item.Item1, 5, 9);
                settings.timeFormat = TimeFormat.TwelveHour;
                AssertEx.Equal(item.Item2, TimeFormat.Short(time), "12-hour short time");
                AssertEx.Equal(item.Item3, TimeFormat.Long(time), "12-hour time with seconds");
                AssertEx.Equal("12-hour", TimeFormat.Label(settings.timeFormat), "12-hour label");
                settings.timeFormat = "unknown";
                AssertEx.Equal(item.Item4, TimeFormat.Short(time), "unknown format defaults to 24-hour");
                AssertEx.Equal(item.Item5, TimeFormat.Long(time), "24-hour seconds retained");
                AssertEx.Equal("24-hour", TimeFormat.Label(settings.timeFormat), "fallback label");
            }
        });
        public static void MemeModeKeepsItsEmojiLabel()
        {
            AssertEx.Equal(TemperatureUnit.MemeMode,
                TemperatureUnit.Normalize(TemperatureUnit.MemeMode),
                "custom temperature unit normalization");
            AssertEx.Equal(TemperatureUnit.MemeLabel,
                TemperatureUnit.Label(TemperatureUnit.MemeMode, "Fahrenheit"),
                "custom temperature unit label");
            AssertEx.Equal("glazed🍩/bald🦅", TemperatureUnit.MemeLabel,
                "custom temperature unit keeps the real emoji label");
        }

    }
}
