using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // GameComponentUpdate runs after ticking. Enforce Eco at the tick boundary as well,
    // letting vanilla clear ticksThisFrame and take its normal paused return.
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickManagerUpdate))]
    public static class Patch_EcoTickBoundary
    {
        [HarmonyPriority(Priority.Last)]
        static void Prefix(TickManager __instance)
        {
            if (Eco.Resting) __instance.CurTimeSpeed = TimeSpeed.Paused;
        }
    }

    // Own the game clock: start fresh colonies, hold Eco paused, and resume only pauses this
    // component owns. Vanilla time controls are hidden, so an external pause needs this fallback.
    public class TimeKeeper : GameComponent
    {
        // Logged once: knowing that something out there still pauses the game, and when,
        // is worth one line.
        bool _reported;

        // Whether the pause about to be lifted is the one this component put there. Eco's
        // stop is not news, and the line below is about the pauses nobody here asked for.
        bool _ours;

        public TimeKeeper(Game game) { }

        public override void GameComponentUpdate()
        {
            if (Verse.Current.ProgramState != ProgramState.Playing) return;

            var ticks = Find.TickManager;
            if (ticks == null) return;

            // Eco mode holds it stopped. Written every frame rather than on the edge:
            // anything that sets a speed - a key, a load, a window closing - is undone on
            // the next one, which is the same insistence the resume below is.
            if (Eco.Resting)
            {
                if (ticks.CurTimeSpeed != TimeSpeed.Paused)
                    ticks.CurTimeSpeed = TimeSpeed.Paused;
                _ours = true;
                return;
            }

            if (ticks.CurTimeSpeed != TimeSpeed.Paused) { _ours = false; return; }
            if (Find.WindowStack != null && Find.WindowStack.WindowsForcePause) return;

            ticks.CurTimeSpeed = TimeSpeed.Normal;

            if (_ours) { _ours = false; return; }
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
