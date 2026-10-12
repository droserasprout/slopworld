using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // Reapply our cursor when vanilla changes cursor mode.
    [HarmonyPatch(typeof(CustomCursor), nameof(CustomCursor.Activate))]
    public static class Patch_CustomCursor_Activate
    {
        static bool Prefix()
        {
            GameCursor.Apply();
            return false;
        }
    }

    [HarmonyPatch(typeof(CustomCursor), nameof(CustomCursor.Deactivate))]
    public static class Patch_CustomCursor_Deactivate
    {
        static bool Prefix()
        {
            GameCursor.Apply();
            return false;
        }
    }
}
