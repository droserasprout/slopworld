using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // GameComponentOnGUI runs before pawn labels. Finish the entire GUI pass before
    // covering the departing colony, and leave long-event loading screens unobscured.
    [HarmonyPatch(typeof(Root), nameof(Root.OnGUI))]
    internal static class Patch_NextPlanetFade
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix()
        {
            if (
                !NextPlanet.Leaving
                || Current.ProgramState != ProgramState.Playing
                || LongEventHandler.ShouldWaitForEvent
            )
                return;

            Current.Game?.GetComponent<NextPlanet>()?.DrawFade();
        }
    }
}
