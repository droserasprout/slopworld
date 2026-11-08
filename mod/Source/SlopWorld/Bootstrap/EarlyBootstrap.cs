using System;
using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // ModEntry runs before XML processing. Keep this independent of definitions, UI fonts,
    // and gameplay patches, which still belong to ModBootstrap.
    internal static class EarlyBootstrap
    {
        internal static bool Ready { get; private set; }
        static bool installed;

        internal static void Install()
        {
            if (installed || !ModProfile.Ok)
                return;
            installed = true;
            NativeFonts.Prepare();
            var harmony = new Harmony("io.drsr.slopworld.startup");
            try
            {
                LoadingScreenHost.Install(harmony);
                Patch_MenuBackgroundDraw.Install(harmony);
                harmony.Patch(
                    AccessTools.Method(typeof(Root), nameof(Root.Update)),
                    prefix: new HarmonyMethod(typeof(EarlyBootstrap), nameof(FollowWindow)),
                    postfix: new HarmonyMethod(typeof(EarlyBootstrap), nameof(Update))
                );
                harmony.Patch(
                    AccessTools.Method(typeof(Root), nameof(Root.Shutdown)),
                    prefix: new HarmonyMethod(typeof(EarlyBootstrap), nameof(Shutdown))
                );
                StartupAudio.Install(harmony);
                Log.Message("[SlopWorld] early startup patches ready");
            }
            catch (Exception e)
            {
                // Installation is transactional: never defer sounds without all consumers,
                // or hide the normal loader when its replacement could not be installed.
                harmony.UnpatchAll(harmony.Id);
                Log.Warning("[SlopWorld] early startup patches unavailable: " + e);
            }
        }

        static void FollowWindow()
        {
            // The native window exists before mod discovery. Run on the game thread,
            // without pulling SessionHub or late definition-dependent owners forward.
            if (Ready)
                return;
            WindowTitle.Startup();
            LinuxGameWindow.Follow();
        }

        static void Update()
        {
            MenuBackground.AdvancePreparation();
            StartupAudio.Update();
        }

        static void Shutdown()
        {
            MenuBackground.CancelPreparation();
            StartupAudio.Flush();
        }

        internal static void Complete() => Ready = true;
    }
}
