using HarmonyLib;
using Verse;
using Verse.Steam;

namespace SlopWorld
{
    // ModBootstrap's profile gate owns registration of this startup-only dismissal.
    [HarmonyPatch(typeof(UIRoot_Entry), nameof(UIRoot_Entry.Init))]
    public static class Patch_IgnoreSteamStartupWarning
    {
        static void Postfix()
        {
            if (SteamManager.Initialized)
                return;

            var stack = Find.WindowStack;
            if (stack == null)
                return;

            string warning = "SteamClientMissing".Translate().ToString();
            var windows = stack.Windows;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                if (windows[i] is Dialog_MessageBox dialog && dialog.text.ToString() == warning)
                {
                    // Vanilla's Ignore action is null: closing is the complete action.
                    // Use the normal close lifecycle to restore focus without a close sound.
                    stack.TryRemove(dialog, doCloseSound: false);
                }
            }
        }
    }
}
