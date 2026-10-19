namespace SlopWorld
{
    // Projects the active profile into runtime callers; ModSettings owns preference data.
    public static class Settings
    {
        public static ModSettings S => ModEntry.Instance.settings;

        public static ConnectionInfo Connection => Endpoint.Resolve();
        public static bool AutoConnect => S.autoConnect;
        public static bool Fullscreen => S.fullscreen;

        // Unclamped: Sidebar owns what a usable column is, and it is the only reader.
        public static bool SidebarHidden => S.sidebarHidden;
        public static float SidebarWidth => S.sidebarWidth;
        public static bool SidebarOpenFilesVisible => S.sidebarOpenFilesVisible;
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
        public static string DateFormat => SlopWorld.DateFormat.Normalize(S.dateFormat);
        public static bool StatusbarClock => StatusbarClockPosition != StatusbarClockMode.Hidden;
        public static bool StatusbarJukebox => S.statusbarJukebox;
        public static bool StatusbarGM => S.statusbarGM;
        public static bool StatusbarAgentIndicators => S.statusbarAgentIndicators;
        public static bool RadioStopOnExit => S.radioStopOnExit;
        public static bool GrandmaMode => S.grandmaMode;
        public static string TemperatureUnit =>
            SlopWorld.TemperatureUnit.Normalize(S.temperatureUnit);
        public static bool EcoMode => S.ecoMode;
        public static bool VSync => S.vsync;
        public static string DisplayMode => FramePolicy.Normalize(S.displayMode);
        public static bool SmoothScrolling => S.smoothScrolling;
        public static int ForegroundFps => FramePolicy.NearestPreset(S.foregroundFps);
        public static float EcoDim => S.ecoDim;
    }
}
