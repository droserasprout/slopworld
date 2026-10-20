using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // Run before Root.Shutdown closes the socket. Keep playback shutdown separate from saving.
    // QuitInterceptor also routes the window close action through Root.Shutdown.
    [HarmonyPatch(typeof(Root), nameof(Root.Shutdown))]
    public static class Patch_RadioOnShutdown
    {
        static void Prefix() => Radio.Quit();
    }
}
