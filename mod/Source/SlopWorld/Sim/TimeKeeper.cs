using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // There is no way for the player to start the clock again if it stops:
    // Patch_HideGui drops GlobalControls wholesale, and vanilla wires up every
    // time-speed key binding inside TimeControls.DoTimeControlsGUI, which only runs
    // while those controls are drawing. So a pause is a pause forever, on a board
    // with no clock to show it - the map just stops, and looks like everything on it
    // has died.
    //
    // It is also the only thing that starts the clock on a fresh colony now the
    // scenario's opening dialog is gone: that box held the pause a new game begins
    // on. Windows that force a pause still hold time while they are up.
    public class TimeKeeper : GameComponent
    {
        // Logged once: knowing that something out there still pauses the game, and when,
        // is worth one line.
        bool _reported;

        public TimeKeeper(Game game) { }

        public override void GameComponentUpdate()
        {
            if (Verse.Current.ProgramState != ProgramState.Playing) return;

            var ticks = Find.TickManager;
            if (ticks == null || ticks.CurTimeSpeed != TimeSpeed.Paused) return;
            if (Find.WindowStack != null && Find.WindowStack.WindowsForcePause) return;

            ticks.CurTimeSpeed = TimeSpeed.Normal;

            if (_reported) return;
            _reported = true;
            Log.Message($"[SlopWorld] something paused the game at tick {ticks.TicksGame}; resumed");
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_Menu), MethodType.Constructor)]
    public static class Patch_MenuNoPause
    {
        static void Postfix(MainTabWindow_Menu __instance) => __instance.forcePause = false;
    }
}
