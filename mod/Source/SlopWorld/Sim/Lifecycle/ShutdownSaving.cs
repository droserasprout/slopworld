using System.Reflection;
using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // Menu quit and restart still enter Root.Shutdown before Application.Quit.
    [HarmonyPatch(typeof(Root), nameof(Root.Shutdown))]
    public static class Patch_SaveOnShutdown
    {
        static void Prefix()
        {
            SaveCoordinator.NoteProgrammaticShutdown();
            SaveCoordinator.SaveOnShutdown();
        }
    }

    // RimWorld 1.7's native window close bypasses Application.wantsToQuit and
    // Root.Shutdown. The engine calls this lifecycle on the main thread after its
    // loop stops, before game shutdown callbacks and runtime teardown. DoAutosave
    // writes synchronously, so it does not need another update or long event.
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
