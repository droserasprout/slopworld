using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // The refusal UI must run even though profile-specific Harmony patches are skipped.
    // No Harmony attributes: valid profiles never install this hook.
    internal static class ProfileRefusal
    {
        internal static void Install()
        {
            var harmony = new Harmony("io.drsr.slopworld.profile-guard");
            harmony.Patch(AccessTools.Method(typeof(Root), nameof(Root.Update)),
                postfix: new HarmonyMethod(typeof(ModProfile), nameof(ModProfile.ShowPendingNotice)));
        }
    }
}
