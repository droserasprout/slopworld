using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // GameComponentUpdate runs after game ticks.
    // Also enforce Eco before TickManagerUpdate so the base game clears ticksThisFrame and returns through its paused branch.
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickManagerUpdate))]
    public static class Patch_EcoTickBoundary
    {
        [HarmonyPriority(Priority.Last)]
        static void Prefix(TickManager __instance)
        {
            if (Eco.Resting) __instance.CurTimeSpeed = TimeSpeed.Paused;
        }
    }

    // Keep the game paused during Eco and resume normal speed afterward.
    // Outside Eco, also resume other pauses unless an open window requires a pause.
    // The mod hides the base-game time controls, so this fallback is necessary.
    public class TimeKeeper : GameComponent
    {
        // Log only the first pause caused outside this component.
        bool _reported;

        // Track pauses set by this component so normal Eco pauses do not produce diagnostics.
        bool _ours;

        public TimeKeeper(Game game) { }

        public override void GameComponentUpdate()
        {
            if (Verse.Current.ProgramState != ProgramState.Playing) return;

            var ticks = Find.TickManager;
            if (ticks == null) return;

            // Enforce the Eco pause every frame.
            // This corrects speed changes from keyboard input, loading, or window closure on the next frame.
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
            Log.Message($"[SlopWorld] Something paused the game at tick {ticks.TicksGame}. Resumed it.");
        }
    }

    [HarmonyPatch(typeof(MainTabWindow_Menu), MethodType.Constructor)]
    public static class Patch_MenuNoPause
    {
        static void Postfix(MainTabWindow_Menu __instance) => __instance.forcePause = false;
    }
}
