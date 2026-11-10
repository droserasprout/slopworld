using System.Reflection;
using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // Menu quit and restart still enter Root.Shutdown before Application.Quit.
    [HarmonyPatch(typeof(Root), nameof(Root.Shutdown))]
    public static class Patch_SaveOnShutdown
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix()
        {
            SaveCoordinator.NoteProgrammaticShutdown();
            SaveCoordinator.SaveOnShutdown();
        }
    }

    // Fallback for engine-requested shutdown/crashes that bypass Root.Shutdown.
    // User window-close requests are intercepted before the loop exits so they can
    // still show UI, cancel and drain session stops. This late hook cannot cancel.
    [HarmonyPatch]
    static class Patch_SaveOnNativeShutdown
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                AccessTools.TypeByName("Waylith.EngineApplication"),
                "ExecuteQuitLifecycle",
                new[] { typeof(string) }
            );

        static void Prefix() => SaveCoordinator.SaveOnShutdown();
    }
}
