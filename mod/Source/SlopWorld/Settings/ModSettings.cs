using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    public static class StatusbarClockMode
    {
        public const string Right = "right";
        public const string Center = "center";
        public const string Hidden = "hidden";

        public static string Normalize(string mode)
        {
            if (mode == Center) return Center;
            if (mode == Hidden) return Hidden;
            return Right;
        }

        public static string Label(string mode)
        {
            if (mode == Center) return "Center";
            if (mode == Hidden) return "Hidden";
            return "Right";
        }
    }

    public static class StatusbarSummaryMode
    {
        public const string Left = "left";
        public const string Center = "center";

        public static string Normalize(string mode) => mode == Center ? Center : Left;

        public static string Label(string mode) => Normalize(mode) == Center ? "Center" : "Left";
    }

    public static class TimeFormat
    {
        public const string TwentyFourHour = "24-hour";
        public const string TwelveHour = "12-hour";

        public static string Normalize(string format) =>
            format == TwelveHour ? TwelveHour : TwentyFourHour;

        public static string Label(string format) =>
            Normalize(format) == TwelveHour ? "12-hour" : "24-hour";

        public static string Short(DateTime time) =>
            time.ToString(Normalize(Settings.TimeFormat) == TwelveHour ? "h:mm tt" : "HH:mm", CultureInfo.CurrentCulture);

        public static string Long(DateTime time) =>
            time.ToString(Normalize(Settings.TimeFormat) == TwelveHour ? "h:mm:ss tt" : "HH:mm:ss", CultureInfo.CurrentCulture);
    }

    // The meme choice changes only the displayed label; temperatures still follow game preferences.
    public static class TemperatureUnit
    {
        public const string MemeMode = "glazed-bald";
        public const string MemeLabel = "glazed🍩/bald🦅";

        public static bool IsMemeMode(string unit) => unit == MemeMode;

        public static string Normalize(string unit) => IsMemeMode(unit) ? MemeMode : "";

        public static string Label(string unit, string fallback) =>
            IsMemeMode(unit) ? MemeLabel : fallback;
    }

    // Loading enables the mod unconditionally. Settings cover daemon connection and UI
    // appearance, with terminal values sharing this settings file and endpoint discovery.
    public class ModSettings
    {
        int _dirtyAge = -1;
        const int FlushAfter = 120;

        public void MarkDirty()
        {
            _dirtyAge = 0;
        }

        public void FlushIfDue()
        {
            if (_dirtyAge < 0) return;
            if (++_dirtyAge < FlushAfter) return;
            // Keep the pending change if persistence fails; the next frame can retry.
            Write();
            _dirtyAge = -1;
        }

        public bool autoConnect = true;
        // The Linux popup window starts borderless to avoid Unity's Alt+Tab freeze. Keep
        // fullscreen on by default, but let the player return to that launch window.
        public bool fullscreen = true;
        // Sidebar preferences belong to the profile, not the colony save.
        public bool sidebarHidden;
        public float sidebarWidth = 210f;
        // The Files tab's open-file pane as a normalized share of the space above its tree.
        public float sidebarFilesOpenFraction = 0.25f;
        // Navigation placement and density are workspace preferences. Unknown values are
        // normalized by Settings so older or hand-edited files remain safe.
        public string sidebarSide = NavigationSide.Left;
        public string uiDensity = UiDensityPreset.Default;
        // Store one project name per line. The parser ignores deleted projects.
        public string foldedProjects = "";
        // Unknown tab names fall back to Agents.
        public string sidebarTab = "agents";
        public bool sidebarShowHidden;
        public bool sidebarShowGitignored;
        // Agent-row visibility: all, or a comma-separated selection of active (working or
        // waiting), idle and down.
        public string sidebarAgentStatus = "all";
        // Store one project name per line. Leave the field blank to show all projects.
        // AgentSidebar defines `[none]` semantics.
        public string sidebarFilter = "";
        public string sidebarWorktrees = "";

        // Command ids, newest first, one per line. The palette validates these against its
        // current catalogue when it opens, so removed commands do not become dead rows.
        public string commandPaletteHistory = "";

        // Per-install quota icon overrides, one `key=defName` per line. Missing rows/defs fall
        // back to UsageReadout's default.
        public string usageIcons = "";
        // Whether quota readouts lead with what is left or what has been spent. Left is the
        // useful default for resources. Spent matches the provider-facing convention.
        public bool usageSpent;

        public int fontSize = 14;
        public string fontName = "";

        public int uiFontSize;
        public string uiFontName = "";

        // Named chrome palette, separate from the terminal's `theme`. Unknown schemes fall back
        // through `UIScheme`.
        public string uiScheme = "slopworld-warm";

        // Match UI follows the chrome palette. A named terminal palette is an override.
        public string theme = "match-ui";
        // Code themes belong to this profile, independently for each daemon highlighter.
        public string codeHighlightTheme = "";
        public string codePygmentsTheme = "";
        public string codeBatTheme = "";
        public bool codeLineNumbers = true;
        // "#rrggbb", or blank for the scheme's cursor color.
        public string cursorColor = "";

        // The hardware cursor's game-asset design. Kept as a key rather than a texture path
        // so an asset can move without invalidating somebody's preference.
        public string cursor = "tame";
        public bool cursorGrayscale = true;

        // "ost" or "station-id:stream-key" from the daemon's catalog. Unknown presets use OST.
        public string radio = "ost";

        // Source ids hidden from the jukebox menu, one per line. An empty list keeps the
        // built-in OST, Spotify, and every user station visible by default.
        public string radioHiddenSources = "";

        // Stops downloads while retaining the selected station for unmute.
        public bool radioMute;

        // Which optional instruments are visible in the top statusbar. These are display
        // preferences rather than the things' own switches. Hiding the Computer Core does not
        // remove it from the map, and hiding Usage does not stop the daemon polling.
        public bool statusbarUsage = true;
        public string statusbarSummaryPosition = StatusbarSummaryMode.Left;
        public string statusbarClockPosition = StatusbarClockMode.Right;
        public string timeFormat = TimeFormat.TwentyFourHour;
        public bool statusbarJukebox = true;
        public bool statusbarGM = true;
        // Whether compact Agents rows show their startup and effective network flags.
        public bool statusbarAgentIndicators = true;

        // Whether the daemon is told to go quiet on the way out. Off by default because slopd
        // outlives the game and should carry the jukebox through a game restart.
        public bool radioStopOnExit;

        // Grandma's visiting hides gore and harmful tips.
        // It also disables destructive and Easter egg effects.
        // The background uses sparkles and rainbows. Plague arrivals use flowers.
        public bool grandmaMode;

        // Blank follows RimWorld's standard temperature preference. The custom unit is an
        // alias for Fahrenheit and also enables bird sounds for camera/UI one-shots.
        public string temperatureUnit = "";

        // Pauses simulation and map drawing while terminals keep running. See Eco.
        public bool ecoMode;

        public string displayMode = FramePolicy.Sync;
        public int foregroundFps = 60;
        public bool smoothScrolling = true;

        // Backdrop dimming: 0 leaves the menu background unchanged. See Eco.Shade.
        public float ecoDim = 0.45f;

        public static ModSettings Load()
        {
            var settings = new ModSettings();
            string path = FilePath();
            try
            {
                if (File.Exists(path))
                {
                    settings.Apply(Toml.ParseFlatScalars(File.ReadAllText(path)));
                    settings.Normalize();
                    return settings;
                }
            }
            catch (Exception e)
            {
                Log.Error("[SlopWorld] could not load TOML settings: " + e);
            }
            return settings;
        }

        public void Write()
        {
            Normalize();
            string path = FilePath();
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) directory = ".";
            Directory.CreateDirectory(directory);

            var text = new StringBuilder();
            foreach (var field in Fields) field(this, null, text);

            string temporary = path + ".tmp";
            File.WriteAllText(temporary, text.ToString(), new UTF8Encoding(false));
            try
            {
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch
            {
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
            }
        }

        static readonly Action<ModSettings, Dictionary<string, Toml.Scalar>, StringBuilder>[] Fields =
        {
            Field("autoConnect", (ModSettings s) => ref s.autoConnect, Bool, String),
            Field("fullscreen", (ModSettings s) => ref s.fullscreen, Bool, String),
            Field("sidebarHidden", (ModSettings s) => ref s.sidebarHidden, Bool, String),
            Field("sidebarWidth", (ModSettings s) => ref s.sidebarWidth, Float, Number),
            Field("sidebarFilesOpenFraction", (ModSettings s) => ref s.sidebarFilesOpenFraction, Float, Number),
            Field("sidebarSide", (ModSettings s) => ref s.sidebarSide, Text, String),
            Field("uiDensity", (ModSettings s) => ref s.uiDensity, Text, String),
            Field("foldedProjects", (ModSettings s) => ref s.foldedProjects, Text, String),
            Field("sidebarTab", (ModSettings s) => ref s.sidebarTab, Text, String),
            Field("sidebarShowHidden", (ModSettings s) => ref s.sidebarShowHidden, Bool, String),
            Field("sidebarShowGitignored", (ModSettings s) => ref s.sidebarShowGitignored, Bool, String),
            Field("sidebarAgentStatus", (ModSettings s) => ref s.sidebarAgentStatus, Text, String),
            Field("sidebarWorktrees", (ModSettings s) => ref s.sidebarWorktrees, Text, String),
            Field("sidebarFilter", (ModSettings s) => ref s.sidebarFilter, Text, String),
            Field("commandPaletteHistory", (ModSettings s) => ref s.commandPaletteHistory, Text, String),
            Field("usageIcons", (ModSettings s) => ref s.usageIcons, Text, String),
            Field("usageSpent", (ModSettings s) => ref s.usageSpent, Bool, String),
            Field("fontSize", (ModSettings s) => ref s.fontSize, Int, Number),
            Field("fontName", (ModSettings s) => ref s.fontName, Text, String),
            Field("uiFontSize", (ModSettings s) => ref s.uiFontSize, Int, Number),
            Field("uiFontName", (ModSettings s) => ref s.uiFontName, Text, String),
            Field("uiScheme", (ModSettings s) => ref s.uiScheme, Text, String),
            Field("codeHighlightTheme", (ModSettings s) => ref s.codeHighlightTheme, Text, String),
            Field("codePygmentsTheme", (ModSettings s) => ref s.codePygmentsTheme, Text, String),
            Field("codeBatTheme", (ModSettings s) => ref s.codeBatTheme, Text, String),
            Field("codeLineNumbers", (ModSettings s) => ref s.codeLineNumbers, Bool, String),
            Field("theme", (ModSettings s) => ref s.theme, Text, String),
            Field("cursorColor", (ModSettings s) => ref s.cursorColor, Text, String),
            Field("cursor", (ModSettings s) => ref s.cursor, Text, String),
            Field("cursorGrayscale", (ModSettings s) => ref s.cursorGrayscale, Bool, String),
            Field("radio", (ModSettings s) => ref s.radio, Text, String),
            Field("radioHiddenSources", (ModSettings s) => ref s.radioHiddenSources, Text, String),
            Field("radioMute", (ModSettings s) => ref s.radioMute, Bool, String),
            Field("statusbarUsage", (ModSettings s) => ref s.statusbarUsage, Bool, String),
            Field("statusbarSummaryPosition", (ModSettings s) => ref s.statusbarSummaryPosition, Text, String),
            Field("statusbarClockPosition", (ModSettings s) => ref s.statusbarClockPosition, Text, String),
            Field("timeFormat", (ModSettings s) => ref s.timeFormat, Text, String),
            Field("statusbarJukebox", (ModSettings s) => ref s.statusbarJukebox, Bool, String),
            Field("statusbarGM", (ModSettings s) => ref s.statusbarGM, Bool, String),
            Field("statusbarAgentIndicators", (ModSettings s) => ref s.statusbarAgentIndicators, Bool, String),
            Field("radioStopOnExit", (ModSettings s) => ref s.radioStopOnExit, Bool, String),
            Field("grandmaMode", (ModSettings s) => ref s.grandmaMode, Bool, String),
            Field("temperatureUnit", (ModSettings s) => ref s.temperatureUnit, Text, String),
            Field("ecoMode", (ModSettings s) => ref s.ecoMode, Bool, String),
            Field("displayMode", (ModSettings s) => ref s.displayMode, Text, String),
            Field("smoothScrolling", (ModSettings s) => ref s.smoothScrolling, Bool, String),
            Field("foregroundFps", (ModSettings s) => ref s.foregroundFps, Int, Number),
            Field("ecoDim", (ModSettings s) => ref s.ecoDim, Float, Number),
        };

        delegate ref T Setting<T>(ModSettings settings);

        static Action<ModSettings, Dictionary<string, Toml.Scalar>, StringBuilder> Field<T>(
            string key, Setting<T> field, Func<Dictionary<string, Toml.Scalar>, string, T, T> read,
            Action<StringBuilder, string, T> write) => (settings, values, text) =>
        {
            ref T value = ref field(settings);
            if (values != null) value = read(values, key, value);
            else write(text, key, value);
        };

        void Apply(Dictionary<string, Toml.Scalar> values)
        {
            foreach (var field in Fields) field(this, values, null);
        }

        void Normalize()
        {
            sidebarFilesOpenFraction = NormalizeSidebarFilesOpenFraction(sidebarFilesOpenFraction);
        }

        public static float NormalizeSidebarFilesOpenFraction(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0.25f;
            return Math.Max(0f, Math.Min(1f, value));
        }

        static string FilePath()
        {
            string profile;
            try { profile = GenFilePaths.SaveDataFolderPath; }
            catch { profile = ""; }
            if (string.IsNullOrEmpty(profile)) return Path.Combine("Config", "SlopWorld.toml");
            return Path.Combine(profile, "Config", "SlopWorld.toml");
        }

        static string Text(Dictionary<string, Toml.Scalar> values, string key, string fallback) =>
            values.TryGetValue(key, out Toml.Scalar value) && value.TryGetString(out string result)
                ? result : fallback;

        static bool Bool(Dictionary<string, Toml.Scalar> values, string key, bool fallback) =>
            values.TryGetValue(key, out Toml.Scalar value) && value.TryGetBoolean(out bool result)
                ? result : fallback;

        static int Int(Dictionary<string, Toml.Scalar> values, string key, int fallback) =>
            values.TryGetValue(key, out Toml.Scalar value) && value.TryGetInteger(out int result)
                ? result : fallback;

        static float Float(Dictionary<string, Toml.Scalar> values, string key, float fallback) =>
            values.TryGetValue(key, out Toml.Scalar value) && value.TryGetFloat(out float result)
                ? result : fallback;

        static void String(StringBuilder text, string key, string value) =>
            text.Append(key).Append(" = ").Append(Toml.Quote(value)).AppendLine();

        static void String(StringBuilder text, string key, bool value) =>
            text.Append(key).Append(" = ").Append(value ? "true" : "false").AppendLine();

        static void Number(StringBuilder text, string key, int value) =>
            text.Append(key).Append(" = ").Append(value.ToString(CultureInfo.InvariantCulture))
                .AppendLine();

        static void Number(StringBuilder text, string key, float value) =>
            text.Append(key).Append(" = ").Append(value.ToString("R", CultureInfo.InvariantCulture))
                .AppendLine();
    }

    public static class Settings
    {
        public static ModSettings S => ModEntry.Instance.settings;

        public static ConnectionInfo Connection => Endpoint.Resolve();
        public static bool AutoConnect => S.autoConnect;
        public static bool Fullscreen => S.fullscreen;
        // Unclamped: AgentSidebar owns what a usable column is, and it is the only reader.
        public static bool SidebarHidden => S.sidebarHidden;
        public static float SidebarWidth => S.sidebarWidth;
        public static float SidebarFilesOpenFraction =>
            ModSettings.NormalizeSidebarFilesOpenFraction(S.sidebarFilesOpenFraction);
        public static string SidebarSide => NavigationSide.Normalize(S.sidebarSide);
        public static string UiDensity => UiDensityPreset.Normalize(S.uiDensity);
        public static string FoldedProjects => S.foldedProjects ?? "";
        public static string SidebarTab => S.sidebarTab ?? "";
        public static bool SidebarShowHidden => S.sidebarShowHidden;
        public static bool SidebarShowGitignored => S.sidebarShowGitignored;
        public static string SidebarAgentStatus => S.sidebarAgentStatus ?? "all";
        public static string SidebarFilter => S.sidebarFilter ?? "";
        public static string CommandPaletteHistory => S.commandPaletteHistory ?? "";
        public static string UsageIcons => S.usageIcons ?? "";
        public static bool UsageSpent => S.usageSpent;
        public static int FontSize => S.fontSize;
        public static string FontName => S.fontName ?? "";
        public static int UIFontSize => S.uiFontSize;
        public static string UIFontName => S.uiFontName ?? "";
        public static string UIScheme => S.uiScheme ?? "";
        public static string Theme => S.theme ?? "";
        public static string CursorColor => S.cursorColor ?? "";
        public static string Cursor => S.cursor ?? "tame";
        public static bool CursorGrayscale => S.cursorGrayscale;
        public static string Radio => S.radio ?? "";
        public static string RadioHiddenSources => S.radioHiddenSources ?? "";
        public static bool RadioMute => S.radioMute;
        public static bool StatusbarUsage => S.statusbarUsage;
        public static string StatusbarSummaryPosition =>
            StatusbarSummaryMode.Normalize(S.statusbarSummaryPosition);
        public static string StatusbarClockPosition =>
            StatusbarClockMode.Normalize(S.statusbarClockPosition);
        public static string TimeFormat => SlopWorld.TimeFormat.Normalize(S.timeFormat);
        public static bool StatusbarClock => StatusbarClockPosition != StatusbarClockMode.Hidden;
        public static bool StatusbarJukebox => S.statusbarJukebox;
        public static bool StatusbarGM => S.statusbarGM;
        public static bool StatusbarAgentIndicators => S.statusbarAgentIndicators;
        public static bool RadioStopOnExit => S.radioStopOnExit;
        public static bool GrandmaMode => S.grandmaMode;
        public static string TemperatureUnit =>
            SlopWorld.TemperatureUnit.Normalize(S.temperatureUnit);
        public static bool EcoMode => S.ecoMode;
        public static string DisplayMode => FramePolicy.Normalize(S.displayMode);
        public static bool SmoothScrolling => S.smoothScrolling;
        public static int ForegroundFps => FramePolicy.NearestPreset(S.foregroundFps);
        public static float EcoDim => S.ecoDim;
    }
}
