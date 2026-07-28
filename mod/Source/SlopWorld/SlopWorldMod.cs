using System.Linq;
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
    // tests. What is left is the two things about this machine rather than about the
    // design: where the daemon is, and how big the font is on this screen.
    public class SlopSettings : ModSettings
    {
        public string host = "127.0.0.1";
        public int port = 7717;
        public string token = "";
        public bool autoConnect = true;

        public int fontSize = 14;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref host, "host", "127.0.0.1");
            Scribe_Values.Look(ref port, "port", 7717);
            Scribe_Values.Look(ref token, "token", "");
            Scribe_Values.Look(ref autoConnect, "autoConnect", true);
            Scribe_Values.Look(ref fontSize, "fontSize", 14);
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
            // The pointer's own animation, which has nowhere else to run.
            DeadCursor.Tick();
            // Update and not OnGUI, so it fires per frame rather than per event, and below
            // HandleEventsHighPriority, where the clicks that count are used -
            // GetMouseButtonDown still sees them, that flag being Input's own.
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                DeadCursor.Click();
        }
    }

    public class MainButtonWorker_Projects : RimWorld.MainButtonWorker
    {
        public override void Activate() => ProjectsWindow.Toggle();
    }

    public class MainButtonWorker_Agents : RimWorld.MainButtonWorker
    {
        public override void Activate() => SessionsWindow.Toggle();
    }

    public class MainButtonWorker_Shortcuts : RimWorld.MainButtonWorker
    {
        public override void Activate() => ShortcutsWindow.Toggle();
    }

    public class MainButtonWorker_Config : RimWorld.MainButtonWorker
    {
        public override void Activate() => ConfigMenuWindow.Toggle();
    }
}
