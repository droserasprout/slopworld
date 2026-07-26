using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    public class SlopSettings : ModSettings
    {
        public string host = "127.0.0.1";
        public int port = 7717;
        public string token = "";
        public bool autoConnect = true;

        /// Kill the colony sim: needs, health, aging, mental breaks, storyteller.
        public bool stripSim = true;
        /// Spawn and despawn a colonist per daemon session.
        public bool spawnPawns = true;
        /// Draw state icons over agent colonists.
        public bool overlay = true;
        /// Withhold the scenario's starting resources: a dead world gives nothing.
        public bool noResources = true;
        /// Print every duration on the wall clock instead of RimWorld's calendar.
        public bool realTime = true;
        /// Draw what is left of the subscription where the resource readout was.
        /// Costs nothing when the daemon is not polling: with no numbers to show
        /// the readout draws nothing at all.
        public bool usageReadout = true;

        /// Load the newest save on launch instead of stopping at the main menu.
        /// The mod is rebuilt far more often than the colony is, so the default
        /// answer to "what do you want to look at" is "the same thing as before".
        public bool resumeLastSave = true;
        /// Real minutes between autosaves. RimWorld counts them in game days,
        /// which at 1x is about a quarter of an hour - too coarse to be the thing
        /// a restart falls back on. 0 leaves it to the game.
        public int autosaveMinutes = 2;
        /// Reopen whichever agent's terminal was up when the game last closed.
        public bool reopenTerminal = true;

        public int fontSize = 14;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref host, "host", "127.0.0.1");
            Scribe_Values.Look(ref port, "port", 7717);
            Scribe_Values.Look(ref token, "token", "");
            Scribe_Values.Look(ref autoConnect, "autoConnect", true);
            Scribe_Values.Look(ref stripSim, "stripSim", true);
            Scribe_Values.Look(ref spawnPawns, "spawnPawns", true);
            Scribe_Values.Look(ref overlay, "overlay", true);
            Scribe_Values.Look(ref noResources, "noResources", true);
            Scribe_Values.Look(ref realTime, "realTime", true);
            Scribe_Values.Look(ref usageReadout, "usageReadout", true);
            Scribe_Values.Look(ref resumeLastSave, "resumeLastSave", true);
            Scribe_Values.Look(ref autosaveMinutes, "autosaveMinutes", 2);
            Scribe_Values.Look(ref reopenTerminal, "reopenTerminal", true);
            Scribe_Values.Look(ref fontSize, "fontSize", 14);
        }
    }

    /// <summary>Static shorthand so call sites don't reach through the Mod instance.</summary>
    public static class Settings
    {
        public static SlopSettings S => SlopWorldMod.Instance.settings;

        public static string Host => S.host;
        public static int Port => S.port;
        public static string Token => S.token;
        public static bool AutoConnect => S.autoConnect;
        public static bool StripSim => S.stripSim;
        public static bool SpawnPawns => S.spawnPawns;
        public static bool Overlay => S.overlay;
        public static bool NoResources => S.noResources;
        public static bool RealTime => S.realTime;
        public static bool UsageReadout => S.usageReadout;
        public static bool ResumeLastSave => S.resumeLastSave;
        public static int AutosaveMinutes => S.autosaveMinutes;
        public static bool ReopenTerminal => S.reopenTerminal;
        public static int FontSize => S.fontSize;
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
            l.CheckboxLabeled("Strip the colony sim", ref settings.stripSim,
                "Freezes needs, health, aging, mental breaks and the storyteller. " +
                "Turn off to let RimWorld run normally underneath.");
            l.CheckboxLabeled("Spawn a colonist per session", ref settings.spawnPawns);
            l.CheckboxLabeled("Draw agent state over colonists", ref settings.overlay);
            l.CheckboxLabeled("Withhold starting resources", ref settings.noResources,
                "A new colony lands with none of the scenario's usual resources.");
            l.CheckboxLabeled("Show times on the wall clock", ref settings.realTime,
                "Every duration the game prints - the 'occurred X ago' on a colonist's " +
                "log above all - is the span that really passed, not the one RimWorld's " +
                "calendar makes of it.");

            l.CheckboxLabeled("Show what is left of the subscription", ref settings.usageReadout,
                "The session and weekly limits, top left, where RimWorld's resource " +
                "readout used to be. The daemon does the asking; turn its [daemon] " +
                "usage setting off to stop it entirely.");

            l.Gap(6f);
            l.CheckboxLabeled("Resume the newest colony on launch", ref settings.resumeLastSave,
                "Skips the main menu and loads the last save. Turn off to start from " +
                "the menu like any other game.");
            l.CheckboxLabeled("Reopen the last terminal", ref settings.reopenTerminal,
                "Whichever agent you were typing at when the game closed is back on " +
                "screen once its session reports in.");

            l.Label(settings.autosaveMinutes > 0
                ? $"Autosave every {settings.autosaveMinutes} real minutes"
                : "Autosave left to RimWorld's own schedule");
            settings.autosaveMinutes = Mathf.RoundToInt(l.Slider(settings.autosaveMinutes, 0, 30));

            l.Gap(6f);
            l.Label($"Terminal font size: {settings.fontSize}");
            settings.fontSize = Mathf.RoundToInt(l.Slider(settings.fontSize, 8, 28));

            l.Gap(10f);
            if (l.ButtonText("Reconnect now"))
                SessionHub.Instance.Connect();

            l.End();
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            TerminalFont.Invalidate();
            SessionHub.Instance.Connect();
        }
    }

    [StaticConstructorOnStartup]
    public static class SlopWorldBootstrap
    {
        static SlopWorldBootstrap()
        {
            var h = new Harmony("drsr.slopworld");
            try
            {
                h.PatchAll(Assembly.GetExecutingAssembly());
                Patch_HideGui.Apply(h);
                Patch_MainButtons.Apply(h);
                Patch_InspectTabs.Apply(h);
            }
            catch (Exception e)
            {
                // A patch that fails to bind must not take the rest down with
                // it: PatchAll aborts on the first bad one, and a mod with no
                // patches at all is indistinguishable from a mod that isn't
                // there. Log loud and keep whatever bound before the throw.
                Log.Error($"[SlopWorld] patching incomplete: {e}");
            }
            Log.Message("[SlopWorld] patched; daemon at " + SlopClient.BaseUrl);
        }
    }

    /// <summary>Drives the hub. Root.Update runs on the menu and in-game alike.</summary>
    [HarmonyPatch(typeof(Root), nameof(Root.Update))]
    public static class Patch_Root_Update
    {
        static void Postfix() => SessionHub.Instance.Update();
    }

    public class MainButtonWorker_Agents : RimWorld.MainButtonWorker
    {
        public override void Activate() => SessionsWindow.Toggle();
    }

    public class MainButtonWorker_Config : RimWorld.MainButtonWorker
    {
        public override void Activate() => ConfigMenuWindow.Toggle();
    }
}
