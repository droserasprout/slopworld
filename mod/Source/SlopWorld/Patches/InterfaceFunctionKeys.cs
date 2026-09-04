using HarmonyLib;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Dispatch interface function keys from WindowStack's high-priority input boundary. This
    // runs before GameComponentOnGUI and before any Window draws a native IMGUI text control,
    // so a focused editor cannot consume a key that belongs to the interface.
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.HandleEventsHighPriority))]
    public static class Patch_InterfaceFunctionKeys
    {
        static void Prefix()
        {
            var e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;
            if (Current.ProgramState != ProgramState.Playing) return;

            // A binding listener is the one intentional exception: it owns the next key and
            // must be able to record an interface key instead of activating that key now.
            if (SlopOptions.KeyboardCaptureActive) return;

            if (TerminalWindow.HandleFunctionKey(e)) e.Use();
        }
    }
}
