using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    // An assembly change requires a restart. Hook the first MainMenuOnGUI frame so resume
    // starts from an idle menu, once per process and only without a loaded game.
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    public static class Patch_AutoResume
    {
        static bool _tried;

        // Observe Pending before the normal-priority landing hook consumes it.
        [HarmonyPriority(Priority.First)]
        static void Prefix()
        {
            // Void Harmony prefixes still run when the recovery hook skips menu drawing.
            if (LoadFailureRecovery.Pending) { _tried = true; return; }

            // NextPlanet is creating a new colony through the menu.
            // Do not start a competing resume operation.
            if (NextPlanet.Pending) { _tried = true; return; }

            if (_tried) return;
            _tried = true;

            if (Current.Game != null) return; // back from a colony, not a cold start

            try
            {
                var newest = GenFilePaths.AllSavedGameFiles.FirstOrDefault();
                if (newest == null)
                {
                    // A new profile has no saved colony.
                    // Use the New colony action to enter a colony on the first launch.
                    Log.Message("[SlopWorld] No colony exists here yet. Starting one.");
                    QuickStart.Queue();
                    return;
                }

                Log.Message($"[SlopWorld] resuming {Path.GetFileNameWithoutExtension(newest.Name)}");
                // LoadGame queues the load operation and changes the scene, as for the Continue button.
                GameDataSaveLoader.LoadGame(newest);
            }
            catch (Exception e)
            {
                // Asynchronous failures go through ErrorWhileLoadingGame; failures
                // before queuing need the same notice and new-colony handoff.
                LoadFailureRecovery.Begin(e);
            }
        }
    }
}
