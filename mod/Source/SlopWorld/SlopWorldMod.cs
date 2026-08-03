using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    // Everything the mod does to the game is unconditional: being loaded is the
    // switch, as it always was for the UI stripping. A checkbox that turns the mod
    // back into RimWorld is not a setting anyone wants, it is a second product nobody
    // tests. What is left is about this machine and about the eyes reading it, never
    // about the design: where the daemon is, and what a pane looks like on this screen.
    //
    // The pane's half of that is edited in TerminalSettingsWindow rather than here, but
    // it is scribed here, because there is one settings file.
    public class SlopSettings : ModSettings
    {
        public string host = "127.0.0.1";
        public int port = 7717;
        public string token = "";
        public bool autoConnect = true;
        // The column's width, dragged rather than typed, and the projects rolled up in it.
        // Both are about this screen the way the layout itself is, so they live beside it -
        // and a project is the daemon's rather than a colony's, so neither belongs in a save.
        // Folds are one name per line; a project that has gone is a name nothing matches.
        public float sidebarWidth = 210f;
        public string foldedProjects = "";

        public int fontSize = 14;
        public string fontName = "";

        // The pane's palette, by name. A scheme this build no longer ships reads as the
        // default rather than as no colours at all.
        public string theme = "clankers";
        // "#rrggbb", or blank for the scheme's own. The one colour worth overriding on
        // its own: everything else is the scheme's business, and a cursor you cannot find
        // is about the screen it is on.
        public string cursorColor = "";

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref host, "host", "127.0.0.1");
            Scribe_Values.Look(ref port, "port", 7717);
            Scribe_Values.Look(ref token, "token", "");
            Scribe_Values.Look(ref autoConnect, "autoConnect", true);
            Scribe_Values.Look(ref sidebarWidth, "sidebarWidth", 210f);
            Scribe_Values.Look(ref foldedProjects, "foldedProjects", "");
            Scribe_Values.Look(ref fontSize, "fontSize", 14);
            Scribe_Values.Look(ref fontName, "fontName", "");
            Scribe_Values.Look(ref theme, "theme", "clankers");
            Scribe_Values.Look(ref cursorColor, "cursorColor", "");
        }
    }

    // Static shorthand so call sites don't reach through the Mod instance.
    public static class Settings
    {
        public static SlopSettings S => SlopWorldMod.Instance.settings;

        public static string Host => S.host;
        public static int Port => S.port;
        public static string Token => S.token;
        public static bool AutoConnect => S.autoConnect;
        // Unclamped: AgentSidebar owns what a usable column is, and it is the only reader.
        public static float SidebarWidth => S.sidebarWidth;
        public static string FoldedProjects => S.foldedProjects ?? "";
        public static int FontSize => S.fontSize;
        public static string FontName => S.fontName ?? "";
        public static string Theme => S.theme ?? "";
        public static string CursorColor => S.cursorColor ?? "";
    }

    public class SlopWorldMod : Mod
    {
        public static SlopWorldMod Instance;
        public readonly SlopSettings settings;

        public SlopWorldMod(ModContentPack content) : base(content)
        {
            Instance = this;
            settings = GetSettings<SlopSettings>();
        }

        public override string SettingsCategory() => "SlopWorld";

        public override void DoSettingsWindowContents(Rect rect)
        {
            var l = new Listing_Standard();
            l.Begin(rect);

            l.Label($"Daemon: {SlopClient.BaseUrl}  [{SessionHub.Instance.Status}]");
            l.Gap(4f);

            l.Label("Host");
            settings.host = l.TextEntry(settings.host);

            l.Label($"Port: {settings.port}");
            settings.port = Mathf.RoundToInt(l.Slider(settings.port, 1024, 65535));

            l.Label("Token (blank = no auth)");
            settings.token = l.TextEntry(settings.token);

            l.Gap(6f);
            l.CheckboxLabeled("Auto-connect and reconnect", ref settings.autoConnect);

            l.Gap(10f);
            if (l.ButtonText("Reconnect now"))
                SessionHub.Instance.Connect();

            l.End();
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            TerminalFont.Invalidate();
            TerminalTheme.Invalidate();
            SessionHub.Instance.Connect();
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
        }
    }

    // Drives the hub. Root.Update runs on the menu and in-game alike.
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    public static class Patch_Root_Update
    {
        static void Postfix()
        {
            SessionHub.Instance.Update();
            // Intercepts Alt+F4 / window close: shows the confirmation dialog on the
            // frame after the save completes.
            QuitInterceptor.Check();
            // Drops the framerate while the window is behind something else. Here because
            // it has to hold on the menu too, and because focus is a per-frame question.
            BackgroundFrames.Follow();
            // The pointer's own animation, which has nowhere else to run.
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

    public class MainButtonWorker_Projects : MainButtonWorker_Slop
    {
        protected override void Open() => ProjectsWindow.Toggle();
    }

    public class MainButtonWorker_Agents : MainButtonWorker_Slop
    {
        protected override void Open() => SessionsWindow.Toggle();
    }

    public class MainButtonWorker_Shortcuts : MainButtonWorker_Slop
    {
        protected override void Open() => ShortcutsWindow.Toggle();
    }

    public class MainButtonWorker_Config : MainButtonWorker_Slop
    {
        protected override void Open() => SlopOptions.Toggle();
    }
}
