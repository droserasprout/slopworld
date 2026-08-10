using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // The keyboard, cut to what this game answers to. Vanilla ships fifty-nine key bindings
    // and generates one more for every main button carrying a defaultHotKey; nearly all of
    // them drive something this mod does not run - the architect menu and its designators,
    // gizmo hotkeys, the time controls, the overlays in the play-settings row - so they are
    // keys with nothing behind them, offered on the Shortcuts page as though they were
    // choices.
    //
    // Kept: the camera, the two that walk the colonist bar, and Accept/Cancel, which are
    // barely key bindings at all - Window.InnerWindowOnGUI and
    // WindowStack.HandleEventsHighPriority read Cancel by name, so Escape closing a window
    // *is* this def and nothing else. Everything else vanilla ships is dropped.
    //
    // The defs stay in the database, for the reason StripOptions leaves its category there:
    // `AllDefs` is the database's own list, KeyPrefs keys its table on the def, and a def
    // pulled out is a hole for every other reader while its key still binds. Dropped means
    // unlisted - KeyBindingsPage draws `Kept` - and unbound, which is `NotBound` below.
    public static class StripKeys
    {
        static readonly HashSet<string> Keep = new HashSet<string>
        {
            "MapDolly_Up", "MapDolly_Down", "MapDolly_Left", "MapDolly_Right",
            "MapZoom_In", "MapZoom_Out",
            "PreviousColonist", "NextColonist",
            "Accept", "Cancel",
        };

        // Ours by content pack rather than by name: every binding this mod ships is one it
        // put there on purpose, and a table of them here would be a second place to
        // remember when Defs/KeyBindings.xml grows a row.
        public static bool Kept(KeyBindingDef def) =>
            def != null
            && (Keep.Contains(def.defName)
                || (SlopWorldMod.Instance != null
                    && def.modContentPack == SlopWorldMod.Instance.Content));

        // The four ways vanilla asks whether a binding is down, patched from a table the
        // way Patch_HideGui's targets are. MainKey is left off it: that is what a label is
        // drawn from, and every label of a dropped binding belongs to something that is not
        // drawn either.
        static readonly string[] Reads =
        {
            "KeyDownEvent", "IsDownEvent", "JustPressed", "IsDown",
        };

        public static void Apply(Harmony h)
        {
            var pre = new HarmonyMethod(
                AccessTools.Method(typeof(StripKeys), nameof(NotBound)));
            foreach (var read in Reads)
                h.Patch(AccessTools.PropertyGetter(typeof(KeyBindingDef), read), prefix: pre);
        }

        // A dropped binding is unbound, not merely unlisted. Most are read from a path this
        // mod never draws - the time controls and the play-settings row are both inside the
        // GlobalControls that Patch_HideGui skips, and the designators and gizmo hotkeys
        // have nothing left to hang off - but not all of them, and the two exceptions are
        // ones no amount of hiding would reach:
        //
        // ScreenshotTaker.Update reads TakeScreenshot off Root.Update through JustPressed,
        // which is Input's own frame flag, so no window absorbing input and no Event.Use()
        // can touch it. Bare F10 typed into a TUI is passed to the agent and writes a
        // screenshot on the way past.
        //
        // And hiding a main button does not free its key. KeyBindingDefGenerator hands every
        // MainButtonDef with a defaultHotKey an implied binding and writes it back onto the
        // def - Tab, and F1 through F9, which is our command palette and the sidebar's four
        // views - and MainButtonsRoot.MainButtonsOnGUI Uses the event *before*
        // InterfaceTryActivate, where Patch_MainButtons turns the press away: the tab does
        // not open and the key is swallowed anyway. Only reachable while a cutscene plays,
        // TerminalHotkeys running ahead of MainButtonsRoot in UIRoot_Play.UIRootOnGUI and
        // Using the key first - but that is an ordering between two callers, not a claim on
        // the key, and F1 belongs to whoever this mod says it does.
        //
        // So the read is answered rather than each system patched where it sits: there is
        // one question here - is this key bound - and one place vanilla asks it.
        static bool NotBound(KeyBindingDef __instance, ref bool __result)
        {
            if (Kept(__instance)) return true;
            __result = false;
            return false;
        }
    }
}
