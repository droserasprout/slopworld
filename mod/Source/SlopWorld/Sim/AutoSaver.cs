using HarmonyLib;
using UnityEngine;
using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    // Vanilla autosaves are too infrequent for process restarts; rotating autosave slots preserve rebuilds without replacing named saves.
    public class AutoSaver : GameComponent
    {
        // Short enough that a crash costs a colony nothing worth mourning, long enough
        // that the write is not what the player notices.
        const float Minutes = 2f;

        // Runtime only: a save is worth taking on the far side of a restart too.
        float _next;

        public AutoSaver(Game game) { }

        public override void GameComponentTick()
        {
            float now = Time.realtimeSinceStartup;
            // First tick of a session: arm the timer rather than saving immediately, which
            // would put a save between the load and the first frame.
            if (_next <= 0f)
            {
                _next = now + Minutes * 60f;
                return;
            }
            if (now < _next) return;

            _next = now + Minutes * 60f;
            SaveNow();
        }

        // Save failures must not block shutdown, but skip colonies pending NextPlanet so AutoResume cannot restore a discarded map.
        public static void SaveNow()
        {
            try
            {
                if (NextPlanet.Pending) return;
                if (Current.ProgramState != ProgramState.Playing) return;
                Current.Game?.autosaver?.DoAutosave();
            }
            catch (Exception e)
            {
                Log.Error($"[SlopWorld] autosave failed: {e}");
            }
        }
    }

    // Covers the in-game restart and the ordinary quit alike, so "unsaved work will
    // be lost" stops being true.
    [HarmonyPatch(typeof(Root), nameof(Root.Shutdown))]
    public static class Patch_SaveOnShutdown
    {
        static void Prefix()
        {
            // Tells the interceptor that this quit is programmatic (the profile's Quit
            // button), so the wantsToQuit event that fires when
            // Root.Shutdown calls Application.Quit is let through rather than intercepted.
            QuitInterceptor.NoteProgrammaticShutdown();
            AutoSaver.SaveNow();
        }
    }

    // Quitting to the main menu, which drops the game without touching the process. A
    // colony on its way to the bin is refused by SaveNow itself.
    [HarmonyPatch(typeof(GenScene), nameof(GenScene.GoToMainMenu))]
    public static class Patch_SaveOnMainMenu
    {
        static void Prefix() => AutoSaver.SaveNow();
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
