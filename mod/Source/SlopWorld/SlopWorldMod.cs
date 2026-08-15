using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
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

    // Loading enables the mod unconditionally; settings cover daemon connection and UI
    // appearance, with terminal values sharing this settings file and endpoint discovery.
    public class SlopSettings
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
            _dirtyAge = -1;
            Write();
        }

        public bool autoConnect = true;
        // The column's width, dragged rather than typed, and the projects rolled up in it.
        // Both are about this screen the way the layout itself is, so they live beside it -
        // and a project is the daemon's rather than a colony's, so neither belongs in a save.
        // Folds are one name per line; a project that has gone is a name nothing matches.
        public float sidebarWidth = 210f;
        public string foldedProjects = "";
        // Which of the column's two views is up, and whether its tree says anything about
        // dotfiles. Same argument: about this screen, not about a colony. A name this build
        // does not know reads as the agents, which is the view that is always worth having.
        public string sidebarTab = "agents";
        public bool sidebarShowHidden;
        // The projects ticked in the column's filter, one name a line and blank for all of
        // them - the folds' own format, kept here for the folds' own reason. See
        // AgentSidebar for what an unticked name and the `[none]` line mean.
        public string sidebarFilter = "";

        // Per-install quota icon overrides, one `key=defName` per line; missing rows/defs fall
        // back to UsageReadout's default.
        public string usageIcons = "";
        // Whether quota readouts lead with what is left or what has been spent. Left is the
        // useful default for resources; spent matches the provider-facing convention.
        public bool usageSpent;

        public int fontSize = 14;
        public string fontName = "";

        public int uiFontSize;
        public string uiFontName = "";

        // Named chrome palette, separate from the terminal's `theme`; unknown schemes fall back
        // through `UIScheme`.
        public string uiScheme = "slopworld";

        // The pane's palette, by name. A scheme this build no longer ships reads as the
        // default rather than as no colors at all.
        public string theme = "slopworld";
        // "#rrggbb", or blank for the scheme's own. The one color worth overriding on
        // its own: everything else is the scheme's business, and a cursor you cannot find
        // is about the screen it is on.
        public string cursorColor = "";

        // The hardware cursor's game-asset design. Kept as a key rather than a texture path
        // so an asset can move without invalidating somebody's preference.
        public string cursor = "tame";
        public bool cursorGrayscale = true;

        // Which station the jukebox is on: "ost", or "station-id:stream-key" from the
        // daemon's catalog. Here rather than in a save for the reason the theme is: it is
        // about this room and these ears, and it is wanted back on the next colony rather
        // than buried with this one. A preset this build no longer lists reads as the OST.
        public string radio = "ost";

        // The jukebox's off switch: it is a stop rather than a volume of zero, so nothing
        // is downloaded for nobody. Kept apart from the station so unmuting comes back to
        // what was on.
        public bool radioMute;

        // Which optional instruments are visible in the top statusbar. These are display
        // preferences rather than the things' own switches: hiding the Computer Core does
        // not remove it from the map, and hiding Usage does not stop the daemon polling.
        public bool statusbarUsage = true;
        public string statusbarClockPosition = StatusbarClockMode.Right;
        public bool statusbarJukebox = true;
        public bool statusbarGM = true;

        // Whether the daemon is told to go quiet on the way out. On by default: slopd
        // outlives the game, and music playing on a machine with nothing on screen to stop
        // it from is the surprise, not the feature.
        public bool radioStopOnExit = true;

        // Grandma mode removes gore, harmful tips, and destructive/easter-egg effects; the
        // background becomes sparkles/rainbows and plague arrivals grow flowers.
        public bool grandmaMode;

        // Eco mode: the board stops. The clock is held paused, the map's draw chain stands
        // down, the frames are capped, and with the pane closed the menu's own background is
        // drawn where the board was. Everything the terminal is made of keeps running. See
        // Eco.
        public bool ecoMode;

        // How far the eco backdrop is taken down behind the agents, 0 being the picture as the
        // menu draws it. Eco is a mode somebody leaves the game sitting in, so this is taste
        // and not a constant. See Eco.Shade.
        public float ecoDim = 0.45f;

        public static SlopSettings Load()
        {
            var settings = new SlopSettings();
            string path = FilePath();
            try
            {
                if (File.Exists(path))
                {
                    settings.Apply(Toml.ParseFlat(File.ReadAllText(path)));
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
            string path = FilePath();
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory)) directory = ".";
            Directory.CreateDirectory(directory);

            var text = new StringBuilder();
            String(text, "autoConnect", autoConnect);
            Number(text, "sidebarWidth", sidebarWidth);
            String(text, "foldedProjects", foldedProjects);
            String(text, "sidebarTab", sidebarTab);
            String(text, "sidebarShowHidden", sidebarShowHidden);
            String(text, "sidebarFilter", sidebarFilter);
            String(text, "usageIcons", usageIcons);
            String(text, "usageSpent", usageSpent);
            Number(text, "fontSize", fontSize);
            String(text, "fontName", fontName);
            Number(text, "uiFontSize", uiFontSize);
            String(text, "uiFontName", uiFontName);
            String(text, "uiScheme", uiScheme);
            String(text, "theme", theme);
            String(text, "cursorColor", cursorColor);
            String(text, "cursor", cursor);
            String(text, "cursorGrayscale", cursorGrayscale);
            String(text, "radio", radio);
            String(text, "radioMute", radioMute);
            String(text, "statusbarUsage", statusbarUsage);
            String(text, "statusbarClockPosition", statusbarClockPosition);
            String(text, "statusbarJukebox", statusbarJukebox);
            String(text, "statusbarGM", statusbarGM);
            String(text, "radioStopOnExit", radioStopOnExit);
            String(text, "grandmaMode", grandmaMode);
            String(text, "ecoMode", ecoMode);
            Number(text, "ecoDim", ecoDim);

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

        void Apply(Dictionary<string, string> values)
        {
            autoConnect = Bool(values, "autoConnect", autoConnect);
            sidebarWidth = Float(values, "sidebarWidth", sidebarWidth);
            foldedProjects = Text(values, "foldedProjects", foldedProjects);
            sidebarTab = Text(values, "sidebarTab", sidebarTab);
            sidebarShowHidden = Bool(values, "sidebarShowHidden", sidebarShowHidden);
            sidebarFilter = Text(values, "sidebarFilter", sidebarFilter);
            usageIcons = Text(values, "usageIcons", usageIcons);
            usageSpent = Bool(values, "usageSpent", usageSpent);
            fontSize = Int(values, "fontSize", fontSize);
            fontName = Text(values, "fontName", fontName);
            uiFontSize = Int(values, "uiFontSize", uiFontSize);
            uiFontName = Text(values, "uiFontName", uiFontName);
            uiScheme = Text(values, "uiScheme", uiScheme);
            theme = Text(values, "theme", theme);
            cursorColor = Text(values, "cursorColor", cursorColor);
            cursor = Text(values, "cursor", cursor);
            cursorGrayscale = Bool(values, "cursorGrayscale", cursorGrayscale);
            radio = Text(values, "radio", radio);
            radioMute = Bool(values, "radioMute", radioMute);
            statusbarUsage = Bool(values, "statusbarUsage", statusbarUsage);
            statusbarClockPosition = Text(values, "statusbarClockPosition", statusbarClockPosition);
            statusbarJukebox = Bool(values, "statusbarJukebox", statusbarJukebox);
            statusbarGM = Bool(values, "statusbarGM", statusbarGM);
            radioStopOnExit = Bool(values, "radioStopOnExit", radioStopOnExit);
            grandmaMode = Bool(values, "grandmaMode", grandmaMode);
            ecoMode = Bool(values, "ecoMode", ecoMode);
            ecoDim = Float(values, "ecoDim", ecoDim);
        }

        static string FilePath()
        {
            string profile;
            try { profile = GenFilePaths.SaveDataFolderPath; }
            catch { profile = ""; }
            if (string.IsNullOrEmpty(profile)) return Path.Combine("Config", "SlopWorld.toml");
            return Path.Combine(profile, "Config", "SlopWorld.toml");
        }

        static string Text(Dictionary<string, string> values, string key, string fallback) =>
            values.TryGetValue(key, out string value) ? value : fallback;

        static bool Bool(Dictionary<string, string> values, string key, bool fallback) =>
            values.TryGetValue(key, out string value) && bool.TryParse(value, out bool result)
                ? result : fallback;

        static int Int(Dictionary<string, string> values, string key, int fallback) =>
            values.TryGetValue(key, out string value) && int.TryParse(value,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                    ? result : fallback;

        static float Float(Dictionary<string, string> values, string key, float fallback) =>
            values.TryGetValue(key, out string value) && float.TryParse(value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out float result)
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

    // Static shorthand so call sites don't reach through the Mod instance.
    public static class Settings
    {
        public static SlopSettings S => SlopWorldMod.Instance.settings;

        public static ConnectionInfo Connection => Endpoint.Resolve();
        public static bool AutoConnect => S.autoConnect;
        // Unclamped: AgentSidebar owns what a usable column is, and it is the only reader.
        public static float SidebarWidth => S.sidebarWidth;
        public static string FoldedProjects => S.foldedProjects ?? "";
        public static string SidebarTab => S.sidebarTab ?? "";
        public static bool SidebarShowHidden => S.sidebarShowHidden;
        public static string SidebarFilter => S.sidebarFilter ?? "";
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
        public static bool RadioMute => S.radioMute;
        public static bool StatusbarUsage => S.statusbarUsage;
        public static string StatusbarClockPosition =>
            StatusbarClockMode.Normalize(S.statusbarClockPosition);
        public static bool StatusbarClock => StatusbarClockPosition != StatusbarClockMode.Hidden;
        public static bool StatusbarJukebox => S.statusbarJukebox;
        public static bool StatusbarGM => S.statusbarGM;
        public static bool RadioStopOnExit => S.radioStopOnExit;
        public static bool GrandmaMode => S.grandmaMode;
        public static bool EcoMode => S.ecoMode;
        public static float EcoDim => S.ecoDim;
    }

    public class SlopWorldMod : Mod
    {
        public static SlopWorldMod Instance;
        public readonly SlopSettings settings;

        public SlopWorldMod(ModContentPack content) : base(content)
        {
            Instance = this;
            settings = SlopSettings.Load();
        }

        public override string SettingsCategory() => "SlopWorld";

        public override void DoSettingsWindowContents(Rect rect)
        {
            var l = new Listing_Standard();
            l.Begin(rect);

            l.Label($"Daemon: {SlopClient.BaseUrl}  [{SessionHub.Instance.Status}]");
            l.Gap(SlopWidgets.GapM);

            l.Label("Connection: daemon endpoint");

            l.Gap(SlopWidgets.GapM);
            settings.autoConnect =
                SlopWidgets.Checkbox(l, "Auto-connect and reconnect", settings.autoConnect);

            l.Gap(SlopWidgets.GapM);
            if (SlopWidgets.Button(l.GetRect(SlopWidgets.BtnH), "Reconnect now"))
                SessionHub.Instance.Connect();

            l.End();
        }

        public override void WriteSettings()
        {
            settings.Write();
            TerminalFont.Invalidate();
            TerminalTheme.Invalidate();
            SlopUIFont.Apply();
            if (Settings.AutoConnect) SessionHub.Instance.Connect();
            else SessionHub.Instance.Disconnect();
        }
    }

    [StaticConstructorOnStartup]
    public static class SlopWorldBootstrap
    {
        static SlopWorldBootstrap()
        {
            // First, and before anything is patched: this mod is only ever run in a save
            // folder of its own, and in anybody else's game it does nothing at all. See
            // SlopProfile.
            if (!SlopProfile.Ok)
            {
                SlopProfile.Complain();
                return;
            }

            var h = new Harmony("drsr.slopworld");
            try
            {
                h.PatchAll(Assembly.GetExecutingAssembly());
                Patch_HideGui.Apply(h);
                Patch_MainButtons.Apply(h);
                StripKeys.Apply(h);
                Patch_InspectTabs.Apply(h);
                Patch_NoRelateAgents.Apply(h);
            }
            catch (Exception e)
            {
                // PatchAll aborts on the first bad one, and a mod with no patches at all is
                // indistinguishable from a mod that isn't there. Log loud and keep whatever bound
                // before the throw.
                Log.Error($"[SlopWorld] patching incomplete: {e}");
            }
            // Not a patch: a preference this build insists on, which has to be put right once
            // for a file that has it off.
            Patch_RunInBackground.Enforce();
            // Nor is this one: a field moved on a def vanilla already reads, which is how a
            // whole options category goes.
            StripOptions.Hide();
            // And this is the category that arrives in its place, added to the database
            // rather than shipped as XML so a refusing mod leaves no empty tab behind.
            SlopOptions.Install();
            // Intercepts Alt+F4 / window close to save and show a confirmation dialog.
            QuitInterceptor.Register();
            Log.Message("[SlopWorld] patched; daemon at " + SlopClient.BaseUrl);
            SlopUIFont.Apply();
        }
    }

    // Drives the hub. Root.Update runs on the menu and in-game alike.
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    public static class Patch_Root_Update
    {
        static void Postfix()
        {
            // The jukebox has no MonoBehaviour of its own. Run it before the client's
            // synchronous reconnect can block this frame while the daemon is restarting.
            Radio.Update();
            SessionHub.Instance.Update();
            // Intercepts Alt+F4 / window close: shows the confirmation dialog on the
            // frame after the save completes.
            QuitInterceptor.Check();
            // Drops the framerate while the window is behind something else. Here because
            // it has to hold on the menu too, and because focus is a per-frame question.
            BackgroundFrames.Follow();
            SlopWorldMod.Instance?.settings.FlushIfDue();
            DeadCursor.Tick();
            // Update and not OnGUI, so it fires per frame rather than per event, and below
            // HandleEventsHighPriority, where the clicks that count are used -
            // GetMouseButtonDown still sees them, that flag being Input's own.
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                DeadCursor.Click();
        }
    }

    // A def is the one thing a refusing mod cannot take back: these four buttons are
    // added to the bar by XML, which is read whether we patched anything or not. So in
    // somebody's ordinary game they are doors onto the explanation rather than onto a
    // daemon we never dialled.
    public abstract class MainButtonWorker_Slop : RimWorld.MainButtonWorker
    {
        public sealed override void Activate()
        {
            if (!SlopProfile.Ok)
            {
                SlopProfile.Complain();
                return;
            }
            Open();
        }

        protected abstract void Open();
    }

    public class MainButtonWorker_Config : MainButtonWorker_Slop
    {
        protected override void Open() => SlopOptions.Toggle();
    }
}
