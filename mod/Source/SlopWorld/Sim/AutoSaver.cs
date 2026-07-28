using HarmonyLib;
using UnityEngine;
using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    // RimWorld's own interval is measured in game days - a quarter of an hour at 1x -
    // which is far too coarse for a colony that exists to be restarted: every rebuild
    // of the mod needs a fresh process, and the save is what carries the colony
    // across it. The save itself is vanilla's, into the rotating autosave slots, so
    // this never overwrites one the player named.
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

        // Everything that quits goes through here, including paths the player did not
        // choose, so a failure must never be what stops the game closing.
        //
        // Except a colony already on its way to the bin. "Next planet" says so several
        // seconds before the game is gone - long enough for the interval to come round on
        // a map that is burning - and a save taken there would hand Patch_AutoResume a
        // discarded colony to come back to.
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
        static void Prefix() => AutoSaver.SaveNow();
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
