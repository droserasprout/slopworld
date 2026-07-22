using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

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
            h.PatchAll(Assembly.GetExecutingAssembly());
            Patch_HideGui.Apply(h);
            Patch_MainButtons.Apply(h);
            Patch_InspectTabs.Apply(h);
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
}
