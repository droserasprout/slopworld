using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace SlopWorld
{
    // Use the base game quick start sequence with ModScenario. Skip setup pages and starting pawns.
    // Keep scenario hooks in their original order.
    // Set startingPawnCount after the hooks run.
    public static class QuickStart
    {
        // Use a smaller map than the base game quick start size of 250 to reduce generation time.
        const int MapSize = 200;

        // Use the base game quick start coverage.
        // LandingSite needs enough tiles to find a green lowland site.
        const float PlanetCoverage = 0.3f;

        // Game days between automatic saves when the player has disabled them.
        const float AutosaveDays = 1f;

        // Both a player request and the absence of a saved colony use this method.
        // Queue generation so callers can finish drawing. Use the base game exception handler.
        public static void Queue() =>
            LongEventHandler.QueueLongEvent(Begin, "GeneratingMap", true,
                GameAndMapInitExceptionHandlers.ErrorWhileGeneratingMap);

        // This runs outside the main thread, like the base game quick start.
        // Modify the new game only. Do not access the UI.
        static void Begin()
        {
            Current.ProgramState = ProgramState.Entry;
            Game.ClearCaches();
            Current.Game = new Game { InitData = new GameInitData() };

            Current.Game.Scenario = ModScenario.Get();
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
            // Keep the random tile as a fallback if LandingSite finds no suitable tile.
            LandingSite.Choose();

            // This hook normally generates starting pawns. ModScenario removes the parts that generate them.
            Find.Scenario.PostIdeoChosen();

            // Set the count after PostIdeoChosen so scenario hooks cannot override it.
            // PrepForMapGen uses this count to index the pawn list. The default value is -1.
            Find.GameInitData.startingPawnCount = 0;

            // Enable automatic saves so AutoResume can restore this colony.
            if (Prefs.AutosaveIntervalDays <= 0f) Prefs.AutosaveIntervalDays = AutosaveDays;

            Log.Message("[SlopWorld] scripted colony start");
            PageUtility.InitGameStart();
        }
    }

    // Replace the New colony setup page before it enters the stack. Queuing in PreOpen
    // still lets Add retain the page, which can draw before the loading event takes over.
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    public static class Patch_QuickStart
    {
        static bool Prefix(Window window)
        {
            if (!(window is Page_SelectScenario)) return true;
            QuickStart.Queue();
            return false;
        }
    }
}
