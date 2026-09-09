using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // Dialogs opened over fullscreen `Super` chrome otherwise land underneath it. Promote only
    // ordinary dialog windows; `GameUI` and existing `Super` windows retain their layers.
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    public static class Patch_DialogsOverChrome
    {
        static void Prefix(Window window)
        {
            if (window == null || window.layer != WindowLayer.Dialog) return;
            if (window is TerminalWindow) return;
            if (Find.WindowStack?.WindowOfType<TerminalWindow>() == null) return;

            window.layer = WindowLayer.Super;
        }
    }
}
