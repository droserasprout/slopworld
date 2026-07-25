using Verse;

namespace SlopWorld
{
    /// <summary>
    /// Keeps the clock running. There is no way for the player to start it again if
    /// it stops: Patch_HideGui drops GlobalControls wholesale, and vanilla wires up
    /// every time-speed key binding - pause, 1, 2, 3, 4 - inside
    /// TimeControls.DoTimeControlsGUI, which only ever runs while those controls are
    /// drawing. So a pause from anywhere (a new game starts paused, a letter with a
    /// pause mode, an error with the dev pref on) is a pause forever, on a board with
    /// no clock to show it: the map just stops, and looks for all the world like
    /// every last thing on it has died.
    ///
    /// A viewer's sim has no business sitting still, so the mod owns the clock.
    /// Windows that force a pause - the opening dialog, a game-over box - still hold
    /// time while they are up; this only stops the pause from outliving them.
    ///
    /// GameComponents are built for every subclass automatically, so this needs no
    /// def.
    /// </summary>
    public class TimeKeeper : GameComponent
    {
        // Logged once: knowing that something out there still pauses the game, and
        // when, is worth one line. Repeating it every time would be noise.
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
}
