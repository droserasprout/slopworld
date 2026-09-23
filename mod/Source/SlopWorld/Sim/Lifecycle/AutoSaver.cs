using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Save frequently to reduce lost progress during process restarts.
    // Rotate autosave slots without replacing named saves.
    public class AutoSaver : GameComponent
    {
        // Balance recovery frequency against the cost of writing saves.
        const float Minutes = 2f;

        // Keep the timer in memory so each process starts a new save interval.
        float _next;

        public AutoSaver(Game game) { }

        public override void GameComponentTick()
        {
            float now = Time.realtimeSinceStartup;
            // Start the timer on the first tick without saving immediately.
            // This avoids a save between loading and the first frame.
            if (_next <= 0f)
            {
                _next = now + Minutes * 60f;
                return;
            }
            if (now < _next) return;

            _next = now + Minutes * 60f;
            SaveCoordinator.SaveNow();
        }
    }

    // Request a save for both in-game restart and ordinary shutdown.
    [HarmonyPatch(typeof(Root), nameof(Root.Shutdown))]
    public static class Patch_SaveOnShutdown
    {
        static void Prefix()
        {
            // Mark this shutdown as programmatic.
            // The interceptor then permits the wantsToQuit event from Root.Shutdown without starting another save sequence.
            SaveCoordinator.NoteProgrammaticShutdown();
            SaveCoordinator.SaveNow();
        }
    }

    // Save before returning to the main menu without ending the process.
    // SaveNow excludes colonies that NextPlanet will discard.
    [HarmonyPatch(typeof(GenScene), nameof(GenScene.GoToMainMenu))]
    public static class Patch_SaveOnMainMenu
    {
        static void Prefix() => SaveCoordinator.SaveNow();
    }

    [HarmonyPatch(typeof(GameDataSaveLoader),
        nameof(GameDataSaveLoader.CurrentGameStateIsValuable), MethodType.Getter)]
    public static class Patch_NothingToLose
    {
        static bool Prefix(ref bool __result)
        {
            __result = false;
            return false;
        }
    }
}
