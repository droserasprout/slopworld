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
            var harmony = new Harmony("io.drsr.slopworld.startup");
            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(Root), nameof(Root.Update)),
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
                // Installation is transactional: never defer sounds without all consumers.
                harmony.UnpatchAll(harmony.Id);
                Log.Warning("[SlopWorld] early startup patches unavailable: " + e);
            }
        }

        static void Update()
        {
            StartupAudio.Update();
        }

        static void Shutdown()
        {
            StartupAudio.Flush();
        }

        internal static void Complete() => Ready = true;
    }
}
