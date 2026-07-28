using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace SlopWorld
{
    // The five pages a new colony asks for are moot here: nobody starts on the map
    // and it is eaten by the plague regardless.
    //
    // The chain is copied out of Root_Play.SetupForQuickTestPlay - what the dev Quick
    // Test button runs - with the scenario swapped for SlopScenario. Copied rather
    // than called, because the scenario has to be in place before PreConfigure and
    // PostIdeoChosen fan out over its parts, and those sit in the middle of that
    // method with no seam to reach.
    //
    // The one thing the quick start gets wrong for us is startedFromEntry, which it
    // leaves false: that is GameInitData.QuickStarted, which the rest of the game
    // reads as "this is a dev test". Nothing it changes reaches us any more now that
    // no pawn starts on the map, but the honest answer is still that this is a real
    // start.
    [HarmonyPatch(typeof(Page_SelectScenario), "PreOpen")]
    public static class Patch_QuickStart
    {
        // Vanilla's own quick-start size is 250; 200 generates faster and is more than
        // enough for a colony that is only ever looked at.
        const int MapSize = 200;

        // Vanilla's quick-start figure. The colony occupies one tile, but LandingSite
        // needs enough world to find a green lowland one in.
        const float PlanetCoverage = 0.3f;

        // In-game days between autosaves, if the player has them switched off.
        const float AutosaveDays = 1f;

        // PreOpen is the only opening hook Page_SelectScenario declares. The page itself
        // is left alone: Root.OnGUI skips the window stack while a long event is pending,
        // so it never draws. The exception handler is vanilla's.
        static void Postfix() =>
            LongEventHandler.QueueLongEvent(Begin, "GeneratingMap", true,
                GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);

        // Runs off the main thread, like vanilla's own quick start: touch the game being
        // built, nothing that belongs to the UI.
        static void Begin()
        {
            Current.ProgramState = ProgramState.Entry;
            Game.ClearCaches();
            Current.Game = new Game { InitData = new GameInitData() };

            Current.Game.Scenario = SlopScenario.Get();
            Find.Scenario.PreConfigure();

            Current.Game.storyteller =
                new Storyteller(StorytellerDefOf.Cassandra, DifficultyDefOf.Rough);
            Current.Game.World = WorldGenerator.GenerateWorld(
                PlanetCoverage, GenText.RandomSeedString(),
                OverallRainfall.Normal, OverallTemperature.Normal,
                OverallPopulation.Normal, LandmarkDensity.Normal);
            Find.GameInitData.ChooseRandomStartingTile();

            Find.GameInitData.mapSize = MapSize;
            Find.GameInitData.startedFromEntry = true;
            // Overwrites the tile just chosen, and needs it there to fall back on.
            LandingSite.Choose();

            // Last, as in vanilla: this is where a scenario generates its starting pawns.
            // Ours has no part left that does, so it generates none.
            Find.Scenario.PostIdeoChosen();

            // And this is what says so. Written after PostIdeoChosen rather than before,
            // so it is the last word whatever the parts did; -1 is the field's own default
            // and PrepForMapGen indexes the pawn list with it.
            Find.GameInitData.startingPawnCount = 0;

            // Autosave off would mean a colony that can never be picked back up.
            if (Prefs.AutosaveIntervalDays <= 0f) Prefs.AutosaveIntervalDays = AutosaveDays;

            Log.Message("[SlopWorld] scripted colony start");
            PageUtility.InitGameStart();
        }
    }
}
