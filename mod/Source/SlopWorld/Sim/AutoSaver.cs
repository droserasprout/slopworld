using HarmonyLib;
using UnityEngine;
using Verse;
using Exception = System.Exception;

namespace SlopWorld
{
    /// <summary>
    /// Autosave on the wall clock, and on the way out.
    ///
    /// RimWorld's own autosave interval is measured in game days - about a quarter
    /// of an hour of real time at 1x - which is fine for a colony you are playing
    /// and far too coarse for one that exists to be restarted. Every rebuild of the
    /// mod needs a fresh process, and the save is what carries the colony across it,
    /// so it has to be recent by definition rather than by luck.
    ///
    /// The save itself is vanilla's: `Autosaver.DoAutosave` writes to the rotating
    /// autosave slots, so this never overwrites a save the player named.
    ///
    /// GameComponents are constructed for every subclass automatically, so this
    /// needs no def.
    /// </summary>
    public class AutoSaver : GameComponent
    {
        // Real minutes between saves. Short enough that a crash costs a colony
        // nothing worth mourning, long enough that the write is not what the
        // player notices.
        const float Minutes = 2f;

        // Runtime only: a save is worth taking on the far side of a restart too.
        float _next;

        public AutoSaver(Game game) { }

        public override void GameComponentTick()
        {
            float now = Time.realtimeSinceStartup;
            // First tick of a session: arm the timer rather than saving immediately,
            // which would put a save between the load and the first frame.
            if (_next <= 0f)
            {
                _next = now + Minutes * 60f;
                return;
            }
            if (now < _next) return;

            _next = now + Minutes * 60f;
            SaveNow();
        }

        /// <summary>
        /// Saves if there is anything to save. Everything that quits goes through
        /// here, including paths the player did not choose - a failure must never
        /// be what stops the game from closing.
        ///
        /// Except a colony already on its way to the bin. "Next planet" says this
        /// one is finished with, and it says so several seconds before the game is
        /// gone - long enough for the interval below to come round on a map that is
        /// burning. A save taken in there would both contradict the button and hand
        /// `Patch_AutoResume` a discarded colony to come back to. The guard is here
        /// rather than at the callers because every road to a save passes through
        /// this method and only one of them used to check.
        /// </summary>
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

    /// <summary>Quitting the process. Covers the in-game restart and the ordinary
    /// quit alike, so "unsaved work will be lost" stops being true.</summary>
    [HarmonyPatch(typeof(Root), nameof(Root.Shutdown))]
    public static class Patch_SaveOnShutdown
    {
        static void Prefix() => AutoSaver.SaveNow();
    }

    /// <summary>Quitting to the main menu, which drops the game without touching
    /// the process. A colony on its way to the bin is refused by SaveNow itself.</summary>
    [HarmonyPatch(typeof(GenScene), nameof(GenScene.GoToMainMenu))]
    public static class Patch_SaveOnMainMenu
    {
        static void Prefix() => AutoSaver.SaveNow();
    }
}
