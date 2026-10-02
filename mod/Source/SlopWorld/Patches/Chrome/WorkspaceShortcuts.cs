using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Dispatch workspace shortcuts before GameComponentOnGUI and native IMGUI text controls.
    // This prevents a focused editor from consuming interface shortcuts.
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.HandleEventsHighPriority))]
    public static class Patch_WorkspaceShortcuts
    {
        static void Prefix()
        {
            var e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;
            if (Current.ProgramState != ProgramState.Playing) return;

            // Let the binding listener capture the next key without activating its interface action.
            if (ModOptions.KeyboardCaptureActive) return;

            if (TerminalWindow.HandleFunctionKey(e)) e.Use();
        }
    }
}
