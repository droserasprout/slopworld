using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // With the mod loaded RimWorld is a viewer, not a colony you play, so most of
    // its own UI is dead weight. These patches strip it wholesale and
    // unconditionally: there is no setting, being loaded is the switch.
    //
    // Kept on purpose (agent-facing or navigation): the colonist bar, the inspect
    // pane, the Menu main button (save / load / quit) and our own Agents button.
    // Everything else in the bottom bar, both notification stacks, the alert
    // readout, the resource readout, the global controls (date / speed / temp) and
    // the mouseover readout are suppressed. See DROPS.md for the full list.
    //
    // Each patch prefixes an OnGUI method and returns false to skip its draw. The
    // target names are the 1.6 ones (this mod is 1.6-only).

    /// <summary>Message toasts, top-left.</summary>
    [HarmonyPatch(typeof(Messages), "MessagesDoGUI")]
    public static class Patch_Hide_Messages
    {
        static bool Prefix() => false;
    }

    /// <summary>The letter stack, right edge.</summary>
    [HarmonyPatch(typeof(LetterStack), "LettersOnGUI")]
    public static class Patch_Hide_Letters
    {
        static bool Prefix() => false;
    }

    /// <summary>The alert list, right edge.</summary>
    [HarmonyPatch(typeof(AlertsReadout), "AlertsReadoutOnGUI")]
    public static class Patch_Hide_Alerts
    {
        static bool Prefix() => false;
    }

    /// <summary>Colony resource counts, bottom-left.</summary>
    [HarmonyPatch(typeof(ResourceReadout), "ResourceReadoutOnGUI")]
    public static class Patch_Hide_Resources
    {
        static bool Prefix() => false;
    }

    /// <summary>Date, temperature, season and the speed / time buttons, bottom-right.</summary>
    [HarmonyPatch(typeof(GlobalControls), "GlobalControlsOnGUI")]
    public static class Patch_Hide_GlobalControls
    {
        static bool Prefix() => false;
    }

    /// <summary>Terrain / thing-under-cursor readout, bottom-left.</summary>
    [HarmonyPatch(typeof(MouseoverReadout), "MouseoverReadoutOnGUI")]
    public static class Patch_Hide_Mouseover
    {
        static bool Prefix() => false;
    }

    /// <summary>
    /// Hides every bottom-bar button except Menu, Inspect and our Agents tab.
    /// MainButtonWorker.Visible is virtual and overridden by several workers
    /// (World, Quests, Mechs...), so we postfix the base getter plus every
    /// declared override. Patched manually from the bootstrap because the target
    /// set is discovered by reflection.
    /// </summary>
    public static class Patch_MainButtons
    {
        // Inspect is kept because it backs the inspect pane; Menu for save / quit.
        static readonly HashSet<string> Keep = new HashSet<string>
        {
            "Menu", "Inspect", "SlopWorld_Agents",
        };

        public static void Apply(Harmony h)
        {
            var post = new HarmonyMethod(
                AccessTools.Method(typeof(Patch_MainButtons), nameof(KeepOnly)));

            h.Patch(AccessTools.PropertyGetter(typeof(MainButtonWorker), "Visible"), postfix: post);
            foreach (var t in typeof(MainButtonWorker).AllSubclassesNonAbstract())
            {
                var g = AccessTools.DeclaredPropertyGetter(t, "Visible");
                if (g != null) h.Patch(g, postfix: post);
            }
        }

        static void KeepOnly(MainButtonWorker __instance, ref bool __result)
        {
            if (!__result) return;
            if (Keep.Contains(__instance.def?.defName)) return;
            __result = false;
        }
    }
}
