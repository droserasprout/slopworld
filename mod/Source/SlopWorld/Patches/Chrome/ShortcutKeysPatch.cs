using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace SlopWorld
{
    // Dispatch map shortcuts to the workspace input owner before vanilla navigation.
    [HarmonyPatch(typeof(ShortcutKeys), "ShortcutKeysOnGUI")]
    public static class Patch_ShortcutKeysOnGUI
    {
        static bool Prefix() => !TerminalInputController.HandleMapSessionNavigation(Event.current);
    }
}
