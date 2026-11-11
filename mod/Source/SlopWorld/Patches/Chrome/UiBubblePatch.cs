using HarmonyLib;
using UnityEngine;

namespace SlopWorld
{
    // Framework boundary: draw after queued windows so opaque panes cannot bury bubbles.
    [HarmonyPatch(typeof(GUI), "ProcessPendingWindows")]
    internal static class Patch_UiBubble
    {
        [HarmonyPriority(Priority.Normal)]
        static void Postfix() => UiBubble.DrawAfterWindows();
    }
}
