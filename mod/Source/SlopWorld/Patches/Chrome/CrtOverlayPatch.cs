using HarmonyLib;
using UnityEngine;

namespace SlopWorld
{
    // Waylith flushes queued windows after MonoBehaviour.OnGUI, regardless of GUI.depth.
    [HarmonyPatch(typeof(GUI), "ProcessPendingWindows")]
    internal static class Patch_CrtOverlay
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix() => CrtOverlay.DrawAfterWindows();
    }
}
