using HarmonyLib;
using UnityEngine;
using Waylith;

namespace SlopWorld
{
    // The compatibility app's focus field is initialized true but never follows GLFW.
    // Share the engine's per-frame window state with FPS policy and other focus consumers.
    [HarmonyPatch(typeof(Application), nameof(Application.isFocused), MethodType.Getter)]
    static class Patch_ApplicationFocus
    {
        static bool Prefix(ref bool __result)
        {
            __result = EngineWindow.IsFocused;
            return false;
        }
    }
}
