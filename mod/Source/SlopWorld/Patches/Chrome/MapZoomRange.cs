using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // Expand each newly created map camera configuration once. Keep its default zoom
    // and input sensitivity; only the near and far limits change.
    [HarmonyPatch(typeof(CameraMapConfig), MethodType.Constructor)]
    internal static class Patch_MapZoomRange
    {
        static void Postfix(CameraMapConfig __instance)
        {
            __instance.sizeRange = new FloatRange(
                __instance.sizeRange.min / 1.5f,
                __instance.sizeRange.max * 1.5f
            );
        }
    }
}
