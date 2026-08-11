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
    // Kept: the camera and Accept/Cancel, which are barely key bindings at all -
    // Window.InnerWindowOnGUI and WindowStack.HandleEventsHighPriority read Cancel by name,
    // so Escape closing a window *is* this def and nothing else. Everything else vanilla
    // ships is dropped.
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

        // Dropped bindings must be unbound, not merely hidden. Two readers bypass the hidden UI:
        //
        // ScreenshotTaker.Update reads Input's frame flag, so F10 typed into a TUI also takes
        // a screenshot regardless of window input handling.
        //
        // KeyBindingDefGenerator also assigns hidden main buttons Tab and F1-F9, and
        // MainButtonsOnGUI consumes them before Patch_MainButtons rejects activation.
        //
        // Answer the shared binding query instead of patching every reader.
        static bool NotBound(KeyBindingDef __instance, ref bool __result)
        {
            if (Kept(__instance)) return true;
            __result = false;
            return false;
        }
    }
}
