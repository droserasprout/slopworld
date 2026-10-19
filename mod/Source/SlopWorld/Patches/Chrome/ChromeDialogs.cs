using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // Place ordinary dialogs above an open terminal by assigning the Super layer.
    // Keep other window layers unchanged.
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Add))]
    public static class Patch_DialogsOverChrome
    {
        static void Prefix(Window window)
        {
            if (window == null || window.layer != WindowLayer.Dialog)
                return;
            if (window is WorkspaceWindow)
                return;
            if (Find.WindowStack?.WindowOfType<WorkspaceWindow>() == null)
                return;

            window.layer = WindowLayer.Super;
        }
    }
}
