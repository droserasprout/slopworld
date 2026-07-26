using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    /// <summary>
    /// Loads the newest save on launch, so the main menu is not a stop on the way
    /// back from a rebuild.
    ///
    /// The mod's assembly is read once per process: seeing a change to it means
    /// restarting the game, and this is what makes that cost a loading screen
    /// rather than a menu, a colony chooser and a click. `Patch_QuickStart` still
    /// owns the other case - "New colony" - and the two never meet, because a run
    /// that resumes never reaches the scenario page.
    ///
    /// Hooked on the menu's own draw rather than on UI root construction: it is a
    /// plain static that has been there for many versions, and the first frame it
    /// draws is exactly the moment the game is idle and ready to load something.
    /// Once per process, and only when no game is loaded, so quitting to the menu
    /// leaves you at the menu instead of dragging you back in.
    /// </summary>
    [HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.MainMenuOnGUI))]
    public static class Patch_AutoResume
    {
        static bool _tried;

        static void Prefix()
        {
            // The player is passing through the menu on their way to a new colony,
            // not arriving at it. Resuming here would race NewColony for the frame.
            if (NewColony.Pending) return;

            if (_tried) return;
            _tried = true;

            if (Current.Game != null) return; // back from a colony, not a cold start

            try
            {
                var newest = GenFilePaths.AllSavedGameFiles.FirstOrDefault();
                if (newest == null)
                {
                    Log.Message("[SlopWorld] nothing to resume; staying on the menu");
                    return;
                }

                Log.Message($"[SlopWorld] resuming {Path.GetFileNameWithoutExtension(newest.Name)}");
                // LoadGame queues its own long event and does the scene change, so
                // this is the whole of it - the same call the Continue button makes.
                GameDataSaveLoader.LoadGame(newest);
            }
            catch (Exception e)
            {
                // A save that will not load is not a reason to lose the menu too.
                Log.Error($"[SlopWorld] resume failed: {e}");
            }
        }
    }
}
