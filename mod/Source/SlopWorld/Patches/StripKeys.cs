using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace SlopWorld
{
    // Retains camera and Accept/Cancel bindings; the removed gameplay systems leave the
    // other vanilla bindings inert. Keep every def in the database because KeyPrefs and
    // other readers key on it. "Dropped" means omitted by KeyBindingsPage and forced to
    // NotBound below. Cancel must remain: WindowStack reads it by name to close windows.
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

        // Patch KeyBindingDef reads because hidden bindings still reach ScreenshotTaker and MainButtonsOnGUI (F10, Tab, and F1-F9).
        static bool NotBound(KeyBindingDef __instance, ref bool __result)
        {
            if (Kept(__instance)) return true;
            __result = false;
            return false;
        }
    }
}
