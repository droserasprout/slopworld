using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace SlopWorld
{
    // Reproduce vanilla quick-start with `SlopScenario`, but skip setup pages and starting pawns.
    // The sequence is copied because scenario hooks are interleaved with setup; mark a real
    // entry start and set `startingPawnCount` after those hooks run.
    public static class QuickStart
    {
        // Vanilla's own quick-start size is 250; 200 generates faster and is more than
        // enough for a colony that is only ever looked at.
        const int MapSize = 200;

        // Vanilla's quick-start figure. The colony occupies one tile, but LandingSite
        // needs enough world to find a green lowland one in.
        const float PlanetCoverage = 0.3f;

        // In-game days between autosaves, if the player has them switched off.
        const float AutosaveDays = 1f;

        // The two ways in are the same call: the player asking for a colony, and there
        // being no colony to ask about. Queued rather than run, so both callers can be
        // in the middle of drawing something. The exception handler is vanilla's.
        public static void Queue() =>
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

    // The player's own way in. PreOpen is the only opening hook Page_SelectScenario
    // declares; the page itself is left alone, because Root.OnGUI skips the window
    // stack while a long event is pending and so it never draws.
    [HarmonyPatch(typeof(Page_SelectScenario), "PreOpen")]
    public static class Patch_QuickStart
    {
        static void Postfix() => QuickStart.Queue();
    }
}
