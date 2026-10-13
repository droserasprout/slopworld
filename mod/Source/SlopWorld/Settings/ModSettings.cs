using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SlopWorld
{
    // Owns profile preference data, typed TOML serialization, and dirty timing.
    // ProfileSettings supplies persistence; Settings projects the active profile.
    public class ModSettings
    {
        int _dirtyAge = -1;
        const int FlushAfter = 120;

        public void MarkDirty()
        {
            _dirtyAge = 0;
        }

        public void FlushIfDue(Action<ModSettings> write)
        {
            if (_dirtyAge < 0)
                return;
            if (++_dirtyAge < FlushAfter)
                return;
            // Keep the pending change if persistence fails; the next frame can retry.
            write(this);
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
        // normalized by Settings so hand-edited files remain safe.
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
        public string dateFormat = DateFormat.YearFirst;
        public bool statusbarJukebox = true;
        public bool statusbarGM = true;

        // Whether compact Agents rows show their startup and effective network flags.
        public bool statusbarAgentIndicators = true;

        // Whether the daemon is told to go quiet on the way out. Off by default because slopd
        // outlives the game and should carry the jukebox through a game restart.
        public bool radioStopOnExit;

        // Grandma's visiting hides gore and harmful tips.
        // It also disables destructive and Easter egg effects.
        // Plague arrivals use flowers.
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

        public static ModSettings FromToml(string text)
        {
            var settings = new ModSettings();
            settings.Apply(Toml.ParseFlatScalars(text));
            settings.Normalize();
            return settings;
        }

        public string ToToml()
        {
            Normalize();
            var text = new StringBuilder();
            foreach (var field in Fields)
                field(this, null, text);
            return text.ToString();
        }

        static readonly Action<
            ModSettings,
            Dictionary<string, Toml.Scalar>,
            StringBuilder
        >[] Fields =
        {
            Field("autoConnect", (ModSettings s) => ref s.autoConnect, Bool, String),
            Field("fullscreen", (ModSettings s) => ref s.fullscreen, Bool, String),
            Field("sidebarHidden", (ModSettings s) => ref s.sidebarHidden, Bool, String),
            Field("sidebarWidth", (ModSettings s) => ref s.sidebarWidth, Float, Number),
            Field(
                "sidebarFilesOpenFraction",
                (ModSettings s) => ref s.sidebarFilesOpenFraction,
                Float,
                Number
            ),
            Field("sidebarSide", (ModSettings s) => ref s.sidebarSide, Text, String),
            Field("uiDensity", (ModSettings s) => ref s.uiDensity, Text, String),
            Field("foldedProjects", (ModSettings s) => ref s.foldedProjects, Text, String),
            Field("sidebarTab", (ModSettings s) => ref s.sidebarTab, Text, String),
            Field("sidebarShowHidden", (ModSettings s) => ref s.sidebarShowHidden, Bool, String),
            Field(
                "sidebarShowGitignored",
                (ModSettings s) => ref s.sidebarShowGitignored,
                Bool,
                String
            ),
            Field("sidebarAgentStatus", (ModSettings s) => ref s.sidebarAgentStatus, Text, String),
            Field("sidebarWorktrees", (ModSettings s) => ref s.sidebarWorktrees, Text, String),
            Field("sidebarFilter", (ModSettings s) => ref s.sidebarFilter, Text, String),
            Field(
                "commandPaletteHistory",
                (ModSettings s) => ref s.commandPaletteHistory,
                Text,
                String
            ),
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
            Field(
                "statusbarSummaryPosition",
                (ModSettings s) => ref s.statusbarSummaryPosition,
                Text,
                String
            ),
            Field(
                "statusbarClockPosition",
                (ModSettings s) => ref s.statusbarClockPosition,
                Text,
                String
            ),
            Field("timeFormat", (ModSettings s) => ref s.timeFormat, Text, String),
            Field("dateFormat", (ModSettings s) => ref s.dateFormat, Text, String),
            Field("statusbarJukebox", (ModSettings s) => ref s.statusbarJukebox, Bool, String),
            Field("statusbarGM", (ModSettings s) => ref s.statusbarGM, Bool, String),
            Field(
                "statusbarAgentIndicators",
                (ModSettings s) => ref s.statusbarAgentIndicators,
                Bool,
                String
            ),
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
            string key,
            Setting<T> field,
            Func<Dictionary<string, Toml.Scalar>, string, T, T> read,
            Action<StringBuilder, string, T> write
        ) =>
            (settings, values, text) =>
            {
                ref T value = ref field(settings);
                if (values != null)
                    value = read(values, key, value);
                else
                    write(text, key, value);
            };

        void Apply(Dictionary<string, Toml.Scalar> values)
        {
            foreach (var field in Fields)
                field(this, values, null);
        }

        void Normalize()
        {
            sidebarFilesOpenFraction = NormalizeSidebarFilesOpenFraction(sidebarFilesOpenFraction);
        }

        public static float NormalizeSidebarFilesOpenFraction(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0.25f;
            return Math.Max(0f, Math.Min(1f, value));
        }

        static string Text(Dictionary<string, Toml.Scalar> values, string key, string fallback) =>
            values.TryGetValue(key, out Toml.Scalar value) && value.TryGetString(out string result)
                ? result
                : fallback;

        static bool Bool(Dictionary<string, Toml.Scalar> values, string key, bool fallback) =>
            values.TryGetValue(key, out Toml.Scalar value) && value.TryGetBoolean(out bool result)
                ? result
                : fallback;

        static int Int(Dictionary<string, Toml.Scalar> values, string key, int fallback) =>
            values.TryGetValue(key, out Toml.Scalar value) && value.TryGetInteger(out int result)
                ? result
                : fallback;

        static float Float(Dictionary<string, Toml.Scalar> values, string key, float fallback) =>
            values.TryGetValue(key, out Toml.Scalar value) && value.TryGetFloat(out float result)
                ? result
                : fallback;

        static void String(StringBuilder text, string key, string value) =>
            text.Append(key).Append(" = ").Append(Toml.Quote(value)).AppendLine();

        static void String(StringBuilder text, string key, bool value) =>
            text.Append(key).Append(" = ").Append(value ? "true" : "false").AppendLine();

        static void Number(StringBuilder text, string key, int value) =>
            text.Append(key)
                .Append(" = ")
                .Append(value.ToString(CultureInfo.InvariantCulture))
                .AppendLine();

        static void Number(StringBuilder text, string key, float value) =>
            text.Append(key)
                .Append(" = ")
                .Append(value.ToString("R", CultureInfo.InvariantCulture))
                .AppendLine();
    }
}
