using HarmonyLib;
using Waylith;

namespace SlopWorld
{
    // The native close callback requests shutdown and always declines immediate
    // destruction. Defer its request while the game can still draw confirmation.
    [HarmonyPatch(typeof(EngineApplication), "RequestShutdown")]
    static class Patch_NativeWindowClose
    {
        static bool Prefix(EngineApplication __instance, string __0) =>
            __0 != "WindowClose"
            || !QuitInterceptor.DeferNativeClose(
                true,
                !string.IsNullOrEmpty(__instance.ShutdownRequestedReason)
            );
    }
}
