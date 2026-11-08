using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // Save first, then flush the configured stop before Root.Shutdown terminates the engine.
    // QuitInterceptor also routes the window close action through Root.Shutdown.
    [HarmonyPatch(typeof(Root), nameof(Root.Shutdown))]
    public static class Patch_RadioOnShutdown
    {
        [HarmonyPriority(Priority.Last)]
        static void Prefix() => Radio.Quit();
    }
}
