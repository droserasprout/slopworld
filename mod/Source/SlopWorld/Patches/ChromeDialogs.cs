using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // The chrome is a fullscreen window on the `Super` layer, so anything opened while it is
    // up and left on the ordinary dialog layer is added *underneath* it: drawn, listening,
    // and invisible. `TerminalWindow.OpenOverPane` is that answer given by hand at every
    // place this half opens a window - but the options pages are vanilla's, and vanilla adds
    // its own the ordinary way. Mod settings, the resolution confirmation and the language
    // restart are all dialogs opened from inside a page that is now content in the chrome
    // ([mod-content-views](docslop/mod-content-views.md)).
    //
    // So the promotion is done where the window arrives instead. Only the dialog layer is
    // touched: a `MainTabWindow` is `GameUI` and belongs under the pane, and anything already
    // asking for `Super` has said where it wants to be.
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
