using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Starting a colony asks five pages of questions - scenario, storyteller,
    /// planet, landing site, colonists - none of which a viewer answers, and all of
    /// which are moot here: the starters are blown up on landing and the map is
    /// eaten by the plague. So "New colony" skips straight to a generated map.
    ///
    /// The work is RimWorld's own: Root_Play.SetupForQuickTestPlay is what the dev
    /// Quick Test button runs, and it does the whole chain headlessly - Crashlanded,
    /// Cassandra on Rough, a random planet, a random tile, and (via
    /// Scenario.PostIdeoChosen) the three starting pawns. PageUtility.InitGameStart
    /// then loads the Play scene and generates the map, exactly as the last page
    /// would have.
    ///
    /// Unconditional, like the UI stripping: being loaded is the switch. Loading a
    /// save never comes through here.
    ///
    /// The one thing the quick start gets wrong for us is startedFromEntry, which it
    /// leaves false - that is GameInitData.QuickStarted, and the scenario reads it as
    /// "this is a dev test": the pods insta-drop with the colonists already standing
    /// on the ground, and the game-start dialog (the one thing we do want, with our
    /// text in it) never fires. We came from the menu like anyone else, so we say so.
    /// </summary>
    [HarmonyPatch(typeof(Page_SelectScenario), "PreOpen")]
    public static class Patch_QuickStart
    {
        // Vanilla's own map size for a quick start is 250; 200 generates faster and
        // is more than enough for a colony that is only ever looked at.
        const int MapSize = 200;

        // In-game days between autosaves, if the player has them switched off.
        const float AutosaveDays = 1f;

        // PreOpen is the only opening hook Page_SelectScenario declares. The page
        // itself is left alone: Root.OnGUI skips the window stack entirely while a
        // long event is pending, so it never draws, and the Play scene it loads
        // builds a fresh UI root anyway. The exception handler is vanilla's - a map
        // that fails to generate drops back to the menu rather than leaving a
        // half-built game behind.
        static void Postfix() =>
            LongEventHandler.QueueLongEvent(Begin, "GeneratingMap", true,
                GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);

        // Runs off the main thread, like vanilla's own quick start: touch the game
        // being built, nothing that belongs to the UI.
        static void Begin()
        {
            Root_Play.SetupForQuickTestPlay();
            Find.GameInitData.mapSize = MapSize;
            Find.GameInitData.startedFromEntry = true;

            // Autosave off would mean a colony that can never be picked back up.
            if (Prefs.AutosaveIntervalDays <= 0f) Prefs.AutosaveIntervalDays = AutosaveDays;

            Log.Message("[SlopWorld] scripted colony start");
            PageUtility.InitGameStart();
        }
    }
}
