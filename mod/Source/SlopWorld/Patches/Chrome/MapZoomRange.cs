using HarmonyLib;
using Verse;

namespace SlopWorld
{
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
